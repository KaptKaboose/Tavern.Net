using Tavern.Net.Game;

namespace Tavern.Net.Online;

public enum OnlineMessageKind
{
    Hello,
    Ready,
    DiceRoll,
    TurnOrderChoice,
    StartGame,
    PlayerState,
    GameState,
}

/// <summary>
/// A single flat message type covering every kind exchanged over a <see cref="GameConnection"/>,
/// rather than a message-per-kind hierarchy — avoids polymorphic JSON (de)serialization entirely,
/// at the cost of most fields being null/unused for any given <see cref="Kind"/>. Simple to reason
/// about for the small, fixed set of messages this app needs.
/// </summary>
public sealed class OnlineMessage
{
    public OnlineMessageKind Kind { get; set; }

    /// <summary>Hello.</summary>
    public string? PlayerName { get; set; }

    /// <summary>Ready.</summary>
    public bool Ready { get; set; }

    /// <summary>DiceRoll — the two individual die faces (so the opponent can show real dice, not
    /// just a total) — informational only, never enforced.</summary>
    public int Die1 { get; set; }

    public int Die2 { get; set; }

    /// <summary>TurnOrderChoice (who was picked) / StartGame (who actually goes first).</summary>
    public int FirstPlayerNumber { get; set; }

    /// <summary>PlayerState — reuses the same DTO built for game saves as the wire format for a full
    /// live-player broadcast.</summary>
    public SavedPlayer? PlayerState { get; set; }

    /// <summary>GameState.</summary>
    public TurnPhase? Phase { get; set; }

    /// <summary>GameState.</summary>
    public int? ActivePlayerNumber { get; set; }
}
