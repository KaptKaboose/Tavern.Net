using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Input;

namespace Tavern.Net.Game;

public sealed partial class Player : ObservableObject
{
    public string Name { get; }

    public int PlayerNumber { get; }

    public int StartingHandSize => _startingHandSize;

    public bool StartsInMemory => _startsInMemory;

    private int _startingHandSize = 7;
    private bool _startsInMemory = false;

    [ObservableProperty]
    private int _life = 15;

    public GameStats Stats { get; } = new();

    /// <summary>This player's deck lists (Main/Material/sideboard, plus the registered arrangement
    /// for resets) — see <see cref="DeckArrangement"/>. Null for the local mirror of an online
    /// opponent, whose deck is never sent over the wire.</summary>
    public DeckArrangement? Deck { get; set; }

    public Dictionary<ZoneType, Zone> Zones { get; } = Enum.GetValues<ZoneType>()
        .ToDictionary(z => z, z => new Zone(z));

    public Player(string name, int playerNumber, int startingLife = 15)
    {
        Name = name;
        PlayerNumber = playerNumber;
        _life = startingLife;
    }

    public Zone GetZone(ZoneType type) => Zones[type];

    public void SetStartingHandSize(int size)
    {
        if (size < 0 || size > 20)
            throw new ArgumentOutOfRangeException(nameof(size), "Starting hand size must be between 0 and 20.");
        _startingHandSize = size;
    }

    public void SetStartsInMemory(bool startsInMemory)
    {
        _startsInMemory = startsInMemory;
    }
}