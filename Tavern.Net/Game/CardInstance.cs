using CommunityToolkit.Mvvm.ComponentModel;
using Tavern.Net.GameData.Models;

namespace Tavern.Net.Game;

/// <summary>
/// One physical copy of a card as it exists in a game session. Multiple
/// instances can wrap the same <see cref="CardDto"/> (e.g. 4 copies of the
/// same card in a deck).
/// </summary>
public sealed partial class CardInstance : ObservableObject
{
    public Guid InstanceId { get; } = Guid.NewGuid();

    public CardDto Card { get; }

    /// <summary>
    /// Which starting deck this copy belongs to (MainDeck or MaterialDeck) and its position within
    /// that deck's original decklist order. Set once at import time and never changed afterward —
    /// GameSession.StartNewGame uses it to figure out where a card that's since wandered off to
    /// Hand/Field/Graveyard/etc. belongs when rebuilding the two decks for a fresh game.
    /// </summary>
    public ZoneType HomeZone { get; }

    public int HomeOrder { get; }

    [ObservableProperty]
    private bool _isTapped;

    /// <summary>Showing its other face (double-click) — a real flip for a double-faced card
    /// (e.g. a Fatestone), or just the generic card back for an ordinary one.</summary>
    [ObservableProperty]
    private bool _isFlipped;

    /// <summary>Position on the Field's freeform canvas. Meaningless outside the Field zone.</summary>
    [ObservableProperty]
    private double _fieldX;

    [ObservableProperty]
    private double _fieldY;

    public CardInstance(CardDto card, ZoneType homeZone = ZoneType.MainDeck, int homeOrder = 0)
    {
        Card = card;
        HomeZone = homeZone;
        HomeOrder = homeOrder;
    }
}
