using Tavern.Net.GameData;
using Tavern.Net.GameData.Models;

namespace Tavern.Net.Game;

/// <summary>
/// Converts a live GameSession + its Players to/from a <see cref="SavedGame"/> — the plumbing
/// behind GameBoardViewModel's Save Game panel, SavedGamesViewModel's Load, and (via
/// <see cref="CapturePlayer"/>/<see cref="ApplyToPlayer"/> individually) an online game's
/// broadcast-and-mirror state sync, which is really the same "player &lt;-&gt; DTO" conversion run
/// continuously instead of once.
/// </summary>
public static class GameSessionSerializer
{
    public static SavedGame Capture(string name, GameSession session, TimeSpan? elapsedTime = null)
    {
        var players = session.Players.Select(p => CapturePlayer(p, includeDeck: true)).ToList();
        var first = session.Players.FirstOrDefault();
        var summary = first is null ? "Empty game" : $"Turn {first.Stats.TurnCount + 1} · {first.Life} life";

        return new SavedGame(name, DateTime.UtcNow, summary, session.CurrentPhase, players, elapsedTime);
    }

    /// <summary>Writes every distinct card this save references into the by-slug cache
    /// GetCardBySlugAsync reads from — same reasoning as DeckImportViewModel.SaveDeck, so the very
    /// first load of this save doesn't re-fetch every card one by one. Cards repeat heavily across
    /// zones and MajorEvent snapshots, so this dedupes by slug first rather than caching each
    /// occurrence (which could mean thousands of redundant disk writes for a long game).</summary>
    public static void WarmCardCache(GameSession session, GrandArchiveApiClient apiClient)
    {
        var zoneCards = session.Players
            .SelectMany(p => p.Zones.Values.Where(z => z.Type != ZoneType.Tokens).SelectMany(z => z.Cards))
            .Select(c => c.Card);

        var snapshotCards = session.Players
            .SelectMany(p => p.Stats.MajorEvents)
            .SelectMany(e => e.Snapshot.Cards)
            .Select(c => c.Card);

        var deckCards = session.Players
            .Where(p => p.Deck is not null)
            .SelectMany(p => p.Deck!.AllCards);

        foreach (var card in zoneCards.Concat(snapshotCards).Concat(deckCards).DistinctBy(c => c.Slug))
        {
            apiClient.CacheCard(card);
        }
    }

    /// <summary>Captures one player's full live state as a <see cref="SavedPlayer"/> — used both for
    /// a whole-game save (<see cref="Capture"/>) and, in an online game, to serialize just the local
    /// player for broadcast to the opponent.</summary>
    /// <param name="includeDeck">True only for a whole-game save — the broadcast must never carry the
    /// player's sideboard/deck lists.</param>
    public static SavedPlayer CapturePlayer(Player player, bool includeDeck = false)
    {
        var cards = player.Zones.Values
            .Where(zone => zone.Type != ZoneType.Tokens)
            .SelectMany(zone => zone.Cards.Select(card => CaptureCardInstance(card, zone.Type)))
            .ToList();

        var stats = player.Stats;
        var savedStats = new SavedGameStats(
            stats.TurnCount,
            stats.CardsDrawnCount,
            stats.CardsPlayedCount,
            stats.DamageDealtCount,
            stats.LifeRecoveredCount,
            stats.DeadTurnsCount,
            stats.PlayedCardThisTurn,
            stats.CardsLostToMemoryDecayCount,
            stats.ChampionLevelMilestones.ToList(),
            stats.PlayLog.ToList(),
            stats.MajorEvents.Select(CaptureMajorEvent).ToList());

        return new SavedPlayer(
            player.Name,
            player.PlayerNumber,
            player.Life,
            player.StartingHandSize,
            player.StartsInMemory,
            cards,
            savedStats,
            includeDeck && player.Deck is not null ? CaptureDeck(player.Deck) : null);
    }

    private static SavedDeckArrangement CaptureDeck(DeckArrangement deck) => new(
        deck.RegisteredMain.Select(c => c.Slug).ToList(),
        deck.RegisteredMaterial.Select(c => c.Slug).ToList(),
        deck.RegisteredSideboard.Select(c => c.Slug).ToList(),
        deck.Main.Select(c => c.Slug).ToList(),
        deck.Material.Select(c => c.Slug).ToList(),
        deck.Sideboard.Select(c => c.Slug).ToList());

