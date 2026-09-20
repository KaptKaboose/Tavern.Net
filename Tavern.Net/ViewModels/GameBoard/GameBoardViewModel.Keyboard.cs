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

/// <summary>Keyboard shortcuts (HandleKey) and the 'is any overlay open' guard.</summary>
public sealed partial class GameBoardViewModel
{
    private void StartActionFromKey(ArmedChord action)
    {
        OpenActionsMenu();
        SelectAction(action);
    }

    /// <summary>Keyboard shortcuts for the board — add more cases here as they come up.</summary>
    public bool HandleKey(Key key)
    {
        // Glimpsing locks out every key — the count was already decided up front by the Glimpse
        // action that opened it, so there's nothing left for a keystroke to do until sorting
        // finishes.
        if (IsGlimpsing || IsRevealPanelOpen)
        {
            return true;
        }

        // The sideboard panel's "move how many?" prompt takes digits/Backspace/Enter/Escape itself.
        if (SideboardPanel is { IsPromptingMoveCount: true } sideboardPanel)
        {
            return sideboardPanel.HandleKey(key);
        }

        // While the Actions menu's counter is showing, digits/Backspace type directly into
        // ActionCount and Enter confirms — everything else is swallowed, same as any other overlay.
        if (IsEnteringActionCount)
        {
            if (TryGetDigit(key, out var digit))
            {
                ActionCount = _hasTypedActionCount ? Math.Min(ActionCount * 10 + digit, 99) : digit;
                _hasTypedActionCount = true;
                return true;
            }

            if (key == Key.Back)
            {
                ActionCount = Math.Max(ActionCount / 10, 1);
                return true;
            }

            if (key == Key.Enter && ConfirmActionCommand.CanExecute(null))
            {
                ConfirmActionCommand.Execute(null);
            }

            return true;
        }

        // Same count-entry mechanics as the Actions menu, for how many Generate copies to create.
        if (IsGenerateCountOpen)
        {
            if (TryGetDigit(key, out var generateDigit))
            {
                GenerateCount = _hasTypedGenerateCount ? Math.Min(GenerateCount * 10 + generateDigit, 99) : generateDigit;
                _hasTypedGenerateCount = true;
                return true;
            }

            if (key == Key.Back)
            {
                GenerateCount = Math.Max(GenerateCount / 10, 1);
                return true;
            }

            if (key == Key.Enter && ConfirmGenerateCountCommand.CanExecute(null))
            {
                ConfirmGenerateCountCommand.Execute(null);
            }

            return true;
        }

        // While the Zoom overlay shows a Counter box (Field/Champion only — see
        // ShowCounterAndStatusPanel), digits/Backspace type directly into it, same as the Actions
        // menu's own count entry.
        if (ZoomedCard is not null && ShowCounterAndStatusPanel)
        {
            if (TryGetDigit(key, out var counterDigit))
            {
                var current = ZoomedCard.Instance.Counter;
                var target = _hasTypedZoomCounterDigit ? current * 10 + counterDigit : counterDigit;
                _session.AdjustCounter(Player, ZoomedCard.Instance, target - current);
                _hasTypedZoomCounterDigit = true;
                return true;
            }

            if (key == Key.Back)
            {
                var current = ZoomedCard.Instance.Counter;
                _session.AdjustCounter(Player, ZoomedCard.Instance, current / 10 - current);
                return true;
            }
        }

        // +/- adjust life (or damage dealt in a non-Online game) — checked ahead of the general overlay
        if (key is Key.Add or Key.OemPlus or Key.Subtract or Key.OemMinus && !IsAnyOverlayOpen(ignoreOpponentPanel: true))
        {
            var delta = key is Key.Add or Key.OemPlus ? 1 : -1;
            if (IsOnline) _session.AdjustLife(Player, delta); else _session.AdjustDamageDealt(Player, delta);
            return true;
        }

        // O toggles the opponent panel — checked ahead of the general overlay guard below because
        // that guard would otherwise swallow the key while the panel itself is the open overlay (so
        // O could never close it). It only acts when nothing else is open, though: the panel sits
        // beneath most other modals, so opening it behind one would leave it invisible while still
        // blocking every other shortcut.
        if (key == Key.O && IsOnline)
        {
            if (!IsAnyOverlayOpen(ignoreOpponentPanel: true))
            {
                ToggleOpponentPanel();
            }

            return true;
        }

        // Every other overlay (Zoom, Peek, Dice, Save Game, Opponent panel, the Actions menu's own
        // list view, the snapshot viewer and its pile popup, ...) blocks shortcuts the same way
        // Glimpsing already did above — a key meant for the board shouldn't reach through a modal
        // that's currently covering it.
        if (IsAnyOverlayOpen())
        {
            return true;
        }

        // Banish is common enough during ordinary play to warrant a bare digit key rather than a
        // trip through the Actions menu every time — no letter, no arming, just press a count.
        if (TryGetDigit(key, out var banishCount) && banishCount > 0)
        {
            _session.Banish(Player, banishCount);
            return true;
        }

        switch (key)
        {
            case Key.Space:
                if (NextPhaseCommand.CanExecute(null))
                {
                    NextPhaseCommand.Execute(null);
                }
                return true;
            case Key.D:
                // Shift+D reads as "D, but into Memory" — checked here rather than a separate case
                // since both share the same key.
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
                {
                    if (DrawCardIntoMemoryCommand.CanExecute(null))
                    {
                        DrawCardIntoMemoryCommand.Execute(null);
                    }
                }
                else if (DrawCardCommand.CanExecute(null))
                {
                    DrawCardCommand.Execute(null);
                }

                return true;
            case Key.S:
                // Same as picking Shuffle from the Actions menu — no count, runs immediately (with Undo).
                SelectAction(ArmedChord.Shuffle);
                return true;
            // Same flow as picking the action from the Actions menu — opens the count overlay for
            // it directly (type a number/Enter, or +/-), rather than arming anything silently.
            case Key.B:
                StartActionFromKey(ArmedChord.Banish);
                return true;
            case Key.G:
                StartActionFromKey(ArmedChord.Glimpse);
                return true;
            case Key.M:
                StartActionFromKey(ArmedChord.Mill);
                return true;
            case Key.R:
                StartActionFromKey(ArmedChord.Reveal);
                return true;
            case Key.P:
                if (CanUseOnlineAction)
                {
                    StartActionFromKey(ArmedChord.Give);
                }

                return true;
            case Key.N:
                if (IsOnline)
                {
                    RequestNewGameOnline();
                }
                else
                {
                    OpenSideboardPanel(canCancel: true);
                }

                return true;
            default:
                return false;
        }
    }

