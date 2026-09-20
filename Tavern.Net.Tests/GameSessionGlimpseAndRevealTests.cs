using Tavern.Net.Game;
using Tavern.Net.GameData.Models;
using static Tavern.Net.Tests.SessionTestData;

namespace Tavern.Net.Tests;

/// <summary>Glimpse and the Reveal panel: pulling cards off Main and putting them back.</summary>
public class GameSessionGlimpseAndRevealTests
{
    [Fact]
    public void GlimpseNextCard_RemovesTopCardFromMainEntirely()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var top = MakeCard("Top");
        var rest = MakeCard("Rest");
        player.GetZone(ZoneType.MainDeck).Cards.Add(top);
        player.GetZone(ZoneType.MainDeck).Cards.Add(rest);

        var glimpsed = session.GlimpseNextCard(player);

        Assert.Same(top, glimpsed);
        Assert.Equal(new[] { rest }, player.GetZone(ZoneType.MainDeck).Cards);
    }

    [Fact]
    public void GlimpseNextCard_FromEmptyDeck_ReturnsNull()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");

        Assert.Null(session.GlimpseNextCard(player));
    }

    [Fact]
    public void GlimpseCards_PullsExactlyCount_InOrder()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var top = MakeCard("Top");
        var second = MakeCard("Second");
        var third = MakeCard("Third");
        player.GetZone(ZoneType.MainDeck).Cards.Add(top);
        player.GetZone(ZoneType.MainDeck).Cards.Add(second);
        player.GetZone(ZoneType.MainDeck).Cards.Add(third);

        var glimpsed = session.GlimpseCards(player, 2);

        Assert.Equal(new[] { top, second }, glimpsed);
        Assert.Equal(new[] { third }, player.GetZone(ZoneType.MainDeck).Cards);
    }

    [Fact]
    public void GlimpseCards_FewerThanCountInDeck_StopsEarly()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var only = MakeCard();
        player.GetZone(ZoneType.MainDeck).Cards.Add(only);

        var glimpsed = session.GlimpseCards(player, 5);

        Assert.Equal(new[] { only }, glimpsed);
        Assert.Empty(player.GetZone(ZoneType.MainDeck).Cards);
    }

    [Fact]
    public void FinishGlimpse_TopLandsOnTopInOrder_BottomLandsAtBottomInOrder()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var remaining = MakeCard("Remaining");
        player.GetZone(ZoneType.MainDeck).Cards.Add(remaining);
        var top0 = MakeCard("Top 0");
        var top1 = MakeCard("Top 1");
        var bottom0 = MakeCard("Bottom 0");
        var bottom1 = MakeCard("Bottom 1");

        session.FinishGlimpse(player, new[] { top0, top1 }, new[] { bottom0, bottom1 });

        Assert.Equal(new[] { top0, top1, remaining, bottom0, bottom1 }, player.GetZone(ZoneType.MainDeck).Cards);
    }

    private static (GameSession Session, Player Player, List<CardInstance> Pulled) SessionWithPulledCards(int deckSize, int pull)
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        for (var i = 0; i < deckSize; i++)
        {
            player.GetZone(ZoneType.MainDeck).Cards.Add(new CardInstance(new CardDto { Name = $"C{i}", Slug = $"c{i}" }, ZoneType.MainDeck));
        }

        return (session, player, session.GlimpseCards(player, pull));
    }

    [Fact]
    public void MoveRevealedCard_PutsItInTheZone_AsAnOrdinaryMoveOutOfMain()
    {
        var (session, player, pulled) = SessionWithPulledCards(5, 3);

        Assert.True(session.MoveRevealedCard(player, pulled[1], ZoneType.Hand));

        Assert.Contains(pulled[1], player.GetZone(ZoneType.Hand).Cards);
        Assert.DoesNotContain(pulled[1], player.GetZone(ZoneType.MainDeck).Cards);
        Assert.Equal(1, player.Stats.CardsDrawnCount);
        Assert.Equal(2, player.GetZone(ZoneType.MainDeck).Cards.Count);
    }

    [Fact]
    public void MoveRevealedCard_ToAZoneAMainCardCannotEnter_IsRefusedAndLeavesMainAlone()
    {
        var (session, player, pulled) = SessionWithPulledCards(4, 2);

        Assert.False(session.MoveRevealedCard(player, pulled[0], ZoneType.Champion));

        Assert.DoesNotContain(pulled[0], player.GetZone(ZoneType.MainDeck).Cards);
        Assert.Empty(player.GetZone(ZoneType.Champion).Cards);
    }

    [Fact]
    public void ReturnRevealedCards_ToTop_KeepsTheGivenOrderAboveTheRestOfTheDeck()
    {
        var (session, player, pulled) = SessionWithPulledCards(5, 3);
        var reversed = pulled.AsEnumerable().Reverse().ToList();

        session.ReturnRevealedCards(player, reversed, toTop: true);

        var deck = player.GetZone(ZoneType.MainDeck).Cards;
        Assert.Equal(5, deck.Count);
        Assert.Equal(reversed, deck.Take(3).ToList());
    }

    [Fact]
    public void ReturnRevealedCards_ToBottom_AppendsInOrder()
    {
        var (session, player, pulled) = SessionWithPulledCards(5, 3);

        session.ReturnRevealedCards(player, pulled, toTop: false);

        var deck = player.GetZone(ZoneType.MainDeck).Cards;
        Assert.Equal(5, deck.Count);
        Assert.Equal(pulled, deck.Skip(2).ToList());
    }
}
