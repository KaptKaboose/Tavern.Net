using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Tavern.Net.Game;
using Tavern.Net.GameData;

namespace Tavern.Net.ViewModels;

/// <summary>Bindable wrapper around a <see cref="CardInstance"/>, including lazily-loaded artwork.</summary>
public sealed partial class CardViewModel : ObservableObject
{
    private readonly GrandArchiveApiClient _apiClient;

    public CardInstance Instance { get; }

    /// <summary>
    /// The board this card belongs to, so context-menu/drop commands can be reached with a
    /// plain property-path binding (e.g. "Board.PlayToFieldCommand") that works no matter
    /// where the card is visually hosted — a normal zone list, a ContextMenu, or a Popup.
    /// Those all break a RelativeSource/AncestorType visual-tree walk, but not a direct
    /// binding relative to the card's own (always-correct) DataContext.
    /// </summary>
    public GameBoardViewModel Board { get; }

    public string Name => Instance.Card.Name;

    public bool IsTapped => Instance.IsTapped;

    [ObservableProperty]
    private BitmapImage? _artwork;

    public CardViewModel(CardInstance instance, GrandArchiveApiClient apiClient, GameBoardViewModel board)
    {
        Instance = instance;
        _apiClient = apiClient;
        Board = board;
        Instance.PropertyChanged += (_, _) => OnPropertyChanged(nameof(IsTapped));
        _ = LoadArtworkAsync();
    }

    private async Task LoadArtworkAsync()
    {
        var imagePath = Instance.Card.PrimaryEdition?.Image;
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return;
        }

        var localPath = await _apiClient.GetCardImagePathAsync(imagePath);
        if (localPath is null)
        {
            return;
        }

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(localPath, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();

        Artwork = bitmap;
    }
}
