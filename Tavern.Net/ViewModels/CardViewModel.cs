using System.ComponentModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Tavern.Net.Game;
using Tavern.Net.GameData;
using Tavern.Net.GameData.Models;

namespace Tavern.Net.ViewModels;

/// <summary>
/// Bindable wrapper around a <see cref="CardInstance"/>. Loads both faces up front — the front
/// artwork, and the flip target (a real other-orientation image for a double-faced card like a
/// Fatestone, or the generic card back for an ordinary one) — so double-clicking to flip
/// (CardInstance.IsFlipped) just swaps which already-loaded bitmap <see cref="Artwork"/> points to.
/// </summary>
public sealed partial class CardViewModel : ObservableObject
{
    private static readonly Lazy<BitmapImage> GenericCardBack = new(() =>
        LoadBitmap(new Uri("pack://application:,,,/GameData/Images/Grand%20Archive%20Back.jpg", UriKind.Absolute)));

    private readonly GrandArchiveApiClient _apiClient;

    private BitmapImage? _frontArtwork;
    private BitmapImage? _flippedArtwork;

    public CardInstance Instance { get; }

    /// <summary>
    /// The board this card belongs to, so context-menu/drop commands can be reached with a
    /// plain property-path binding (e.g. "Board.PlayToFieldCommand") that works no matter
    /// where the card is visually hosted — a normal zone list, a ContextMenu, or a Popup.
    /// Those all break a RelativeSource/AncestorType visual-tree walk, but not a direct
    /// binding relative to the card's own (always-correct) DataContext.
    /// </summary>
    public GameBoardViewModel Board { get; }

    private CardOtherOrientation? OtherOrientation => Instance.Card.PrimaryEdition?.OtherOrientations?.FirstOrDefault();

    public string Name => Instance.IsFlipped ? (OtherOrientation?.Name ?? Instance.Card.Name) : Instance.Card.Name;

    public bool IsTapped => Instance.IsTapped;

    [ObservableProperty]
    private BitmapImage? _artwork;

    public CardViewModel(CardInstance instance, GrandArchiveApiClient apiClient, GameBoardViewModel board)
    {
        Instance = instance;
        _apiClient = apiClient;
        Board = board;
        Instance.PropertyChanged += OnInstancePropertyChanged;
        _ = LoadArtworkAsync();
    }

    private void OnInstancePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(CardInstance.IsTapped):
                OnPropertyChanged(nameof(IsTapped));
                break;
            case nameof(CardInstance.IsFlipped):
                OnPropertyChanged(nameof(Name));
                Artwork = Instance.IsFlipped ? _flippedArtwork : _frontArtwork;
                break;
        }
    }

    private async Task LoadArtworkAsync()
    {
        var imagePath = Instance.Card.PrimaryEdition?.Image;
        if (!string.IsNullOrWhiteSpace(imagePath))
        {
            var localPath = await _apiClient.GetCardImagePathAsync(imagePath);
            if (localPath is not null)
            {
                _frontArtwork = LoadBitmap(new Uri(localPath, UriKind.Absolute));
            }
        }

        var otherImagePath = OtherOrientation?.Edition?.Image;
        if (!string.IsNullOrWhiteSpace(otherImagePath))
        {
            var flippedLocalPath = await _apiClient.GetCardImagePathAsync(otherImagePath);
            _flippedArtwork = flippedLocalPath is not null ? LoadBitmap(new Uri(flippedLocalPath, UriKind.Absolute)) : GenericCardBack.Value;
        }
        else
        {
            _flippedArtwork = GenericCardBack.Value;
        }

        Artwork = Instance.IsFlipped ? _flippedArtwork : _frontArtwork;
    }

    private static BitmapImage LoadBitmap(Uri uri)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = uri;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
