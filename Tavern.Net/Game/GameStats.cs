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

    public ObservableCollection<string> PlayLog { get; } = new();

    // TurnCount is 0-based internally — +1 here so the log reads naturally (a player never sees
    // "Turn 0").
    public void Log(string message) => PlayLog.Add($"Turn {TurnCount + 1}: {message}");
}