    /// <summary>True while any modal overlay is covering the board — see HandleKey's own comment on
    /// why that blocks every keyboard shortcut. Extend this alongside each new overlay-flag
    /// property (and give that property an OnXChanged hook calling CloseActionsMenu(), so a
    /// half-configured action can't go stale if some other overlay opens first).</summary>
    /// <param name="ignoreOpponentPanel">True to ask "is anything *else* open" — what the O shortcut
    /// needs, since it toggles the opponent panel itself.</param>
    private bool IsAnyOverlayOpen(bool ignoreOpponentPanel = false) =>
        (IsOpponentPanelOpen && !ignoreOpponentPanel) || IsRollingDice || IsSavingGame || IsSealedPanelOpen || IsGiveTargetPickerOpen || IsActionsMenuOpen ||
        IsGenerateSearchOpen || IsGenerateCountOpen || IsHelpOpen || IsSideboardPanelOpen || IsNewGameAgreementOpen || IsRevealPanelOpen ||
        ZoomedCard is not null || PeekedZone is not null || ViewedMajorEvent is not null ||
        ViewedSnapshotPile is not null || ZoomedSnapshotCard is not null || ActiveReveal is not null;

    internal static bool TryGetDigit(Key key, out int digit)
    {
        if (key is >= Key.D0 and <= Key.D9)
        {
            digit = key - Key.D0;
            return true;
        }

        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            digit = key - Key.NumPad0;
            return true;
        }

        digit = 0;
        return false;
    }
}
