using Tavern.Net.Game;
using Tavern.Net.GameData.Models;
using static Tavern.Net.Tests.SessionTestData;

namespace Tavern.Net.Tests;

/// <summary>Giving cards to (and receiving them from) the opponent.</summary>
public class GameSessionGiveTests
{
    [Fact]
    public void RemoveCardForGive_RemovesFromNamedZone()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var card = MakeCard();
        player.GetZone(ZoneType.Hand).Cards.Add(card);

        session.RemoveCardForGive(player, card, ZoneType.Hand);

        Assert.Empty(player.GetZone(ZoneType.Hand).Cards);
    }

    [Fact]
    public void TakeTopCardsForGive_RemovesExactlyTopN()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var top = MakeCard("Top");
        var second = MakeCard("Second");
        var third = MakeCard("Third");
        player.GetZone(ZoneType.MainDeck).Cards.Add(top);
        player.GetZone(ZoneType.MainDeck).Cards.Add(second);
        player.GetZone(ZoneType.MainDeck).Cards.Add(third);

        var taken = session.TakeTopCardsForGive(player, 2);

        Assert.Equal(new[] { top, second }, taken);
        Assert.Equal(new[] { third }, player.GetZone(ZoneType.MainDeck).Cards);
    }

    [Fact]
    public void ReceiveGivenCard_ToField_AddsAndRecordsMajorEvent()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var cardDto = new CardDto { Name = "Curse" };

        var instance = session.ReceiveGivenCard(player, cardDto, ZoneType.Field, "Hand");

        Assert.Same(instance, Assert.Single(player.GetZone(ZoneType.Field).Cards));
        Assert.Contains(player.Stats.MajorEvents, e => e.Description.Contains("Curse"));
    }

    [Fact]
    public void ReceiveGivenCard_ToSealed_AddsWithoutRecordingMajorEvent()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var cardDto = new CardDto { Name = "Secret" };

        var instance = session.ReceiveGivenCard(player, cardDto, ZoneType.Sealed, "Main Deck (blind)");

        Assert.Same(instance, Assert.Single(player.GetZone(ZoneType.Sealed).Cards));
        Assert.Empty(player.Stats.MajorEvents);
    }

    [Fact]
    public void ReceiveGivenCard_HomeZoneIsDestination_SoNewGameDiscardsIt()
    {
        var player = MakePlayerWithDeck(deckSize: 10);
        var session = new GameSession();
        session.Players.Add(player);
        player.GetZone(ZoneType.MaterialDeck).Cards.Add(MakeBaseChampion());

        session.ReceiveGivenCard(player, new CardDto { Name = "Given" }, ZoneType.Field, "Hand");
        Assert.Single(player.GetZone(ZoneType.Field).Cards);

        session.StartNewGame();

        Assert.DoesNotContain(player.Zones.Values.SelectMany(z => z.Cards), c => c.Card.Name == "Given");
    }
}
