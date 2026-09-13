using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tavern.Net.Decklists;
using Tavern.Net.Game;
using Tavern.Net.GameData;

namespace Tavern.Net.ViewModels;

public sealed partial class DeckImportViewModel : ObservableObject
{
    private readonly GrandArchiveApiClient _apiClient;
    private readonly DecklistImporter _importer;

    [ObservableProperty]
    private string _decklistText = "";

    [ObservableProperty]
    private bool _isImporting;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _canStartGame;

    public ObservableCollection<ResolvedLineViewModel> ResolvedLines { get; } = new();

    /// <summary>Raised once the player confirms all lines and wants to begin play.</summary>
    public event Action<GameSession, Player>? DeckReady;

    public DeckImportViewModel(GrandArchiveApiClient apiClient)
    {
        _apiClient = apiClient;
        _importer = new DecklistImporter(apiClient);
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
                var lineViewModel = new ResolvedLineViewModel(entry);
                lineViewModel.PropertyChanged += (_, _) => RecalculateCanStartGame();
                ResolvedLines.Add(lineViewModel);
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

    [RelayCommand]
    private void StartGame()
    {
        if (!CanStartGame)
        {
            return;
        }

        var session = new GameSession();
        var player = session.AddPlayer("You");

        foreach (var line in ResolvedLines)
        {
            // Sideboards aren't meaningful in a solo goldfish session — parsed and shown, not played.
            if (line.EffectiveSection == DeckSection.Sideboard)
            {
                continue;
            }

            var card = line.SelectedCard!;
            // Champions start in the Material Deck alongside Regalia, same as Omnidex decklists —
            // materialize your starting champion onto the Field yourself once the game begins.
            var zoneType = line.EffectiveSection == DeckSection.Main ? ZoneType.MainDeck : ZoneType.MaterialDeck;

            for (var i = 0; i < line.SourceLine.Quantity; i++)
            {
                player.GetZone(zoneType).Cards.Add(new CardInstance(card));
            }
        }

        session.Shuffle(player, ZoneType.MainDeck);
        DeckReady?.Invoke(session, player);
    }

    private void RecalculateCanStartGame()
    {
        CanStartGame = ResolvedLines.Count > 0 && ResolvedLines.All(l => l.IsResolved);
    }
}
