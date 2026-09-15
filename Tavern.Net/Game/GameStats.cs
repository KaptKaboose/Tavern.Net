using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Tavern.Net.Game;

/// <summary>Session statistics for a single player, tracked as the game is played.</summary>
public sealed partial class GameStats : ObservableObject
{
    [ObservableProperty]
    private int _turnCount = 0;

    [ObservableProperty]
    private int _cardsDrawnCount;

    /// <summary>Running total of damage dealt this game — goldfishing has no opponent board to
    /// compute this from, so it's a manually-tracked tally (see GameSession.AdjustDamageDealt).</summary>
    [ObservableProperty]
    private int _damageDealtCount;

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
