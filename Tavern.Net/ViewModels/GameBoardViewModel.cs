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

public sealed partial class GameBoardViewModel : ObservableObject, IKeyboardShortcutHandler
{
    private readonly GameSession _session;
    private readonly GrandArchiveApiClient _apiClient;
    private readonly GameStorageService _gameStorage;
    private readonly GameConnection? _connection;

    // Local to this game's lifetime: the opponent's cards repeat heavily across every incoming
    // PlayerState broadcast, so this avoids re-awaiting GetCardBySlugAsync for a slug already seen
    // — same reasoning as GameSessionSerializer's own per-restore cache, just kept alive for as
    // long as the connection is (many broadcasts), not just one load.
    private readonly Dictionary<string, CardDto> _opponentCardCache = new();

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

    /// <summary>The opponent's mirrored Player in an online game — kept up to date by ApplyOpponentStateAsync
    /// every time a PlayerState broadcast arrives. Null for solo.</summary>
    public Player? OpponentPlayer { get; }

    public bool IsOnline => _connection is not null;

    /// <summary>The inverse of IsOnline — for XAML bindings that hide something online-only (the
    /// damage-dealt tally makes no sense once there's a real opponent whose Life is directly
    /// visible; see the header's own binding).</summary>
    public bool IsSolo => !IsOnline;

    /// <summary>Solo is always "my turn" (there's no one else); online, this tracks GameSession's
    /// shared ActivePlayer. Drives both Advance Phase's gating and the opponent panel's auto-open/
    /// close — see UpdateIsMyTurn.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextPhaseCommand))]
    private bool _isMyTurn = true;

    /// <summary>Whether the opponent panel overlay is open — auto-toggled on every IsMyTurn
    /// transition (see UpdateIsMyTurn) but can also be opened/closed manually at any time via
    /// ToggleOpponentPanelCommand, regardless of whose turn it is.</summary>
    [ObservableProperty]
    private bool _isOpponentPanelOpen;

    /// <summary>Set once the connection drops — shown as a banner rather than forcing navigation
    /// away, so the player can finish looking at the board before backing out via Menu themselves.</summary>
    [ObservableProperty]
    private string? _connectionLostMessage;

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
    [NotifyPropertyChangedFor(nameof(HasViewedMemoryCards), nameof(HasViewedHandCards), nameof(CurrentlyViewedEvent))]
    private MajorEvent? _viewedMajorEvent;

    /// <summary>Online only: whether ViewedMajorEvent belongs to this player (true) or the opponent
    /// (false) — decides the "Your Board"/"Opponent's Board" tab labels and which side
    /// ViewedOtherPlayerEvent searches on. Meaningless (and unused) for solo.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryTabLabel), nameof(OtherTabLabel))]
    private bool _primaryEntryIsOwn = true;

    /// <summary>The other player's own most recent MajorEvent at or before ViewedMajorEvent's
    /// Timestamp — "what did their board look like at roughly this same point in the game." Null if
    /// they hadn't recorded anything yet by then (or offline). Recomputed whenever ViewedMajorEvent
    /// changes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentlyViewedEvent))]
    private MajorEvent? _viewedOtherPlayerEvent;

    /// <summary>Whether the review popup is currently showing ViewedOtherPlayerEvent's board instead
    /// of ViewedMajorEvent's — see PopulateViewedCollections, re-run on every toggle.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentlyViewedEvent))]
    private bool _isShowingOtherPlayerTab;

    public bool HasOtherPlayerTab => OpponentPlayer is not null;

    public string PrimaryTabLabel => PrimaryEntryIsOwn ? "Your Board" : "Opponent's Board";

    public string OtherTabLabel => PrimaryEntryIsOwn ? "Opponent's Board" : "Your Board";

    /// <summary>Whichever event's board the popup is currently showing — drives the header's own
    /// Turn/Description/Life/Phase line, which needs to follow the active tab the same way the
    /// card collections below it do.</summary>
    public MajorEvent? CurrentlyViewedEvent => IsShowingOtherPlayerTab ? ViewedOtherPlayerEvent : ViewedMajorEvent;

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

