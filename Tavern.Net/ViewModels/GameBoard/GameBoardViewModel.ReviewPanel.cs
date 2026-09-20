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

/// <summary>The review panel: viewing a past event's board (snapshot viewer) and its highlight.</summary>
public sealed partial class GameBoardViewModel
{
    /// <summary>The Major event currently being viewed (read-only) in the snapshot overlay, or
    /// null when it's closed. Viewing never mutates the live game — see GameSession.TakeSnapshot's
    /// own doc comment on why this is a display-only feature, not a rollback.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasViewedMemoryCards), nameof(HasViewedHandCards), nameof(CurrentlyViewedEvent), nameof(ViewedPhase))]
    private MajorEvent? _viewedMajorEvent;

    /// <summary>Online only: whether ViewedMajorEvent belongs to this player (true) or the opponent
    /// (false) — decides the "Your Board"/"Opponent's Board" tab labels and which side
    /// ViewedOtherPlayerEvent searches on. Meaningless (and unused) for solo.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryTabLabel), nameof(OtherTabLabel), nameof(CurrentlyViewedEventIsOwn), nameof(HideViewedPrivateZones))]
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
    [NotifyPropertyChangedFor(nameof(CurrentlyViewedEvent), nameof(CurrentlyViewedEventIsOwn), nameof(HideViewedPrivateZones))]
    private bool _isShowingOtherPlayerTab;

    public bool HasOtherPlayerTab => OpponentPlayer is not null;

    /// <summary>Which Play Log to show: the merged two-player one whenever there's a second player
    /// (live online, or a loaded save of an online game), otherwise the solo one.</summary>
    public bool ShowMergedLog => OpponentPlayer is not null;

    public bool ShowSoloLog => OpponentPlayer is null;

    public string PrimaryTabLabel => PrimaryEntryIsOwn ? "Your Board" : "Opponent's Board";

    public string OtherTabLabel => PrimaryEntryIsOwn ? "Opponent's Board" : "Your Board";

    /// <summary>Whichever event's board the popup is currently showing — drives the header's own
    /// Turn/Description/Life/Phase line, which needs to follow the active tab the same way the
    /// card collections below it do.</summary>
    public MajorEvent? CurrentlyViewedEvent => IsShowingOtherPlayerTab ? ViewedOtherPlayerEvent : ViewedMajorEvent;

    /// <summary>Whether <see cref="CurrentlyViewedEvent"/> is this player's own (always true solo) —
    /// colors the header's "Turn X:" green vs. orange, matching the Play Log.</summary>
    public bool CurrentlyViewedEventIsOwn => IsShowingOtherPlayerTab ? !PrimaryEntryIsOwn : PrimaryEntryIsOwn;

    /// <summary>True while the review panel is showing the opponent's board in a live online game —
    /// their Hand/Memory/Material/Main show as face-down counts, same as the Opponent panel, since
    /// the snapshots hold the real cards. (Your own Main is hidden too in a live online game — see
    /// PopulateViewedCollections.) A game reviewed later from a save (not online) shows everything.</summary>
    public bool HideViewedPrivateZones => IsOnline && !CurrentlyViewedEventIsOwn;

    /// <summary>The phase for the review header — always the clicked entry's own snapshot, so both
    /// tabs agree. (The other tab's own event is just that player's nearest earlier event, whose
    /// snapshot phase is from whenever they last did something.)</summary>
    public TurnPhase? ViewedPhase => ViewedMajorEvent?.Snapshot.Phase;

    [ObservableProperty]
    private int _viewedHandCount;

    [ObservableProperty]
    private int _viewedMemoryCount;

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
    // click-to-open piles, matching how the live board renders them. Champion isn't here — it gets
    // a row to itself (ViewedChampionPile), since a tapped champion needs the room. The rest pair
    // up to match the UniformGrid's 2-per-row layout: Graveyard+Banishment, then Main+Material.
    private static readonly ZoneType[] SnapshotPileOrder =
    {
        ZoneType.Graveyard,
        ZoneType.Banishment,
        ZoneType.MainDeck,
        ZoneType.MaterialDeck,
    };

    private static SnapshotZoneGroup EmptyChampionPile() => new(ZoneType.Champion, Array.Empty<CardSnapshotViewModel>());

    /// <summary>The snapshot viewer's Champion pile, shown centered on its own row above the rest.</summary>
    [ObservableProperty]
    private SnapshotZoneGroup _viewedChampionPile = EmptyChampionPile();

    // Card types in the order a Field is laid out by — anything not listed sorts after, alphabetically.
    private static readonly string[] FieldTypeOrder = { "ally", "weapon", "item", "domain", "phantasia", "regalia", "action", "attack" };

