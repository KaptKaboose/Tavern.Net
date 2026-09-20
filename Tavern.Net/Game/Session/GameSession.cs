using Tavern.Net.GameData.Models;

namespace Tavern.Net.Game;

/// <summary>
/// Orchestrates a play session. Modeled around a list of players (rather
/// than a hardcoded pair) so a future networked mode can add a second,
/// remotely-controlled player without reshaping this class.
/// The class is split by feature into the files in this folder (each a <c>partial</c>); this one holds the players and construction.
/// </summary>
public sealed partial class GameSession
{
    private readonly Random _random;

    public List<Player> Players { get; } = new();

    public GameSession(Random? random = null)
    {
        _random = random ?? new Random();
    }

    public Player AddPlayer(string name, int startingLife = 15)
    {
        var player = new Player(name, Players.Count, startingLife);
        Players.Add(player);
        ActivePlayer ??= player;
        return player;
    }
}
