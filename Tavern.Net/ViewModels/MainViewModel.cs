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
        startMenuViewModel.GameReady += (session, player) => CurrentView = CreateGameBoardViewModel(session, player);
        _startMenuViewModel = startMenuViewModel;
        return startMenuViewModel;
    }

    private DeckImportViewModel CreateImportViewModel(bool allowStartGame)
    {
        var importViewModel = new DeckImportViewModel(_apiClient, _deckStorage, allowStartGame);
        importViewModel.DeckReady += (session, player) => CurrentView = CreateGameBoardViewModel(session, player);
        importViewModel.BackRequested += () => CurrentView = ReturnToStartMenu();
        return importViewModel;
    }

    private SavedGamesViewModel CreateSavedGamesViewModel()
    {
        var savedGamesViewModel = new SavedGamesViewModel(_apiClient, _gameStorage);
        savedGamesViewModel.GameLoaded += (session, player) => CurrentView = CreateGameBoardViewModel(session, player);
        savedGamesViewModel.BackRequested += () => CurrentView = ReturnToStartMenu();
        return savedGamesViewModel;
    }

    private OnlineLobbyViewModel CreateOnlineLobbyViewModel()
    {
        var onlineLobbyViewModel = new OnlineLobbyViewModel(_apiClient, _deckStorage, _gameStorage, _playerNameStorage);
        onlineLobbyViewModel.GameStarted += (session, player, connection) => CurrentView = CreateGameBoardViewModel(session, player, connection);
        onlineLobbyViewModel.BackRequested += () => CurrentView = ReturnToStartMenu();
        return onlineLobbyViewModel;
    }

    private GameBoardViewModel CreateGameBoardViewModel(GameSession session, Player player, GameConnection? connection = null)
    {
        var opponentPlayer = connection is not null ? session.Players.First(p => p != player) : null;
        var gameBoardViewModel = new GameBoardViewModel(session, player, _apiClient, _gameStorage, connection, opponentPlayer);
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
