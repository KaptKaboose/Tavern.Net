using CommunityToolkit.Mvvm.ComponentModel;

namespace Tavern.Net.ViewModels;

/// <summary>
/// One die in the Roll Dice panel. Purely a UI/animation concern — not domain state, so it has no
/// GameSession/Player involvement at all (same reasoning as Glimpse's staging lists). The actual
/// result is decided once, fairly, the instant Roll is clicked (<see cref="FinalValue"/>); the
/// face-cycling animation driven by GameBoardViewModel is cosmetic suspense on top of that, not
/// what determines the outcome.
/// </summary>
public sealed partial class DieViewModel : ObservableObject
{
    /// <summary>The face actually shown right now — cycles randomly while rolling, then holds at
    /// <see cref="FinalValue"/> once <see cref="IsSettled"/>.</summary>
    [ObservableProperty]
    private int _faceValue = 1;

    [ObservableProperty]
    private bool _isSettled;

    public int FinalValue { get; }

    /// <summary>How far into the shared animation clock this die stops — staggered per-die
    /// (GameBoardViewModel.RollDice picks this randomly) so a multi-die roll doesn't freeze every
    /// die in lockstep.</summary>
    public TimeSpan StopAt { get; }

    public DieViewModel(int finalValue, TimeSpan stopAt)
    {
        FinalValue = finalValue;
        StopAt = stopAt;
    }
}
