using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tavern.Net.Game;
using Tavern.Net.GameData;
using Tavern.Net.GameData.Models;

namespace Tavern.Net.ViewModels;

public enum DeckListKind
{
    Sideboard,
    Material,
    Main,
}

/// <summary>Every copy of one card within a single list, shown as one row ("x3 Name"). The card's
/// artwork (shown on hover) is loaded lazily the first time something actually reads
/// <see cref="Artwork"/> — i.e. the first time its hover preview opens — so a 90-card deck doesn't
/// fetch 90 images up front.</summary>
public sealed class DeckCardGroup : ObservableObject
{
    private readonly DeckArtworkCache? _artworkCache;

    public DeckCardGroup(CardDto card, int count, DeckArtworkCache? artworkCache = null)
    {
        Card = card;
        Count = count;
        _artworkCache = artworkCache;
    }

    public CardDto Card { get; }

    public int Count { get; }

    public string Name => Card.Name;

    public string Detail => $"{ToTitleCase(DeckSorting.PrimaryElement(Card))} · {string.Join(", ", Card.Types)}";

    public BitmapImage? Artwork => _artworkCache?.Get(Card, () => OnPropertyChanged(nameof(Artwork)));

    private static string ToTitleCase(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..].ToLowerInvariant();
}

/// <summary>Loads and remembers card artwork by slug for the sideboard panel's hover previews —
/// shared across the panel's row objects, which are rebuilt on every change.</summary>
public sealed class DeckArtworkCache
{
    private readonly GrandArchiveApiClient _apiClient;
    private readonly Dictionary<string, BitmapImage?> _loaded = new();
    private readonly Dictionary<string, List<Action>> _waiting = new();

    public DeckArtworkCache(GrandArchiveApiClient apiClient) => _apiClient = apiClient;

    public BitmapImage? Get(CardDto card, Action onLoaded)
    {
        if (_loaded.TryGetValue(card.Slug, out var bitmap))
        {
            return bitmap;
        }

        if (_waiting.TryGetValue(card.Slug, out var callbacks))
        {
            callbacks.Add(onLoaded);
            return null;
        }

        _waiting[card.Slug] = new List<Action> { onLoaded };
        _ = LoadAsync(card);
        return null;
    }

    private async Task LoadAsync(CardDto card)
    {
        BitmapImage? bitmap = null;
        try
        {
            var imagePath = card.PrimaryEdition?.Image;
            if (!string.IsNullOrWhiteSpace(imagePath))
            {
                var localPath = await _apiClient.GetCardImagePathAsync(imagePath);
                if (localPath is not null)
                {
                    bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(localPath, UriKind.Absolute);
                    bitmap.EndInit();
                    bitmap.Freeze();
                }
            }
        }
        catch
        {
            // No preview is better than an error while hovering.
        }

        _loaded[card.Slug] = bitmap;
        var callbacks = _waiting[card.Slug];
        _waiting.Remove(card.Slug);
        foreach (var callback in callbacks)
        {
            callback();
        }
    }
}

/// <param name="Target">Null means "wherever this card naturally goes" — a double-click quick move.</param>
public sealed record DeckMoveRequest(DeckCardGroup Group, DeckListKind Source, DeckListKind? Target);

/// <summary>
/// The sideboarding panel shown before every game: the player's sideboard on one side and their
/// Material and Main decks on the other, with single copies dragged (or double-clicked) between
/// them. Edits go straight into the player's <see cref="DeckArrangement"/> — the same lists the next
/// game is built from — so what's on screen when Ready is pressed is exactly the deck that gets
/// played, and the arrangement carries over from game to game until <see cref="ResetCommand"/>
/// puts it back to the registered deck. Material cards can only live in Material and everything else
/// only in Main (or the sideboard); the one other rule enforced is having a Level 0 champion.
/// </summary>
public sealed partial class SideboardViewModel : ObservableObject
{
    private readonly DeckArrangement _deck;
    private readonly Action _onReady;
    private readonly Action? _onCancel;

    public ObservableCollection<DeckCardGroup> SideboardCards { get; } = new();

    public ObservableCollection<DeckCardGroup> MaterialCards { get; } = new();

