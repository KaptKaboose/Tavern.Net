using CommunityToolkit.Mvvm.ComponentModel;

namespace Tavern.Net.Game;

public sealed partial class Player : ObservableObject
{
    public string Name { get; }

    public int PlayerNumber { get; }

    [ObservableProperty]
    private int _life = 15;

    public GameStats Stats { get; } = new();

    public Dictionary<ZoneType, Zone> Zones { get; } = Enum.GetValues<ZoneType>()
        .ToDictionary(z => z, z => new Zone(z));

    public Player(string name, int playerNumber, int startingLife = 15)
    {
        Name = name;
        PlayerNumber = playerNumber;
        _life = startingLife;
    }

    public Zone GetZone(ZoneType type) => Zones[type];
}
