using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tavern.Net.Game;
using Tavern.Net.GameData;

namespace Tavern.Net.ViewModels;

public sealed partial class GameBoardViewModel : ObservableObject, IKeyboardShortcutHandler
{
    private readonly GameSession _session;
    private readonly GrandArchiveApiClient _apiClient;
    private readonly GameStorageService _gameStorage;
    private readonly Random _diceRandom = new();

    // Set by the 'N' handler when StartNewGame hands back cards to glimpse instead of a normal
    // opening hand; consumed in MoveGlimpseCard the instant that glimpse finishes, drawing the
    // hand StartNewGame skipped in favor of the glimpse.
    private bool _drawAfterGlimpsing;

    // Drives the face-cycling animation for every die in Dice at once — a single shared clock
    // rather than one timer per die, since they all just need "how much time has elapsed" to know
    // whether they've reached their own (independently randomized) StopAt yet.
    private DispatcherTimer? _diceAnimationTimer;
    private TimeSpan _diceAnimationElapsed;

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

    /// <summary>The Major event currently being viewed (read-only) in the snapshot overlay, or
    /// null when it's closed. Viewing never mutates the live game — see GameSession.TakeSnapshot's
    /// own doc comment on why this is a display-only feature, not a rollback.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasViewedMemoryCards), nameof(HasViewedHandCards))]
    private MajorEvent? _viewedMajorEvent;

    /// <summary>Which zone's full contents are currently drilled into from the snapshot viewer's
    /// pile boxes (Champion, Graveyard, Banishment, Material, Main — the same zones StackZoneView
    /// renders as piles on the live board), or null when that secondary popup is closed.</summary>
    [ObservableProperty]
    private SnapshotZoneGroup? _viewedSnapshotPile;

    /// <summary>The snapshot card currently shown full-size in its own zoom overlay (right-click,
    /// via CardZoomBehavior), or null when it's closed. Parallel to <see cref="ZoomedCard"/> but
    /// with no status/counter side panel — a snapshot is read-only, so there's nothing to edit.</summary>
    [ObservableProperty]
    private CardSnapshotViewModel? _zoomedSnapshotCard;

    // Display order for the snapshot viewer's pile boxes — not the ZoneType enum's declaration
    // order, which reads oddly here. Field/Hand/Memory aren't included: they're spread out
    // directly (see ViewedFieldCards/ViewedHandCards/ViewedMemoryCards) rather than shown as
    // click-to-open piles, matching how the live board renders them. Paired to match the
    // UniformGrid's 2-per-row layout: Champion+Material, then Graveyard+Banishment, then Main
    // alone.
    private static readonly ZoneType[] SnapshotPileOrder =
    {
        ZoneType.Champion,
        ZoneType.MaterialDeck,
        ZoneType.Graveyard,
        ZoneType.Banishment,
        ZoneType.MainDeck,
    };

    /// <summary>The pile boxes shown in the snapshot viewer (empty ones omitted) — click one to
    /// open ViewedSnapshotPile. Rebuilt whenever ViewedMajorEvent changes.</summary>
    public ObservableCollection<SnapshotZoneGroup> ViewedSnapshotPiles { get; } = new();

    /// <summary>Field cards for the snapshot viewer — flowed into a wrapping grid rather than each
    /// CardSnapshot's own captured FieldX/FieldY, since reconstructing the live board's freeform
    /// drag positions turned out to be more trouble than it was worth for a read-only view.</summary>
    public ObservableCollection<CardSnapshotViewModel> ViewedFieldCards { get; } = new();

    public ObservableCollection<CardSnapshotViewModel> ViewedHandCards { get; } = new();

    public ObservableCollection<CardSnapshotViewModel> ViewedMemoryCards { get; } = new();

    public bool HasViewedMemoryCards => ViewedMemoryCards.Count > 0;

    public bool HasViewedHandCards => ViewedHandCards.Count > 0;

