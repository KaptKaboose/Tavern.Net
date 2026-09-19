using System.ComponentModel;

namespace Tavern.Net.Game;

/// <summary>
/// Distinguishes the two coalescing event kinds (see GameSession.RecordLifeOrDamageChange) from
/// everything else. Only Life/Damage changes ever merge into a previous entry; every other kind
/// always gets its own MajorEvent and closes any open coalescing streak.
/// </summary>
public enum MajorEventKind
{
    Other,
    LifeChanged,
    DamageDealt,
}

/// <summary>
/// One entry in the curated, always-visible Play Log — as opposed to GameStats.PlayLog, which
/// still records literally everything. Mutable (not a record) because a coalescing Life/Damage
/// streak updates the same entry in place — its Description/NetDelta/Snapshot change as more
/// same-direction changes arrive, rather than piling up a new entry per click.
/// </summary>
public sealed class MajorEvent : INotifyPropertyChanged
{
    private string _description = "";

    /// <summary>Raises PropertyChanged so the Play Log list re-renders when a coalescing streak
    /// rewrites this in place — without it, the list kept showing the first click's text
    /// ("1 damage") while the review panel (which reads it fresh on open) showed the real total.</summary>
    public required string Description
    {
        get => _description;
        set
        {
            if (_description == value)
            {
                return;
            }

            _description = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Description)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The card this event is about, if any — a substring of <see cref="Description"/> the
    /// Play Log shows in bold. Null for events with no single card (life, turns, game start).</summary>
    public string? CardName { get; init; }

    /// <summary>Which zone <see cref="CardName"/> ended up in (Field, Graveyard, Banishment,
    /// Champion, ...) — so the review panel highlights the right copy of the card. Null when unknown
    /// (older saves) or when there is no card.</summary>
    public ZoneType? CardZone { get; init; }

    public required MajorEventKind Kind { get; init; }

    /// <summary>When this event happened — set implicitly at construction. Used to interleave both
    /// players' MajorEvents into one chronological log in an online game (see
    /// GameBoardViewModel's merged Play Log); irrelevant for solo, where there's only one stream.</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>Only meaningful for Kind == LifeChanged/DamageDealt — the running total for the
    /// currently-open coalescing streak.</summary>
    public int NetDelta { get; set; }

    public required int Turn { get; init; }

    public required GameSnapshot Snapshot { get; set; }
}
