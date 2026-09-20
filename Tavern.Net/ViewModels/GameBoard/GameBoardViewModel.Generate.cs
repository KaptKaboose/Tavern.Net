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

/// <summary>Generate: adding extra copies of a card to the Main Deck.</summary>
public sealed partial class GameBoardViewModel
{
    // --- Generate: a card effect that produces extra copies of a card already in the deck. Unlike
    // every other Actions-menu entry, the source card might not be reachable anywhere on the board
    // to zoom (every remaining copy buried in Main, or the player started with zero copies), so
    // this doesn't reuse the generic ArmedChord/count flow — it's its own two-step chain: search
    // the card catalog by name, pick a result, then a count (GameSession.GenerateCard, looped).

    [ObservableProperty]
    private bool _isGenerateSearchOpen;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchGenerateCardsCommand))]
    private string _generateSearchQuery = "";

    [ObservableProperty]
    private bool _isGenerateSearching;

    public ObservableCollection<CardDto> GenerateSearchResults { get; } = new();

    private CardDto? _generateCandidateCard;

    [ObservableProperty]
    private bool _isGenerateCountOpen;

    [ObservableProperty]
    private int _generateCount = 1;

    private bool _hasTypedGenerateCount;

    partial void OnIsGenerateSearchOpenChanged(bool value)
    {
        if (value)
        {
            CloseActionsMenu();
        }
    }

    partial void OnIsGenerateCountOpenChanged(bool value)
    {
        if (value)
        {
            CloseActionsMenu();
        }
    }

    [RelayCommand]
    private void OpenGenerateSearch()
    {
        CloseActionsMenu();
        GenerateSearchQuery = "";
        GenerateSearchResults.Clear();
        IsGenerateSearchOpen = true;
    }

    private bool CanSearchGenerateCards() => !string.IsNullOrWhiteSpace(GenerateSearchQuery);

    [RelayCommand(CanExecute = nameof(CanSearchGenerateCards))]
    private async Task SearchGenerateCardsAsync()
    {
        var query = GenerateSearchQuery.Trim();
        IsGenerateSearching = true;
        try
        {
            var response = await _apiClient.SearchCardsAsync(query, pageSize: 20);
            GenerateSearchResults.Clear();
            foreach (var card in response.Data)
            {
                GenerateSearchResults.Add(card);
            }
        }
        finally
        {
            IsGenerateSearching = false;
        }
    }

    [RelayCommand]
    private void SelectGenerateCard(CardDto? card)
    {
        if (card is null)
        {
            return;
        }

        _generateCandidateCard = card;
        IsGenerateSearchOpen = false;
        GenerateCount = 1;
        _hasTypedGenerateCount = false;
        IsGenerateCountOpen = true;
    }

    [RelayCommand]
    private void CancelGenerateSearch() => IsGenerateSearchOpen = false;

    [RelayCommand]
    private void IncreaseGenerateCount() => GenerateCount = Math.Min(GenerateCount + 1, 99);

    [RelayCommand]
    private void DecreaseGenerateCount() => GenerateCount = Math.Max(GenerateCount - 1, 1);

    [RelayCommand]
    private void CancelGenerateCount()
    {
        IsGenerateCountOpen = false;
        _generateCandidateCard = null;
    }

    [RelayCommand]
    private void ConfirmGenerateCount()
    {
        IsGenerateCountOpen = false;

        if (_generateCandidateCard is not { } cardDto)
        {
            return;
        }

        var count = GenerateCount;
        _generateCandidateCard = null;

        var generated = new List<CardInstance>(count);
        for (var i = 0; i < count; i++)
        {
            generated.Add(_session.GenerateCard(Player, cardDto));
        }

        ArmUndo(
            "Generate",
            () =>
            {
                var deck = Player.GetZone(ZoneType.MainDeck);
                foreach (var card in generated)
                {
                    deck.Cards.Remove(card);
                }
            },
            // Nothing shuffles automatically — a player could reasonably assume otherwise, so this
            // says exactly where the new cards landed rather than leaving them to guess.
            detailMessage: "Added to the bottom of the deck — not shuffled in.");
    }
}
