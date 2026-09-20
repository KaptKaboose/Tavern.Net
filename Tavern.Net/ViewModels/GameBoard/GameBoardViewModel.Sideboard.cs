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

/// <summary>Sideboarding at the start of every game, and starting a new game.</summary>
public sealed partial class GameBoardViewModel
{
    // --- Sideboarding: every new game (solo: the first one included) starts from this panel, where
    // the player can swap cards between their sideboard and their Material/Main decks before
    // pressing Ready. See SideboardViewModel for the panel itself; the swaps live in Player.Deck and
    // carry from game to game until Reset. The panel being non-null IS the "open" state.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSideboardPanelOpen))]
    private SideboardViewModel? _sideboardPanel;

    public bool IsSideboardPanelOpen => SideboardPanel is not null;

    partial void OnSideboardPanelChanged(SideboardViewModel? value)
    {
        if (value is not null)
        {
            CloseActionsMenu();
        }
    }

    private void OpenSideboardPanel(bool canCancel)
    {
        if (Player.Deck is null)
        {
            // Older saves (made before decks were tracked) have no deck lists to sideboard from —
            // solo just starts the game directly, exactly as before.
            if (!IsOnline)
            {
                TriggerNewGame();
                return;
            }

            // Online always has one (the lobby loads it), but never leave the ready-up without a
            // panel: treat whatever's in Main/Material right now as the registered deck.
            Player.Deck = new DeckArrangement(
                Player.GetZone(ZoneType.MainDeck).Cards.Select(c => c.Card),
                Player.GetZone(ZoneType.MaterialDeck).Cards.Select(c => c.Card),
                Array.Empty<CardDto>());
        }

        // Online, the panel's own Ready/Start controls run through this board (the New Game region
        // above) and Cancel also tells the opponent; solo's Ready just starts the game. With no game
        // to cancel back to (a session's first game) the panel offers Back to Menu instead.
        Action? onCancel = null;
        if (canCancel)
        {
            onCancel = IsOnline ? CancelNewGameReadyUp : () => SideboardPanel = null;
        }

        var panel = new SideboardViewModel(
            Player.Deck,
            onReady: StartGameFromSideboard,
            onCancel: onCancel,
            apiClient: _apiClient,
            board: IsOnline ? this : null,
            onBackToMenu: BackToMenu);

        // Ready is gated on a Level 0 champion, which changes as cards move.
        panel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SideboardViewModel.HasLevelZeroChampion))
            {
                ToggleNewGameReadyCommand.NotifyCanExecuteChanged();
            }
        };

        SideboardPanel = panel;
        ToggleNewGameReadyCommand.NotifyCanExecuteChanged();
    }

    private void StartGameFromSideboard()
    {
        SideboardPanel = null;

        // Rebuild Main/Material from the arrangement the player just set up, then the normal reset.
        Decklists.DeckSessionBuilder.ApplyArrangement(Player);
        TriggerNewGame();
    }

    /// <summary>Resets the board for a fresh game — solo's 'N' key (see HandleKey, which routes
    /// online instead to RequestNewGameOnline), and also called directly from the constructor for a
    /// brand-new solo game (see isFreshSoloGame) so the player doesn't have to remember to press it
    /// themselves.</summary>
    private void TriggerNewGame()
    {
        // A pending Undo (Bottom/Mill/Shuffle/Glimpse) references cards/zones from the game that's
        // about to be wiped out — discard it now, or a still-visible toast clicked after the reset
        // would try to move a card that no longer exists where it expects.
        DiscardUndo();
        AbortRevealPanel();

        // StartNewGame already draws the opening hand as part of setup — unless the base champion's
        // effect calls for an opening glimpse instead, in which case it hands the already-drawn
        // cards back here so they can actually be shown (it has no reference to GlimpseStaging/
        // IsGlimpsing to do that itself).
        _suppressCounterFlash = true;
        var glimpsedOnNewGame = _session.StartNewGame();
        _suppressCounterFlash = false;
        CurrentPhase = _session.CurrentPhase;
        if (glimpsedOnNewGame.Count > 0)
        {
            _drawAfterGlimpsing = true;
            IsGlimpsing = true;
            foreach (var card in glimpsedOnNewGame)
            {
                GlimpseStaging.Add(new CardViewModel(card, _apiClient, this));
            }
        }
    }
}
