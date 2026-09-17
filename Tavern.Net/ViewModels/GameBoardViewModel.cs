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

    /// <summary>True while the opponent has done something (played a card, moved one to the
    /// Graveyard/Banishment, taken their turn, ...) since the panel was last open — glows the
    /// header's Opponent button (see GameBoardView's OpponentButtonStyle) rather than auto-opening
    /// the panel itself, so the opponent can never yank the screen away mid-drag/mid-zoom; the
    /// player decides when to actually look. Cleared the moment the panel opens, by any means
    /// (manual toggle or the turn-handoff auto-open in UpdateIsMyTurn) — see
    /// OnIsOpponentPanelOpenChanged. Set from RefreshOpponentPanel for non-Life/Damage MajorEvents
    /// only — Life changes get their own always-visible OpponentLife number + flash instead (see
    /// OpponentLifeJustChanged), since Life matters too much to only surface once the player already
    /// went looking.</summary>
    [ObservableProperty]
    private bool _hasUnseenOpponentActivity;

    /// <summary>OpponentPlayer.Stats.MajorEvents.Count(Kind == Other) as of the last
    /// RefreshOpponentPanel call — that collection is fully cleared and rebuilt from scratch on
    /// every ~300ms broadcast (see GameSessionSerializer.ApplyToPlayer), even when nothing actually
    /// changed, so a raw CollectionChanged subscription would glow constantly. Comparing counts
    /// across calls is what actually detects a genuinely new event.</summary>
    private int _lastSeenOpponentMajorEventCount;

    /// <summary>False until the very first PlayerState broadcast arrives. That first call always
    /// carries the opponent's own game-start setup (their "Game started." MajorEvent, base champion
    /// materialized, opening hand drawn, starting Life) — automated setup, not something the player
    /// did, so it shouldn't glow the Opponent button or flash OpponentLife either. This just seeds
    /// _lastSeenOpponentMajorEventCount/OpponentLife from that first snapshot instead of comparing
    /// against it.</summary>
    private bool _hasSeenInitialOpponentState;

    partial void OnIsOpponentPanelOpenChanged(bool value)
    {
        if (value)
        {
            HasUnseenOpponentActivity = false;
            CloseActionsMenu();
        }
    }

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

    /// <summary>Private, fully interactive like Hand (drag in/out, zoomable) — hidden from the
    /// opponent (see OpponentSealedCount, the Opponent panel's own count-only view of it). Cards a
    /// player has sealed away themselves, or received from the opponent via Give (see GameSession.
    /// ReceiveGivenCard).</summary>
    public ZoneViewModel Sealed { get; }

    /// <summary>The card currently shown full-size in the zoom overlay, or null when it's closed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCounterAndStatusPanel), nameof(ShowZoomSidePanel), nameof(CanBottomZoomedCard), nameof(CanRevealZoomedCard), nameof(CanGiveZoomedCard))]
    [NotifyCanExecuteChangedFor(nameof(BottomZoomedCardCommand), nameof(RevealZoomedCardCommand), nameof(OpenGiveTargetPickerForZoomedCardCommand))]
    private CardViewModel? _zoomedCard;

    /// <summary>Which of the player's own zones ZoomedCard currently sits in, or null if it isn't in
    /// any of them (e.g. zoomed from Glimpse — GlimpseStaging/Top/Bottom are ViewModel-only lists,
    /// not domain zones, hence the null-safe FirstOrDefault rather than First).</summary>
    private ZoneType? ZoneOfZoomedCard() =>
        ZoomedCard is null ? null : Player.Zones.FirstOrDefault(kv => kv.Value.Cards.Contains(ZoomedCard.Instance)).Value?.Type;

    /// <summary>
    /// Whether the Zoom overlay's status/counter side panel should show for the currently-zoomed
    /// card — Counter/statuses are only ever meaningful in play (Field or Champion; see
    /// GameSession.MoveCard's reset rule), so the panel is irrelevant everywhere else (Hand, a
    /// deck peek, Glimpse, ...).
    /// </summary>
    public bool ShowCounterAndStatusPanel => ZoomedCard is not null && ZoneOfZoomedCard() is ZoneType.Field or ZoneType.Champion;

    /// <summary>Whether the Zoom overlay's whole side panel Border should show at all — the Status/
    /// Counter/Tapped content only matters for Field/Champion, but Bottom/Reveal/Give matter
    /// precisely for the private zones that gate covers (Hand/Memory/Material/Sealed), so the outer
    /// panel needs its own, broader visibility check rather than reusing ShowCounterAndStatusPanel
    /// directly.</summary>
    public bool ShowZoomSidePanel => ShowCounterAndStatusPanel || CanBottomZoomedCard || CanRevealZoomedCard || CanGiveZoomedCard;

    public bool CanBottomZoomedCard => ZoomedCard is not null && ZoneOfZoomedCard() != ZoneType.MainDeck;

    [RelayCommand(CanExecute = nameof(CanBottomZoomedCard))]
    private void BottomZoomedCard()
    {
        if (ZoomedCard is null)
        {
            return;
        }

        var from = ZoneOfZoomedCard();
        if (from is null)
        {
            return;
        }

        var card = ZoomedCard.Instance;
        var originalZone = from.Value;
        _session.MoveCard(Player, card, originalZone, ZoneType.MainDeck, toBottom: true);
        // Reverses through the same MoveCard the original move used — any side effect that move
        // triggered (e.g. a Champion level change) is correctly re-triggered in reverse too, rather
        // than a raw list edit that would only fix Main's own contents.
        ArmUndo("Bottom", () => _session.MoveCard(Player, card, ZoneType.MainDeck, originalZone));
        ZoomedCard = null;
    }

    private static readonly HashSet<ZoneType> RevealablePrivateZones = new()
    {
        ZoneType.Hand,
        ZoneType.Memory,
        ZoneType.MaterialDeck,
        ZoneType.Sealed,
    };

    public bool CanRevealZoomedCard => IsOnline && ZoomedCard is not null && RevealablePrivateZones.Contains(ZoneOfZoomedCard() ?? default);

    /// <summary>Reveal doesn't remove the card, so unlike Give/Bottom there's no forced reason to
    /// close the Zoom overlay afterward — the player might still want to look at or act on it.</summary>
    [RelayCommand(CanExecute = nameof(CanRevealZoomedCard))]
    private void RevealZoomedCard()
    {
        if (ZoomedCard is null)
        {
            return;
        }

        SendReveal(new[] { ZoomedCard.Instance });
    }

    private void SendReveal(IReadOnlyList<CardInstance> cards)
    {
        if (!IsOnline || cards.Count == 0)
        {
            return;
        }

        _ = _connection!.SendAsync(new OnlineMessage
        {
            Kind = OnlineMessageKind.RevealCards,
            RevealedCards = cards.Select(c => new RevealedCardEntry(c.Card.Slug, c.IsFlipped)).ToList(),
        });
    }

    /// <summary>'R' chord: reveals a batch off the top of Main at once — nothing to snapshot for
    /// Undo here, since (unlike Bottom/Mill/Give's blind path) Reveal never actually moves anything.</summary>
    private void RevealTopCards(int count)
    {
        if (!IsOnline)
        {
            return;
        }

        var cards = Player.GetZone(ZoneType.MainDeck).Cards.Take(count).ToList();
        SendReveal(cards);
    }

    // --- Give: self-initiated, no request/approval handshake — whoever's card effect calls for a
    // transfer is the one who reads it and performs it themselves, same "manual bookkeeping, no
    // rules engine" philosophy as every other action in this app. Both entry points (a specific
    // card via the Zoom overlay, or a blind top-N off Main via the 'P' chord) open the same small
    // Field/Sealed target-picker overlay; nothing actually moves until one of those two is clicked.

    private CardViewModel? _giveCandidateCard;
    private int? _giveBlindCount;

    [ObservableProperty]
    private bool _isGiveTargetPickerOpen;

    partial void OnIsGiveTargetPickerOpenChanged(bool value)
    {
        if (value)
        {
            CloseActionsMenu();
        }
    }

    public bool CanGiveZoomedCard => IsOnline && ZoomedCard is not null;

    [RelayCommand(CanExecute = nameof(CanGiveZoomedCard))]
    private void OpenGiveTargetPickerForZoomedCard()
    {
        if (ZoomedCard is null)
        {
            return;
        }

        _giveCandidateCard = ZoomedCard;
        _giveBlindCount = null;
        ZoomedCard = null;
        IsGiveTargetPickerOpen = true;
    }

    private void ArmGiveBlind(int count)
    {
        if (!IsOnline)
        {
            return;
        }

        _giveCandidateCard = null;
        _giveBlindCount = count;
        IsGiveTargetPickerOpen = true;
    }

    [RelayCommand]
    private void ConfirmGiveToField() => ConfirmGive(ZoneType.Field);

    [RelayCommand]
    private void ConfirmGiveToSealed() => ConfirmGive(ZoneType.Sealed);

    [RelayCommand]
    private void CancelGive()
    {
        IsGiveTargetPickerOpen = false;
        _giveCandidateCard = null;
        _giveBlindCount = null;
    }

    private void ConfirmGive(ZoneType destination)
    {
        IsGiveTargetPickerOpen = false;

        List<CardInstance> given;
        string sourceLabel;

        if (_giveCandidateCard is { } single)
        {
            var from = Player.Zones.First(kv => kv.Value.Cards.Contains(single.Instance)).Key;
            _session.RemoveCardForGive(Player, single.Instance, from);
            given = new List<CardInstance> { single.Instance };
            sourceLabel = from.ToString();
        }
        else if (_giveBlindCount is { } count)
        {
            // Deliberately no Undo entry here — by the time this shows, the TransferCard message
            // below has already reached the opponent, so a local-only undo would duplicate the
            // cards instead of actually taking them back (see the Undo section's own comment).
            given = _session.TakeTopCardsForGive(Player, count);
            sourceLabel = "Main Deck (blind)";
        }
        else
        {
            given = new List<CardInstance>();
            sourceLabel = "";
        }

        if (given.Count > 0 && IsOnline)
        {
            _ = _connection!.SendAsync(new OnlineMessage
            {
                Kind = OnlineMessageKind.TransferCard,
                TransferCardSlugs = given.Select(c => c.Card.Slug).ToList(),
                TransferTargetZone = destination,
                TransferSourceLabel = sourceLabel,
            });
        }

        _giveCandidateCard = null;
        _giveBlindCount = null;
    }

    private async Task ReceiveTransferAsync(IReadOnlyList<string> slugs, ZoneType destination, string? sourceLabel)
    {
        foreach (var slug in slugs)
        {
            _session.ReceiveGivenCard(Player, await ResolveOpponentCardAsync(slug), destination, sourceLabel);
        }
    }

    // --- Receiving a reveal: queued, since more than one can arrive close together (e.g. one from
    // Hand, then moments later a batch off Main) — shown one at a time via a countdown-ring overlay
    // (CountdownRingView), auto-advancing to the next queued entry once the current one closes.

    private readonly Queue<IReadOnlyList<CardSnapshotViewModel>> _pendingReveals = new();

    [ObservableProperty]
    private IReadOnlyList<CardSnapshotViewModel>? _activeReveal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowKeepRevealOpenButton))]
    private bool _revealKeptOpen;

    public bool ShowKeepRevealOpenButton => !RevealKeptOpen;

    [ObservableProperty]
    private double _revealRemainingFraction = 1.0;

    private DispatcherTimer? _revealTimer;
    private DateTime _revealStartedAt;

    partial void OnActiveRevealChanged(IReadOnlyList<CardSnapshotViewModel>? value)
    {
        if (value is not null)
        {
            CloseActionsMenu();
        }
    }

    private async Task EnqueueRevealAsync(IReadOnlyList<RevealedCardEntry> entries)
    {
        var resolved = new List<CardSnapshotViewModel>();
        foreach (var entry in entries)
        {
            var cardDto = await ResolveOpponentCardAsync(entry.Slug);
            var snapshot = new CardSnapshot(cardDto, ZoneType.Hand, false, entry.IsFlipped, 0, 0, 0, false, false, false, false, false, false);
            resolved.Add(new CardSnapshotViewModel(snapshot, _apiClient, this));
        }

        _pendingReveals.Enqueue(resolved);
        if (ActiveReveal is null)
        {
            ShowNextReveal();
        }
    }

    private void ShowNextReveal()
    {
        if (_pendingReveals.Count == 0)
        {
            ActiveReveal = null;
            _revealTimer?.Stop();
            return;
        }

        ActiveReveal = _pendingReveals.Dequeue();
        RevealKeptOpen = false;
        RevealRemainingFraction = 1.0;
        _revealStartedAt = DateTime.UtcNow;

        _revealTimer?.Stop();
        _revealTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _revealTimer.Tick += OnRevealTimerTick;
        _revealTimer.Start();
    }

    private void OnRevealTimerTick(object? sender, EventArgs e)
    {
        var fraction = 1.0 - (DateTime.UtcNow - _revealStartedAt).TotalSeconds / 5.0;
        if (fraction <= 0)
        {
            _revealTimer!.Stop();
            ShowNextReveal();
            return;
        }

        RevealRemainingFraction = fraction;
    }

    [RelayCommand]
    private void KeepRevealOpen()
    {
        RevealKeptOpen = true;
        _revealTimer?.Stop();
    }

    [RelayCommand]
    private void CloseActiveReveal()
    {
        _revealTimer?.Stop();
        ShowNextReveal();
    }

    // --- Undo: a single-level, Main-Deck-scoped safety net for the blind actions (Mill/Bottom/
    // Glimpse) — NOT a general undo system. Main Deck can't be peeked online, so a mis-counted
    // blind action would otherwise be unrecoverable; each call site below hands ArmUndo the exact
    // closure that reverses IT SPECIFICALLY (not a generic "restore Main's card list" snapshot,
    // which — tried first — left the same CardInstance sitting in two zones at once for anything
    // that crossed a zone boundary, e.g. undoing a Mill left the milled cards in the Graveyard
    // *and* put copies of them back in Main). A flat, non-cancellable 10-second window; only ever
    // one level deep, since the next blind action's own ArmUndo call overwrites this one. Give's
    // blind path deliberately does NOT get an Undo entry — by the time it would show, the
    // TransferCard message has already reached the opponent, so a local-only "undo" would just
    // duplicate the cards instead of actually taking them back.

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    private bool _isUndoAvailable;

    [ObservableProperty]
    private string? _undoActionLabel;

    [ObservableProperty]
    private double _undoRemainingFraction = 1.0;

    private Action? _pendingUndo;
    private DispatcherTimer? _undoTimer;
    private DateTime _undoStartedAt;

    private void ArmUndo(string actionLabel, Action undo)
    {
        _pendingUndo = undo;
        UndoActionLabel = actionLabel;
        IsUndoAvailable = true;
        UndoRemainingFraction = 1.0;
        _undoStartedAt = DateTime.UtcNow;

        _undoTimer?.Stop();
        _undoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _undoTimer.Tick += OnUndoTimerTick;
        _undoTimer.Start();
    }

    private void OnUndoTimerTick(object? sender, EventArgs e)
    {
        var fraction = 1.0 - (DateTime.UtcNow - _undoStartedAt).TotalSeconds / 10.0;
        if (fraction <= 0)
        {
            DiscardUndo();
            return;
        }

        UndoRemainingFraction = fraction;
    }

    private void DiscardUndo()
    {
        _undoTimer?.Stop();
        IsUndoAvailable = false;
        _pendingUndo = null;
        UndoActionLabel = null;
    }

    [RelayCommand(CanExecute = nameof(IsUndoAvailable))]
    private void Undo()
    {
        if (_pendingUndo is null)
        {
            return;
        }

        var actionLabel = UndoActionLabel;
        _pendingUndo();

        // The opponent may have already reasoned about now-stale deck-order information (e.g. what
        // a Reveal just showed them, or what they know got milled) — a courtesy notice, not
        // anything that affects their own board.
        if (IsOnline)
        {
            _ = _connection!.SendAsync(new OnlineMessage { Kind = OnlineMessageKind.UndoNotice, UndoActionLabel = actionLabel });
        }

        DiscardUndo();
    }

    // --- Toast: a small, non-modal, single-purpose notification — currently only used for the
    // opponent's own Undo notice. Deliberately not a general queue/primitive; nothing else needs
    // one yet.

    [ObservableProperty]
    private string? _toastMessage;

    private DispatcherTimer? _toastTimer;

    private void ShowToast(string message)
    {
        ToastMessage = message;
        _toastTimer?.Stop();
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _toastTimer.Tick += (_, _) =>
        {
            ToastMessage = null;
            _toastTimer!.Stop();
        };
        _toastTimer.Start();
    }

    partial void OnZoomedCardChanged(CardViewModel? value)
    {
        if (value is not null)
        {
            CloseActionsMenu();
        }
    }

    /// <summary>The pile currently fanned out in the peek overlay, or null when it's closed.</summary>
    [ObservableProperty]
    private PeekedZoneInfo? _peekedZone;

    partial void OnPeekedZoneChanged(PeekedZoneInfo? value)
    {
        if (value is not null)
        {
            CloseActionsMenu();
        }
    }

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

    partial void OnViewedSnapshotPileChanged(SnapshotZoneGroup? value)
    {
        if (value is not null)
        {
            CloseActionsMenu();
        }
    }

    /// <summary>The snapshot card currently shown full-size in its own zoom overlay (right-click,
    /// via CardZoomBehavior), or null when it's closed. Parallel to <see cref="ZoomedCard"/> but
    /// with no status/counter side panel — a snapshot is read-only, so there's nothing to edit.</summary>
    [ObservableProperty]
    private CardSnapshotViewModel? _zoomedSnapshotCard;

    partial void OnZoomedSnapshotCardChanged(CardSnapshotViewModel? value)
    {
        if (value is not null)
        {
            CloseActionsMenu();
        }
    }

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

    /// <summary>Count-only, same redaction treatment as Hand/Memory/Material/Main — the Opponent
    /// panel's own box for this is hidden entirely at 0 rather than always showing "Sealed: 0", per
    /// how rarely this zone is actually used (see GameBoardView's own IntToVisibility binding).</summary>
    [ObservableProperty]
    private int _opponentSealedCount;

    [ObservableProperty]
    private int _opponentLife;

    [ObservableProperty]
    private int _opponentTurnCount;

    /// <summary>True for one second right after OpponentLife changes — gold-highlights the number
    /// itself (see GameBoardView's OpponentLifeTextStyle) instead of the Opponent button glowing for
    /// it; Life is important enough to warrant its own always-visible number rather than only
    /// showing up once the player goes looking for it in the panel.</summary>
    [ObservableProperty]
    private bool _opponentLifeJustChanged;

    private DispatcherTimer? _opponentLifeGlowTimer;

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
        OpponentSealedCount = OpponentPlayer.GetZone(ZoneType.Sealed).Cards.Count;

        var previousOpponentLife = OpponentLife;
        OpponentLife = OpponentPlayer.Life;
        OpponentTurnCount = OpponentPlayer.Stats.TurnCount;

        if (_hasSeenInitialOpponentState && OpponentLife != previousOpponentLife)
        {
            FlashOpponentLifeChanged();
        }

        // Life/Damage MajorEvents get their own always-visible number + flash (above) instead —
        // only "something happened on their board" kinds (a card played, moved to the Graveyard/
        // Banishment, a turn started, ...) glow the Opponent button now.
        var majorEventCount = OpponentPlayer.Stats.MajorEvents.Count(e => e.Kind == MajorEventKind.Other);
        if (_hasSeenInitialOpponentState && majorEventCount > _lastSeenOpponentMajorEventCount && !IsOpponentPanelOpen)
        {
            HasUnseenOpponentActivity = true;
        }

        _lastSeenOpponentMajorEventCount = majorEventCount;
        _hasSeenInitialOpponentState = true;

        RefreshMergedLog();
    }

    private void FlashOpponentLifeChanged()
    {
        OpponentLifeJustChanged = true;

        if (_opponentLifeGlowTimer is null)
        {
            _opponentLifeGlowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _opponentLifeGlowTimer.Tick += (_, _) =>
            {
                OpponentLifeJustChanged = false;
                _opponentLifeGlowTimer!.Stop();
            };
        }

        _opponentLifeGlowTimer.Stop();
        _opponentLifeGlowTimer.Start();
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
        if (value is not null)
        {
            CloseActionsMenu();
        }

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

    partial void OnIsRollingDiceChanged(bool value)
    {
        if (value)
        {
            CloseActionsMenu();
        }
    }

    /// <summary>How many dice the next Roll will create — set via the panel's +/- pair, clamped
    /// to a sane [1, 20] range.</summary>
    [ObservableProperty]
    private int _diceToRoll = 1;

    public ObservableCollection<DieViewModel> Dice { get; } = new();

    /// <summary>Whether the Save Game panel is open.</summary>
    [ObservableProperty]
    private bool _isSavingGame;

    partial void OnIsSavingGameChanged(bool value)
    {
        if (value)
        {
            CloseActionsMenu();
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmSaveGameCommand))]
    private string _saveGameName = "";

    [ObservableProperty]
    private string? _saveGameStatusMessage;

    /// <summary>The online match clock — ticks once a second while genuinely live (IsOnline and not
    /// yet frozen), starts from a loaded save's own frozen value when viewing one later (see the
    /// constructor's loadedElapsedTime param), and is otherwise irrelevant to solo. See
    /// ShowElapsedTime for when it's actually displayed, and FreezeElapsedTime/ConfirmSaveGame for
    /// how saving stops it.</summary>
    [ObservableProperty]
    private TimeSpan _elapsedTime;

    /// <summary>Strictly online: true for a live game (IsOnline) or when viewing a saved game that
    /// was online (a non-null loadedElapsedTime was passed in) — a solo game never shows this at
    /// all, live or loaded.</summary>
    public bool ShowElapsedTime => IsOnline || _isElapsedTimeFrozen;

    // Set immediately if constructed with a loaded save's own frozen ElapsedTime (never ticks in
    // that case — there's no live connection driving it anyway) and permanently once ConfirmSaveGame
    // successfully saves a live online game (that save's own copy of ElapsedTime is what's frozen;
    // this stops the live display from drifting past it for the rest of this process).
    private bool _isElapsedTimeFrozen;
    private DispatcherTimer? _elapsedTimeTimer;

    /// <summary>Raised when the player wants to return to the start menu, leaving this game running
    /// in the background (nothing is auto-saved — see Save Game).</summary>
    public event Action? BackToMenuRequested;

    public GameBoardViewModel(
        GameSession session,
        Player player,
        GrandArchiveApiClient apiClient,
        GameStorageService gameStorage,
        GameConnection? connection = null,
        Player? opponentPlayer = null,
        TimeSpan? loadedElapsedTime = null)
    {
        _session = session;
        _apiClient = apiClient;
        _gameStorage = gameStorage;
        _connection = connection;
        Player = player;
        OpponentPlayer = opponentPlayer;
        _currentPhase = session.CurrentPhase;
        _isMyTurn = !IsOnline || session.ActivePlayer == player;

        if (loadedElapsedTime is { } elapsed)
        {
            ElapsedTime = elapsed;
            _isElapsedTimeFrozen = true;
        }
        else if (IsOnline)
        {
            _elapsedTimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _elapsedTimeTimer.Tick += (_, _) => ElapsedTime += TimeSpan.FromSeconds(1);
            _elapsedTimeTimer.Start();
        }

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
        Sealed = new ZoneViewModel(player.GetZone(ZoneType.Sealed), apiClient, this);

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
            case OnlineMessageKind.RevealCards when message.RevealedCards is { Count: > 0 } entries:
                _ = EnqueueRevealAsync(entries);
                break;
            case OnlineMessageKind.TransferCard when message.TransferCardSlugs is { Count: > 0 } slugs && message.TransferTargetZone is not null:
                _ = ReceiveTransferAsync(slugs, message.TransferTargetZone.Value, message.TransferSourceLabel);
                break;
            case OnlineMessageKind.UndoNotice:
                ShowToast(message.UndoActionLabel is { } label ? $"Opponent used Undo ({label})." : "Opponent used Undo.");
                break;
        }
    }

    /// <summary>Resolves a slug to a CardDto for a card that isn't (and for Reveal, never will be)
    /// in any local zone — reuses the same per-connection cache ApplyOpponentStateAsync already
    /// keeps warm, since Reveal/Give overwhelmingly reference cards the opponent's own broadcasts
    /// have already resolved once.</summary>
    private async Task<CardDto> ResolveOpponentCardAsync(string slug)
    {
        if (_opponentCardCache.TryGetValue(slug, out var cached))
        {
            return cached;
        }

        var card = await _apiClient.GetCardBySlugAsync(slug)
            ?? throw new InvalidOperationException($"Couldn't resolve \"{slug}\" for a Reveal/Give.");
        _opponentCardCache[slug] = card;
        return card;
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

    /// <summary>Applies a GameState update from the active side of the handoff. The sender always
    /// broadcasts WakeUp as the landing phase (its own mirror of me never tracks whether this is
    /// genuinely my first turn — only my own authoritative session does), so when this makes ME
    /// newly active, MY session's own first-turn bookkeeping decides what actually happens here —
    /// see the two branches below.</summary>
    private void ApplyRemoteGameState(TurnPhase phase, int activePlayerNumber)
    {
        var activePlayer = activePlayerNumber == Player.PlayerNumber ? Player : OpponentPlayer;
        if (activePlayer is null)
        {
            return;
        }

        if (activePlayer == Player && _session.ActivePlayer != Player)
        {
            if (_session.IsAwaitingFirstTurn(Player))
            {
                // Genuinely my first turn ever — land on Materialization instead of the sender's
                // broadcast WakeUp, and don't bump TurnCount, so this whole turn still displays as
                // "Turn 1" rather than "Turn 2". My own next AdvancePhase call (GameSession's own
                // fast-forward branch) handles skipping straight to Draw from here.
                phase = TurnPhase.Materialization;
            }
            else
            {
                // The handoff's own NextTurn call (inside GameSession.AdvancePhase, on the departing
                // side) only ever runs against that side's own mirror of me — inert, since my own
                // broadcasts overwrite it anyway. So this is the one place MY OWN TurnCount/
                // DeadTurnsCount/"Turn X started" log — and WakeUp's untap, which the departing
                // side's own SetPhase call likewise only ever ran against that same inert mirror —
                // actually happens for my own authoritative Player.
                _session.NextTurn(Player);
                _session.WakeUp(Player);
            }
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

    /// <summary>Whether the Sealed panel is open — usable in solo too (unlike the Opponent panel),
    /// since Sealed is just a private zone of your own, not an online-only concept.</summary>
    [ObservableProperty]
    private bool _isSealedPanelOpen;

    partial void OnIsSealedPanelOpenChanged(bool value)
    {
        if (value)
        {
            CloseActionsMenu();
        }
    }

    [RelayCommand]
    private void ToggleSealedPanel() => IsSealedPanelOpen = !IsSealedPanelOpen;

    [RelayCommand]
    private void DrawCard() => _session.DrawCard(Player);

    [RelayCommand]
    private void DrawCardIntoMemory() => _session.DrawCard(Player, ZoneType.MainDeck, ZoneType.Memory);

    // Captured the instant a glimpse starts pulling cards off Main, but not shown as an available
    // Undo until FinishGlimpse actually commits (see MoveGlimpseCard) — while still sorting, the
    // player has full visibility/control over Staging/Top/Bottom already, so surfacing "Undo" that
    // early is just confusing, and clicking it mid-sort would restore Main out from under cards
    // still sitting in Staging.
    private List<CardInstance>? _pendingGlimpseUndoSnapshot;

    /// <summary>Glimpse action: pulls up to count cards off the top of Main in one batch and opens
    /// the overlay already populated — replaces the old repeat-G-to-add-one flow now that Undo
    /// makes a mis-counted glimpse recoverable. Stops early, same as GameSession.GlimpseCards, once
    /// Main is empty.</summary>
    private void GlimpseCardsForChord(int count)
    {
        _pendingGlimpseUndoSnapshot = Player.GetZone(ZoneType.MainDeck).Cards.ToList();
        var glimpsed = _session.GlimpseCards(Player, count);
        if (glimpsed.Count == 0)
        {
            _pendingGlimpseUndoSnapshot = null;
            return;
        }

        IsGlimpsing = true;
        foreach (var card in glimpsed)
        {
            GlimpseStaging.Add(new CardViewModel(card, _apiClient, this));
        }
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

            // Only the Glimpse action's own chord takes this snapshot — the very first, opening-hand
            // glimpse (triggered by New Game, not the player) has none, and shouldn't offer Undo.
            if (_pendingGlimpseUndoSnapshot is { } snapshot)
            {
                _pendingGlimpseUndoSnapshot = null;
                ArmUndo("Glimpse", () => _session.RestoreMainDeckOrder(Player, snapshot));
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

    // --- Actions menu: click the header's Actions button, pick Banish/Reveal/Glimpse/Mill/Give
    // (Reveal/Give hidden solo — see CanUseOnlineAction), then set a count and confirm. Replaces
    // the old keyboard arm-then-digit chords (B/R/P/M/G) entirely — a visible menu instead of an
    // invisible armed state, and a real counter instead of a single 1-9 keypress, since some of
    // these (Glimpse especially) genuinely need double-digit counts.

    /// <summary>Which action is currently selected for count entry — None means the menu is still
    /// showing the top-level list (see IsShowingActionsList/IsEnteringActionCount).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShowingActionsList), nameof(IsEnteringActionCount), nameof(SelectedActionLabel))]
    private ArmedChord _selectedAction;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShowingActionsList), nameof(IsEnteringActionCount))]
    private bool _isActionsMenuOpen;

    [ObservableProperty]
    private int _actionCount = 1;

    // Whether the count still reads its opening default (1) or the player has actually typed a
    // digit yet — decides whether the next digit *replaces* it (so typing "5" reads 5, not 15) or
    // *appends* to it (so typing "1" then "0" reads 10). Reset every time a new action is selected.
    private bool _hasTypedActionCount;

    public bool IsShowingActionsList => IsActionsMenuOpen && SelectedAction == ArmedChord.None;

    public bool IsEnteringActionCount => IsActionsMenuOpen && SelectedAction != ArmedChord.None;

    public string SelectedActionLabel => SelectedAction switch
    {
        ArmedChord.Banish => "Banish",
        ArmedChord.Reveal => "Reveal",
        ArmedChord.Give => "Give",
        ArmedChord.Mill => "Mill",
        ArmedChord.Glimpse => "Glimpse",
        _ => "",
    };

    /// <summary>Reveal and Give are online-only — solo has no opponent to reveal to or give to, so
    /// the menu hides them entirely there rather than showing a permanently-disabled option.</summary>
    public bool CanUseOnlineAction => IsOnline;

    [RelayCommand]
    private void OpenActionsMenu()
    {
        IsActionsMenuOpen = true;
        SelectedAction = ArmedChord.None;
    }

    [RelayCommand]
    private void SelectAction(ArmedChord action)
    {
        SelectedAction = action;
        ActionCount = 1;
        _hasTypedActionCount = false;
    }

    [RelayCommand]
    private void BackToActionsList() => SelectedAction = ArmedChord.None;

    [RelayCommand]
    private void CloseActionsMenu()
    {
        IsActionsMenuOpen = false;
        SelectedAction = ArmedChord.None;
    }

    [RelayCommand]
    private void IncreaseActionCount() => ActionCount = Math.Min(ActionCount + 1, 99);

    [RelayCommand]
    private void DecreaseActionCount() => ActionCount = Math.Max(ActionCount - 1, 1);

    [RelayCommand]
    private void ConfirmAction()
    {
        var action = SelectedAction;
        var count = ActionCount;
        CloseActionsMenu();
        RunAction(action, count);
    }

    private void RunAction(ArmedChord action, int count)
    {
        switch (action)
        {
            case ArmedChord.Banish:
                _session.Banish(Player, count);
                break;
            case ArmedChord.Mill:
                var milled = _session.Mill(Player, count);
                ArmUndo("Mill", () =>
                {
                    var graveyard = Player.GetZone(ZoneType.Graveyard);
                    foreach (var card in milled)
                    {
                        graveyard.Cards.Remove(card);
                    }

                    // Insert back-to-front so milled[0] (the original top card) ends up on top
                    // again — ObservableCollection has no InsertRange.
                    var deck = Player.GetZone(ZoneType.MainDeck);
                    for (var i = milled.Count - 1; i >= 0; i--)
                    {
                        deck.Cards.Insert(0, milled[i]);
                    }
                });
                break;
            case ArmedChord.Glimpse:
                GlimpseCardsForChord(count);
                break;
            case ArmedChord.Reveal:
                RevealTopCards(count);
                break;
            case ArmedChord.Give:
                ArmGiveBlind(count);
                break;
        }
    }

    /// <summary>Keyboard shortcuts for the board — add more cases here as they come up.</summary>
    public bool HandleKey(Key key)
    {
        // Glimpsing locks out every key — the count was already decided up front by the Glimpse
        // action that opened it, so there's nothing left for a keystroke to do until sorting
        // finishes.
        if (IsGlimpsing)
        {
            return true;
        }

        // While the Actions menu's counter is showing, digits/Backspace type directly into
        // ActionCount and Enter confirms — everything else is swallowed, same as any other overlay.
        if (IsEnteringActionCount)
        {
            if (TryGetDigit(key, out var digit))
            {
                ActionCount = _hasTypedActionCount ? Math.Min(ActionCount * 10 + digit, 99) : digit;
                _hasTypedActionCount = true;
                return true;
            }

            if (key == Key.Back)
            {
                ActionCount = Math.Max(ActionCount / 10, 1);
                return true;
            }

            if (key == Key.Enter && ConfirmActionCommand.CanExecute(null))
            {
                ConfirmActionCommand.Execute(null);
            }

            return true;
        }

        // Every other overlay (Zoom, Peek, Dice, Save Game, Opponent panel, the Actions menu's own
        // list view, the snapshot viewer and its pile popup, ...) blocks shortcuts the same way
        // Glimpsing already did above — a key meant for the board shouldn't reach through a modal
        // that's currently covering it.
        if (IsAnyOverlayOpen())
        {
            return true;
        }

        // Banish is common enough during ordinary play to warrant a bare digit key rather than a
        // trip through the Actions menu every time — no letter, no arming, just press a count.
        if (TryGetDigit(key, out var banishCount) && banishCount > 0)
        {
            _session.Banish(Player, banishCount);
            return true;
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
            default:
                return false;
        }
    }

    /// <summary>True while any modal overlay is covering the board — see HandleKey's own comment on
    /// why that blocks every keyboard shortcut. Extend this alongside each new overlay-flag
    /// property (and give that property an OnXChanged hook calling CloseActionsMenu(), so a
    /// half-configured action can't go stale if some other overlay opens first).</summary>
    private bool IsAnyOverlayOpen() =>
        IsOpponentPanelOpen || IsRollingDice || IsSavingGame || IsSealedPanelOpen || IsGiveTargetPickerOpen || IsActionsMenuOpen ||
        ZoomedCard is not null || PeekedZone is not null || ViewedMajorEvent is not null ||
        ViewedSnapshotPile is not null || ZoomedSnapshotCard is not null || ActiveReveal is not null;

    private static bool TryGetDigit(Key key, out int digit)
    {
        if (key is >= Key.D0 and <= Key.D9)
        {
            digit = key - Key.D0;
            return true;
        }

        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            digit = key - Key.NumPad0;
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
        var saved = GameSessionSerializer.Capture(SaveGameName.Trim(), _session, IsOnline ? ElapsedTime : null);
        _gameStorage.Save(saved);
        SaveGameStatusMessage = $"Saved \"{saved.Name}\".";

        // The saved copy of ElapsedTime is now fixed at this instant — stop the live clock here too
        // so what's on screen for the rest of this session can't drift past what got saved.
        if (IsOnline)
        {
            _elapsedTimeTimer?.Stop();
            _isElapsedTimeFrozen = true;
        }
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

    /// <summary>Clicking the same pile again closes it; clicking a different one switches to it.
    /// Main Deck is excluded online (CanPeekZone) — knowing your own draw order ahead of time is a
    /// solo goldfishing convenience, not something a real opponent should be able to see you do.</summary>
    [RelayCommand(CanExecute = nameof(CanPeekZone))]
    private void PeekZone(PeekedZoneInfo? info)
    {
        if (info is null)
        {
            return;
        }

        PeekedZone = PeekedZone?.Zone == info.Zone ? null : info;
    }

    private bool CanPeekZone(PeekedZoneInfo? info) => info is null || info.Zone.Type != ZoneType.MainDeck || IsSolo;

    [RelayCommand]
    private void ClosePeek() => PeekedZone = null;

    /// <summary>Called by CardDragBehavior right before a drag actually starts — closes every
    /// overlay that covers the whole board (Peek, Sealed) so a card dragged out of one has
    /// somewhere real to land, rather than the overlay itself being on top of every drop target.</summary>
    internal void CloseOverlaysThatBlockDragTarget()
    {
        PeekedZone = null;
        IsSealedPanelOpen = false;
    }

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
