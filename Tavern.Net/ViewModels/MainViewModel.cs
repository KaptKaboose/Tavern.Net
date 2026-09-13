using CommunityToolkit.Mvvm.ComponentModel;
using Tavern.Net.GameData;

namespace Tavern.Net.ViewModels;

/// <summary>Root view model — switches between the deck-import screen and the game board.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly GrandArchiveApiClient _apiClient = new();

    [ObservableProperty]
    private ObservableObject _currentView;

    public MainViewModel()
    {
        _currentView = CreateImportViewModel();
    }

    private DeckImportViewModel CreateImportViewModel()
    {
        var importViewModel = new DeckImportViewModel(_apiClient);
        importViewModel.DeckReady += (session, player) =>
        {
            CurrentView = new GameBoardViewModel(session, player, _apiClient);
        };
        return importViewModel;
    }
}
