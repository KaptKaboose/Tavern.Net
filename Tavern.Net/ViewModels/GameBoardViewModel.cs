using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tavern.Net.Game;
using Tavern.Net.GameData;

namespace Tavern.Net.ViewModels;

public sealed partial class GameBoardViewModel : ObservableObject, IKeyboardShortcutHandler
{
    private readonly GameSession _session;
    private readonly GrandArchiveApiClient _apiClient;

    // Set by the 'N' handler when StartNewGame hands back cards to glimpse instead of a normal
    // opening hand; consumed in MoveGlimpseCard the instant that glimpse finishes, drawing the
    // hand StartNewGame skipped in favor of the glimpse.
    private bool _drawAfterGlimpsing;

    public Player Player { get; }

    public GameStats Stats => Player.Stats;

    public ZoneViewModel Hand { get; }
    public ZoneViewModel Field { get; }
    public ZoneViewModel MainDeck { get; }
    public ZoneViewModel MaterialDeck { get; }
    public ZoneViewModel Graveyard { get; }
    public ZoneViewModel Banishment { get; }
    public ZoneViewModel Memory { get; }
    public ZoneViewModel Champion { get; }
    public ZoneViewModel Tokens { get; }

    /// <summary>The card currently shown full-size in the zoom overlay, or null when it's closed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCounterAndStatusPanel))]
    private CardViewModel? _zoomedCard;

    /// <summary>
    /// Whether the Zoom overlay's status/counter side panel should show for the currently-zoomed
    /// card — Counter/statuses are only ever meaningful in play (Field or Champion; see
    /// GameSession.MoveCard's reset rule), so the panel is irrelevant everywhere else (Hand, a
    /// deck peek, Glimpse, ...). A card being zoomed from Glimpse isn't in any Player.Zones entry
    /// at all (GlimpseStaging/Top/Bottom are ViewModel-only lists, not domain zones — see their
    /// own doc comment below), hence the null-safe FirstOrDefault rather than First.
    /// </summary>
    public bool ShowCounterAndStatusPanel
    {
        get
        {
            if (ZoomedCard is null)
            {
                return false;
            }

            var zone = Player.Zones.FirstOrDefault(kv => kv.Value.Cards.Contains(ZoomedCard.Instance)).Value?.Type;
            return zone == ZoneType.Field || zone == ZoneType.Champion;
        }
    }

    /// <summary>The pile currently fanned out in the peek overlay, or null when it's closed.</summary>
    [ObservableProperty]
    private PeekedZoneInfo? _peekedZone;

    /// <summary>Mirrors <see cref="GameSession.CurrentPhase"/> so the board can bind to it.</summary>
    [ObservableProperty]
    private TurnPhase _currentPhase;

    /// <summary>
    /// Glimpsing state — self-contained ViewModel-side lists, not domain zones, since a glimpsed
    /// card is genuinely removed from Main until <see cref="FinishGlimpse"/> puts every one of
    /// them back (see GameSession.GlimpseNextCard/FinishGlimpse). Staging holds cards drawn by 'G'
    /// that haven't been sorted yet; Top/Bottom are the two sortable piles they get dragged into.
    /// </summary>
    [ObservableProperty]
    private bool _isGlimpsing;

    public ObservableCollection<CardViewModel> GlimpseStaging { get; } = new();
    public ObservableCollection<CardViewModel> GlimpseTop { get; } = new();
    public ObservableCollection<CardViewModel> GlimpseBottom { get; } = new();

