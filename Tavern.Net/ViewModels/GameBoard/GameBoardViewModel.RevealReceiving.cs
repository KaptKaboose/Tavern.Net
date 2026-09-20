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

/// <summary>Receiving reveals: the opponent's reveal overlay, its queue and countdown.</summary>
public sealed partial class GameBoardViewModel
{
    // --- Receiving a reveal: queued, since more than one can arrive close together (e.g. one from
    // Hand, then moments later a batch off Main) — shown one at a time via a countdown-ring overlay
    // (CountdownRingView), auto-advancing to the next queued entry once the current one closes.

    private readonly Queue<(string? Id, IReadOnlyList<CardSnapshotViewModel> Cards)> _pendingReveals = new();

    // The Reveal panel id of the reveal currently on screen (null for a one-off), and the highest
    // sequence number applied per id — see EnqueueRevealAsync.
    private string? _activeRevealId;
    private readonly Dictionary<string, int> _revealSeqSeen = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RevealCardWidth), nameof(RevealCardHeight))]
    private IReadOnlyList<CardSnapshotViewModel>? _activeReveal;

    /// <summary>Card size in the reveal overlay: full size up to 10 cards (two rows of five), then
    /// smaller steps so a long run still fits several rows before it has to scroll.</summary>
    public double RevealCardWidth => (ActiveReveal?.Count ?? 0) switch
    {
        <= 10 => 240,
        <= 21 => 170,
        _ => 125,
    };

    public double RevealCardHeight => RevealCardWidth * 1.4;

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

    /// <summary>Queues (or, for the same Reveal-panel id, updates) an incoming reveal. A panel session
    /// resends its whole set on every "Next": if that reveal is on screen or queued it is replaced in
    /// place — the new card appears and the countdown restarts — rather than stacking another overlay;
    /// if the opponent already closed it, it simply opens again. A stale (lower-sequence) resend that
    /// arrives late is dropped.</summary>
    private async Task EnqueueRevealAsync(IReadOnlyList<RevealedCardEntry> entries, string? revealId = null, int revealSeq = 0)
    {
        var resolved = new List<CardSnapshotViewModel>();
        foreach (var entry in entries)
        {
            var cardDto = await ResolveOpponentCardAsync(entry.Slug);
            var snapshot = new CardSnapshot(cardDto, ZoneType.Hand, false, entry.IsFlipped, 0, 0, 0, false, false, false, false, false, false);
            resolved.Add(new CardSnapshotViewModel(snapshot, _apiClient, this));
        }

        if (revealId is not null)
        {
            if (_revealSeqSeen.TryGetValue(revealId, out var seen) && revealSeq <= seen)
            {
                return;
            }

            _revealSeqSeen[revealId] = revealSeq;

            if (ActiveReveal is not null && _activeRevealId == revealId)
            {
                ActiveReveal = resolved;
                if (!RevealKeptOpen)
                {
                    RevealRemainingFraction = 1.0;
                    _revealStartedAt = DateTime.UtcNow;
                }

                return;
            }

            if (_pendingReveals.Any(p => p.Id == revealId))
            {
                var queued = _pendingReveals.ToList();
                _pendingReveals.Clear();
                foreach (var item in queued)
                {
                    _pendingReveals.Enqueue(item.Id == revealId ? (revealId, resolved) : item);
                }

                return;
            }
        }

        _pendingReveals.Enqueue((revealId, resolved));
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
            _activeRevealId = null;
            _revealTimer?.Stop();
            return;
        }

        var next = _pendingReveals.Dequeue();
        ActiveReveal = next.Cards;
        _activeRevealId = next.Id;
        RevealKeptOpen = false;
        RevealRemainingFraction = 1.0;
        _revealStartedAt = DateTime.UtcNow;

        _revealTimer?.Stop();
        _revealTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _revealTimer.Tick += OnRevealTimerTick;
        _revealTimer.Start();
    }

    private DateTime _lastRevealTick = DateTime.UtcNow;

    private void OnRevealTimerTick(object? sender, EventArgs e)
    {
        // Looking at a zoomed card shouldn't burn the countdown: slide the start time forward by
        // however long this tick took, so the ring holds still until the zoom closes.
        var now = DateTime.UtcNow;
        if (ZoomedSnapshotCard is not null)
        {
            _revealStartedAt += now - _lastRevealTick;
        }

        _lastRevealTick = now;

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
}
