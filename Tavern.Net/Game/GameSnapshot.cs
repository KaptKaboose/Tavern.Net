namespace Tavern.Net.Game;

/// <summary>
/// A full, immutable copy of one player's board at the moment a Major event happened — taken by
/// GameSession.TakeSnapshot and attached to that event's <see cref="MajorEvent"/> entry so the
/// Play Log can show a read-only "what did the board look like then" view. The Tokens zone is
/// deliberately excluded: it's a static catalog, not game state (see GameSession.StartNewGame's
/// own reasoning for excluding it elsewhere).
/// </summary>
public sealed record GameSnapshot(
    int Life,
    int DamageDealtCount,
    int TurnCount,
    TurnPhase Phase,
    IReadOnlyList<CardSnapshot> Cards);
