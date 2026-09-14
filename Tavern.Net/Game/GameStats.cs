using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Tavern.Net.Game;

public sealed record LifeHistoryEntry(int Turn, int Life)
{
    public override string ToString() => $"Turn {Turn}: {Life} life";
}

/// <summary>Session statistics for a single player, tracked as the game is played.</summary>
public sealed partial class GameStats : ObservableObject
{
    [ObservableProperty]
    private int _turnCount = 1;

    [ObservableProperty]
    private int _cardsDrawnCount;

    public ObservableCollection<LifeHistoryEntry> LifeHistory { get; } = new();

    public ObservableCollection<string> PlayLog { get; } = new();

    public void Log(string message) => PlayLog.Add($"Turn {TurnCount}: {message}");

    public void RecordLife(int newLife) => LifeHistory.Add(new LifeHistoryEntry(TurnCount, newLife));
}
