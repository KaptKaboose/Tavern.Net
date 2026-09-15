using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Tavern.Net.Game;

/// <summary>Session statistics for a single player, tracked as the game is played.</summary>
public sealed partial class GameStats : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AverageCardsPlayedPerTurn))]
    private int _turnCount = 0;

    [ObservableProperty]
    private int _cardsDrawnCount;

    /// <summary>Running total of cards played from Hand to Field this game — see
    /// GameSession.MoveCard. Distinct from DeadTurnsCount's PlayedCardThisTurn flag: this counts
    /// every card, not just whether at least one was played.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AverageCardsPlayedPerTurn))]
    private int _cardsPlayedCount;

    /// <summary>Cards played per turn so far, including the turn in progress — TurnCount is
    /// 0-based, so +1 matches the turn number the player actually sees (never zero, so this never
    /// divides by zero).</summary>
    public double AverageCardsPlayedPerTurn => CardsPlayedCount / (double)(TurnCount + 1);

    /// <summary>Running total of damage dealt this game — goldfishing has no opponent board to
    /// compute this from, so it's a manually-tracked tally (see GameSession.AdjustDamageDealt).</summary>
    [ObservableProperty]
    private int _damageDealtCount;

    /// <summary>Running total of life gained via the Life panel's + button (see
    /// GameSession.AdjustLife's trackRecovery parameter) — tracked separately from Life itself
    /// since goldfishing has no opponent to actually deal damage, so every point lost is either a
    /// self-inflicted correction or a level-down, neither of which "recovered" anything.</summary>
    [ObservableProperty]
    private int _lifeRecoveredCount;

    /// <summary>How many turns ended without a single card having been played from Hand to Field
    /// (see GameSession.NextTurn/PlayedCardThisTurn) — a rough measure of how often a goldfish run
    /// stalled out.</summary>
    [ObservableProperty]
    private int _deadTurnsCount;

    /// <summary>Whether any card has been played from Hand to Field yet this turn — set by
    /// GameSession.MoveCard, checked and reset by GameSession.NextTurn to tally DeadTurnsCount.
    /// Not an [ObservableProperty]: it's internal bookkeeping between those two, never bound to the
    /// UI itself.</summary>
    public bool PlayedCardThisTurn { get; set; }

    /// <summary>Running total of cards lost to Memory decay (banished from Memory rather than
    /// returned to Hand via Recollection) — see GameSession.Banish.</summary>
    [ObservableProperty]
    private int _cardsLostToMemoryDecayCount;

    /// <summary>The turn each distinct Champion level was first reached, in the order they were
    /// reached (not sorted by level — a champion can level down as well as up). See
    /// GameSession's RecordChampionLevelReached.</summary>
    public ObservableCollection<ChampionLevelMilestone> ChampionLevelMilestones { get; } = new();

    /// <summary>Every action, literally — the complete record. Never shown directly in the UI
    /// anymore (see MajorEvents), kept for whenever a fuller trail is wanted.</summary>
    public ObservableCollection<string> PlayLog { get; } = new();

    /// <summary>The curated subset of PlayLog-worthy moments the Play Log panel actually shows —
    /// each carrying a read-only GameSnapshot of the board at that point. Built by GameSession
    /// (see RecordMajorEvent/RecordLifeOrDamageChange), not derived from PlayLog.</summary>
    public ObservableCollection<MajorEvent> MajorEvents { get; } = new();

    // TurnCount is 0-based internally — +1 here so the log reads naturally (a player never sees
    // "Turn 0").
    public void Log(string message) => PlayLog.Add($"Turn {TurnCount + 1}: {message}");
}
