using Tavern.Net.GameData.Models;

namespace Tavern.Net.Game;

/// <summary>Moving cards between zones (the movement rules), drawing, tapping, flipping, counters and statuses.</summary>
public sealed partial class GameSession
{
    private static readonly HashSet<(ZoneType From, ZoneType To)> ZoneBarriers = new()
    {
        (ZoneType.MaterialDeck, ZoneType.MainDeck),
        (ZoneType.MaterialDeck, ZoneType.Graveyard),
        (ZoneType.MaterialDeck, ZoneType.Hand),
        (ZoneType.MaterialDeck, ZoneType.Memory),
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

    /// <summary>
    /// Whether <paramref name="card"/> may go from <paramref name="from"/> to <paramref name="to"/> —
    /// the one rulebook MoveCard enforces (silently, on a rejected move) and the board's drag-and-drop
    /// consults up front, so a zone that would refuse the drop isn't offered as a target. Same-zone
    /// "moves" only mean something on the Field (repositioning).
    /// </summary>
    public bool CanMove(CardInstance card, ZoneType from, ZoneType to)
    {
        if (from == to)
        {
            return to == ZoneType.Field;
        }

        if (card.Card.IsToken)
        {
            return (from == ZoneType.Tokens && to == ZoneType.Field) || (from == ZoneType.Field && to == ZoneType.Tokens);
        }

        return CanMove(card, to);
    }

    // The destination-only half, for ordinary (non-token) cards: one-way zone barriers (e.g.
    // MaterialDeck -> Hand), only Champion cards may enter the Champion zone, and nothing but a
    // token may ever enter the Tokens zone (tokens are handled separately in CanMove above).
    private static bool CanMove(CardInstance card, ZoneType to) =>
        !ZoneBarriers.Contains((card.HomeZone, to))
        && !(to == ZoneType.Champion && !card.Card.IsChampion)
        && to != ZoneType.Tokens;

    /// <param name="toBottom">For a stack zone (Insert(0, ...) by default, i.e. the top), append to
    /// the end instead — the Zoom overlay's "Bottom of Main" action.</param>
    public void MoveCard(Player player, CardInstance card, ZoneType from, ZoneType to, double? fieldX = null, double? fieldY = null, bool toBottom = false)
    {
        // Tokens follow entirely different rules from every other card — see MoveToken.
        if (card.Card.IsToken)
        {
            MoveToken(player, card, from, to, fieldX, fieldY);
            return;
        }

        // Silently ignore an illegal destination (a one-way zone barrier, or a non-champion into
        // Champion — see CanMove) rather than throw, since a drag-drop that lands on a blocked zone
        // shouldn't crash.
        if (!CanMove(card, to))
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
        if (StackZones.Contains(to) && !toBottom)
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
                RecordMajorEvent(player, $"Played {card.Card.Name} to the Field.", cardName: card.Card.Name, cardZone: ZoneType.Field);
                if (from == ZoneType.Hand)
                {
                    player.Stats.PlayedCardThisTurn = true;
                    player.Stats.CardsPlayedCount++;
                }

                break;
            case ZoneType.Graveyard:
                RecordMajorEvent(player, $"{card.Card.Name} went to the Graveyard.", cardName: card.Card.Name, cardZone: ZoneType.Graveyard);
                break;
            case ZoneType.Banishment:
                if (from == ZoneType.Memory)
                {
                    player.Stats.CardsLostToMemoryDecayCount++;
                }

                RecordMajorEvent(player, $"Banished {card.Card.Name}.", cardName: card.Card.Name, cardZone: ZoneType.Banishment);
                break;
            case ZoneType.Champion:
                RecordMajorEvent(player, $"{card.Card.Name} materialized as Champion.", cardName: card.Card.Name, cardZone: ZoneType.Champion);
                break;
            case ZoneType.MaterialDeck:
                // Face-down and easy to miss, so worth an entry — e.g. a Main card going in via an
                // effect like Preserve, or a misdrop.
                RecordMajorEvent(player, $"{card.Card.Name} was put into the Material Deck.", cardName: card.Card.Name);
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
            RecordMajorEvent(player, $"Summoned {card.Card.Name} token.", cardName: card.Card.Name, cardZone: ZoneType.Field);
            return;
        }

        if (from == ZoneType.Field && to == ZoneType.Tokens)
        {
            if (player.GetZone(ZoneType.Field).Cards.Remove(card))
            {
                player.Stats.Log($"Discarded {card.Card.Name} token.");
                RecordMajorEvent(player, $"Discarded {card.Card.Name} token.", cardName: card.Card.Name);
            }
        }
    }

    /// <summary>Repositions a card already on the Field, without any zone change or log entry — used while dragging within the Field.</summary>
    public void RepositionOnField(CardInstance card, double x, double y)
    {
        card.FieldX = x;
        card.FieldY = y;
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
}
