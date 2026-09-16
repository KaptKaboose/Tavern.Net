using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tavern.Net.Decklists;
using Tavern.Net.Game;
using Tavern.Net.GameData;

namespace Tavern.Net.ViewModels;

/// <summary>The "Change Deck" screen: paste/import a new decklist, or load one saved earlier, then
/// optionally save it under a name and/or start a solo game with it.</summary>
public sealed partial class DeckImportViewModel : ObservableObject
{
    private readonly GrandArchiveApiClient _apiClient;
    private readonly DeckStorageService _deckStorage;
    private readonly DecklistImporter _importer;

    /// <summary>Whether Start Game should be offered at all. True when this screen was reached via
    /// Solo (with no active deck yet, so it's standing in for "pick a deck to play now"); false when
    /// reached via the menu's Change Deck option, which is purely for managing decks — from there we
    /// don't know whether the player even wants to play right now, let alone Solo vs. Online.</summary>
    public bool AllowStartGame { get; }

    [ObservableProperty]
    private string _decklistText = "";

    [ObservableProperty]
    private bool _isImporting;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveDeckCommand))]
    private bool _canStartGame;

    [ObservableProperty]
    private string? _cacheStatusMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveDeckCommand))]
    private string _deckName = "";

    [ObservableProperty]
    private string? _saveStatusMessage;

    [ObservableProperty]
    private SavedDeck? _selectedSavedDeck;

    public ObservableCollection<ResolvedLineViewModel> ResolvedLines { get; } = new();

    public ObservableCollection<SavedDeck> SavedDecks { get; } = new();

    /// <summary>Raised once the player confirms all lines and wants to begin play.</summary>
    public event Action<GameSession, Player>? DeckReady;

    /// <summary>Raised when the player wants to return to the start menu without starting a game.</summary>
    public event Action? BackRequested;

    public DeckImportViewModel(GrandArchiveApiClient apiClient, DeckStorageService deckStorage, bool allowStartGame)
    {
        _apiClient = apiClient;
        _deckStorage = deckStorage;
        _importer = new DecklistImporter(apiClient);
        AllowStartGame = allowStartGame;
        RefreshSavedDecks();
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (string.IsNullOrWhiteSpace(DecklistText))
        {
            ErrorMessage = "Paste a decklist first.";
            return;
        }

        IsImporting = true;
        ErrorMessage = null;
        SaveStatusMessage = null;
        ResolvedLines.Clear();
        CanStartGame = false;

        try
        {
            var parsed = DecklistParser.Parse(DecklistText);
            if (parsed.Count == 0)
            {
                ErrorMessage = "Couldn't find any card lines in that text.";
                return;
            }

            var resolved = await _importer.ResolveAsync(parsed);
            foreach (var entry in resolved)
            {
                AddResolvedLine(new ResolvedLineViewModel(entry));
            }

            RecalculateCanStartGame();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Import failed: {ex.Message}";
        }
        finally
        {
            IsImporting = false;
        }
    }

    /// <summary>Loads a previously saved deck straight from its stored entries — no re-resolution
    /// against the search API, just a per-card slug lookup (disk-cached from the original import),
    /// so an unambiguous deck reloads exactly as it was saved.</summary>
    [RelayCommand]
    private async Task LoadDeckAsync(SavedDeck? deck)
    {
        if (deck is null)
        {
            return;
        }

        IsImporting = true;
        ErrorMessage = null;
        SaveStatusMessage = null;
        ResolvedLines.Clear();
        CanStartGame = false;

        try
        {
            foreach (var entry in deck.Entries)
            {
                var card = await _apiClient.GetCardBySlugAsync(entry.Slug);
                if (card is null)
                {
                    ErrorMessage = $"Couldn't find \"{entry.CardName}\" anymore — it may have been renamed. Try re-importing this deck instead.";
                    ResolvedLines.Clear();
                    return;
                }

                var sourceLine = new ParsedDecklistLine(entry.Quantity, entry.CardName, entry.Section);
                var resolvedEntry = new ResolvedDecklistEntry(sourceLine, entry.Section, ResolutionStatus.Matched, card, new[] { card });
                AddResolvedLine(new ResolvedLineViewModel(resolvedEntry));
            }

            DecklistText = deck.DecklistText;
            DeckName = deck.Name;
            _deckStorage.SetActiveDeck(deck.Name);
            RecalculateCanStartGame();
        }
        finally
        {
            IsImporting = false;
        }
    }

    [RelayCommand]
    private void DeleteDeck(SavedDeck? deck)
    {
        if (deck is null)
        {
            return;
        }

        _deckStorage.Delete(deck.Name);
        RefreshSavedDecks();
        if (SelectedSavedDeck?.Name == deck.Name)
        {
            SelectedSavedDeck = null;
        }

        SaveStatusMessage = $"Deleted \"{deck.Name}\".";
    }

    [RelayCommand(CanExecute = nameof(CanSaveDeck))]
    private void SaveDeck()
    {
        var entries = new List<SavedDeckEntry>();
        foreach (var line in ResolvedLines.Where(l => l.EffectiveSection != DeckSection.Sideboard))
        {
            var card = line.SelectedCard!;

            // The card data we already have came back from SearchCardsAsync, cached under a
            // different key than GetCardBySlugAsync looks under — write it into that cache too, so
            // the very first LoadDeckAsync/Solo reload of this deck doesn't re-fetch every card.
            _apiClient.CacheCard(card);
            entries.Add(new SavedDeckEntry(card.Slug, card.Name, line.SourceLine.Quantity, line.EffectiveSection));
        }

        var deck = new SavedDeck(DeckName.Trim(), DecklistText, DateTime.UtcNow, entries);
        _deckStorage.SaveAndActivate(deck);
        RefreshSavedDecks();
        SelectedSavedDeck = SavedDecks.FirstOrDefault(d => d.Name == deck.Name);
        SaveStatusMessage = $"Saved \"{deck.Name}\" and set it as your active deck.";
    }

    private bool CanSaveDeck() => CanStartGame && !string.IsNullOrWhiteSpace(DeckName);

    [RelayCommand]
    private void ClearCache()
    {
        _apiClient.ClearCache();
        CacheStatusMessage = "Cache cleared — the next import will re-fetch card data.";
    }

    [RelayCommand]
    private void StartGame()
    {
        if (!AllowStartGame || !CanStartGame)
        {
            return;
        }

        var entries = ResolvedLines.Select(l =>
            new DeckSessionBuilder.Entry(l.SelectedCard!, l.EffectiveSection, l.SourceLine.Quantity));

        var (session, player) = DeckSessionBuilder.BuildSoloSession(entries);
        DeckReady?.Invoke(session, player);
    }

    [RelayCommand]
    private void Back() => BackRequested?.Invoke();

    private void AddResolvedLine(ResolvedLineViewModel lineViewModel)
    {
        lineViewModel.PropertyChanged += (_, _) => RecalculateCanStartGame();
        ResolvedLines.Add(lineViewModel);
    }

    private void RefreshSavedDecks()
    {
        SavedDecks.Clear();
        foreach (var deck in _deckStorage.LoadAll().OrderByDescending(d => d.SavedAtUtc))
        {
            SavedDecks.Add(deck);
        }
    }

    private void RecalculateCanStartGame()
    {
        CanStartGame = ResolvedLines.Count > 0 && ResolvedLines.All(l => l.IsResolved);
    }
}
