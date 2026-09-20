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

/// <summary>The Zoom overlay (card and snapshot), counters/statuses, and pile Peek.</summary>
public sealed partial class GameBoardViewModel
{
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

    /// <summary>
    /// Excludes a card already in Main, and — keyed off HomeZone rather than the card's *current*
    /// zone — any card that originated from the Material deck, no matter where it's wandered off
    /// to since (Champion, or Graveyard/Banishment after dying): none of those have a "back to the
    /// bottom of the deck" mechanic. (ZoneBarriers already silently blocks the actual move for a
    /// Material-origin card, so this is purely about not offering a button that would do nothing.)
    /// Tokens are excluded outright — a token isn't a real deck card, it can only be summoned to
    /// the Field or discarded (see GameSession.MoveToken), never enter Main at all.
    /// </summary>
    public bool CanBottomZoomedCard =>
        ZoomedCard is not null &&
        ZoneOfZoomedCard() != ZoneType.MainDeck &&
        ZoomedCard.Instance.HomeZone != ZoneType.MaterialDeck &&
        !ZoomedCard.Instance.Card.IsToken;

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

    partial void OnZoomedCardChanged(CardViewModel? value)
    {
        _hasTypedZoomCounterDigit = false;

        if (value is not null)
        {
            CloseActionsMenu();
        }
    }

    // Mirrors _hasTypedActionCount for the Zoom overlay's own Counter box: whether the player has
    // typed a digit yet for the currently-zoomed card, deciding whether the next one replaces the
    // counter's value or appends to it. Reset whenever ZoomedCard changes (see above).
    private bool _hasTypedZoomCounterDigit;

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
}
