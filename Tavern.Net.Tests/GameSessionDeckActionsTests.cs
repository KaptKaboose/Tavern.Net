using Tavern.Net.Game;
using Tavern.Net.GameData.Models;
using static Tavern.Net.Tests.SessionTestData;

namespace Tavern.Net.Tests;

/// <summary>Main Deck actions: restore order, generate, banish, mill.</summary>
public class GameSessionDeckActionsTests
{
    [Fact]
    public void RestoreMainDeckOrder_ResetsToExactSnapshotOrder()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var first = MakeCard("First");
        var second = MakeCard("Second");
        var third = MakeCard("Third");
        player.GetZone(ZoneType.MainDeck).Cards.Add(first);
        player.GetZone(ZoneType.MainDeck).Cards.Add(second);
        player.GetZone(ZoneType.MainDeck).Cards.Add(third);
        var snapshot = player.GetZone(ZoneType.MainDeck).Cards.ToList();

        session.Mill(player, 2);
        Assert.Equal(new[] { third }, player.GetZone(ZoneType.MainDeck).Cards);

        session.RestoreMainDeckOrder(player, snapshot);

        Assert.Equal(snapshot, player.GetZone(ZoneType.MainDeck).Cards);
    }

    [Fact]
    public void GenerateCard_AddsANewInstanceAtMainsBottomFlaggedAsSessionGenerated()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var existingTop = MakeCard("Existing Top");
        player.GetZone(ZoneType.MainDeck).Cards.Add(existingTop);
        var cardDto = new CardDto { Name = "Extra Copy" };

        var generated = session.GenerateCard(player, cardDto);

        Assert.Equal(new[] { existingTop, generated }, player.GetZone(ZoneType.MainDeck).Cards);
        Assert.Equal(ZoneType.MainDeck, generated.HomeZone);
        Assert.True(generated.IsSessionGenerated);
    }

    [Fact]
    public void Banish_MovesExactlyCountRandomCardsFromMemoryToBanishment()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        for (var i = 0; i < 5; i++)
        {
            player.GetZone(ZoneType.Memory).Cards.Add(MakeCard($"Memory {i}"));
        }

        session.Banish(player, 3);

        Assert.Equal(2, player.GetZone(ZoneType.Memory).Cards.Count);
        Assert.Equal(3, player.GetZone(ZoneType.Banishment).Cards.Count);
    }

    [Fact]
    public void Banish_CountExceedingMemorySize_BanishesWhateverIsThere()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        player.GetZone(ZoneType.Memory).Cards.Add(MakeCard());

        session.Banish(player, 5);

        Assert.Empty(player.GetZone(ZoneType.Memory).Cards);
        Assert.Single(player.GetZone(ZoneType.Banishment).Cards);
    }

    [Fact]
    public void Mill_MovesTopNCardsToGraveyard_InOrder()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var top = MakeCard("Top");
        var second = MakeCard("Second");
        var third = MakeCard("Third");
        player.GetZone(ZoneType.MainDeck).Cards.Add(top);
        player.GetZone(ZoneType.MainDeck).Cards.Add(second);
        player.GetZone(ZoneType.MainDeck).Cards.Add(third);

        session.Mill(player, 2);

        Assert.Equal(new[] { third }, player.GetZone(ZoneType.MainDeck).Cards);
        Assert.Equal(new[] { second, top }, player.GetZone(ZoneType.Graveyard).Cards);
    }

    [Fact]
    public void Mill_CountExceedingDeckSize_MillsWhateverIsThere()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        player.GetZone(ZoneType.MainDeck).Cards.Add(MakeCard());

        session.Mill(player, 5);

        Assert.Empty(player.GetZone(ZoneType.MainDeck).Cards);
        Assert.Single(player.GetZone(ZoneType.Graveyard).Cards);
    }
}