    partial void OnViewedMajorEventChanged(MajorEvent? value)
    {
        ViewedSnapshotPiles.Clear();
        ViewedFieldCards.Clear();
        ViewedHandCards.Clear();
        ViewedMemoryCards.Clear();
        ViewedSnapshotPile = null;
        ZoomedSnapshotCard = null;

        if (value is null)
        {
            return;
        }

        foreach (var group in value.Snapshot.Cards.GroupBy(c => c.Zone))
        {
            var cards = group.Select(c => new CardSnapshotViewModel(c, _apiClient, this)).ToList();
            switch (group.Key)
            {
                case ZoneType.Field:
                    foreach (var card in cards)
                    {
                        ViewedFieldCards.Add(card);
                    }

                    break;
                case ZoneType.Hand:
                    foreach (var card in cards)
                    {
                        ViewedHandCards.Add(card);
                    }

                    break;
                case ZoneType.Memory:
                    foreach (var card in cards)
                    {
                        ViewedMemoryCards.Add(card);
                    }

                    break;
                default:
                    ViewedSnapshotPiles.Add(new SnapshotZoneGroup(group.Key, cards));
                    break;
            }
        }

        // Re-sort piles into SnapshotPileOrder — GroupBy above doesn't guarantee any particular
        // zone order, and Tokens (excluded from TakeSnapshot already) is the only zone that could
        // otherwise slip through the default case, so this also acts as a final filter.
        var ordered = ViewedSnapshotPiles.OrderBy(g => Array.IndexOf(SnapshotPileOrder, g.Zone)).ToList();
        ViewedSnapshotPiles.Clear();
        foreach (var group in ordered)
        {
            ViewedSnapshotPiles.Add(group);
        }
    }

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

    /// <summary>Whether the Roll Dice panel is open. Not tied to whether an animation is
    /// currently running — closing the panel mid-roll just hides it; the timer keeps going and
    /// settles the dice regardless, so reopening later shows the finished result.</summary>
    [ObservableProperty]
    private bool _isRollingDice;

    /// <summary>How many dice the next Roll will create — set via the panel's +/- pair, clamped
    /// to a sane [1, 20] range.</summary>
    [ObservableProperty]
    private int _diceToRoll = 1;

    public ObservableCollection<DieViewModel> Dice { get; } = new();

