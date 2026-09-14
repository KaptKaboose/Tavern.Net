namespace Tavern.Net.Game;

/// <summary>
/// Orchestrates a play session. Modeled around a list of players (rather
/// than a hardcoded pair) so a future networked mode can add a second,
/// remotely-controlled player without reshaping this class.
/// </summary>
public sealed class GameSession
{
    public const int DefaultOpeningHandSize = 7;

    private static readonly TurnPhase[] PhaseOrder =
    {
        TurnPhase.WakeUp,
        TurnPhase.Materialization,
        TurnPhase.Recollection,
        TurnPhase.Draw,
        TurnPhase.Main,
        TurnPhase.End,
    };

    private static readonly HashSet<(ZoneType From, ZoneType To)> ZoneBarriers = new()
    {
        (ZoneType.MaterialDeck, ZoneType.MainDeck),
        (ZoneType.MaterialDeck, ZoneType.Graveyard),
        (ZoneType.MaterialDeck, ZoneType.Hand),
        (ZoneType.MaterialDeck, ZoneType.Memory),
        (ZoneType.MainDeck, ZoneType.MaterialDeck),
    };

    private readonly Random _random;

    public List<Player> Players { get; } = new();

    public TurnPhase CurrentPhase { get; private set; } = TurnPhase.WakeUp;

    public GameSession(Random? random = null)
    {
        _random = random ?? new Random();
    }

    public Player AddPlayer(string name, int startingLife = 20)
    {
        var player = new Player(name, Players.Count, startingLife);
        Players.Add(player);
        return player;
    }

    public void Shuffle(Player player, ZoneType zoneType = ZoneType.MainDeck)
    {
        var zone = player.GetZone(zoneType);
        var shuffled = zone.Cards.OrderBy(_ => _random.Next()).ToList();
        zone.Cards.Clear();
        foreach (var card in shuffled)
        {
            zone.Cards.Add(card);
        }

        player.Stats.Log($"Shuffled {zoneType}.");
    }

    /// <summary>
    /// Resets for a fresh game using the same imported deck: every card — wherever it's wandered
    /// off to since (Field, Graveyard, a materialized Material card, etc.) — goes back to its
    /// original deck (<see cref="CardInstance.HomeZone"/>), Material restored in its original
    /// decklist order (<see cref="CardInstance.HomeOrder"/>) since that order can matter, Main
    /// reassembled the same way and then shuffled since its order never matters. Each player then
    /// draws their opening hand — that's as much a part of "the game has started" as the shuffle,
    /// so it belongs here rather than something every caller has to remember to do afterward.
    /// </summary>
    public void StartNewGame()
    {
        foreach (var player in Players)
        {
            player.Stats.PlayLog.Clear();
            player.Stats.TurnCount = 0;
            player.Life = 20;

            var allCards = player.Zones.Values.SelectMany(zone => zone.Cards).ToList();
            foreach (var zone in player.Zones.Values)
            {
                zone.Cards.Clear();
            }

            foreach (var card in allCards)
            {
                card.IsTapped = false;
                card.FieldX = 0;
                card.FieldY = 0;
            }

            var materialDeck = player.GetZone(ZoneType.MaterialDeck);
            foreach (var card in allCards.Where(c => c.HomeZone == ZoneType.MaterialDeck).OrderBy(c => c.HomeOrder))
            {
                materialDeck.Cards.Add(card);
            }

            var mainDeck = player.GetZone(ZoneType.MainDeck);
            foreach (var card in allCards.Where(c => c.HomeZone == ZoneType.MainDeck).OrderBy(c => c.HomeOrder))
            {
                mainDeck.Cards.Add(card);
            }

            Shuffle(player, ZoneType.MainDeck);

            // Play the base champion from the Material Deck to the Field, since that's a required starting action
            var baseChampion = allCards.FirstOrDefault(c => c.Card.IsChampion && c.Card.Level == 0 && c.HomeZone == ZoneType.MaterialDeck);
            MoveCard(player, baseChampion!, ZoneType.MaterialDeck, ZoneType.Field, 0, 0);

            // Draw the opening hand based on the base champion effect
            var baseChampionEffect = baseChampion?.Card.Effect?.ToLower();
            var openingHandSize = DefaultOpeningHandSize;
            if (baseChampionEffect is not null)
            {
                switch (baseChampionEffect)
                {
                    case string e when e.Contains("draw seven"):
                        openingHandSize = 7;
                        break;
                    case string e when e.Contains("draw six"):
                        openingHandSize = 6;
                        break;
                }
            }

            for (var i = 0; i < openingHandSize; i++)
            {
                DrawCard(player);
            }
        }

        SetPhase(TurnPhase.Materialization, Players[0]);
    }

