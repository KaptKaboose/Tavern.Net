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

/// <summary>The small non-modal toast notification.</summary>
public sealed partial class GameBoardViewModel
{
    // --- Toast: a small, non-modal, single-purpose notification — currently only used for the
    // opponent's own Undo notice. Deliberately not a general queue/primitive; nothing else needs
    // one yet.

    [ObservableProperty]
    private string? _toastMessage;

    private DispatcherTimer? _toastTimer;

    private void ShowToast(string message)
    {
        ToastMessage = message;
        _toastTimer?.Stop();
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _toastTimer.Tick += (_, _) =>
        {
            ToastMessage = null;
            _toastTimer!.Stop();
        };
        _toastTimer.Start();
    }
}