    /// <summary>Whether the Save Game panel is open.</summary>
    [ObservableProperty]
    private bool _isSavingGame;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmSaveGameCommand))]
    private string _saveGameName = "";

    [ObservableProperty]
    private string? _saveGameStatusMessage;

    /// <summary>Raised when the player wants to return to the start menu, leaving this game running
    /// in the background (nothing is auto-saved — see Save Game).</summary>
    public event Action? BackToMenuRequested;

    public GameBoardViewModel(GameSession session, Player player, GrandArchiveApiClient apiClient, GameStorageService gameStorage)
    {
        _session = session;
        _apiClient = apiClient;
        _gameStorage = gameStorage;
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

    [RelayCommand]
    private void IncreaseDamageDealt() => _session.AdjustDamageDealt(Player, 1);

    [RelayCommand]
    private void DecreaseDamageDealt() => _session.AdjustDamageDealt(Player, -1);

    [RelayCommand]
    private void OpenDicePanel() => IsRollingDice = true;

    [RelayCommand]
    private void CloseDicePanel() => IsRollingDice = false;

    [RelayCommand]
    private void OpenSaveGamePanel()
    {
        SaveGameStatusMessage = null;
        IsSavingGame = true;
    }

    [RelayCommand]
    private void CloseSaveGamePanel() => IsSavingGame = false;

    [RelayCommand(CanExecute = nameof(CanConfirmSaveGame))]
    private void ConfirmSaveGame()
    {
        GameSessionSerializer.WarmCardCache(_session, _apiClient);
        var saved = GameSessionSerializer.Capture(SaveGameName.Trim(), _session);
        _gameStorage.Save(saved);
        SaveGameStatusMessage = $"Saved \"{saved.Name}\".";
    }

    private bool CanConfirmSaveGame() => !string.IsNullOrWhiteSpace(SaveGameName);

    [RelayCommand]
    private void BackToMenu() => BackToMenuRequested?.Invoke();

    [RelayCommand]
    private void IncreaseDiceToRoll() => DiceToRoll = Math.Min(20, DiceToRoll + 1);

    [RelayCommand]
    private void DecreaseDiceToRoll() => DiceToRoll = Math.Max(1, DiceToRoll - 1);

    /// <summary>
    /// Rolls DiceToRoll dice: each one's real result is picked right here, fairly, once — the
    /// cycling animation that follows is cosmetic suspense on top of an already-decided outcome,
    /// not what determines it. Each die gets its own randomized StopAt so they don't all freeze in
    /// lockstep; a single shared DispatcherTimer then just advances the clock and checks each
    /// still-rolling die against its own StopAt every tick.
    /// </summary>
    [RelayCommand]
    private void RollDice()
    {
        _diceAnimationTimer?.Stop();
        Dice.Clear();
        _diceAnimationElapsed = TimeSpan.Zero;

        for (var i = 0; i < DiceToRoll; i++)
        {
            var finalValue = _diceRandom.Next(1, 7);
            var stopAt = TimeSpan.FromMilliseconds(800 + _diceRandom.Next(0, 701));
            Dice.Add(new DieViewModel(finalValue, stopAt));
        }

        _diceAnimationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _diceAnimationTimer.Tick += OnDiceAnimationTick;
        _diceAnimationTimer.Start();
    }

    private void OnDiceAnimationTick(object? sender, EventArgs e)
    {
        _diceAnimationElapsed += TimeSpan.FromMilliseconds(80);
        var anyStillRolling = false;

        foreach (var die in Dice)
        {
            if (die.IsSettled)
            {
                continue;
            }

            if (_diceAnimationElapsed >= die.StopAt)
            {
                die.FaceValue = die.FinalValue;
                die.IsSettled = true;
            }
            else
            {
                die.FaceValue = _diceRandom.Next(1, 7);
                anyStillRolling = true;
            }
        }

        if (!anyStillRolling)
        {
            _diceAnimationTimer!.Stop();
            _diceAnimationTimer.Tick -= OnDiceAnimationTick;
            _diceAnimationTimer = null;
        }
    }

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
    /// tap control lives in the Zoom overlay's side panel instead — see ToggleZoomedCardTapped.
    /// The actual mutation lives in GameSession.ToggleTapped now, not here, so it can log and
    /// participate in Life/Damage coalescing (see GameSession's own doc comments).
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

        _session.ToggleTapped(Player, card.Instance);
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

        _session.FlipCard(Player, card.Instance);
    }

    [RelayCommand]
    private void ZoomCard(CardViewModel? card) => ZoomedCard = card;

    /// <summary>Tap toggle for the Zoom overlay's side panel — bypasses ToggleTapped's Field-only
    /// gate deliberately, since this panel is already only ever visible for a Field/Champion card
    /// (see ShowCounterAndStatusPanel).</summary>
    [RelayCommand]
    private void ToggleZoomedCardTapped()
    {
        if (ZoomedCard is not null)
        {
            _session.ToggleTapped(Player, ZoomedCard.Instance);
        }
    }

    /// <summary>Status toggle for the Zoom overlay's side panel — statusName matches one of the
    /// six names CardInstance.ToggleStatus recognizes (e.g. "Ranged").</summary>
    [RelayCommand]
    private void ToggleZoomedCardStatus(string? statusName)
    {
        if (ZoomedCard is not null && statusName is not null)
        {
            _session.ToggleStatus(Player, ZoomedCard.Instance, statusName);
        }
    }

    /// <summary>+/- for the Zoom overlay's counter control — operates on whichever card is
    /// currently zoomed in on.</summary>
    [RelayCommand]
    private void IncreaseCounter()
    {
        if (ZoomedCard is not null)
        {
            _session.AdjustCounter(Player, ZoomedCard.Instance, 1);
        }
    }

    [RelayCommand]
    private void DecreaseCounter()
    {
        if (ZoomedCard is not null)
        {
            _session.AdjustCounter(Player, ZoomedCard.Instance, -1);
        }
    }

    [RelayCommand]
    private void CloseZoom() => ZoomedCard = null;

    [RelayCommand]
    private void ZoomSnapshotCard(CardSnapshotViewModel? card) => ZoomedSnapshotCard = card;

    [RelayCommand]
    private void CloseSnapshotZoom() => ZoomedSnapshotCard = null;

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

    /// <summary>Opens the read-only snapshot viewer for a Major event — see GameSession.TakeSnapshot
    /// and ViewedMajorEvent's own doc comments on why this never touches the live game.</summary>
    [RelayCommand]
    private void ViewMajorEvent(MajorEvent? majorEvent) => ViewedMajorEvent = majorEvent;

    [RelayCommand]
    private void CloseMajorEventView() => ViewedMajorEvent = null;

    /// <summary>Opens the secondary popup showing a pile's full contents (Champion, Graveyard,
    /// Banishment, Material, Main — see ViewedSnapshotPiles' own doc comment).</summary>
    [RelayCommand]
    private void ViewSnapshotPile(SnapshotZoneGroup? group) => ViewedSnapshotPile = group;

    [RelayCommand]
    private void CloseSnapshotPileView() => ViewedSnapshotPile = null;

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
