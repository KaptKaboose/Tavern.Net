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
public sealed class MajorEvent
{
    public required string Description { get; set; }

    public required MajorEventKind Kind { get; init; }

    /// <summary>Only meaningful for Kind == LifeChanged/DamageDealt — the running total for the
    /// currently-open coalescing streak.</summary>
    public int NetDelta { get; set; }

    public required int Turn { get; init; }

    public required GameSnapshot Snapshot { get; set; }
}
