namespace Tavern.Net.Game;

/// <summary>
/// Orchestrates a play session. Modeled around a list of players (rather
/// than a hardcoded pair) so a future networked mode can add a second,
/// remotely-controlled player without reshaping this class.
/// </summary>
public sealed class GameSession
{
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

    // Stack zones: a new arrival goes on top (index 0), matching how a physical pile works and
    // how StackZoneView's peek fans them out. Field/Hand/Memory/MaterialDeck keep append order.
    private static readonly HashSet<ZoneType> StackZones = new()
    {
        ZoneType.Banishment,
        ZoneType.MainDeck,
        ZoneType.Graveyard,
    };

    // Set (per player) by StartNewGame, consumed by that player's very first AdvancePhase call
    // afterward. Deliberately NOT inferred from TurnCount == 0 — now that TurnCount also defaults
    // to 0 for a plain session that never called StartNewGame (so the very first-ever game, which
    // goes through DeckImportViewModel.StartGame instead, still shows "Turn 1" and gets the same
    // fast-forward), TurnCount == 0 no longer uniquely means "StartNewGame just ran" — it's equally
    // true partway through any ordinary player's first turn, which made the fast-forward re-fire
    // there too instead of just once.
    private readonly HashSet<Player> _pendingFirstTurnFastForward = new();

    private readonly Random _random;

    public List<Player> Players { get; } = new();

    public TurnPhase CurrentPhase { get; private set; } = TurnPhase.WakeUp;

    public GameSession(Random? random = null)
    {
        _random = random ?? new Random();
    }

    public Player AddPlayer(string name, int startingLife = 15)
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
            player.Stats.PlayLog.Clear();
            player.Stats.TurnCount = 0;
            player.Life = 15;
            _pendingFirstTurnFastForward.Add(player);

            var allCards = player.Zones.Values.SelectMany(zone => zone.Cards).ToList();
            foreach (var zone in player.Zones.Values)
            {
                zone.Cards.Clear();
            }

            foreach (var card in allCards)
            {
                card.IsTapped = false;
                card.IsFlipped = false;
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
                        var glimpsedCards = new List<CardInstance>();
                        for (var i = 0; i < glimpseCount; i++)
                        {
                            var card = GlimpseNextCard(player);
                            if (card is not null)
                            {
                                glimpsedCards.Add(card);
                            }
                        }

                        if (player == Players[0])
                        {
                            glimpsedForFirstPlayer = glimpsedCards;
                        }
                    }
                }
                else
                {
                    DrawStartingHand(player);
                }
            }
        }

        SetPhase(TurnPhase.Materialization, Players[0]);

        return glimpsedForFirstPlayer;
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
        card.IsFlipped = false;
        var destination = player.GetZone(to);
        if (StackZones.Contains(to))
        {
            destination.Cards.Insert(0, card);
        }
        else
        {
            destination.Cards.Add(card);
        }

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

    /// <summary>
    /// Advances to the next phase of <paramref name="player"/>'s turn, running whichever automatic
    /// behavior belongs to the phase being entered. Advancing past End starts the next turn — in
    /// goldfish mode that's just <paramref name="player"/> going again; multiplayer (not implemented
    /// yet) will need this to hand the turn to whichever player is up next instead.
    /// </summary>
    public void AdvancePhase(Player player)
    {
        // Fires exactly once per StartNewGame, on that player's first AdvancePhase call afterward
        // — see _pendingFirstTurnFastForward's own comment for why this can't just be inferred
        // from TurnCount == 0 anymore.
        if (_pendingFirstTurnFastForward.Remove(player))
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

    /// <summary>
    /// Banishes up to <paramref name="count"/> random cards from Memory — fewer if Memory doesn't
    /// have that many.
    /// </summary>
    public void Banish(Player player, int count)
    {
        var memory = player.GetZone(ZoneType.Memory);
        var chosen = memory.Cards.OrderBy(_ => _random.Next()).Take(count).ToList();
        foreach (var card in chosen)
        {
            MoveCard(player, card, ZoneType.Memory, ZoneType.Banishment);
        }
    }

    /// <summary>
    /// Pulls the next card off the top of Main for glimpsing. The card leaves Main entirely —
    /// not tracked in any zone — until <see cref="FinishGlimpse"/> puts every glimpsed card back.
    /// Returns null if Main is empty.
    /// </summary>
    public CardInstance? GlimpseNextCard(Player player)
    {
        var deck = player.GetZone(ZoneType.MainDeck);
        if (deck.Cards.Count == 0)
        {
            return null;
        }

        var card = deck.Cards[0];
        deck.Cards.RemoveAt(0);
        return card;
    }

    /// <summary>
    /// Reinserts every glimpsed card into Main once all of them have been sorted into these two
    /// piles. <paramref name="top"/>[0] ends up on top of the deck (lowest index), the rest follow
    /// in order below it; <paramref name="bottom"/> is appended in order, so its last entry ends up
    /// at the very bottom of the deck.
    /// </summary>
    public void FinishGlimpse(Player player, IReadOnlyList<CardInstance> top, IReadOnlyList<CardInstance> bottom)
    {
        var deck = player.GetZone(ZoneType.MainDeck);
        for (var i = 0; i < top.Count; i++)
        {
            deck.Cards.Insert(i, top[i]);
        }

        foreach (var card in bottom)
        {
            deck.Cards.Add(card);
        }

        player.Stats.Log($"Glimpsed {top.Count + bottom.Count} card(s): {top.Count} to the top, {bottom.Count} to the bottom.");
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