    /// <summary>Moves the top card of <paramref name="from"/> to <paramref name="to"/>. Returns false if the source zone was empty.</summary>
    public bool DrawCard(Player player, ZoneType from = ZoneType.MainDeck, ZoneType to = ZoneType.Hand)
    {
        var source = player.GetZone(from);
        if (source.Cards.Count == 0)
        {
            player.Stats.Log($"Tried to draw from {from} but it was empty.");
            return false;
        }

        var card = source.Cards[0];
        source.Cards.RemoveAt(0);
        player.GetZone(to).Cards.Add(card);

        if (to == ZoneType.Hand)
        {
            player.Stats.CardsDrawnCount++;
        }

        player.Stats.Log($"Drew {card.Card.Name} from {from} to {to}.");
        return true;
    }

    public void MoveCard(Player player, CardInstance card, ZoneType from, ZoneType to, double? fieldX = null, double? fieldY = null)
    {
        // Silently ignore a move across a one-way zone barrier (e.g. MaterialDeck -> Hand) —
        // rather than throw, since a drag-drop that lands on a blocked zone shouldn't crash.
        if (ZoneBarriers.Contains((card.HomeZone, to)))
        {
            return;
        }

        var source = player.GetZone(from);
        if (!source.Cards.Remove(card))
        {
            throw new InvalidOperationException($"{card.Card.Name} was not found in {from}.");
        }

        card.IsTapped = false;
        player.GetZone(to).Cards.Add(card);

        if (to == ZoneType.Field && fieldX is not null && fieldY is not null)
        {
            card.FieldX = fieldX.Value;
            card.FieldY = fieldY.Value;
        }

        player.Stats.Log($"Moved {card.Card.Name} from {from} to {to}.");
    }

    /// <summary>Repositions a card already on the Field, without any zone change or log entry — used while dragging within the Field.</summary>
    public void RepositionOnField(CardInstance card, double x, double y)
    {
        card.FieldX = x;
        card.FieldY = y;
    }

    public void AdjustLife(Player player, int delta)
    {
        player.Life += delta;
        player.Stats.RecordLife(player.Life);
        player.Stats.Log($"Life changed by {delta:+0;-0} to {player.Life}.");
    }

    public void NextTurn(Player player)
    {
        player.Stats.TurnCount++;
        player.Stats.Log("New turn.");
    }

    /// <summary>
    /// Advances to the next phase of <paramref name="player"/>'s turn, running whichever automatic
    /// behavior belongs to the phase being entered. Advancing past End starts the next turn — in
    /// goldfish mode that's just <paramref name="player"/> going again; multiplayer (not implemented
    /// yet) will need this to hand the turn to whichever player is up next instead.
    /// </summary>
    public void AdvancePhase(Player player)
    {
        // Turn 1 fast-forward, but only the very first advance after StartNewGame (still sitting
        // in the Materialization it sets) — guarding on TurnCount == 0 alone re-triggers this on
        // every later call too, since nothing in this branch ever increments it, which trapped the
        // phase on Main/Draw forever instead of ever reaching End.
        if (player.Stats.TurnCount == 0 && CurrentPhase == TurnPhase.Materialization)
        {
            if (player.PlayerNumber == 0)
            {
                SetPhase(TurnPhase.Main, player);
                return;
            }

            SetPhase(TurnPhase.Draw, player);
            return;
        }

        var nextIndex = Array.IndexOf(PhaseOrder, CurrentPhase) + 1;
        if (nextIndex >= PhaseOrder.Length)
        {
            NextTurn(player);
            nextIndex = 0;
        }

        SetPhase(PhaseOrder[nextIndex], player);
    }

    /// <summary>Untaps every card on the Field. Runs automatically at the start of the Wake Up phase.</summary>
    public void WakeUp(Player player)
    {
        foreach (var card in player.GetZone(ZoneType.Field).Cards)
        {
            card.IsTapped = false;
        }

        player.Stats.Log("Wake Up: untapped the Field.");
    }

    /// <summary>
    /// Returns every card in Memory to Hand. Runs automatically at the start of the Recollection
    /// phase, but is also its own public method so a future card effect can call it independently
    /// of the phase it happens to be resolved in.
    /// </summary>
    public void Recollect(Player player)
    {
        var memory = player.GetZone(ZoneType.Memory);
        foreach (var card in memory.Cards.ToList())
        {
            MoveCard(player, card, ZoneType.Memory, ZoneType.Hand);
        }
    }

    void SetPhase(TurnPhase phase, Player player)
    {
        CurrentPhase = phase;

        switch (CurrentPhase)
        {
            case TurnPhase.WakeUp:
                WakeUp(player);
                break;
            case TurnPhase.Recollection:
                Recollect(player);
                break;
            case TurnPhase.Draw:
                DrawCard(player);
                break;
        }
    }
}
