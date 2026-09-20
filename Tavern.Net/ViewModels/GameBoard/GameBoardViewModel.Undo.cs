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

/// <summary>The single-level Undo for blind Main Deck actions.</summary>
public sealed partial class GameBoardViewModel
{
    // --- Undo: a single-level, Main-Deck-scoped safety net for the blind actions (Mill/Bottom/
    // Glimpse) — NOT a general undo system. Main Deck can't be peeked online, so a mis-counted
    // blind action would otherwise be unrecoverable; each call site below hands ArmUndo the exact
    // closure that reverses IT SPECIFICALLY (not a generic "restore Main's card list" snapshot,
    // which — tried first — left the same CardInstance sitting in two zones at once for anything
    // that crossed a zone boundary, e.g. undoing a Mill left the milled cards in the Graveyard
    // *and* put copies of them back in Main). A flat, non-cancellable 10-second window; only ever
    // one level deep, since the next blind action's own ArmUndo call overwrites this one. Give's
    // blind path deliberately does NOT get an Undo entry — by the time it would show, the
    // TransferCard message has already reached the opponent, so a local-only "undo" would just
    // duplicate the cards instead of actually taking them back.

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    private bool _isUndoAvailable;

    [ObservableProperty]
    private string? _undoActionLabel;

    /// <summary>Optional second line under the action label — for context a short label can't
    /// convey on its own (e.g. Generate's cards landing at the bottom rather than being shuffled
    /// in, which a player might otherwise assume happened automatically).</summary>
    [ObservableProperty]
    private string? _undoDetailMessage;

    [ObservableProperty]
    private double _undoRemainingFraction = 1.0;

    private Action? _pendingUndo;
    private DispatcherTimer? _undoTimer;
    private DateTime _undoStartedAt;

    private void ArmUndo(string actionLabel, Action undo, string? detailMessage = null)
    {
        _pendingUndo = undo;
        UndoActionLabel = actionLabel;
        UndoDetailMessage = detailMessage;
        IsUndoAvailable = true;
        UndoRemainingFraction = 1.0;
        _undoStartedAt = DateTime.UtcNow;

        _undoTimer?.Stop();
        _undoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _undoTimer.Tick += OnUndoTimerTick;
        _undoTimer.Start();
    }

    private void OnUndoTimerTick(object? sender, EventArgs e)
    {
        var fraction = 1.0 - (DateTime.UtcNow - _undoStartedAt).TotalSeconds / 10.0;
        if (fraction <= 0)
        {
            DiscardUndo();
            return;
        }

        UndoRemainingFraction = fraction;
    }

    private void DiscardUndo()
    {
        _undoTimer?.Stop();
        IsUndoAvailable = false;
        _pendingUndo = null;
        UndoActionLabel = null;
        UndoDetailMessage = null;
    }

    [RelayCommand(CanExecute = nameof(IsUndoAvailable))]
    private void Undo()
    {
        if (_pendingUndo is null)
        {
            return;
        }

        var actionLabel = UndoActionLabel;
        try
        {
            _pendingUndo();
        }
        catch (InvalidOperationException)
        {
            // Belt-and-suspenders: some unforeseen board change since this was armed left the
            // closure's card/zone assumptions stale (the known case — a New Game reset — is
            // guarded against directly above in HandleKey's 'N' case). Better to silently drop an
            // unusable Undo than crash the whole app over what's just a convenience feature.
            DiscardUndo();
            return;
        }

        // The opponent may have already reasoned about now-stale deck-order information (e.g. what
        // a Reveal just showed them, or what they know got milled) — a courtesy notice, not
        // anything that affects their own board.
        if (IsOnline)
        {
            _ = _connection!.SendAsync(new OnlineMessage { Kind = OnlineMessageKind.UndoNotice, UndoActionLabel = actionLabel });
        }

        DiscardUndo();
    }
}
