using Tavern.Net.GameData.Models;

namespace Tavern.Net.Game;

/// <summary>Play-log Major events, Life/Damage coalescing, and the board snapshot each event carries.</summary>
public sealed partial class GameSession
{
    // True while the most recent MajorEvent for this player is a Life/Damage change that hasn't
    // been closed yet — by a tap, a flip, or any other Major event — and so can still absorb the
    // next same-direction Life/Damage change instead of starting a new log entry. See
    // RecordLifeOrDamageChange and BreakCoalescingStreak.
    private readonly Dictionary<Player, bool> _canCoalesceLastMajorEvent = new();

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
    /// <paramref name="cardName"/> is the card the description is about, if any — only so the Play
    /// Log can bold it (see MajorEvent.CardName).
    /// </summary>
    private void RecordMajorEvent(Player player, string description, bool insertAtStart = false, string? cardName = null, ZoneType? cardZone = null)
    {
        var majorEvent = new MajorEvent
        {
            Description = description,
            CardName = cardName,
            CardZone = cardZone,
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
