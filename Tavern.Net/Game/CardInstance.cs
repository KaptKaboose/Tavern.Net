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

    [ObservableProperty]
    private bool _isTapped;

    /// <summary>Position on the Field's freeform canvas. Meaningless outside the Field zone.</summary>
    [ObservableProperty]
    private double _fieldX;

    [ObservableProperty]
    private double _fieldY;

    public CardInstance(CardDto card)
    {
        Card = card;
    }
}