    private static SavedCardInstance CaptureCardInstance(CardInstance card, ZoneType zone) => new(
        card.Card.Slug,
        zone,
        card.HomeZone,
        card.HomeOrder,
        card.IsTapped,
        card.IsFlipped,
        card.FieldX,
        card.FieldY,
        card.Counter,
        card.IsEphemeral,
        card.IsIgnited,
        card.IsImbued,
        card.IsRanged,
        card.IsRooted,
        card.IsWarded,
        card.IsSessionGenerated);

    private static SavedMajorEvent CaptureMajorEvent(MajorEvent majorEvent)
    {
        var snapshot = majorEvent.Snapshot;
        var cards = snapshot.Cards.Select(c => new SavedCardSnapshot(
            c.Card.Slug,
            c.Zone,
            c.IsTapped,
            c.IsFlipped,
            c.FieldX,
            c.FieldY,
            c.Counter,
            c.IsEphemeral,
            c.IsIgnited,
            c.IsImbued,
            c.IsRanged,
            c.IsRooted,
            c.IsWarded)).ToList();

        var savedSnapshot = new SavedGameSnapshot(snapshot.Life, snapshot.DamageDealtCount, snapshot.TurnCount, snapshot.Phase, cards);

        return new SavedMajorEvent(majorEvent.Description, majorEvent.Kind, majorEvent.NetDelta, majorEvent.Turn, majorEvent.Timestamp, savedSnapshot);
    }

    /// <summary>Rebuilds a full GameSession from a save. Throws if a card's slug can no longer be
    /// resolved (cache miss and the API lookup also failed/renamed) — callers should catch and show
    /// that message rather than silently returning a save with cards missing.</summary>
    public static async Task<(GameSession Session, Player FirstPlayer)> RestoreAsync(SavedGame saved, GrandArchiveApiClient apiClient)
    {
        var session = new GameSession();

        // Local to this restore: the same card shows up across many zones and MajorEvent
        // snapshots, so this avoids re-awaiting GetCardBySlugAsync for a slug already resolved.
        var cardCache = new Dictionary<string, CardDto>();

        foreach (var savedPlayer in saved.Players)
        {
            var player = session.AddPlayer(savedPlayer.Name, savedPlayer.Life);
            await ApplyToPlayer(player, savedPlayer, apiClient, cardCache);
        }

        session.SetPhaseForRestore(saved.CurrentPhase);
        return (session, session.Players[0]);
    }

    /// <summary>
    /// Rebuilds <paramref name="target"/>'s zones and stats in place from <paramref name="saved"/> —
    /// clearing and re-populating rather than assuming it's empty, since this is also how an online
    /// game keeps a mirrored copy of the opponent's Player up to date: called again every time a
    /// fresh PlayerState broadcast arrives, not just once at game load.
    /// </summary>
    public static async Task ApplyToPlayer(Player target, SavedPlayer saved, GrandArchiveApiClient apiClient, Dictionary<string, CardDto>? cardCache = null)
    {
        cardCache ??= new Dictionary<string, CardDto>();

        target.Life = saved.Life;
        target.SetStartingHandSize(saved.StartingHandSize);
        target.SetStartsInMemory(saved.StartsInMemory);

        foreach (var zone in target.Zones.Values)
        {
            zone.Cards.Clear();
        }

        foreach (var savedCard in saved.Cards)
        {
            var cardDto = await ResolveCardAsync(savedCard.Slug, apiClient, cardCache);
            var instance = new CardInstance(cardDto, savedCard.HomeZone, savedCard.HomeOrder, savedCard.IsSessionGenerated)
            {
                IsTapped = savedCard.IsTapped,
                IsFlipped = savedCard.IsFlipped,
                FieldX = savedCard.FieldX,
                FieldY = savedCard.FieldY,
                Counter = savedCard.Counter,
                IsEphemeral = savedCard.IsEphemeral,
                IsIgnited = savedCard.IsIgnited,
                IsImbued = savedCard.IsImbued,
                IsRanged = savedCard.IsRanged,
                IsRooted = savedCard.IsRooted,
                IsWarded = savedCard.IsWarded,
            };
            target.GetZone(savedCard.Zone).Cards.Add(instance);
        }

        await ApplyStatsAsync(target.Stats, saved.Stats, apiClient, cardCache);

        // Only a whole-game save carries the deck lists; an online broadcast leaves the mirror's
        // Deck untouched (null) rather than clearing anything.
        if (saved.Deck is not null)
        {
            target.Deck = await RestoreDeckAsync(saved.Deck, apiClient, cardCache);
        }
    }

