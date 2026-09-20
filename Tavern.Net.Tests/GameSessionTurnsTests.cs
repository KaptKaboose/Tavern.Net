using Tavern.Net.Game;
using Tavern.Net.GameData.Models;
using static Tavern.Net.Tests.SessionTestData;

namespace Tavern.Net.Tests;

/// <summary>Turn structure: phases, turns, wake-up and recollection.</summary>
public class GameSessionTurnsTests
{
    [Fact]
    public void NextTurn_IncrementsTurnCount()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");

        session.NextTurn(player);
        session.NextTurn(player);

        Assert.Equal(2, player.Stats.TurnCount);
    }

    [Fact]
    public void WakeUp_UntapsFieldButNotOtherZones()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var onField = MakeCard("On Field");
        onField.IsTapped = true;
        player.GetZone(ZoneType.Field).Cards.Add(onField);
        var inHand = MakeCard("In Hand");
        inHand.IsTapped = true;
        player.GetZone(ZoneType.Hand).Cards.Add(inHand);

        session.WakeUp(player);

        Assert.False(onField.IsTapped);
        Assert.True(inHand.IsTapped);
    }

    [Fact]
    public void Recollect_MovesAllMemoryCardsToHandUntapped()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var card = MakeCard();
        card.IsTapped = true;
        player.GetZone(ZoneType.Memory).Cards.Add(card);

        session.Recollect(player);

        Assert.Empty(player.GetZone(ZoneType.Memory).Cards);
        Assert.Same(card, Assert.Single(player.GetZone(ZoneType.Hand).Cards));
        Assert.False(card.IsTapped);
    }

    [Fact]
    public void AdvancePhase_StepsThroughPhasesInOrder()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        Assert.Equal(TurnPhase.WakeUp, session.CurrentPhase);

        session.AdvancePhase(player);
        Assert.Equal(TurnPhase.Materialization, session.CurrentPhase);

        session.AdvancePhase(player);
        Assert.Equal(TurnPhase.Recollection, session.CurrentPhase);

        session.AdvancePhase(player);
        Assert.Equal(TurnPhase.Draw, session.CurrentPhase);

        session.AdvancePhase(player);
        Assert.Equal(TurnPhase.Main, session.CurrentPhase);

        session.AdvancePhase(player);
        Assert.Equal(TurnPhase.End, session.CurrentPhase);
    }

    [Fact]
    public void AdvancePhase_PastEnd_StartsNextTurnAtWakeUp()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        for (var i = 0; i < 5; i++)
        {
            session.AdvancePhase(player);
        }
        Assert.Equal(TurnPhase.End, session.CurrentPhase);
        Assert.Equal(0, player.Stats.TurnCount);

        session.AdvancePhase(player);

        Assert.Equal(TurnPhase.WakeUp, session.CurrentPhase);
        Assert.Equal(1, player.Stats.TurnCount);
    }

    [Fact]
    public void AdvancePhase_PastEnd_WithTwoPlayers_HandsTurnToOtherPlayerAndIncrementsTheirTurnCount()
    {
        var session = new GameSession();
        var playerA = session.AddPlayer("A");
        var playerB = session.AddPlayer("B");
        for (var i = 0; i < 5; i++)
        {
            session.AdvancePhase(playerA);
        }
        Assert.Equal(TurnPhase.End, session.CurrentPhase);
        Assert.Equal(playerA, session.ActivePlayer);

        session.AdvancePhase(playerA);

        Assert.Equal(TurnPhase.WakeUp, session.CurrentPhase);
        Assert.Equal(playerB, session.ActivePlayer);
        Assert.Equal(1, playerB.Stats.TurnCount);
        Assert.Equal(0, playerA.Stats.TurnCount);
    }

    [Fact]
    public void AdvancePhase_ToWakeUp_UntapsField()
    {
        var player = MakePlayerWithDeck(deckSize: 5);
        var session = new GameSession();
        session.Players.Add(player);
        var card = MakeCard();
        card.IsTapped = true;
        player.GetZone(ZoneType.Field).Cards.Add(card);
        for (var i = 0; i < 5; i++)
        {
            session.AdvancePhase(player);
        }

        session.AdvancePhase(player);

        Assert.Equal(TurnPhase.WakeUp, session.CurrentPhase);
        Assert.False(card.IsTapped);
    }

    [Fact]
    public void AdvancePhase_ToRecollection_ReturnsMemoryToHand()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var card = MakeCard();
        player.GetZone(ZoneType.Memory).Cards.Add(card);
        session.AdvancePhase(player); // WakeUp -> Materialization

        session.AdvancePhase(player); // Materialization -> Recollection

        Assert.Equal(TurnPhase.Recollection, session.CurrentPhase);
        Assert.Empty(player.GetZone(ZoneType.Memory).Cards);
        Assert.Same(card, Assert.Single(player.GetZone(ZoneType.Hand).Cards));
    }

    [Fact]
    public void AdvancePhase_ToDraw_DrawsOneCard()
    {
        var player = MakePlayerWithDeck(deckSize: 5);
        var session = new GameSession();
        session.Players.Add(player);
        session.AdvancePhase(player); // WakeUp -> Materialization
        session.AdvancePhase(player); // Materialization -> Recollection

        session.AdvancePhase(player); // Recollection -> Draw

        Assert.Equal(TurnPhase.Draw, session.CurrentPhase);
        Assert.Single(player.GetZone(ZoneType.Hand).Cards);
        Assert.Equal(4, player.GetZone(ZoneType.MainDeck).Cards.Count);
    }
}
