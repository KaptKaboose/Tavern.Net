using Tavern.Net.Game;
using Tavern.Net.GameData.Models;
using static Tavern.Net.Tests.SessionTestData;

namespace Tavern.Net.Tests;

/// <summary>Life and damage-dealt adjustments.</summary>
public class GameSessionStatsTests
{
    [Fact]
    public void AdjustLife_UpdatesLife()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo", startingLife: 15);

        session.AdjustLife(player, -3);

        Assert.Equal(12, player.Life);
    }

    [Fact]
    public void AdjustDamageDealt_UpdatesRunningTotal()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");

        session.AdjustDamageDealt(player, 5);
        session.AdjustDamageDealt(player, 3);

        Assert.Equal(8, player.Stats.DamageDealtCount);
    }

    [Fact]
    public void AdjustDamageDealt_ClampsAtZero()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        session.AdjustDamageDealt(player, 2);

        session.AdjustDamageDealt(player, -5);

        Assert.Equal(0, player.Stats.DamageDealtCount);
    }
}
