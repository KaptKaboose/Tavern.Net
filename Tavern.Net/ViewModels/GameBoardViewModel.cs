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

    public Player Player { get; }

    public GameStats Stats => Player.Stats;

    public ZoneViewModel Hand { get; }
    public ZoneViewModel Field { get; }
    public ZoneViewModel MainDeck { get; }
    public ZoneViewModel MaterialDeck { get; }
    public ZoneViewModel Graveyard { get; }
    public ZoneViewModel Banishment { get; }
    public ZoneViewModel Memory { get; }

    /// <summary>The card currently shown full-size in the zoom overlay, or null when it's closed.</summary>
    [ObservableProperty]
    private CardViewModel? _zoomedCard;

    /// <summary>The pile currently fanned out in the peek overlay, or null when it's closed.</summary>
    [ObservableProperty]
    private PeekedZoneInfo? _peekedZone;

    /// <summary>Mirrors <see cref="GameSession.CurrentPhase"/> so the board can bind to it.</summary>
    [ObservableProperty]
    private TurnPhase _currentPhase;

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
    }

    [RelayCommand]
    private void DrawCard() => _session.DrawCard(Player);

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
            case Key.N:
                // StartNewGame already draws each player's opening hand as part of setup.
                _session.StartNewGame();
                CurrentPhase = _session.CurrentPhase;
                return true;
            case Key.B:
                // Arm the chord; the count comes from whatever digit key (1-9) is pressed next.
                _banishArmed = true;
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

    /// <summary>Tap state only means something on the Field, so a click anywhere else is a no-op.</summary>
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

    [RelayCommand]
    private void ZoomCard(CardViewModel? card) => ZoomedCard = card;

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
