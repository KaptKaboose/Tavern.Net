using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Tavern.Net.Game;

public sealed record LifeHistoryEntry(int Turn, int Life)
{
    // Turn is stored 0-based (matches GameStats.TurnCount, which GameSession.StartNewGame/
    // AdvancePhase rely on starting at 0 as a fast-forward sentinel) — only the display text
    // is 1-based, so a player never sees "Turn 0".
    public override string ToString() => $"Turn {Turn + 1}: {Life} life";
}

/// <summary>Session statistics for a single player, tracked as the game is played.</summary>
public sealed partial class GameStats : ObservableObject
{
    [ObservableProperty]
    private int _turnCount = 0;

    [ObservableProperty]
    private int _cardsDrawnCount;

    public ObservableCollection<LifeHistoryEntry> LifeHistory { get; } = new();

    public ObservableCollection<string> PlayLog { get; } = new();

    // TurnCount is 0-based internally (see LifeHistoryEntry) — +1 here so the log reads naturally.
    public void Log(string message) => PlayLog.Add($"Turn {TurnCount + 1}: {message}");

    public void RecordLife(int newLife) => LifeHistory.Add(new LifeHistoryEntry(TurnCount, newLife));
}
