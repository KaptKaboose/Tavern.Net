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

/// <summary>Save Game and the keyboard-shortcuts help panel.</summary>
public sealed partial class GameBoardViewModel
{
    /// <summary>Whether the Save Game panel is open.</summary>
    [ObservableProperty]
    private bool _isSavingGame;

    partial void OnIsSavingGameChanged(bool value)
    {
        if (value)
        {
            CloseActionsMenu();
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmSaveGameCommand))]
    private string _saveGameName = "";

    [ObservableProperty]
    private string? _saveGameStatusMessage;

    [RelayCommand]
    private void OpenSaveGamePanel()
    {
        SaveGameStatusMessage = null;
        IsSavingGame = true;
    }

    [RelayCommand]
    private void CloseSaveGamePanel() => IsSavingGame = false;

    public string AppVersionText => $"Tavern.Net v{AppVersion.Display}";

    /// <summary>Whether the keyboard-shortcuts reference panel is open.</summary>
    [ObservableProperty]
    private bool _isHelpOpen;

    partial void OnIsHelpOpenChanged(bool value)
    {
        if (value)
        {
            CloseActionsMenu();
        }
    }

    [RelayCommand]
    private void OpenHelp() => IsHelpOpen = true;

    [RelayCommand]
    private void CloseHelp() => IsHelpOpen = false;

    [RelayCommand(CanExecute = nameof(CanConfirmSaveGame))]
    private void ConfirmSaveGame()
    {
        GameSessionSerializer.WarmCardCache(_session, _apiClient);
        var saved = GameSessionSerializer.Capture(SaveGameName.Trim(), _session, IsOnline ? ElapsedTime : null);
        _gameStorage.Save(saved);
        SaveGameStatusMessage = $"Saved \"{saved.Name}\".";

        // The saved copy of ElapsedTime is now fixed at this instant — stop the live clock here too
        // so what's on screen for the rest of this session can't drift past what got saved.
        if (IsOnline)
        {
            _elapsedTimeTimer?.Stop();
            _isElapsedTimeFrozen = true;
        }
    }

    private bool CanConfirmSaveGame() => !string.IsNullOrWhiteSpace(SaveGameName);
}
