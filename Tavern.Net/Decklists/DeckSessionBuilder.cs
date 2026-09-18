using Tavern.Net.Game;
using Tavern.Net.GameData;
using Tavern.Net.GameData.Models;

namespace Tavern.Net.Decklists;

/// <summary>
/// Builds a <see cref="GameSession"/> from a resolved deck — the plumbing shared by
/// DeckImportViewModel.StartGame (a freshly resolved decklist), StartMenuViewModel.Solo (a saved
/// deck's entries re-resolved by slug), and OnlineLobbyViewModel (loading just the local player's
/// side of a 2-player online session — see LoadDeckIntoPlayer's own doc comment on why the opponent
/// side is never built this way).
/// </summary>
public static class DeckSessionBuilder
{
    public readonly record struct Entry(CardDto Card, DeckSection Section, int Quantity);

    public static (GameSession Session, Player Player) BuildSoloSession(IEnumerable<Entry> entries)
    {
        var session = new GameSession();
        var player = session.AddPlayer("You");
        LoadDeckIntoPlayer(player, entries);
        session.Shuffle(player, ZoneType.MainDeck);
        return (session, player);
    }

    /// <summary>
    /// Loads a resolved deck's cards into an already-existing player's Main/Material zones (no
    /// shuffle — the caller does that once loading is done, same as BuildSoloSession). Used directly
    /// (rather than through BuildSoloSession) by the online lobby, which needs to add both players to
    /// one shared GameSession itself — in a consistent host-then-guest order on both sides, so
    /// PlayerNumber means the same physical player on both ends — before loading just its own local
    /// player's deck; the opponent's Player object stays empty until their own PlayerState broadcast
    /// arrives, since only their own client is authoritative for what's actually in it.
    /// </summary>
    public static void LoadDeckIntoPlayer(Player player, IEnumerable<Entry> entries)
    {
        var main = new List<CardDto>();
        var material = new List<CardDto>();
        var sideboard = new List<CardDto>();

        foreach (var entry in entries)
        {
            // Champions start in the Material Deck alongside Regalia, same as Omnidex decklists —
            // materialize your starting champion onto the Field yourself once the game begins.
            var target = entry.Section switch
            {
                DeckSection.Sideboard => sideboard,
                DeckSection.Main => main,
                _ => material,
            };

            for (var i = 0; i < entry.Quantity; i++)
            {
                target.Add(entry.Card);
            }
        }

        player.Deck = new DeckArrangement(main, material, sideboard);
        ApplyArrangement(player);
    }

    /// <summary>
    /// Rebuilds the player's Main and Material zones from their <see cref="Player.Deck"/>'s
    /// current lists — clearing every other zone too (except the static Tokens catalog), since this
    /// is only ever called to set up a fresh game (initial load, or after sideboarding, right before
    /// StartNewGame). The sideboard itself never enters any zone.
    /// </summary>
    public static void ApplyArrangement(Player player)
    {
        var deck = player.Deck ?? throw new InvalidOperationException("This player has no deck arrangement to apply.");

        foreach (var zone in player.Zones.Values.Where(zone => zone.Type != ZoneType.Tokens))
        {
            zone.Cards.Clear();
        }

        // HomeOrder records each copy's place in the arrangement so GameSession.StartNewGame can
        // rebuild Material in that order later.
        var mainZone = player.GetZone(ZoneType.MainDeck);
        for (var i = 0; i < deck.Main.Count; i++)
        {
            mainZone.Cards.Add(new CardInstance(deck.Main[i], ZoneType.MainDeck, i));
        }

        var materialZone = player.GetZone(ZoneType.MaterialDeck);
        for (var i = 0; i < deck.Material.Count; i++)
        {
            materialZone.Cards.Add(new CardInstance(deck.Material[i], ZoneType.MaterialDeck, i));
        }
    }

    /// <summary>Resolves a saved deck's slugs back into full CardDto entries (each already cached
    /// from when the deck was saved — see DeckImportViewModel.SaveDeck) — the async step
    /// BuildSoloSession/LoadDeckIntoPlayer themselves don't need to know about.</summary>
    public static async Task<List<Entry>> ResolveEntriesAsync(SavedDeck deck, GrandArchiveApiClient apiClient)
    {
        var entries = new List<Entry>();
        foreach (var savedEntry in deck.Entries)
        {
            var card = await apiClient.GetCardBySlugAsync(savedEntry.Slug)
                ?? throw new InvalidOperationException($"Couldn't find \"{savedEntry.CardName}\" anymore — it may have been renamed.");
            entries.Add(new Entry(card, savedEntry.Section, savedEntry.Quantity));
        }

        return entries;
    }
}