    public ObservableCollection<DeckCardGroup> MainCards { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSideboardEmpty))]
    private int _sideboardCount;

    [ObservableProperty]
    private int _materialCount;

    [ObservableProperty]
    private int _mainCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyCanExecuteChangedFor(nameof(ReadyCommand))]
    private bool _hasLevelZeroChampion;

    public bool IsSideboardEmpty => SideboardCount == 0;

    public string? ValidationMessage =>
        HasLevelZeroChampion ? null : "Your Material deck needs a Level 0 champion to start a game.";

    /// <summary>Cancel only exists when there's a game already in progress to go back to — the very
    /// first game of a session has nothing to cancel back into.</summary>
    public bool CanCancel => _onCancel is not null;

    private readonly DeckArtworkCache? _artwork;

    private readonly Action? _onBackToMenu;

    /// <param name="board">Set only for an online game: the panel then shows the ready-up/Start
    /// controls, which live on (and bind through) the board — see GameBoardViewModel's New Game
    /// region. Null for solo, where <see cref="ReadyCommand"/> just starts the game.</param>
    /// <param name="onBackToMenu">Offered instead of Cancel when there's no game to cancel back to
    /// (the first game of a session) — otherwise the modal would leave no way out.</param>
    public SideboardViewModel(
        DeckArrangement deck,
        Action onReady,
        Action? onCancel = null,
        GrandArchiveApiClient? apiClient = null,
        GameBoardViewModel? board = null,
        Action? onBackToMenu = null)
    {
        _deck = deck;
        _onReady = onReady;
        _onCancel = onCancel;
        _onBackToMenu = onBackToMenu;
        Board = board;
        _artwork = apiClient is null ? null : new DeckArtworkCache(apiClient);
        Refresh();
    }

    public GameBoardViewModel? Board { get; }

    public bool IsOnline => Board is not null;

    public bool IsSolo => Board is null;

    public bool CanBackToMenu => _onBackToMenu is not null && _onCancel is null;

    /// <summary>The host's "who goes first" pick — only for a later game. A session's first game (no
    /// Cancel: nothing to go back to) already had its first player decided in the lobby, and
    /// offering it here would just contradict that.</summary>
    public bool ShowFirstPlayerChoice => Board is { IsHost: true } && _onCancel is not null;

    public bool ShowWaitingForHost => Board is { IsGuest: true };

    /// <summary>Online: true while this player is Ready — edits are locked so what the opponent is
    /// waiting on can't change under them; un-readying unlocks.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveDeckCardCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    private bool _isLocked;

    [RelayCommand]
    private void BackToMenu() => _onBackToMenu?.Invoke();

    // --- Move-count prompt: a double-click on a row with more than one copy asks how many to move
    // (rather than moving one copy per click, which made it easy to move things by accident while
    // clicking repeatedly). A row with a single copy just moves.

    private DeckMoveRequest? _pendingMove;
    private bool _hasTypedMoveCount;

    [ObservableProperty]
    private bool _isPromptingMoveCount;

    [ObservableProperty]
    private int _moveCount = 1;

    [ObservableProperty]
    private int _moveCountMax = 1;

    [ObservableProperty]
    private string? _movePromptTitle;

    [ObservableProperty]
    private string? _movePromptTarget;

    private void BeginMovePrompt(DeckMoveRequest request)
    {
        _pendingMove = request;
        _hasTypedMoveCount = false;
        MoveCountMax = ListFor(request.Source).Count(c => c.Slug == request.Group.Card.Slug);
        MoveCount = 1;
        MovePromptTitle = $"Move how many {request.Group.Name}?";
        MovePromptTarget = $"{request.Source} → {ResolveTarget(request)}";
        IsPromptingMoveCount = true;
    }

    [RelayCommand]
    private void IncreaseMoveCount() => MoveCount = Math.Min(MoveCount + 1, MoveCountMax);

    [RelayCommand]
    private void DecreaseMoveCount() => MoveCount = Math.Max(MoveCount - 1, 1);

    [RelayCommand]
    private void ConfirmMove()
    {
        var request = _pendingMove;
        var count = MoveCount;
        CancelMovePromptState();

        if (request is not null)
        {
            MoveCopies(request, count);
        }
    }

    [RelayCommand]
    private void CancelMove() => CancelMovePromptState();

    private void CancelMovePromptState()
    {
        _pendingMove = null;
        IsPromptingMoveCount = false;
    }

    /// <summary>Keyboard handling while the move-count prompt is up: digits type into the count
    /// (clamped to what's available), Backspace deletes, Enter confirms, Escape cancels. Always
    /// swallows the key, like every other modal.</summary>
    public bool HandleKey(Key key)
    {
        if (GameBoardViewModel.TryGetDigit(key, out var digit))
        {
            var typed = _hasTypedMoveCount ? MoveCount * 10 + digit : digit;
            MoveCount = Math.Clamp(typed, 1, MoveCountMax);
            _hasTypedMoveCount = true;
        }
        else if (key == Key.Back)
        {
            MoveCount = Math.Max(MoveCount / 10, 1);
        }
        else if (key == Key.Enter)
        {
            ConfirmMove();
        }
        else if (key == Key.Escape)
        {
            CancelMovePromptState();
        }

        return true;
    }

    /// <summary>Whether a card belongs in the Material deck. What the registered deck itself did with
    /// this card wins (so an odd Material-deck card is still treated as Material); anything else falls
    /// back to the card's type (champions and regalia are Material).</summary>
    public bool IsMaterialCard(CardDto card)
    {
        if (_deck.RegisteredMaterial.Any(c => c.Slug == card.Slug))
        {
            return true;
        }

        if (_deck.RegisteredMain.Any(c => c.Slug == card.Slug))
        {
            return false;
        }

        return card.IsChampionOrRegalia;
    }

    private List<CardDto> ListFor(DeckListKind kind) => kind switch
    {
        DeckListKind.Sideboard => _deck.Sideboard,
        DeckListKind.Material => _deck.Material,
        _ => _deck.Main,
    };

    private DeckListKind ResolveTarget(DeckMoveRequest request)
    {
        if (request.Target is { } target)
        {
            return target;
        }

        if (request.Source != DeckListKind.Sideboard)
        {
            return DeckListKind.Sideboard;
        }

        return IsMaterialCard(request.Group.Card) ? DeckListKind.Material : DeckListKind.Main;
    }

    private bool CanMoveDeckCard(DeckMoveRequest? request)
    {
        if (request is null || IsLocked)
        {
            return false;
        }

        var target = ResolveTarget(request);
        if (target == request.Source || !ListFor(request.Source).Any(c => c.Slug == request.Group.Card.Slug))
        {
            return false;
        }

        return target switch
        {
            DeckListKind.Material => IsMaterialCard(request.Group.Card),
            DeckListKind.Main => !IsMaterialCard(request.Group.Card),
            _ => true,
        };
    }

    /// <summary>Moves a card between lists — a drag/drop (an explicit target) moves one copy; a
    /// double-click (no target: the card's natural other side) moves a single copy straight away, or
    /// asks how many first when the row has several.</summary>
    [RelayCommand(CanExecute = nameof(CanMoveDeckCard))]
    private void MoveDeckCard(DeckMoveRequest? request)
    {
        if (request is null)
        {
            return;
        }

        if (request.Target is null && request.Group.Count > 1)
        {
            BeginMovePrompt(request);
            return;
        }

        MoveCopies(request, 1);
    }

    private void MoveCopies(DeckMoveRequest request, int count)
    {
        var source = ListFor(request.Source);
        var target = ListFor(ResolveTarget(request));

        for (var i = 0; i < count; i++)
        {
            var index = source.FindIndex(c => c.Slug == request.Group.Card.Slug);
            if (index < 0)
            {
                break;
            }

            var card = source[index];
            source.RemoveAt(index);
            target.Add(card);
        }

        Refresh();
    }

    /// <summary>Puts every list back to the originally registered deck.</summary>
    [RelayCommand(CanExecute = nameof(CanReset))]
    private void Reset()
    {
        _deck.Reset();
        Refresh();
    }

    private bool CanReset() => !IsLocked;

    [RelayCommand(CanExecute = nameof(HasLevelZeroChampion))]
    private void Ready() => _onReady();

    [RelayCommand]
    private void Cancel() => _onCancel?.Invoke();

    private void Refresh()
    {
        DeckSorting.SortInPlace(_deck);

        Rebuild(SideboardCards, _deck.Sideboard);
        Rebuild(MaterialCards, _deck.Material);
        Rebuild(MainCards, _deck.Main);

        SideboardCount = _deck.Sideboard.Count;
        MaterialCount = _deck.Material.Count;
        MainCount = _deck.Main.Count;
        HasLevelZeroChampion = _deck.HasLevelZeroChampion;
    }

    private void Rebuild(ObservableCollection<DeckCardGroup> target, List<CardDto> sorted)
    {
        target.Clear();

        // GroupBy keeps first-appearance order, and the list is already sorted, so the rows come
        // out in the same order the cards are stored in.
        foreach (var group in sorted.GroupBy(c => c.Slug))
        {
            target.Add(new DeckCardGroup(group.First(), group.Count(), _artwork));
        }
    }
}