    /// <summary>Live version of ViewedFieldCards/ViewedSnapshotPiles, fed from OpponentPlayer's
    /// current state instead of one frozen historical MajorEvent — see RefreshOpponentPanel, called
    /// every time a PlayerState broadcast arrives. Zoom/pile-browse reuse ZoomSnapshotCardCommand/
    /// ViewSnapshotPileCommand as-is, since those already just react to "some CardSnapshotViewModel/
    /// SnapshotZoneGroup was clicked" regardless of where it came from.</summary>
    public ObservableCollection<CardSnapshotViewModel> OpponentFieldCards { get; } = new();

    // Individually bound (rather than one ItemsControl-driven collection) so the opponent panel's
    // XAML can lay them out in the exact same Champion/Material/Graveyard/Banishment/Main order and
    // pairing the snapshot viewer uses (SnapshotPileOrder) — each always present, even with an empty
    // Cards list, so an empty zone still shows as a "0" box instead of vanishing.
    [ObservableProperty]
    private SnapshotZoneGroup _opponentChampionPile = new(ZoneType.Champion, Array.Empty<CardSnapshotViewModel>());

    [ObservableProperty]
    private SnapshotZoneGroup _opponentGraveyardPile = new(ZoneType.Graveyard, Array.Empty<CardSnapshotViewModel>());

    [ObservableProperty]
    private SnapshotZoneGroup _opponentBanishmentPile = new(ZoneType.Banishment, Array.Empty<CardSnapshotViewModel>());

    [ObservableProperty]
    private int _opponentHandCount;

    [ObservableProperty]
    private int _opponentMemoryCount;

    [ObservableProperty]
    private int _opponentMaterialDeckCount;

    [ObservableProperty]
    private int _opponentMainDeckCount;

    [ObservableProperty]
    private int _opponentLife;

    [ObservableProperty]
    private int _opponentTurnCount;

    /// <summary>Online only: both players' MajorEvents interleaved by Timestamp, "You"/"Opp"
    /// tagged — the actual order things happened in, including responses played during the other
    /// player's turn, not two disconnected per-player lists. Rebuilt whenever either side's
    /// MajorEvents changes (see RefreshOpponentPanel and the constructor's own CollectionChanged
    /// hook on this player's MajorEvents).</summary>
    public ObservableCollection<MergedLogEntry> MergedLog { get; } = new();

    private void RefreshMergedLog()
    {
        if (OpponentPlayer is null)
        {
            return;
        }

        var merged = Player.Stats.MajorEvents.Select(e => new MergedLogEntry(true, e))
            .Concat(OpponentPlayer.Stats.MajorEvents.Select(e => new MergedLogEntry(false, e)))
            .OrderBy(e => e.Event.Timestamp)
            .ToList();

        MergedLog.Clear();
        foreach (var entry in merged)
        {
            MergedLog.Add(entry);
        }
    }

    /// <summary>Rebuilds every opponent-panel property from OpponentPlayer's current (just-updated)
    /// state. Simplest-correct approach: full rebuild each time rather than an incremental diff —
    /// updates are already coalesced to the connection's ~300ms broadcast tick, so this is cheap.</summary>
    private void RefreshOpponentPanel()
    {
        if (OpponentPlayer is null)
        {
            return;
        }

        OpponentFieldCards.Clear();
        foreach (var card in BuildOpponentCardSnapshots(ZoneType.Field))
        {
            OpponentFieldCards.Add(card);
        }

        OpponentChampionPile = new SnapshotZoneGroup(ZoneType.Champion, BuildOpponentCardSnapshots(ZoneType.Champion));
        OpponentGraveyardPile = new SnapshotZoneGroup(ZoneType.Graveyard, BuildOpponentCardSnapshots(ZoneType.Graveyard));
        OpponentBanishmentPile = new SnapshotZoneGroup(ZoneType.Banishment, BuildOpponentCardSnapshots(ZoneType.Banishment));

        OpponentHandCount = OpponentPlayer.GetZone(ZoneType.Hand).Cards.Count;
        OpponentMemoryCount = OpponentPlayer.GetZone(ZoneType.Memory).Cards.Count;
        OpponentMaterialDeckCount = OpponentPlayer.GetZone(ZoneType.MaterialDeck).Cards.Count;
        OpponentMainDeckCount = OpponentPlayer.GetZone(ZoneType.MainDeck).Cards.Count;
        OpponentLife = OpponentPlayer.Life;
        OpponentTurnCount = OpponentPlayer.Stats.TurnCount;

        RefreshMergedLog();
    }

