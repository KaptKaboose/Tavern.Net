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

/// <summary>Advancing the turn phase.</summary>
public sealed partial class GameBoardViewModel
{
    /// <summary>Advances to the next phase of the turn; advancing past End starts the next turn.
    /// Online, this no-ops unless it's currently this player's turn (see CanAdvancePhase) — playing
    /// cards is never gated this way, only phase progression itself.</summary>
    [RelayCommand(CanExecute = nameof(CanAdvancePhase))]
    private void NextPhase()
    {
        _session.AdvancePhase(Player);
        CurrentPhase = _session.CurrentPhase;

        // CurrentPhase/ActivePlayer are shared session state, not part of either player's own
        // broadcast Player — whoever just advanced (only ever the active player, see
        // CanAdvancePhase) sends this one-shot update on every phase change, not just the End
        // handoff, so the other side's tracker follows along; the passive side never echoes it back
        // (see ApplyRemoteGameState).
        if (IsOnline)
        {
            _ = _connection!.SendAsync(new OnlineMessage
            {
                Kind = OnlineMessageKind.GameState,
                Phase = _session.CurrentPhase,
                ActivePlayerNumber = _session.ActivePlayer!.PlayerNumber,
            });
        }

        UpdateIsMyTurn();
    }

    private bool CanAdvancePhase() => !IsOnline || IsMyTurn;
}