    public GameBoardViewModel(GameSession session, Player player, GrandArchiveApiClient apiClient)
    {
        _session = session;
        _apiClient = apiClient;
        Player = player;
        _currentPhase = session.CurrentPhase;

        Hand = new ZoneViewModel(player.GetZone(ZoneType.Hand), apiClient, this);
        Field = new ZoneViewModel(player.GetZone(ZoneType.Field), apiClient, this);
        MainDeck = new ZoneViewModel(player.GetZone(ZoneType.MainDeck), apiClient, this);
        MaterialDeck = new ZoneViewModel(player.GetZone(ZoneType.MaterialDeck), apiClient, this);
        Graveyard = new ZoneViewModel(player.GetZone(ZoneType.Graveyard), apiClient, this);
        Banishment = new ZoneViewModel(player.GetZone(ZoneType.Banishment), apiClient, this);
        Memory = new ZoneViewModel(player.GetZone(ZoneType.Memory), apiClient, this);
        Champion = new ZoneViewModel(player.GetZone(ZoneType.Champion), apiClient, this);
        Tokens = new ZoneViewModel(player.GetZone(ZoneType.Tokens), apiClient, this);

        // Populated once here, not by GameSession.StartNewGame — the Tokens zone is a static
        // catalog, not deck content, and StartNewGame deliberately leaves it untouched (see its
        // own comment) so it survives every subsequent "New Game" reset without refetching.
        if (player.GetZone(ZoneType.Tokens).Cards.Count == 0)
        {
            _ = LoadTokenCatalogAsync();
        }
    }

