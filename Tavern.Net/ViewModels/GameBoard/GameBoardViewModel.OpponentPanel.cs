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

/// <summary>The opponent panel: live opponent state, merged play log, arrival glow.</summary>
public sealed partial class GameBoardViewModel
{
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
        else
        {
            // Closing the board means the new arrivals have been seen — drop their glow.
            _unseenOpponentArrivals.Clear();
            RebuildOpponentBoard();
        }
    }

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

        TrackOpponentArrivals();
        RebuildOpponentBoard();

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

    // --- Arrival glow: cards that showed up on the opponent's Field/Champion/Graveyard/Banishment
    // since the panel was last open glow gold (piles glow as a whole, and the card inside them when
    // opened). Found by comparing zone contents across updates, not from the log: the log only knows
    // a name, which is ambiguous with duplicate copies.

    private static readonly ZoneType[] ArrivalZones = { ZoneType.Field, ZoneType.Champion, ZoneType.Graveyard, ZoneType.Banishment };

    private Dictionary<(ZoneType Zone, string Slug), int>? _previousOpponentCounts;
    private readonly Dictionary<(ZoneType Zone, string Slug), int> _unseenOpponentArrivals = new();

    private void TrackOpponentArrivals()
    {
        var current = ArrivalZones
            .SelectMany(zone => OpponentPlayer!.GetZone(zone).Cards.Select(card => (zone, card.Card.Slug)))
            .GroupBy(key => key)
            .ToDictionary(group => group.Key, group => group.Count());

        // The very first update after joining / a New Game only sets the baseline.
        if (_hasSeenInitialOpponentState && _previousOpponentCounts is not null)
        {
            foreach (var (key, count) in current)
            {
                var before = _previousOpponentCounts.GetValueOrDefault(key);
                if (count > before)
                {
                    _unseenOpponentArrivals[key] = _unseenOpponentArrivals.GetValueOrDefault(key) + (count - before);
                }
            }
        }

        // An arrival that has since left (or been outnumbered) can't glow more copies than exist.
        foreach (var key in _unseenOpponentArrivals.Keys.ToList())
        {
            var now = current.GetValueOrDefault(key);
            if (now == 0)
            {
                _unseenOpponentArrivals.Remove(key);
            }
            else if (_unseenOpponentArrivals[key] > now)
            {
                _unseenOpponentArrivals[key] = now;
            }
        }

        _previousOpponentCounts = current;
    }

    /// <summary>Rebuilds the opponent panel's Field and pile boxes from OpponentPlayer, marking the
    /// unseen arrivals.</summary>
    private void RebuildOpponentBoard()
    {
        if (OpponentPlayer is null)
        {
            return;
        }

        OpponentFieldCards.Clear();
        foreach (var card in OrderFieldByType(BuildOpponentCardSnapshots(ZoneType.Field)))
        {
            OpponentFieldCards.Add(card);
        }

        OpponentChampionPile = OpponentPile(ZoneType.Champion);
        OpponentGraveyardPile = OpponentPile(ZoneType.Graveyard);
        OpponentBanishmentPile = OpponentPile(ZoneType.Banishment);
    }

    private SnapshotZoneGroup OpponentPile(ZoneType zone)
    {
        var cards = BuildOpponentCardSnapshots(zone);
        return new SnapshotZoneGroup(zone, cards, HasNewArrival: cards.Any(card => card.IsHighlighted));
    }

    private List<CardSnapshotViewModel> BuildOpponentCardSnapshots(ZoneType zone)
    {
        var cards = OpponentPlayer!.GetZone(zone).Cards
            .Select(card => new CardSnapshotViewModel(CardSnapshot.From(card, zone), _apiClient, this))
            .ToList();

        var remaining = _unseenOpponentArrivals
            .Where(entry => entry.Key.Zone == zone)
            .ToDictionary(entry => entry.Key.Slug, entry => entry.Value);
        if (remaining.Count > 0)
        {
            // Newest first: the Field appends (newest last); the piles insert at the top (index 0).
            foreach (var card in zone == ZoneType.Field ? Enumerable.Reverse(cards) : cards)
            {
                var slug = card.Snapshot.Card.Slug;
                if (remaining.TryGetValue(slug, out var left) && left > 0)
                {
                    card.IsHighlighted = true;
                    remaining[slug] = left - 1;
                }
            }
        }

        return cards;
    }

    [RelayCommand]
    private void ToggleOpponentPanel() => IsOpponentPanelOpen = !IsOpponentPanelOpen;
}
