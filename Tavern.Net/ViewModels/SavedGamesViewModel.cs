using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tavern.Net.Game;
using Tavern.Net.GameData;

namespace Tavern.Net.ViewModels;

/// <summary>The "View Game" screen: pick a saved game to resume, or delete one no longer wanted.</summary>
public sealed partial class SavedGamesViewModel : ObservableObject
{
    private readonly GrandArchiveApiClient _apiClient;
    private readonly GameStorageService _gameStorage;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isLoading;

    public ObservableCollection<SavedGame> SavedGames { get; } = new();

    /// <summary>Raised once a saved game has been fully rebuilt — MainViewModel handles it by
    /// switching to the game board. The TimeSpan? is the saved match clock (SavedGame.ElapsedTime,
    /// online games only) — read straight off the SavedGame we already have in hand rather than
    /// threading it through GameSessionSerializer.RestoreAsync's own return shape.</summary>
    public event Action<GameSession, Player, TimeSpan?>? GameLoaded;

    /// <summary>Raised when the player wants to return to the start menu.</summary>
    public event Action? BackRequested;

    public SavedGamesViewModel(GrandArchiveApiClient apiClient, GameStorageService gameStorage)
    {
        _apiClient = apiClient;
        _gameStorage = gameStorage;
        Refresh();
    }

    public void Refresh()
    {
        SavedGames.Clear();
        foreach (var game in _gameStorage.LoadAll().OrderByDescending(g => g.SavedAtUtc))
        {
            SavedGames.Add(game);
        }
    }

    [RelayCommand]
    private async Task LoadAsync(SavedGame? game)
    {
        if (game is null)
        {
            return;
        }

        ErrorMessage = null;
        IsLoading = true;
        try
        {
            var (session, player) = await GameSessionSerializer.RestoreAsync(game, _apiClient);
            GameLoaded?.Invoke(session, player, game.ElapsedTime);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Couldn't load \"{game.Name}\": {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void Delete(SavedGame? game)
    {
        if (game is null)
        {
            return;
        }

        _gameStorage.Delete(game.Name);
        Refresh();
    }

    [RelayCommand]
    private void Back() => BackRequested?.Invoke();
}
