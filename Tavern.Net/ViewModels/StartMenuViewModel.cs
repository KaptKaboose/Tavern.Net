using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tavern.Net.Decklists;
using Tavern.Net.Game;
using Tavern.Net.GameData;

namespace Tavern.Net.ViewModels;

/// <summary>
/// The app's landing screen. Solo jumps straight into a game with the active saved deck (falling
/// back to the Change Deck screen if there isn't one yet); Change Deck is the dedicated screen to
/// load/import/save decks; View Game opens the saved-games list; Online opens the host/join lobby —
/// same active-deck requirement as Solo, since each player's deck is chosen locally before joining.
/// </summary>
public sealed partial class StartMenuViewModel : ObservableObject
{
    private readonly GrandArchiveApiClient _apiClient;
    private readonly DeckStorageService _deckStorage;

    [ObservableProperty]
    private string _activeDeckLabel = "No deck selected yet";

    [ObservableProperty]
    private bool _isStartingSolo;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>Raised when Solo is picked but there's no active deck yet — MainViewModel handles it
    /// by switching to the deck-import ("Change Deck") flow.</summary>
    public event Action? SoloRequested;

    /// <summary>Raised when the player has an active deck and it resolved successfully — MainViewModel
    /// handles it by switching straight to the game board.</summary>
    public event Action<GameSession, Player>? GameReady;

    /// <summary>Raised when the player picks Change Deck.</summary>
    public event Action? ChangeDeckRequested;

    /// <summary>Raised when the player picks View Game.</summary>
    public event Action? ViewGameRequested;

    /// <summary>Raised when the player picks Online and has an active deck.</summary>
    public event Action? OnlineRequested;

    public StartMenuViewModel(GrandArchiveApiClient apiClient, DeckStorageService deckStorage)
    {
        _apiClient = apiClient;
        _deckStorage = deckStorage;
        RefreshActiveDeck();
    }

    /// <summary>Re-reads the active deck's name — call whenever the menu becomes visible again, since
    /// the Change Deck screen may have saved or switched it in the meantime.</summary>
    public void RefreshActiveDeck()
    {
        var deck = _deckStorage.GetActiveDeck();
        ActiveDeckLabel = deck is null ? "No deck selected yet" : $"Active deck: {deck.Name}";
    }

    [RelayCommand]
    private async Task Solo()
    {
        ErrorMessage = null;

        var deck = _deckStorage.GetActiveDeck();
        if (deck is null)
        {
            SoloRequested?.Invoke();
            return;
        }

        IsStartingSolo = true;
        try
        {
            var entries = new List<DeckSessionBuilder.Entry>();
            foreach (var savedEntry in deck.Entries)
            {
                var card = await _apiClient.GetCardBySlugAsync(savedEntry.Slug);
                if (card is null)
                {
                    ErrorMessage = $"Couldn't load \"{savedEntry.CardName}\" from your active deck — open Change Deck to re-import it.";
                    return;
                }

                entries.Add(new DeckSessionBuilder.Entry(card, savedEntry.Section, savedEntry.Quantity));
            }

            var (session, player) = DeckSessionBuilder.BuildSoloSession(entries);
            GameReady?.Invoke(session, player);
        }
        finally
        {
            IsStartingSolo = false;
        }
    }

    /// <summary>Like Solo, Online needs an active deck picked beforehand (each player brings their
    /// own) — falls back to Change Deck the same way if there isn't one yet.</summary>
    [RelayCommand]
    private void Online()
    {
        ErrorMessage = null;

        if (_deckStorage.GetActiveDeck() is null)
        {
            ChangeDeckRequested?.Invoke();
            return;
        }

        OnlineRequested?.Invoke();
    }

    [RelayCommand]
    private void ChangeDeck() => ChangeDeckRequested?.Invoke();

    [RelayCommand]
    private void ViewGame() => ViewGameRequested?.Invoke();
}
