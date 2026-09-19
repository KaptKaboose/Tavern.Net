namespace Tavern.Net.Game;

/// <summary>One card, wherever it currently sits, in a saved game — enough to reconstruct the exact
/// CardInstance (via GameData.GrandArchiveApiClient.GetCardBySlugAsync, disk-cached by
/// GameSessionSerializer.Capture) with all of its live state intact. <see cref="Zone"/> is where the
/// card currently is; <see cref="HomeZone"/>/<see cref="HomeOrder"/> are where it started, same as
/// CardInstance's own fields, needed so a "New Game" restart after loading still rebuilds the deck
/// correctly. <see cref="IsSessionGenerated"/> defaults to false so saves made before this field
/// existed still deserialize.</summary>
public sealed record SavedCardInstance(
    string Slug,
    ZoneType Zone,
    ZoneType HomeZone,
    int HomeOrder,
    bool IsTapped,
    bool IsFlipped,
    double FieldX,
    double FieldY,
    int Counter,
    bool IsEphemeral,
    bool IsIgnited,
    bool IsImbued,
    bool IsRanged,
    bool IsRooted,
    bool IsWarded,
    bool IsSessionGenerated = false);

/// <summary>The by-slug equivalent of <see cref="CardSnapshot"/> — a MajorEvent's board snapshot,
/// serialized without embedding the full CardDto.</summary>
public sealed record SavedCardSnapshot(
    string Slug,
    ZoneType Zone,
    bool IsTapped,
    bool IsFlipped,
    double FieldX,
    double FieldY,
    int Counter,
    bool IsEphemeral,
    bool IsIgnited,
    bool IsImbued,
    bool IsRanged,
    bool IsRooted,
    bool IsWarded);

public sealed record SavedGameSnapshot(
    int Life,
    int DamageDealtCount,
    int TurnCount,
    TurnPhase Phase,
    List<SavedCardSnapshot> Cards);

public sealed record SavedMajorEvent(
    string Description,
    MajorEventKind Kind,
    int NetDelta,
    int Turn,
    DateTime Timestamp,
    SavedGameSnapshot Snapshot,
    string? CardName = null,
    ZoneType? CardZone = null);

/// <summary>The full GameStats for one player — including PlayLog and MajorEvents (with their own
/// snapshots), not just the current board. The whole point of saving a game here is resuming into
/// the same review history, not just the same board position.</summary>
public sealed record SavedGameStats(
    int TurnCount,
    int CardsDrawnCount,
    int CardsPlayedCount,
    int DamageDealtCount,
    int LifeRecoveredCount,
    int DeadTurnsCount,
    bool PlayedCardThisTurn,
    int CardsLostToMemoryDecayCount,
    List<ChampionLevelMilestone> ChampionLevelMilestones,
    List<string> PlayLog,
    List<SavedMajorEvent> MajorEvents);

/// <summary>A <see cref="DeckArrangement"/> by slug, one entry per card copy — the registered deck
/// and the current (possibly sideboarded) lists.</summary>
public sealed record SavedDeckArrangement(
    List<string> RegisteredMain,
    List<string> RegisteredMaterial,
    List<string> RegisteredSideboard,
    List<string> Main,
    List<string> Material,
    List<string> Sideboard);

/// <param name="Deck">Only set for a whole-game save (GameSessionSerializer.Capture) — never for
/// the ~300ms online broadcast, which shares this DTO but must not carry (or leak) the player's
/// sideboard. Null also for saves made before sideboarding existed.</param>
public sealed record SavedPlayer(
    string Name,
    int PlayerNumber,
    int Life,
    int StartingHandSize,
    bool StartsInMemory,
    List<SavedCardInstance> Cards,
    SavedGameStats Stats,
    SavedDeckArrangement? Deck = null);

/// <summary>A named, resumable snapshot of an entire GameSession — every player's zones, stats and
/// full Play Log/Major Event history. Built by GameSessionSerializer.Capture and rebuilt by
/// GameSessionSerializer.RestoreAsync.</summary>
/// <param name="ElapsedTime">Online games only — the match clock's value at the moment of saving
/// (see GameBoardViewModel.ElapsedTime), frozen from then on; null for solo saves, and for any save
/// made before this field existed. Defaulted so older save files without it still deserialize.</param>
public sealed record SavedGame(
    string Name,
    DateTime SavedAtUtc,
    string Summary,
    TurnPhase CurrentPhase,
    List<SavedPlayer> Players,
    TimeSpan? ElapsedTime = null);