    private List<CardSnapshotViewModel> BuildOpponentCardSnapshots(ZoneType zone) =>
        OpponentPlayer!.GetZone(zone).Cards
            .Select(card => new CardSnapshotViewModel(CardSnapshot.From(card, zone), _apiClient, this))
            .ToList();

    /// <summary>Picking a new event resets to its own tab and, online, looks up the other player's
    /// nearest preceding event so the second tab has something to show (see ViewedOtherPlayerEvent's
    /// own doc comment).</summary>
    partial void OnViewedMajorEventChanged(MajorEvent? value)
    {
        IsShowingOtherPlayerTab = false;
        ViewedOtherPlayerEvent = null;
        PrimaryEntryIsOwn = true;

        if (value is not null && OpponentPlayer is not null)
        {
            PrimaryEntryIsOwn = Player.Stats.MajorEvents.Contains(value);
            var otherPlayerEvents = PrimaryEntryIsOwn ? OpponentPlayer.Stats.MajorEvents : Player.Stats.MajorEvents;
            ViewedOtherPlayerEvent = otherPlayerEvents
                .Where(e => e.Timestamp <= value.Timestamp)
                .OrderByDescending(e => e.Timestamp)
                .FirstOrDefault();
        }

        PopulateViewedCollections(value?.Snapshot);
    }

    partial void OnIsShowingOtherPlayerTabChanged(bool value) =>
        PopulateViewedCollections((value ? ViewedOtherPlayerEvent : ViewedMajorEvent)?.Snapshot);

    [RelayCommand]
    private void ShowPrimaryTab() => IsShowingOtherPlayerTab = false;

    [RelayCommand]
    private void ShowOtherPlayerTab() => IsShowingOtherPlayerTab = true;

