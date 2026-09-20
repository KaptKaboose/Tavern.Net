using Tavern.Net.GameData.Models;

namespace Tavern.Net.Game;

/// <summary>Setting up a new game: the reset, opening hand and the first-turn fast-forward.</summary>
public sealed partial class GameSession
{
    /// <summary>
    /// Resets for a fresh game using the same imported deck: every card — wherever it's wandered
    /// off to since (Field, Graveyard, a materialized Material card, etc.) — goes back to its
    /// original deck (<see cref="CardInstance.HomeZone"/>), Material restored in its original
    /// decklist order (<see cref="CardInstance.HomeOrder"/>) since that order can matter, Main
    /// reassembled the same way and then shuffled since its order never matters. Each player then
    /// draws their opening hand — that's as much a part of "the game has started" as the shuffle,
    /// so it belongs here rather than something every caller has to remember to do afterward.
    /// </summary>
    /// <returns>
    /// Cards glimpsed for the first player, if their base champion's effect triggers an opening
    /// glimpse instead of a normal draw — empty otherwise. This class has no reference to the
    /// Glimpse overlay's UI-side state, so it can pull the cards off the deck but can't show them;
    /// the caller (GameBoardViewModel) is responsible for staging them and opening the overlay.
    /// </returns>
    public IReadOnlyList<CardInstance> StartNewGame()
    {
        var glimpsedForFirstPlayer = (IReadOnlyList<CardInstance>)Array.Empty<CardInstance>();

        foreach (var player in Players)
        {
            var glimpsed = InitializePlayerForNewGame(player);
            if (player == Players[0])
            {
                glimpsedForFirstPlayer = glimpsed;
            }
        }

        SetPhase(TurnPhase.Materialization, Players[0]);
        _firstTurnTarget[Players[0]] = TurnPhase.Main;

        return glimpsedForFirstPlayer;
    }

    /// <summary>
    /// The online equivalent of <see cref="StartNewGame"/>, scoped to just <paramref name="player"/>
    /// — an online session's other Player is a mirror of the real opponent's own authoritative
    /// state (see GameBoardViewModel's ApplyOpponentStateAsync), so nothing here may touch it; their
    /// own client runs this same initialization on their own side and broadcasts the result. Doesn't
    /// set the shared CurrentPhase/ActivePlayer either — the caller (the online lobby, once both
    /// sides are ready) sets that once via ApplyRemoteGameState using the agreed first player, not
    /// per-player like solo's own Materialization kickoff.
    /// </summary>
    public IReadOnlyList<CardInstance> StartNewGameForPlayer(Player player)
        => InitializePlayerForNewGame(player);

    private IReadOnlyList<CardInstance> InitializePlayerForNewGame(Player player)
    {
        var glimpsedCards = (IReadOnlyList<CardInstance>)Array.Empty<CardInstance>();

        player.Stats.PlayLog.Clear();
        player.Stats.MajorEvents.Clear();
        _canCoalesceLastMajorEvent[player] = false;
        player.Stats.TurnCount = 0;
        player.Life = 15;
        player.Stats.CardsDrawnCount = 0;
        player.Stats.DamageDealtCount = 0;
        player.Stats.LifeRecoveredCount = 0;
        player.Stats.DeadTurnsCount = 0;
        player.Stats.PlayedCardThisTurn = false;
        player.Stats.CardsPlayedCount = 0;
        player.Stats.CardsLostToMemoryDecayCount = 0;
        player.Stats.ChampionLevelMilestones.Clear();
        _pendingFirstTurnFastForward.Add(player);

        // Tokens is excluded from both the sweep and the clear: it's a static, always-present
        // catalog (one CardInstance per token type, loaded once by GameBoardViewModel), not
        // deck content — sweeping it in would both lose it forever (nothing ever rebuilds it,
        // unlike MaterialDeck/MainDeck below) and reset it needlessly on every new game.
        var allCards = player.Zones.Values.Where(zone => zone.Type != ZoneType.Tokens).SelectMany(zone => zone.Cards).ToList();
        foreach (var zone in player.Zones.Values)
        {
            if (zone.Type == ZoneType.Tokens)
            {
                continue;
            }

            zone.Cards.Clear();
        }

        foreach (var card in allCards)
        {
            card.IsTapped = false;
            card.IsFlipped = false;
            card.FieldX = 0;
            card.FieldY = 0;
            card.ResetCounterAndStatuses();
        }

        var materialDeck = player.GetZone(ZoneType.MaterialDeck);
        foreach (var card in allCards.Where(c => c.HomeZone == ZoneType.MaterialDeck).OrderBy(c => c.HomeOrder))
        {
            materialDeck.Cards.Add(card);
        }

        // IsSessionGenerated cards (GenerateCard) are excluded here — they're extra copies a card
        // effect produced for this game only, not part of the original decklist, so a fresh game
        // shouldn't have them reappear.
        var mainDeck = player.GetZone(ZoneType.MainDeck);
        foreach (var card in allCards.Where(c => c.HomeZone == ZoneType.MainDeck && !c.IsSessionGenerated).OrderBy(c => c.HomeOrder))
        {
            mainDeck.Cards.Add(card);
        }

        Shuffle(player, ZoneType.MainDeck);

        // Play the base champion from the Material Deck to the Champion zone, since that's a required starting action
        var baseChampion = allCards.FirstOrDefault(c => c.Card.IsChampion && c.Card.Level == 0 && c.HomeZone == ZoneType.MaterialDeck);
        MoveCard(player, baseChampion!, ZoneType.MaterialDeck, ZoneType.Champion);

        // Draw the opening hand based on the base champion effect
        var baseChampionEffect = baseChampion?.Card.Effect?.ToLower();
        if (baseChampionEffect is not null)
        {
            if (baseChampionEffect.Contains("memory"))
            {
                player.SetStartsInMemory(true);
            }

            switch (baseChampionEffect)
            {
                case string e when e.Contains("draw seven"):
                    player.SetStartingHandSize(7);
                    break;
                case string e when e.Contains("draw six"):
                    player.SetStartingHandSize(6);
                    break;
            }

            // Check for glimpsing
            if (baseChampionEffect.Contains("glimpse"))
            {
                var glimpseCount = 0;
                switch (baseChampionEffect)
                {
                    case string e when e.Contains("glimpse 6"):
                        glimpseCount = 6;
                        break;
                    case string e when e.Contains("glimpse 7"):
                        glimpseCount = 7;
                        break;
                }

                if (glimpseCount > 0)
                {
                    var drawn = new List<CardInstance>();
                    for (var i = 0; i < glimpseCount; i++)
                    {
                        var card = GlimpseNextCard(player);
                        if (card is not null)
                        {
                            drawn.Add(card);
                        }
                    }

                    glimpsedCards = drawn;
                }
            }
        }

        // A normal opening hand, unless the base champion's effect triggered a glimpse instead —
        // covers "no glimpse keyword at all" and "champion has no effect text" the same way, rather
        // than the two falling out of the branching above differently and one of them (a null-
        // Effect champion, in particular) silently leaving Hand empty at game start.
        if (glimpsedCards.Count == 0)
        {
            DrawStartingHand(player);
        }

        RecordMajorEvent(player, "Game started.", insertAtStart: true);

        return glimpsedCards;
    }

    public void DrawStartingHand(Player player)
    {
        for (var i = 0; i < player.StartingHandSize; i++)
        {
            if (player.StartsInMemory)
            {
                DrawCard(player, ZoneType.MainDeck, ZoneType.Memory);
            }
            else
            {
                DrawCard(player);
            }
        }
    }
}
