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
        ZoneType.Champion,
    };

    // Set (per player) by InitializePlayerForNewGame, consumed by that player's very first
    // AdvancePhase call afterward, whenever it happens to come (immediately for whoever goes first;
    // after their own first End-of-turn handoff arrives for whoever goes second, in an online game).
    private readonly HashSet<Player> _pendingFirstTurnFastForward = new();

    // Which phase _pendingFirstTurnFastForward's consumption should jump straight to for a given
    // player — Main (skip Recollection+Draw) for whoever is actually the game's first mover, Draw
    // (skip only Recollection) for whoever's first turn arrives via a handoff from someone else's
    // turn. Deliberately NOT inferred from TurnCount at consumption time: TurnCount must stay 0
    // for a player's entire first turn (online or not) so it still displays as "Turn 1" rather than
    // "Turn 2" — see GameBoardViewModel.ApplyRemoteGameState, which is why this can no longer double
    // as the "have I gone before" signal the way it briefly did. Set explicitly by StartNewGame
    // (solo) and by the online lobby (via SetFirstTurnTarget) once the agreed first player is known;
    // removed the moment AdvancePhase consumes it.
    private readonly Dictionary<Player, TurnPhase> _firstTurnTarget = new();

    // True while the most recent MajorEvent for this player is a Life/Damage change that hasn't
    // been closed yet — by a tap, a flip, or any other Major event — and so can still absorb the
    // next same-direction Life/Damage change instead of starting a new log entry. See
    // RecordLifeOrDamageChange and BreakCoalescingStreak.
    private readonly Dictionary<Player, bool> _canCoalesceLastMajorEvent = new();

    private readonly Random _random;

    public List<Player> Players { get; } = new();

    public TurnPhase CurrentPhase { get; private set; } = TurnPhase.WakeUp;

    /// <summary>Whose turn it currently is — the one player (in an online game) allowed to advance
    /// the shared phase; irrelevant for solo, where it's always the only player. Defaults to the
    /// first player added. See AdvancePhase's End-of-turn handoff for where this changes.</summary>
    public Player? ActivePlayer { get; private set; }

    public GameSession(Random? random = null)
    {
        _random = random ?? new Random();
    }

    public Player AddPlayer(string name, int startingLife = 15)
    {
        var player = new Player(name, Players.Count, startingLife);
        Players.Add(player);
        ActivePlayer ??= player;
        return player;
    }

    /// <summary>Sets the current phase directly, without running WakeUp/Recollect/Draw's entry
    /// actions the way SetPhase does — used only by GameSessionSerializer.RestoreAsync, where those
    /// actions already happened and are already reflected in the restored zones/stats.</summary>
    internal void SetPhaseForRestore(TurnPhase phase) => CurrentPhase = phase;

    /// <summary>Applies a shared phase/active-player update received from the network — used only
    /// by the passive side of an online game, mirroring whatever the active side already computed
    /// and ran SetPhase's own side effects for locally. No side effects re-run here: the active
    /// side's own broadcast PlayerState already reflects their WakeUp/Recollect/Draw outcome.</summary>
    internal void ApplyRemoteGameState(TurnPhase phase, Player activePlayer)
    {
        CurrentPhase = phase;
        ActivePlayer = activePlayer;
    }

    /// <summary>Whether <paramref name="player"/> hasn't taken their very first turn yet — true from
    /// InitializePlayerForNewGame until their own first AdvancePhase call consumes it. Used online
    /// (GameBoardViewModel.ApplyRemoteGameState) to tell whether becoming newly active is genuinely
    /// a player's first-ever turn (land on Materialization, don't bump TurnCount) or an ordinary
    /// later one (land on WakeUp for real, with its untap effect, and bump TurnCount as usual).</summary>
    public bool IsAwaitingFirstTurn(Player player) => _pendingFirstTurnFastForward.Contains(player);

    /// <summary>Registers which phase <paramref name="player"/>'s own first AdvancePhase call should
    /// jump straight to — see _firstTurnTarget's own comment. Used by the online lobby once the
    /// agreed first player is known; StartNewGame (solo) sets this for itself directly.</summary>
    public void SetFirstTurnTarget(Player player, TurnPhase target) => _firstTurnTarget[player] = target;

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

        var mainDeck = player.GetZone(ZoneType.MainDeck);
        foreach (var card in allCards.Where(c => c.HomeZone == ZoneType.MainDeck).OrderBy(c => c.HomeOrder))
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
            else
            {
                DrawStartingHand(player);
            }
        }

        RecordMajorEvent(player, "Game started.", insertAtStart: true);

        return glimpsedCards;
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
        // Tokens follow entirely different rules from every other card — see MoveToken.
        if (card.Card.IsToken)
        {
            MoveToken(player, card, from, to, fieldX, fieldY);
            return;
        }

        // Silently ignore a move across a one-way zone barrier (e.g. MaterialDeck -> Hand) —
        // rather than throw, since a drag-drop that lands on a blocked zone shouldn't crash.
        if (ZoneBarriers.Contains((card.HomeZone, to)))
        {
            return;
        }

        // Only Champion cards may enter the Champion zone.
        if (to == ZoneType.Champion && !card.Card.IsChampion)
        {
            return;
        }

        // Capture the Champion zone's top BEFORE removing the card below — if it's coming from
        // Champion itself, that removal would otherwise corrupt an after-the-fact "old top" read.
        var championZoneAffected = from == ZoneType.Champion || to == ZoneType.Champion;
        var championZone = player.GetZone(ZoneType.Champion);
        var oldTop = championZoneAffected ? championZone.Cards.FirstOrDefault() : null;

        // Snapshot the old top's counter/statuses now, before the general reset below can
        // clobber them — that happens when oldTop *is* the card being moved out to a non-Field/
        // Champion zone (e.g. it died to the Graveyard), which is exactly the case the transfer
        // further down needs these original values for.
        var oldTopCounter = oldTop?.Counter ?? 0;
        var oldTopIsEphemeral = oldTop?.IsEphemeral ?? false;
        var oldTopIsIgnited = oldTop?.IsIgnited ?? false;
        var oldTopIsImbued = oldTop?.IsImbued ?? false;
        var oldTopIsRanged = oldTop?.IsRanged ?? false;
        var oldTopIsRooted = oldTop?.IsRooted ?? false;
        var oldTopIsWarded = oldTop?.IsWarded ?? false;

        var source = player.GetZone(from);
        if (!source.Cards.Remove(card))
        {
            throw new InvalidOperationException($"{card.Card.Name} was not found in {from}.");
        }

        card.IsTapped = false;
        card.IsFlipped = false;

        // Counter/statuses are only meaningful in play (Field or Champion) — clear them the
        // instant a card goes anywhere else, same reasoning as IsTapped/IsFlipped resetting above.
        if (to != ZoneType.Field && to != ZoneType.Champion)
        {
            card.ResetCounterAndStatuses();
        }

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

        // Leveling up/down: when the Champion zone's top card actually changes (not merely
        // appears from empty or disappears to empty — see the null checks), life shifts by the
        // difference between the old and new top's Life stat.
        if (championZoneAffected)
        {
            var newTop = championZone.Cards.FirstOrDefault();
            if (newTop is not null && newTop.Card.Level is double newTopLevel)
            {
                RecordChampionLevelReached(player, newTopLevel);
            }

            if (oldTop is not null && newTop is not null && oldTop != newTop)
            {
                var delta = (int)((newTop.Card.Life ?? 0) - (oldTop.Card.Life ?? 0));
                if (delta != 0)
                {
                    AdjustLife(player, delta, trackRecovery: false);
                }

                // The new top inherits the old top's counter/statuses — it's the same physical
                // champion leveling up or down, not a different card — then the old one is
                // cleared, since whatever's left of it (buried in the stack, or having left
                // Champion entirely) is no longer the active face.
                newTop.Counter = oldTopCounter;
                newTop.IsEphemeral = oldTopIsEphemeral;
                newTop.IsIgnited = oldTopIsIgnited;
                newTop.IsImbued = oldTopIsImbued;
                newTop.IsRanged = oldTopIsRanged;
                newTop.IsRooted = oldTopIsRooted;
                newTop.IsWarded = oldTopIsWarded;
                oldTop.ResetCounterAndStatuses();

                // The old top's tapped state is specific to it having been the active face —
                // once it's no longer on top (buried, or having left Champion entirely), that
                // no longer applies. Unlike counter/statuses this isn't transferred to newTop,
                // just cleared.
                oldTop.IsTapped = false;
            }
        }

        player.Stats.Log($"Moved {card.Card.Name} from {from} to {to}.");

        // A manual drag out of Main is the same act as DrawCard — just player-directed (picking
        // Memory instead of Hand, or a specific card via Glimpse) rather than always the top card
        // to Hand — so it counts toward CardsDrawnCount the same way.
        if (from == ZoneType.MainDeck && (to == ZoneType.Hand || to == ZoneType.Memory))
        {
            player.Stats.CardsDrawnCount++;
        }

        switch (to)
        {
            case ZoneType.Field:
                RecordMajorEvent(player, $"Played {card.Card.Name} to the Field.");
                if (from == ZoneType.Hand)
                {
                    player.Stats.PlayedCardThisTurn = true;
                    player.Stats.CardsPlayedCount++;
                }

                break;
            case ZoneType.Graveyard:
                RecordMajorEvent(player, $"{card.Card.Name} went to the Graveyard.");
                break;
            case ZoneType.Banishment:
                if (from == ZoneType.Memory)
                {
                    player.Stats.CardsLostToMemoryDecayCount++;
                }

                RecordMajorEvent(player, $"Banished {card.Card.Name}.");
                break;
            case ZoneType.Champion:
                RecordMajorEvent(player, $"{card.Card.Name} materialized as Champion.");
                break;
        }
    }

    /// <summary>
    /// Tokens aren't real deck cards — the Tokens zone holds a permanent, never-moving catalog
    /// (one CardInstance per token type, loaded once by GameBoardViewModel). Dragging a catalog
    /// entry onto the Field spawns a brand-new copy; the catalog entry itself never leaves the
    /// Tokens zone. Dragging a spawned copy back onto Tokens discards it — the only way to get rid
    /// of one, since it can't go to Hand/Graveyard/etc. like a real card. Any other source/
    /// destination pairing for a token is a silent no-op, same as a blocked ZoneBarriers hit.
    /// </summary>
    private void MoveToken(Player player, CardInstance card, ZoneType from, ZoneType to, double? fieldX, double? fieldY)
    {
        if (from == ZoneType.Tokens && to == ZoneType.Field)
        {
            var spawned = new CardInstance(card.Card, ZoneType.Field)
            {
                FieldX = fieldX ?? 0,
                FieldY = fieldY ?? 0,
            };
            player.GetZone(ZoneType.Field).Cards.Add(spawned);
            player.Stats.Log($"Summoned {card.Card.Name} token.");
            RecordMajorEvent(player, $"Summoned {card.Card.Name} token.");
            return;
        }

        if (from == ZoneType.Field && to == ZoneType.Tokens)
        {
            if (player.GetZone(ZoneType.Field).Cards.Remove(card))
            {
                player.Stats.Log($"Discarded {card.Card.Name} token.");
                RecordMajorEvent(player, $"Discarded {card.Card.Name} token.");
            }
        }
    }

    /// <summary>Repositions a card already on the Field, without any zone change or log entry — used while dragging within the Field.</summary>
    public void RepositionOnField(CardInstance card, double x, double y)
    {
        card.FieldX = x;
        card.FieldY = y;
    }

    /// <param name="trackRecovery">Whether a positive delta counts toward LifeRecoveredCount —
    /// true for the Life panel's own +/- buttons, false for a Champion level-up/down's automatic
    /// life shift, which isn't the player "recovering" anything.</param>
    public void AdjustLife(Player player, int delta, bool trackRecovery = true)
    {
        var newLife = Math.Max(0, player.Life + delta);
        var actualDelta = newLife - player.Life;
        if (actualDelta == 0)
        {
            // Already at 0 and dropping further (or an already-clamped attempt repeats) — nothing
            // actually changed, so skip the log/MajorEvent rather than recording a "+0" no-op.
            return;
        }

        player.Life = newLife;
        if (trackRecovery && actualDelta > 0)
        {
            player.Stats.LifeRecoveredCount += actualDelta;
        }

        player.Stats.Log($"Life changed by {actualDelta:+0;-0} to {player.Life}.");
        RecordLifeOrDamageChange(player, MajorEventKind.LifeChanged, "Life", actualDelta);
    }

    /// <summary>Adjusts the running damage-dealt tally (a manual count, since goldfishing has no
    /// opponent board to compute it from) — clamped at 0 so an over-eager decrease can't go
    /// negative.</summary>
    public void AdjustDamageDealt(Player player, int delta)
    {
        player.Stats.DamageDealtCount = Math.Max(0, player.Stats.DamageDealtCount + delta);
        player.Stats.Log($"Damage dealt changed by {delta:+0;-0} to {player.Stats.DamageDealtCount}.");
        RecordLifeOrDamageChange(player, MajorEventKind.DamageDealt, "Damage dealt", delta);
    }

    public void NextTurn(Player player)
    {
        if (!player.Stats.PlayedCardThisTurn)
        {
            player.Stats.DeadTurnsCount++;
        }

        player.Stats.PlayedCardThisTurn = false;

        player.Stats.TurnCount++;
        player.Stats.Log("New turn.");
        RecordMajorEvent(player, $"Turn {player.Stats.TurnCount + 1} started.");
    }

    /// <summary>Toggles a card's tapped state — meaningful in play (Field/Champion), but callable
    /// on any card; GameBoardViewModel gates which UI gestures are allowed to invoke it for which
    /// zones. Breaks any open Life/Damage coalescing streak: tapping is how most cards attack, so
    /// two damage-dealt bursts separated by a different card tapping are genuinely separate
    /// instances, not one running total (see RecordLifeOrDamageChange).</summary>
    public void ToggleTapped(Player player, CardInstance card)
    {
        card.IsTapped = !card.IsTapped;
        player.Stats.Log($"{(card.IsTapped ? "Tapped" : "Untapped")} {card.Card.Name}.");
        BreakCoalescingStreak(player);
    }

    /// <summary>Flips a card and resets its counter/statuses (same as leaving Field/Champion —
    /// see CardInstance.ResetCounterAndStatuses) and breaks any open Life/Damage coalescing streak,
    /// same reasoning as ToggleTapped.</summary>
    public void FlipCard(Player player, CardInstance card)
    {
        card.IsFlipped = !card.IsFlipped;
        card.ResetCounterAndStatuses();
        player.Stats.Log($"Flipped {card.Card.Name}.");
        BreakCoalescingStreak(player);
    }

    public void AdjustCounter(Player player, CardInstance card, int delta)
    {
        card.Counter += delta;
        player.Stats.Log($"{card.Card.Name}'s counter changed by {delta:+0;-0} to {card.Counter}.");
    }

    public void ToggleStatus(Player player, CardInstance card, string statusName)
    {
        card.ToggleStatus(statusName);
        player.Stats.Log($"Toggled {statusName} on {card.Card.Name}.");
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
    /// behavior belongs to the phase being entered. Advancing past End hands the turn to the next
    /// player in <see cref="Players"/> (wrapping around) and starts their turn at WakeUp — for solo
    /// (one player), that next player is always the same one, so this is a no-op change from the
    /// old goldfish-only behavior; for an online 2-player game it correctly alternates.
    /// </summary>
    public void AdvancePhase(Player player)
    {
        // Fires exactly once per player, on their first AdvancePhase call after being initialized
        // for a new game — see _pendingFirstTurnFastForward's own comment. Which phase it jumps to
        // is looked up from _firstTurnTarget, set explicitly back when this player was landed on
        // Materialization in the first place (StartNewGame for solo, the online lobby via
        // SetFirstTurnTarget for online) rather than inferred here — TurnCount can't be used for
        // that anymore now that it's kept at 0 for a player's *entire* first turn, online included.
        if (_pendingFirstTurnFastForward.Remove(player))
        {
            var target = _firstTurnTarget.Remove(player, out var explicitTarget) ? explicitTarget : TurnPhase.Main;
            SetPhase(target, player);
            return;
        }

        var nextIndex = Array.IndexOf(PhaseOrder, CurrentPhase) + 1;
        if (nextIndex >= PhaseOrder.Length)
        {
            var nextPlayer = Players[(Players.IndexOf(player) + 1) % Players.Count];
            NextTurn(nextPlayer);
            ActivePlayer = nextPlayer;
            SetPhase(PhaseOrder[0], nextPlayer);
            return;
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

        foreach (var card in player.GetZone(ZoneType.Champion).Cards)
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

    /// <summary>Records the turn (1-based) a Champion level was first reached — a no-op if that
    /// level has already been recorded, since only the first time matters for a goldfish run's
    /// "how fast did I get there" purposes. Called for every Champion-zone top change, including
    /// the base champion's initial materialization (level 0) at game start.</summary>
    private static void RecordChampionLevelReached(Player player, double level)
    {
        if (player.Stats.ChampionLevelMilestones.Any(m => m.Level == level))
        {
            return;
        }

        player.Stats.ChampionLevelMilestones.Add(new ChampionLevelMilestone(level, player.Stats.TurnCount + 1));
    }

    /// <summary>
    /// Records a non-coalescing Major event: always a new MajorEvents entry, and always closes any
    /// open Life/Damage coalescing streak (see RecordLifeOrDamageChange) — even a card just being
    /// played is a distinct enough moment that a life change before and after it shouldn't merge.
    /// <paramref name="insertAtStart"/> is only for StartNewGame's own "Game started." entry — the
    /// snapshot still has to be taken after the base champion materializes and the opening hand is
    /// drawn (otherwise it'd show an empty board), but the entry itself needs to read first in the
    /// log, ahead of the materialization event that setup incidentally records along the way.
    /// </summary>
    private void RecordMajorEvent(Player player, string description, bool insertAtStart = false)
    {
        var majorEvent = new MajorEvent
        {
            Description = description,
            Kind = MajorEventKind.Other,
            Turn = player.Stats.TurnCount,
            Snapshot = TakeSnapshot(player),
        };

        if (insertAtStart)
        {
            player.Stats.MajorEvents.Insert(0, majorEvent);
        }
        else
        {
            player.Stats.MajorEvents.Add(majorEvent);
        }

        _canCoalesceLastMajorEvent[player] = false;
    }

    /// <summary>
    /// Records a Life or Damage change — extending the previous MajorEvents entry in place (same
    /// kind, same direction, streak still open) rather than adding a new one, so "-1, -1, -1" reads
    /// as a single "Life decreased by 3" instead of three separate entries. A direction change, a
    /// tap, a flip, or any other Major event closes the streak (see BreakCoalescingStreak and
    /// RecordMajorEvent), so the next change always starts a fresh entry.
    /// </summary>
    private void RecordLifeOrDamageChange(Player player, MajorEventKind kind, string label, int delta)
    {
        if (_canCoalesceLastMajorEvent.GetValueOrDefault(player)
            && player.Stats.MajorEvents.Count > 0
            && player.Stats.MajorEvents[^1].Kind == kind
            && Math.Sign(player.Stats.MajorEvents[^1].NetDelta) == Math.Sign(delta))
        {
            var last = player.Stats.MajorEvents[^1];
            last.NetDelta += delta;
            last.Description = FormatNetDelta(label, last.NetDelta);
            last.Snapshot = TakeSnapshot(player);
            return;
        }

        player.Stats.MajorEvents.Add(new MajorEvent
        {
            Description = FormatNetDelta(label, delta),
            Kind = kind,
            NetDelta = delta,
            Turn = player.Stats.TurnCount,
            Snapshot = TakeSnapshot(player),
        });

        _canCoalesceLastMajorEvent[player] = true;
    }

    private static string FormatNetDelta(string label, int netDelta)
        => netDelta >= 0 ? $"{label} increased by {netDelta}." : $"{label} decreased by {-netDelta}.";

    /// <summary>Closes any open Life/Damage coalescing streak without adding a MajorEvents entry
    /// of its own — used by ToggleTapped/FlipCard, which are significant enough to separate combat
    /// instances but not significant enough to show up in the log themselves.</summary>
    private void BreakCoalescingStreak(Player player) => _canCoalesceLastMajorEvent[player] = false;

    /// <summary>
    /// A read-only copy of everything about the player's board worth showing in a historical Play
    /// Log view — every zone except Tokens (a static catalog, not game state; see StartNewGame's
    /// own reasoning for excluding it elsewhere).
    /// </summary>
    private GameSnapshot TakeSnapshot(Player player)
    {
        var cards = player.Zones.Values
            .Where(zone => zone.Type != ZoneType.Tokens)
            .SelectMany(zone => zone.Cards.Select(card => CardSnapshot.From(card, zone.Type)))
            .ToList();

        return new GameSnapshot(player.Life, player.Stats.DamageDealtCount, player.Stats.TurnCount, CurrentPhase, cards);
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
