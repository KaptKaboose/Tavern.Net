using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tavern.Net.Game;
using Tavern.Net.GameData;
using Tavern.Net.GameData.Models;
using Tavern.Net.Online;

namespace Tavern.Net.ViewModels;

/// <summary>The dice panel and its roll animation.</summary>
public sealed partial class GameBoardViewModel
{
    /// <summary>Whether the Roll Dice panel is open. Not tied to whether an animation is
    /// currently running — closing the panel mid-roll just hides it; the timer keeps going and
    /// settles the dice regardless, so reopening later shows the finished result.</summary>
    [ObservableProperty]
    private bool _isRollingDice;

    partial void OnIsRollingDiceChanged(bool value)
    {
        if (value)
        {
            CloseActionsMenu();
        }
    }

    /// <summary>How many dice the next Roll will create — set via the panel's +/- pair, clamped
    /// to a sane [1, 20] range.</summary>
    [ObservableProperty]
    private int _diceToRoll = 1;

    public ObservableCollection<DieViewModel> Dice { get; } = new();

    [RelayCommand]
    private void OpenDicePanel() => IsRollingDice = true;

    [RelayCommand]
    private void CloseDicePanel() => IsRollingDice = false;

    [RelayCommand]
    private void IncreaseDiceToRoll() => DiceToRoll = Math.Min(20, DiceToRoll + 1);

    [RelayCommand]
    private void DecreaseDiceToRoll() => DiceToRoll = Math.Max(1, DiceToRoll - 1);

    /// <summary>
    /// Rolls DiceToRoll dice: each one's real result is picked right here, fairly, once — the
    /// cycling animation that follows is cosmetic suspense on top of an already-decided outcome,
    /// not what determines it. Each die gets its own randomized StopAt so they don't all freeze in
    /// lockstep; a single shared DispatcherTimer then just advances the clock and checks each
    /// still-rolling die against its own StopAt every tick.
    /// </summary>
    [RelayCommand]
    private void RollDice()
    {
        _diceAnimationTimer?.Stop();
        Dice.Clear();
        _diceAnimationElapsed = TimeSpan.Zero;

        for (var i = 0; i < DiceToRoll; i++)
        {
            var finalValue = _diceRandom.Next(1, 7);
            var stopAt = TimeSpan.FromMilliseconds(800 + _diceRandom.Next(0, 701));
            Dice.Add(new DieViewModel(finalValue, stopAt));
        }

        _diceAnimationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _diceAnimationTimer.Tick += OnDiceAnimationTick;
        _diceAnimationTimer.Start();
    }

    private void OnDiceAnimationTick(object? sender, EventArgs e)
    {
        _diceAnimationElapsed += TimeSpan.FromMilliseconds(80);
        var anyStillRolling = false;

        foreach (var die in Dice)
        {
            if (die.IsSettled)
            {
                continue;
            }

            if (_diceAnimationElapsed >= die.StopAt)
            {
                die.FaceValue = die.FinalValue;
                die.IsSettled = true;
            }
            else
            {
                die.FaceValue = _diceRandom.Next(1, 7);
                anyStillRolling = true;
            }
        }

        if (!anyStillRolling)
        {
            _diceAnimationTimer!.Stop();
            _diceAnimationTimer.Tick -= OnDiceAnimationTick;
            _diceAnimationTimer = null;
        }
    }
}
