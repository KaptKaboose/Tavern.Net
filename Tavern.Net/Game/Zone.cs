using System.Collections.ObjectModel;

namespace Tavern.Net.Game;

/// <summary>An ordered pile of cards belonging to one player (e.g. their hand, deck, graveyard).</summary>
public sealed class Zone
{
    public ZoneType Type { get; }

    public ObservableCollection<CardInstance> Cards { get; } = new();

    public Zone(ZoneType type)
    {
        Type = type;
    }
}
