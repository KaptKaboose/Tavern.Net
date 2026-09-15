using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Tavern.Net.Game;
using Tavern.Net.GameData;
using Tavern.Net.GameData.Models;

namespace Tavern.Net.ViewModels;

/// <summary>
/// Bindable, read-only wrapper around a <see cref="CardSnapshot"/> for the Major-event viewer —
/// parallel to <see cref="CardViewModel"/>, but there's no CardInstance to observe (a snapshot
/// never changes) and no drag/tap/flip behavior, since viewing history never mutates the live
/// game. <see cref="Artwork"/> loads whichever single face (front or flipped) the snapshot's
/// IsFlipped calls for, rather than both like CardViewModel — a snapshot's flip state is fixed, so
/// there's no toggle to support. <see cref="FrontArtwork"/> always loads the true printed face
/// regardless of IsFlipped, for the zoom overlay: the player owns this card in every snapshot (it's
/// always their own board), so zooming should never be limited to whatever face happens to be
/// showing on the board.
/// </summary>
public sealed partial class CardSnapshotViewModel : ObservableObject
{
    private static readonly Lazy<BitmapImage> GenericCardBack = new(() =>
        LoadBitmap(new Uri("pack://application:,,,/GameData/Images/Grand%20Archive%20Back.jpg", UriKind.Absolute)));

    public CardSnapshot Snapshot { get; }

    /// <summary>The board this snapshot belongs to, so CardZoomBehavior can reach
    /// ZoomSnapshotCardCommand — parallel to <see cref="CardViewModel.Board"/>.</summary>
    public GameBoardViewModel Board { get; }

    private CardOtherOrientation? OtherOrientation => Snapshot.Card.PrimaryEdition?.OtherOrientations?.FirstOrDefault();

    public string Name => Snapshot.IsFlipped ? (OtherOrientation?.Name ?? Snapshot.Card.Name) : Snapshot.Card.Name;

    [ObservableProperty]
    private BitmapImage? _artwork;

    [ObservableProperty]
    private BitmapImage? _frontArtwork;

    public CardSnapshotViewModel(CardSnapshot snapshot, GrandArchiveApiClient apiClient, GameBoardViewModel board)
    {
        Snapshot = snapshot;
        Board = board;
        _ = LoadArtworkAsync(apiClient);
    }

    private async Task LoadArtworkAsync(GrandArchiveApiClient apiClient)
    {
        var imagePath = Snapshot.Card.PrimaryEdition?.Image;
        if (!string.IsNullOrWhiteSpace(imagePath))
        {
            var localPath = await apiClient.GetCardImagePathAsync(imagePath);
            if (localPath is not null)
            {
                FrontArtwork = LoadBitmap(new Uri(localPath, UriKind.Absolute));
            }
        }

        if (!Snapshot.IsFlipped)
        {
            Artwork = FrontArtwork;
            return;
        }

        var otherImagePath = OtherOrientation?.Edition?.Image;
        if (string.IsNullOrWhiteSpace(otherImagePath))
        {
            Artwork = GenericCardBack.Value;
            return;
        }

        var flippedLocalPath = await apiClient.GetCardImagePathAsync(otherImagePath);
        Artwork = flippedLocalPath is not null ? LoadBitmap(new Uri(flippedLocalPath, UriKind.Absolute)) : GenericCardBack.Value;
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
