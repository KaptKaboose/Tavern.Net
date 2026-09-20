using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tavern.Net.Game;
using Tavern.Net.GameData;
using Tavern.Net.GameData.Models;
using Tavern.Net.Online;

namespace Tavern.Net.ViewModels;

/// <summary>Everyday board actions: drawing, life, moving/tapping/flipping cards, the Sealed panel.</summary>
public sealed partial class GameBoardViewModel
{
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

    // --- "Just changed" highlights for your own Life and Damage numbers (the header and the opponent
    // panel bind to these — see the DataTriggers on those TextBlocks). Any change to the number lights
    // it, whatever caused it (buttons, the +/- keys, a Champion level-up), except a new game's reset.

    [ObservableProperty]
    private bool _lifeJustChanged;

    [ObservableProperty]
    private bool _damageJustChanged;

    private FlashTimer? _lifeFlash;
    private FlashTimer? _damageFlash;

    // True while a new game is resetting Life/Damage to their starting values, which isn't a change
    // the player made and shouldn't flash.
    private bool _suppressCounterFlash;

    private void OnOwnCounterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressCounterFlash)
        {
            return;
        }

        if (e.PropertyName == nameof(Player.Life))
        {
            (_lifeFlash ??= new FlashTimer(lit => LifeJustChanged = lit)).Trigger();
        }
        else if (e.PropertyName == nameof(GameStats.DamageDealtCount))
        {
            (_damageFlash ??= new FlashTimer(lit => DamageJustChanged = lit)).Trigger();
        }
    }

    [RelayCommand]
    private void IncreaseLife() => _session.AdjustLife(Player, 1);

    [RelayCommand]
    private void DecreaseLife() => _session.AdjustLife(Player, -1);

    [RelayCommand]
    private void IncreaseDamageDealt() => _session.AdjustDamageDealt(Player, 1);

    [RelayCommand]
    private void DecreaseDamageDealt() => _session.AdjustDamageDealt(Player, -1);

    /// <summary>Single entry point for drag-and-drop moves, which carry their destination (and, for the Field, a drop position) as data rather than a fixed command per destination.</summary>
    [RelayCommand(CanExecute = nameof(CanMoveCardTo))]
    private void MoveCardTo(MoveCardRequest? request)
    {
        if (request is not null)
        {
            Move(request.Card, request.Destination, request.FieldX, request.FieldY);
        }
    }

    /// <summary>Whether the dragged card may go to that zone at all (GameSession.CanMove) — what
    /// CardDropBehavior asks while a drag is hovering, so a zone that would refuse the drop isn't
    /// highlighted as a target.</summary>
    private bool CanMoveCardTo(MoveCardRequest? request)
    {
        if (request is null)
        {
            return false;
        }

        var from = ZoneOfCard(request.Card);
        return from is not null && _session.CanMove(request.Card.Instance, from.Value, request.Destination);
    }

    private ZoneType? ZoneOfCard(CardViewModel card) =>
        RevealPanelCards.Contains(card)
            ? ZoneType.MainDeck
            : Player.Zones.FirstOrDefault(kv => kv.Value.Cards.Contains(card.Instance)).Value?.Type;

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

        var zone = Player.Zones.FirstOrDefault(kv => kv.Value.Cards.Contains(card.Instance)).Value?.Type;
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

    private void Move(CardViewModel? card, ZoneType destination, double? fieldX = null, double? fieldY = null)
    {
        System.Diagnostics.Debug.WriteLine($"[DragDrop] Move called: card={card?.Name ?? "null"} destination={destination} fieldX={fieldX} fieldY={fieldY}");

        if (card is null)
        {
            return;
        }

        if (RevealPanelCards.Contains(card))
        {
            if (_session.MoveRevealedCard(Player, card.Instance, destination, fieldX, fieldY))
            {
                RevealPanelCards.Remove(card);
                RevealNextCommand.NotifyCanExecuteChanged();
                if (RevealPanelCards.Count == 0)
                {
                    CloseRevealPanel();
                }
            }

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