    private static async Task<DeckArrangement> RestoreDeckAsync(SavedDeckArrangement saved, GrandArchiveApiClient apiClient, Dictionary<string, CardDto> cardCache)
    {
        async Task<List<CardDto>> ResolveAllAsync(IEnumerable<string> slugs)
        {
            var cards = new List<CardDto>();
            foreach (var slug in slugs)
            {
                cards.Add(await ResolveCardAsync(slug, apiClient, cardCache));
            }

            return cards;
        }

        return new DeckArrangement(
            await ResolveAllAsync(saved.RegisteredMain),
            await ResolveAllAsync(saved.RegisteredMaterial),
            await ResolveAllAsync(saved.RegisteredSideboard),
            await ResolveAllAsync(saved.Main),
            await ResolveAllAsync(saved.Material),
            await ResolveAllAsync(saved.Sideboard));
    }

    private static async Task ApplyStatsAsync(GameStats stats, SavedGameStats saved, GrandArchiveApiClient apiClient, Dictionary<string, CardDto> cardCache)
    {
        stats.TurnCount = saved.TurnCount;
        stats.CardsDrawnCount = saved.CardsDrawnCount;
        stats.CardsPlayedCount = saved.CardsPlayedCount;
        stats.DamageDealtCount = saved.DamageDealtCount;
        stats.LifeRecoveredCount = saved.LifeRecoveredCount;
        stats.DeadTurnsCount = saved.DeadTurnsCount;
        stats.PlayedCardThisTurn = saved.PlayedCardThisTurn;
        stats.CardsLostToMemoryDecayCount = saved.CardsLostToMemoryDecayCount;

        stats.ChampionLevelMilestones.Clear();
        foreach (var milestone in saved.ChampionLevelMilestones)
        {
            stats.ChampionLevelMilestones.Add(milestone);
        }

        stats.PlayLog.Clear();
        foreach (var line in saved.PlayLog)
        {
            stats.PlayLog.Add(line);
        }

        stats.MajorEvents.Clear();
        foreach (var savedEvent in saved.MajorEvents)
        {
            var snapshotCards = new List<CardSnapshot>();
            foreach (var savedCard in savedEvent.Snapshot.Cards)
            {
                var cardDto = await ResolveCardAsync(savedCard.Slug, apiClient, cardCache);
                snapshotCards.Add(new CardSnapshot(
                    cardDto,
                    savedCard.Zone,
                    savedCard.IsTapped,
                    savedCard.IsFlipped,
                    savedCard.FieldX,
                    savedCard.FieldY,
                    savedCard.Counter,
                    savedCard.IsEphemeral,
                    savedCard.IsIgnited,
                    savedCard.IsImbued,
                    savedCard.IsRanged,
                    savedCard.IsRooted,
                    savedCard.IsWarded));
            }

            var snapshot = new GameSnapshot(
                savedEvent.Snapshot.Life,
                savedEvent.Snapshot.DamageDealtCount,
                savedEvent.Snapshot.TurnCount,
                savedEvent.Snapshot.Phase,
                snapshotCards);

            stats.MajorEvents.Add(new MajorEvent
            {
                Description = savedEvent.Description,
                Kind = savedEvent.Kind,
                NetDelta = savedEvent.NetDelta,
                Turn = savedEvent.Turn,
                Timestamp = savedEvent.Timestamp,
                Snapshot = snapshot,
            });
        }
    }

    private static async Task<CardDto> ResolveCardAsync(string slug, GrandArchiveApiClient apiClient, Dictionary<string, CardDto> cache)
    {
        if (cache.TryGetValue(slug, out var cached))
        {
            return cached;
        }

        var card = await apiClient.GetCardBySlugAsync(slug)
            ?? throw new InvalidOperationException($"Couldn't find a card for \"{slug}\" anymore — it may have been renamed.");

        cache[slug] = card;
        return card;
    }
}
