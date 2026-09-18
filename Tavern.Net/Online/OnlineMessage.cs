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
    RevealCards,
    TransferCard,
    UndoNotice,

    /// <summary>Mid-game "New Game" ready-up handshake — same "both players agree, host commits"
    /// shape as Ready/StartGame in the pre-game lobby, just reached from the 'N' key instead. See
    /// GameBoardViewModel's own New Game region.</summary>
    NewGameRequest,
    NewGameReady,
    NewGameStart,
    NewGameCancel,

    /// <summary>The other player agreeing to a NewGameRequest — both then move on to the sideboard
    /// panel. (Appended last so the existing kinds' wire values don't shift.)</summary>
    NewGameAgree,
}

/// <summary>One revealed card in a RevealCards message — just enough to resolve and display it on
/// the receiving side (see GameBoardViewModel.ResolveOpponentCardAsync), not a full CardInstance:
/// the card never actually moves, so there's nothing else about it that matters here.</summary>
public sealed record RevealedCardEntry(string Slug, bool IsFlipped);

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

    /// <summary>RevealCards — a batch, since a single reveal action (e.g. the 'R' chord off Main
    /// Deck) can surface more than one card at once.</summary>
    public List<RevealedCardEntry>? RevealedCards { get; set; }

    /// <summary>TransferCard — the Give slugs (a batch for the same reason as RevealedCards, e.g. a
    /// blind top-N 'P' chord), the destination on the receiver's own board (Field or Sealed), and an
    /// informational label of where they came from (shown in the receiver's own log, nothing more).</summary>
    public List<string>? TransferCardSlugs { get; set; }

    public ZoneType? TransferTargetZone { get; set; }

    public string? TransferSourceLabel { get; set; }

    /// <summary>UndoNotice — which action got undone, if known, purely for the toast's own wording.</summary>
    public string? UndoActionLabel { get; set; }
}