    private void PopulateViewedCollections(GameSnapshot? snapshot)
    {
        ViewedSnapshotPiles.Clear();
        ViewedFieldCards.Clear();
        ViewedHandCards.Clear();
        ViewedMemoryCards.Clear();
        ViewedSnapshotPile = null;
        ZoomedSnapshotCard = null;

        if (snapshot is null)
        {
            return;
        }

        var cardsByZone = snapshot.Cards.ToLookup(c => c.Zone);

        foreach (var card in cardsByZone[ZoneType.Field])
        {
            ViewedFieldCards.Add(new CardSnapshotViewModel(card, _apiClient, this));
        }

        foreach (var card in cardsByZone[ZoneType.Hand])
        {
            ViewedHandCards.Add(new CardSnapshotViewModel(card, _apiClient, this));
        }

        foreach (var card in cardsByZone[ZoneType.Memory])
        {
            ViewedMemoryCards.Add(new CardSnapshotViewModel(card, _apiClient, this));
        }

        // Always all 5 piles, even ones with no cards (a "0" box rather than the pile vanishing) —
        // walking SnapshotPileOrder directly rather than grouping+reordering also guarantees that
        // fixed order regardless of which zones the snapshot happens to have cards in.
        foreach (var zone in SnapshotPileOrder)
        {
            var cards = cardsByZone[zone].Select(c => new CardSnapshotViewModel(c, _apiClient, this)).ToList();
            ViewedSnapshotPiles.Add(new SnapshotZoneGroup(zone, cards));
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

    public GameBoardViewModel(
        GameSession session,
        Player player,
        GrandArchiveApiClient apiClient,
        GameStorageService gameStorage,
        GameConnection? connection = null,
        Player? opponentPlayer = null)
    {
        _session = session;
        _apiClient = apiClient;
        _gameStorage = gameStorage;
        _connection = connection;
        Player = player;
        OpponentPlayer = opponentPlayer;
        _currentPhase = session.CurrentPhase;
        _isMyTurn = !IsOnline || session.ActivePlayer == player;

        // Whoever isn't going first should see this open the instant the game starts, not only on
        // the first turn handoff — UpdateIsMyTurn only fires on a transition, and there isn't one
        // yet this early, so it's set directly here instead.
        _isOpponentPanelOpen = IsOnline && !_isMyTurn;

        if (_connection is not null)
        {
            _connection.BroadcastTick += OnBroadcastTick;
            _connection.MessageReceived += OnMessageReceived;
            _connection.Disconnected += OnConnectionLost;

            // My own MajorEvents feed the merged log too, not just the opponent's (see
            // RefreshMergedLog) — this is the one side of that merge that isn't already covered by
            // RefreshOpponentPanel running on every incoming broadcast.
            Player.Stats.MajorEvents.CollectionChanged += (_, _) => RefreshMergedLog();
        }

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

    /// <summary>Fires every ~300ms while connected — broadcasts this player's full current state.
    /// Unconditional (not just on change) so this doubles as the connection's keepalive; see
    /// GameConnection's own doc comment.</summary>
    private void OnBroadcastTick()
    {
        var state = GameSessionSerializer.CapturePlayer(Player);
        _ = _connection!.SendAsync(new OnlineMessage { Kind = OnlineMessageKind.PlayerState, PlayerState = state });
    }

    private void OnMessageReceived(OnlineMessage message)
    {
        switch (message.Kind)
        {
            case OnlineMessageKind.PlayerState when message.PlayerState is not null:
                _ = ApplyOpponentStateAsync(message.PlayerState);
                break;
            case OnlineMessageKind.GameState when message.Phase is not null && message.ActivePlayerNumber is not null:
                ApplyRemoteGameState(message.Phase.Value, message.ActivePlayerNumber.Value);
                break;
        }
    }

    private async Task ApplyOpponentStateAsync(SavedPlayer state)
    {
        if (OpponentPlayer is null)
        {
            return;
        }

        await GameSessionSerializer.ApplyToPlayer(OpponentPlayer, state, _apiClient, _opponentCardCache);
        RefreshOpponentPanel();
    }

    /// <summary>Applies a GameState update from the active side of the handoff — see
    /// GameSession.ApplyRemoteGameState's own doc comment on why this never re-runs WakeUp/
    /// Recollect/Draw locally; that already happened on their end and is reflected in whatever
    /// PlayerState they broadcast alongside this.</summary>
    private void ApplyRemoteGameState(TurnPhase phase, int activePlayerNumber)
    {
        var activePlayer = activePlayerNumber == Player.PlayerNumber ? Player : OpponentPlayer;
        if (activePlayer is null)
        {
            return;
        }

        _session.ApplyRemoteGameState(phase, activePlayer);
        CurrentPhase = _session.CurrentPhase;
        UpdateIsMyTurn();
    }

    /// <summary>Recomputes IsMyTurn from GameSession's shared ActivePlayer and, only on an actual
    /// transition, auto-toggles the opponent panel — open when it just became their turn, closed
    /// when it just became mine. A manual toggle (see ToggleOpponentPanelCommand) still works
    /// independently of this at any other time.</summary>
    private void UpdateIsMyTurn()
    {
        var wasMyTurn = IsMyTurn;
        IsMyTurn = !IsOnline || _session.ActivePlayer == Player;

        if (IsOnline && wasMyTurn != IsMyTurn)
        {
            IsOpponentPanelOpen = !IsMyTurn;
        }
    }

    private void OnConnectionLost() => ConnectionLostMessage = "Connection to your opponent was lost.";

    [RelayCommand]
    private void ToggleOpponentPanel() => IsOpponentPanelOpen = !IsOpponentPanelOpen;

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

    /// <summary>Advances to the next phase of the turn; advancing past End starts the next turn.
    /// Online, this no-ops unless it's currently this player's turn (see CanAdvancePhase) — playing
    /// cards is never gated this way, only phase progression itself.</summary>
    [RelayCommand(CanExecute = nameof(CanAdvancePhase))]
    private void NextPhase()
    {
        var previousActivePlayer = _session.ActivePlayer;
        _session.AdvancePhase(Player);
        CurrentPhase = _session.CurrentPhase;

        // CurrentPhase/ActivePlayer are shared session state, not part of either player's own
        // broadcast Player — only whoever just changed them (the handoff at End) sends this
        // one-shot update; the passive side never echoes it back (see ApplyRemoteGameState).
        if (IsOnline && _session.ActivePlayer != previousActivePlayer)
        {
            _ = _connection!.SendAsync(new OnlineMessage
            {
                Kind = OnlineMessageKind.GameState,
                Phase = _session.CurrentPhase,
                ActivePlayerNumber = _session.ActivePlayer!.PlayerNumber,
            });
        }

        UpdateIsMyTurn();
    }

    private bool CanAdvancePhase() => !IsOnline || IsMyTurn;

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
    private void BackToMenu()
    {
        _connection?.Dispose();
        BackToMenuRequested?.Invoke();
    }

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
