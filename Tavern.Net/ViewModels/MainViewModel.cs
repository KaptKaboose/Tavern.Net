using CommunityToolkit.Mvvm.ComponentModel;
using Tavern.Net.Decklists;
using Tavern.Net.Game;
using Tavern.Net.GameData;
using Tavern.Net.Online;

namespace Tavern.Net.ViewModels;

/// <summary>Root view model — switches between the start menu, the deck-import screen, the
/// saved-games screen, the online lobby and the game board.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly GrandArchiveApiClient _apiClient = new();
    private readonly DeckStorageService _deckStorage = new();
    private readonly GameStorageService _gameStorage = new();
    private readonly PlayerNameStorageService _playerNameStorage = new();

    private StartMenuViewModel? _startMenuViewModel;

    [ObservableProperty]
    private ObservableObject _currentView;

    public MainViewModel()
    {
        _currentView = CreateStartMenuViewModel();
    }

    private StartMenuViewModel CreateStartMenuViewModel()
    {
        var startMenuViewModel = new StartMenuViewModel(_apiClient, _deckStorage);
        // Solo falls back here only when there's no active deck yet — that's still "start a solo
        // game", so Start Game stays available. Change Deck is purely deck management: we don't know
        // whether the player is about to play Solo or Online, so it's hidden there.
        startMenuViewModel.SoloRequested += () => CurrentView = CreateImportViewModel(allowStartGame: true);
        startMenuViewModel.ChangeDeckRequested += () => CurrentView = CreateImportViewModel(allowStartGame: false);
        startMenuViewModel.ViewGameRequested += () => CurrentView = CreateSavedGamesViewModel();
        startMenuViewModel.OnlineRequested += () => CurrentView = CreateOnlineLobbyViewModel();
        // The common Solo path — an active deck already exists, so this is the one most solo games
        // actually take (CreateImportViewModel's own DeckReady only fires when there's no active
        // deck yet, or via Change Deck's Start Game). Same auto-start as that path.
        startMenuViewModel.GameReady += (session, player) => CurrentView = CreateGameBoardViewModel(session, player, isFreshSoloGame: true);
        _startMenuViewModel = startMenuViewModel;
        return startMenuViewModel;
    }

    private DeckImportViewModel CreateImportViewModel(bool allowStartGame)
    {
        var importViewModel = new DeckImportViewModel(_apiClient, _deckStorage, allowStartGame);
        // A brand-new solo game — auto-starts on load rather than making the player press 'N'
        // themselves (see GameBoardViewModel's own isFreshSoloGame param).
        importViewModel.DeckReady += (session, player) => CurrentView = CreateGameBoardViewModel(session, player, isFreshSoloGame: true);
        importViewModel.BackRequested += () => CurrentView = ReturnToStartMenu();
        return importViewModel;
    }

    private SavedGamesViewModel CreateSavedGamesViewModel()
    {
        var savedGamesViewModel = new SavedGamesViewModel(_apiClient, _gameStorage);
        savedGamesViewModel.GameLoaded += (session, player, elapsedTime) =>
            CurrentView = CreateGameBoardViewModel(session, player, loadedElapsedTime: elapsedTime);
        savedGamesViewModel.BackRequested += () => CurrentView = ReturnToStartMenu();
        return savedGamesViewModel;
    }

    private OnlineLobbyViewModel CreateOnlineLobbyViewModel()
    {
        var onlineLobbyViewModel = new OnlineLobbyViewModel(_apiClient, _deckStorage, _gameStorage, _playerNameStorage);
        onlineLobbyViewModel.GameStarted += (session, player, connection, firstPlayerNumber) =>
            CurrentView = CreateGameBoardViewModel(session, player, connection, onlineFirstPlayerNumber: firstPlayerNumber);
        onlineLobbyViewModel.BackRequested += () => CurrentView = ReturnToStartMenu();
        return onlineLobbyViewModel;
    }

    private GameBoardViewModel CreateGameBoardViewModel(
        GameSession session, Player player, GameConnection? connection = null, TimeSpan? loadedElapsedTime = null, bool isFreshSoloGame = false,
        int? onlineFirstPlayerNumber = null)
    {
        var opponentPlayer = connection is not null ? session.Players.First(p => p != player) : null;
        var gameBoardViewModel = new GameBoardViewModel(
            session, player, _apiClient, _gameStorage, connection, opponentPlayer, loadedElapsedTime, isFreshSoloGame, onlineFirstPlayerNumber);
        gameBoardViewModel.BackToMenuRequested += () => CurrentView = ReturnToStartMenu();
        return gameBoardViewModel;
    }

    private StartMenuViewModel ReturnToStartMenu()
    {
        var startMenuViewModel = _startMenuViewModel ?? CreateStartMenuViewModel();
        startMenuViewModel.RefreshActiveDeck();
        return startMenuViewModel;
    }
}
