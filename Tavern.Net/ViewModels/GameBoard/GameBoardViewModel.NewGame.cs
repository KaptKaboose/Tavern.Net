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

/// <summary>New Game online: agreement, ready-up, and the shared reset.</summary>
public sealed partial class GameBoardViewModel
{
    // --- New Game online: pressing 'N' with an opponent needs both sides to agree first — solo's
    // immediate reset would otherwise blow away one side's board mid-turn without any warning to
    // the other. Same "both players ready, host commits" shape as OnlineLobbyViewModel's own
    // Ready/StartGame handshake (see ReadyMarkStyle, shared with that view), just reached from 'N'
    // instead of the lobby screen. Host convention matches the lobby's own: PlayerNumber 0 is
    // always the host (see OnlineLobbyViewModel.TryBuildAndStartAsync's AddPlayer ordering).

    public bool IsHost => Player.PlayerNumber == 0;

    public bool IsGuest => !IsHost;

    // Online, the ready-up IS the sideboarding panel (see the Sideboarding region below): it shows
    // both players' ready state, the host's first-player pick and Start button, and locks a
    // player's own edits while they're Ready. This region owns the handshake state behind it.

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartNewGameOnlineCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleNewGameReadyCommand))]
    private bool _isNewGameReady;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartNewGameOnlineCommand))]
    private bool _isOpponentNewGameReady;

    /// <summary>Host's own choice of who goes first afterward — only the host's copy of this is
    /// ever actually used (see StartNewGameOnline); the guest sees no picker at all (IsHost gates
    /// its visibility in XAML), so their own copy is just along for the ride, unused.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMeChosenFirstForNewGame))]
    private int _newGameFirstPlayerNumber;

    public bool IsMeChosenFirstForNewGame => NewGameFirstPlayerNumber == Player.PlayerNumber;

    /// <summary>Opens the online sideboarding/ready-up panel locally — for the player who pressed 'N'
    /// (see RequestNewGameOnline), for the other side on receiving their NewGameRequest, and for
    /// both at the very start of an online game (no Cancel then — there's no game to go back to).
    /// Already open (e.g. both pressed N at once) is a no-op, so an incoming request can't wipe
    /// swaps someone's already made. The first player defaults to the host (PlayerNumber 0, see
    /// IsHost) — or, for a game's first round, whoever the lobby's dice roll picked — until the host
    /// chooses otherwise.</summary>
    private void OpenNewGameReadyUp(bool canCancel = true, int firstPlayerNumber = 0)
    {
        if (SideboardPanel is not null)
        {
            return;
        }

        IsNewGameReady = false;
        IsOpponentNewGameReady = false;
        NewGameFirstPlayerNumber = firstPlayerNumber;
        OpenSideboardPanel(canCancel);
    }

    [RelayCommand]
    private void ChooseSelfFirstForNewGame() => NewGameFirstPlayerNumber = Player.PlayerNumber;

    [RelayCommand]
    private void ChooseOpponentFirstForNewGame() => NewGameFirstPlayerNumber = OpponentPlayer!.PlayerNumber;

    // --- New Game agreement: before anyone sideboards, both players have to agree to start a new
    // game at all (pressing N mid-game shouldn't drop a sideboard panel on the other player without
    // asking). The player who pressed N has implicitly agreed; the other is asked. Once both have
    // agreed, both go on to the sideboard panel. Not shown for a session's very first game — the
    // lobby already was that agreement.

    [ObservableProperty]
    private bool _isNewGameAgreementOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAgreeButton), nameof(AgreementMessage), nameof(AgreementCancelLabel))]
    private bool _hasAgreedToNewGame;

    private bool _hasOpponentAgreedToNewGame;

    public bool ShowAgreeButton => !HasAgreedToNewGame;

    public string AgreementMessage => HasAgreedToNewGame
        ? "Waiting for your opponent to agree..."
        : "Your opponent wants to start a new game. Both boards will reset.";

    public string AgreementCancelLabel => HasAgreedToNewGame ? "Cancel" : "Decline";

    partial void OnIsNewGameAgreementOpenChanged(bool value)
    {
        if (value)
        {
            CloseActionsMenu();
        }
    }

    private void OpenNewGameAgreement(bool selfAgreed, bool opponentAgreed)
    {
        HasAgreedToNewGame = selfAgreed;
        _hasOpponentAgreedToNewGame = opponentAgreed;
        IsNewGameAgreementOpen = true;
    }

    private void CloseNewGameAgreement()
    {
        IsNewGameAgreementOpen = false;
        HasAgreedToNewGame = false;
        _hasOpponentAgreedToNewGame = false;
    }

    private void RequestNewGameOnline()
    {
        if (IsNewGameAgreementOpen || SideboardPanel is not null)
        {
            return;
        }

        OpenNewGameAgreement(selfAgreed: true, opponentAgreed: false);
        _ = _connection!.SendAsync(new OnlineMessage { Kind = OnlineMessageKind.NewGameRequest });
    }

    private void ReceiveNewGameRequest()
    {
        // Already sideboarding (or in a session's first-game panel): nothing to ask.
        if (SideboardPanel is not null)
        {
            return;
        }

        if (IsNewGameAgreementOpen)
        {
            // Both pressed N at once — each already agreed by pressing it, so that's agreement.
            if (HasAgreedToNewGame)
            {
                _hasOpponentAgreedToNewGame = true;
                ProceedToSideboardAfterAgreement();
            }

            return;
        }

        OpenNewGameAgreement(selfAgreed: false, opponentAgreed: true);
    }

    private void ReceiveNewGameAgree()
    {
        if (!IsNewGameAgreementOpen)
        {
            return;
        }

        _hasOpponentAgreedToNewGame = true;
        if (HasAgreedToNewGame)
        {
            ProceedToSideboardAfterAgreement();
        }
    }

    [RelayCommand]
    private void AgreeToNewGame()
    {
        if (!IsNewGameAgreementOpen || HasAgreedToNewGame)
        {
            return;
        }

        HasAgreedToNewGame = true;
        _ = _connection!.SendAsync(new OnlineMessage { Kind = OnlineMessageKind.NewGameAgree });
        if (_hasOpponentAgreedToNewGame)
        {
            ProceedToSideboardAfterAgreement();
        }
    }

    /// <summary>Decline (as the asked player) or cancel (as the asker) — either way it ends for both.</summary>
    [RelayCommand]
    private void CancelNewGameAgreement()
    {
        CloseNewGameAgreement();
        _ = _connection!.SendAsync(new OnlineMessage { Kind = OnlineMessageKind.NewGameCancel });
    }

    private void ProceedToSideboardAfterAgreement()
    {
        CloseNewGameAgreement();
        OpenNewGameReadyUp();
    }

    /// <summary>Ready/Not Ready — Ready is only allowed with a Level 0 champion in Material, same as
    /// solo's Ready; un-readying is always allowed. Being Ready locks the panel's own edits.</summary>
    [RelayCommand(CanExecute = nameof(CanToggleNewGameReady))]
    private void ToggleNewGameReady()
    {
        IsNewGameReady = !IsNewGameReady;
        if (SideboardPanel is { } panel)
        {
            panel.IsLocked = IsNewGameReady;
        }

        _ = _connection!.SendAsync(new OnlineMessage { Kind = OnlineMessageKind.NewGameReady, Ready = IsNewGameReady });
    }

    private bool CanToggleNewGameReady() => IsNewGameReady || SideboardPanel?.HasLevelZeroChampion != false;

    private void CloseNewGameReadyUp()
    {
        SideboardPanel = null;
        IsNewGameReady = false;
        IsOpponentNewGameReady = false;
    }

    [RelayCommand]
    private void CancelNewGameReadyUp()
    {
        CloseNewGameReadyUp();
        _ = _connection!.SendAsync(new OnlineMessage { Kind = OnlineMessageKind.NewGameCancel });
    }

    private bool CanStartNewGameOnline() => IsHost && IsNewGameReady && IsOpponentNewGameReady;

    /// <summary>Host-only: commits the reset both sides just agreed to, using whichever first
    /// player the host picked (ChooseSelfFirstForNewGame/ChooseOpponentFirstForNewGame — defaults
    /// to the host, but doesn't have to stay that way; there's no dice-roll step for a mid-game
    /// reset the way the initial lobby has one, so the host just decides directly).</summary>
    [RelayCommand(CanExecute = nameof(CanStartNewGameOnline))]
    private void StartNewGameOnline()
    {
        if (!IsHost)
        {
            return;
        }

        var firstPlayerNumber = NewGameFirstPlayerNumber;
        _ = _connection!.SendAsync(new OnlineMessage { Kind = OnlineMessageKind.NewGameStart, FirstPlayerNumber = firstPlayerNumber });
        PerformOnlineNewGame(firstPlayerNumber);
    }

    /// <summary>The actual reset, run independently by both sides once the host's Start fires (once
    /// locally, once via the incoming NewGameStart) — StartNewGameForPlayer is the same per-player
    /// primitive OnlineLobbyViewModel.TryBuildAndStartAsync uses for the very first game, deliberately
    /// scoped to just this player's own Zones/Stats (GameSession.StartNewGame's unscoped, both-
    /// players version isn't safe to call online — it would also reset this session's local mirror
    /// of the opponent, which their own client owns and broadcasts, not this one).</summary>
    private void PerformOnlineNewGame(int firstPlayerNumber)
    {
        DiscardUndo();
        AbortRevealPanel();
        CloseNewGameReadyUp();

        // The match clock resets too — a whole new game shouldn't keep counting up from the
        // previous one's elapsed time. Each side zeroes their own local copy; the timer itself
        // keeps ticking (it's never stopped mid-game, only frozen by Save Game), it just resumes
        // counting up from zero.
        ElapsedTime = TimeSpan.Zero;

        // The opponent's first post-reset broadcast is their game-start setup (fresh "Game started."
        // entry, cleared log) — automated, not something they did, so it shouldn't glow the Opponent
        // button or flash their Life; re-seed instead of comparing (see _hasSeenInitialOpponentState).
        _hasSeenInitialOpponentState = false;
        _lastSeenOpponentMajorEventCount = 0;
        _previousOpponentCounts = null;
        _unseenOpponentArrivals.Clear();

        // Rebuild Main/Material from this player's current (possibly sideboarded) deck lists first —
        // StartNewGameForPlayer then resets everything as usual.
        if (Player.Deck is not null)
        {
            Decklists.DeckSessionBuilder.ApplyArrangement(Player);
        }

        _suppressCounterFlash = true;
        var glimpsedOnNewGame = _session.StartNewGameForPlayer(Player);
        _suppressCounterFlash = false;

        var firstPlayer = firstPlayerNumber == Player.PlayerNumber ? Player : OpponentPlayer!;
        _session.ApplyRemoteGameState(TurnPhase.Materialization, firstPlayer);
        _session.SetFirstTurnTarget(Player, firstPlayer == Player ? TurnPhase.Main : TurnPhase.Draw);
        CurrentPhase = _session.CurrentPhase;
        UpdateIsMyTurn();

        // UpdateIsMyTurn only toggles the opponent panel on an actual turn *transition* — but a new
        // game is a fresh start, so it has to be right regardless of whose turn it was before:
        // whoever isn't going first watches the opponent's board, whoever is going first doesn't.
        IsOpponentPanelOpen = !IsMyTurn;

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