    /// <summary>Orders a Field's cards by type (a fixed preference order, then alphabetical for
    /// anything unlisted) — keeping each type's cards in their original order — and flags the first
    /// card of every group after the first so the view can leave a little space between groups.</summary>
    private static List<CardSnapshotViewModel> OrderFieldByType(IEnumerable<CardSnapshotViewModel> cards)
    {
        // The card's real type, skipping the "Unique" modifier the API lists first ("UNIQUE","ALLY")
        // — so an Ally and a Unique Ally share one group.
        static string GroupKey(CardSnapshotViewModel c) =>
            (c.Snapshot.Card.Types.FirstOrDefault(t => !t.Equals("unique", StringComparison.OrdinalIgnoreCase)) ?? "").ToLowerInvariant();

        static int Rank(CardSnapshotViewModel c)
        {
            var index = Array.IndexOf(FieldTypeOrder, GroupKey(c));
            return index < 0 ? int.MaxValue : index;
        }

        var ordered = cards
            .OrderBy(Rank)
            .ThenBy(GroupKey, StringComparer.Ordinal)
            .ToList();

        for (var i = 1; i < ordered.Count; i++)
        {
            ordered[i].StartsNewGroup = GroupKey(ordered[i]) != GroupKey(ordered[i - 1]);
        }

        return ordered;
    }

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

        PopulateViewedCollections(value?.Snapshot, value);
    }

    partial void OnIsShowingOtherPlayerTabChanged(bool value)
    {
        var shown = value ? ViewedOtherPlayerEvent : ViewedMajorEvent;
        PopulateViewedCollections(shown?.Snapshot, shown);
    }

    [RelayCommand]
    private void ShowPrimaryTab() => IsShowingOtherPlayerTab = false;

    [RelayCommand]
    private void ShowOtherPlayerTab() => IsShowingOtherPlayerTab = true;

    /// <param name="highlightEvent">The event whose card should glow (its CardName in its CardZone) —
    /// the newest matching copy, since the log entry is about the card that just arrived.</param>
    private void PopulateViewedCollections(GameSnapshot? snapshot, MajorEvent? highlightEvent = null)
    {
        ViewedSnapshotPiles.Clear();
        ViewedFieldCards.Clear();
        ViewedHandCards.Clear();
        ViewedMemoryCards.Clear();
        ViewedChampionPile = EmptyChampionPile();
        ViewedSnapshotPile = null;
        ZoomedSnapshotCard = null;

        if (snapshot is null)
        {
            return;
        }

        var cardsByZone = snapshot.Cards.ToLookup(c => c.Zone);

        // Hand/Memory/Material are hidden on the opponent's board in a live online game; Main is
        // hidden for both players in one (knowing your own draw order is off-limits online — see
        // CanPeekZone), and only becomes viewable when reviewing a saved game later.
        var hide = HideViewedPrivateZones;
        var hideMain = IsOnline;

        // Newest copy of the event's card in the event's zone glows: the Field appends (newest last),
        // the piles insert at the top (first).
        void Highlight(ZoneType zone, IReadOnlyList<CardSnapshotViewModel> cards)
        {
            if (highlightEvent is not { CardName: { Length: > 0 } name, CardZone: { } eventZone } || eventZone != zone)
            {
                return;
            }

            var match = (zone == ZoneType.Field ? cards.Reverse() : cards).FirstOrDefault(c => c.Snapshot.Card.Name == name);
            if (match is not null)
            {
                match.IsHighlighted = true;
            }
        }

        var fieldCards = cardsByZone[ZoneType.Field].Select(c => new CardSnapshotViewModel(c, _apiClient, this)).ToList();
        Highlight(ZoneType.Field, fieldCards);
        foreach (var card in OrderFieldByType(fieldCards))
        {
            ViewedFieldCards.Add(card);
        }

        var championCards = cardsByZone[ZoneType.Champion].Select(c => new CardSnapshotViewModel(c, _apiClient, this)).ToList();
        Highlight(ZoneType.Champion, championCards);
        ViewedChampionPile = new SnapshotZoneGroup(ZoneType.Champion, championCards, HasNewArrival: championCards.Any(c => c.IsHighlighted));

        ViewedHandCount = cardsByZone[ZoneType.Hand].Count();
        ViewedMemoryCount = cardsByZone[ZoneType.Memory].Count();

        if (!hide)
        {
            foreach (var card in cardsByZone[ZoneType.Hand])
            {
                ViewedHandCards.Add(new CardSnapshotViewModel(card, _apiClient, this));
            }

            foreach (var card in cardsByZone[ZoneType.Memory])
            {
                ViewedMemoryCards.Add(new CardSnapshotViewModel(card, _apiClient, this));
            }
        }

        // Always all four piles, even ones with no cards (a "0" box rather than the pile vanishing) —
        // walking SnapshotPileOrder directly rather than grouping+reordering also guarantees that
        // fixed order regardless of which zones the snapshot happens to have cards in.
        foreach (var zone in SnapshotPileOrder)
        {
            if ((zone == ZoneType.MaterialDeck && hide) || (zone == ZoneType.MainDeck && hideMain))
            {
                ViewedSnapshotPiles.Add(new SnapshotZoneGroup(zone, Array.Empty<CardSnapshotViewModel>(), IsRedacted: true, RedactedCount: cardsByZone[zone].Count()));
                continue;
            }

            var cards = cardsByZone[zone].Select(c => new CardSnapshotViewModel(c, _apiClient, this)).ToList();
            Highlight(zone, cards);
            ViewedSnapshotPiles.Add(new SnapshotZoneGroup(zone, cards, HasNewArrival: cards.Any(c => c.IsHighlighted)));
        }
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
}