    private async Task LoadTokenCatalogAsync()
    {
        var tokens = await _apiClient.GetTokensAsync();

        // Pin tokens this deck's own cards can actually summon (per the API's referenced_by
        // links) to the front, so the common case doesn't mean scrolling the whole catalog.
        // Deck cards are already loaded into Main/Material by DeckImportViewModel.StartGame
        // before GameBoardViewModel is ever constructed, so this reads real names, not a stub.
        var deckCardNames = Player.GetZone(ZoneType.MainDeck).Cards
            .Concat(Player.GetZone(ZoneType.MaterialDeck).Cards)
            .Select(c => c.Card.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var ordered = tokens.OrderByDescending(token => token.ReferencedBy.Any(r => deckCardNames.Contains(r.Name)));

        var tokenZone = Player.GetZone(ZoneType.Tokens);
        foreach (var token in ordered)
        {
            tokenZone.Cards.Add(new CardInstance(token, ZoneType.Tokens));
        }
    }

    [RelayCommand]
    private void DrawCard() => _session.DrawCard(Player);

    [RelayCommand]
    private void DrawCardIntoMemory() => _session.DrawCard(Player, ZoneType.MainDeck, ZoneType.Memory);

    /// <summary>'G'. First press opens the overlay and draws the top card; every press after that
    /// (while it's open) draws one more, appending to Staging. No-ops once Main is empty.</summary>
    [RelayCommand]
    private void GlimpseNext()
    {
        var card = _session.GlimpseNextCard(Player);
        if (card is null)
        {
            return;
        }

        IsGlimpsing = true;
        GlimpseStaging.Add(new CardViewModel(card, _apiClient, this));
    }

    /// <summary>
    /// Handles every drag within the Glimpse overlay: removes the card from whichever of the
    /// three lists currently holds it, then inserts it into the target list — at InsertBefore's
    /// position if given, otherwise at the end. Auto-finishes (and closes) the instant Staging
    /// empties out, since that means every drawn card has been sorted.
    /// </summary>
    [RelayCommand]
    private void MoveGlimpseCard(GlimpseDropRequest? request)
    {
        if (request is null || request.Card == request.InsertBefore)
        {
            return;
        }

        GlimpseStaging.Remove(request.Card);
        GlimpseTop.Remove(request.Card);
        GlimpseBottom.Remove(request.Card);

        var targetList = request.Target == GlimpseTarget.Top ? GlimpseTop : GlimpseBottom;
        var insertIndex = request.InsertBefore is not null ? targetList.IndexOf(request.InsertBefore) : -1;
        if (insertIndex < 0)
        {
            targetList.Add(request.Card);
        }
        else
        {
            targetList.Insert(insertIndex, request.Card);
        }

        if (GlimpseStaging.Count == 0)
        {
            _session.FinishGlimpse(Player, GlimpseTop.Select(c => c.Instance).ToList(), GlimpseBottom.Select(c => c.Instance).ToList());
            GlimpseTop.Clear();
            GlimpseBottom.Clear();
            IsGlimpsing = false;

            if (_drawAfterGlimpsing)
            {
                _drawAfterGlimpsing = false;
                _session.DrawStartingHand(Player);
            }
        }
    }

    /// <summary>Advances to the next phase of the turn; advancing past End starts the next turn.</summary>
    [RelayCommand]
    private void NextPhase()
    {
        _session.AdvancePhase(Player);
        CurrentPhase = _session.CurrentPhase;
    }

    // Set by the 'B' case below; consumed by the very next key press. Keeps a bare digit key
    // from meaning anything on its own — it's only "banish count" for the one keystroke right
    // after 'B', so future digit-driven shortcuts elsewhere can't collide with this one.
    private bool _banishArmed;

    /// <summary>Keyboard shortcuts for the board — add more cases here as they come up.</summary>
    public bool HandleKey(Key key)
    {
        // Glimpsing locks out everything except G itself, per its own design — swallow every
        // other key outright rather than letting it fall through to the normal switch below.
        if (IsGlimpsing)
        {
            if (key == Key.G && GlimpseNextCommand.CanExecute(null))
            {
                GlimpseNextCommand.Execute(null);
            }

            return true;
        }

        if (_banishArmed)
        {
            _banishArmed = false;
            if (TryGetDigit(key, out var count))
            {
                _session.Banish(Player, count);
                return true;
            }
            // Any non-digit key cancels the chord and falls through to its own normal handling.
        }

        switch (key)
        {
            case Key.Space:
                if (NextPhaseCommand.CanExecute(null))
                {
                    NextPhaseCommand.Execute(null);
                }
                return true;
            case Key.D:
                if (DrawCardCommand.CanExecute(null))
                {
                    DrawCardCommand.Execute(null);
                }
                return true;
            case Key.S:
                if (DrawCardIntoMemoryCommand.CanExecute(null))
                {
                    DrawCardIntoMemoryCommand.Execute(null);
                }
                return true;
            case Key.N:
                // StartNewGame already draws the opening hand as part of setup — unless the base
                // champion's effect calls for an opening glimpse instead, in which case it hands
                // the already-drawn cards back here so they can actually be shown (it has no
                // reference to GlimpseStaging/IsGlimpsing to do that itself).
                var glimpsedOnNewGame = _session.StartNewGame();
                CurrentPhase = _session.CurrentPhase;
                if (glimpsedOnNewGame.Count > 0)
                {
                    _drawAfterGlimpsing = true;
                    IsGlimpsing = true;
                    foreach (var card in glimpsedOnNewGame)
                    {
                        GlimpseStaging.Add(new CardViewModel(card, _apiClient, this));
                    }
                }
                return true;
            case Key.B:
                // Arm the chord; the count comes from whatever digit key (1-9) is pressed next.
                _banishArmed = true;
                return true;
            case Key.G:
                // Not glimpsing yet (the IsGlimpsing branch above handles every later press) —
                // this is the opening press, which GlimpseNext treats identically to any other.
                if (GlimpseNextCommand.CanExecute(null))
                {
                    GlimpseNextCommand.Execute(null);
                }
                return true;
            default:
                return false;
        }
    }

    private static bool TryGetDigit(Key key, out int digit)
    {
        if (key is >= Key.D1 and <= Key.D9)
        {
            digit = key - Key.D1 + 1;
            return true;
        }

        if (key is >= Key.NumPad1 and <= Key.NumPad9)
        {
            digit = key - Key.NumPad1 + 1;
            return true;
        }

        digit = 0;
        return false;
    }

    [RelayCommand]
    private void IncreaseLife() => _session.AdjustLife(Player, 1);

    [RelayCommand]
    private void DecreaseLife() => _session.AdjustLife(Player, -1);

    /// <summary>Single entry point for drag-and-drop moves, which carry their destination (and, for the Field, a drop position) as data rather than a fixed command per destination.</summary>
    [RelayCommand]
    private void MoveCardTo(MoveCardRequest? request)
    {
        if (request is not null)
        {
            Move(request.Card, request.Destination, request.FieldX, request.FieldY);
        }
    }

    /// <summary>
    /// Tap state only means something on the Field, so a click anywhere else is a no-op — this
    /// deliberately excludes Champion too, even though tapping is meaningful there: the only
    /// CardTemplate-rendered place a Champion card ever appears is inside its own Peek overlay
    /// (the pile itself renders through StackZoneView's custom art layer, not CardTemplate), and
    /// clicking a card there is for browsing the stack, not toggling play state. Champion's own
    /// tap control lives in the Zoom overlay's side panel instead, wired as a direct binding that
    /// bypasses this command entirely — see GameBoardView.xaml's "Tapped" ToggleButton.
    /// </summary>
    [RelayCommand]
    private void ToggleTapped(CardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        var zone = Player.Zones.First(kv => kv.Value.Cards.Contains(card.Instance)).Key;
        if (zone != ZoneType.Field)
        {
            return;
        }

        card.Instance.IsTapped = !card.Instance.IsTapped;
    }

    /// <summary>
    /// Double-click target. Unlike tap, flipping isn't Field-only in general — it's meaningful
    /// wherever you'd want to check what a double-faced card becomes — but it's specifically
    /// excluded for Champion: a champion's Peek overlay is the only CardTemplate context it ever
    /// appears in (see ToggleTapped's own comment), and flipping there would also wipe the
    /// ResetCounterAndStatuses side effect onto a card that might be a buried, still-relevant
    /// champion holding stats transferred from a prior level-up (GameSession.MoveCard).
    /// </summary>
    [RelayCommand]
    private void FlipCard(CardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        var zone = Player.Zones.FirstOrDefault(kv => kv.Value.Cards.Contains(card.Instance)).Value?.Type;
        if (zone == ZoneType.Champion)
        {
            return;
        }

        card.Instance.IsFlipped = !card.Instance.IsFlipped;
        card.Instance.ResetCounterAndStatuses();
    }

    [RelayCommand]
    private void ZoomCard(CardViewModel? card) => ZoomedCard = card;

    /// <summary>+/- for the Zoom overlay's counter control — operates on whichever card is
    /// currently zoomed in on.</summary>
    [RelayCommand]
    private void IncreaseCounter()
    {
        if (ZoomedCard is not null)
        {
            ZoomedCard.Instance.Counter++;
        }
    }

    [RelayCommand]
    private void DecreaseCounter()
    {
        if (ZoomedCard is not null)
        {
            ZoomedCard.Instance.Counter--;
        }
    }

    [RelayCommand]
    private void CloseZoom() => ZoomedCard = null;

    /// <summary>Clicking the same pile again closes it; clicking a different one switches to it.</summary>
    [RelayCommand]
    private void PeekZone(PeekedZoneInfo? info)
    {
        if (info is null)
        {
            return;
        }

        PeekedZone = PeekedZone?.Zone == info.Zone ? null : info;
    }

    [RelayCommand]
    private void ClosePeek() => PeekedZone = null;

    private void Move(CardViewModel? card, ZoneType destination, double? fieldX = null, double? fieldY = null)
    {
        System.Diagnostics.Debug.WriteLine($"[DragDrop] Move called: card={card?.Name ?? "null"} destination={destination} fieldX={fieldX} fieldY={fieldY}");

        if (card is null)
        {
            return;
        }

        var from = Player.Zones.First(kv => kv.Value.Cards.Contains(card.Instance)).Key;

        if (from == destination)
        {
            // Repositioning within the same zone only means something on the freeform Field.
            if (destination == ZoneType.Field && fieldX is not null && fieldY is not null)
            {
                _session.RepositionOnField(card.Instance, fieldX.Value, fieldY.Value);
            }

            return;
        }

        _session.MoveCard(Player, card.Instance, from, destination, fieldX, fieldY);
    }
}
