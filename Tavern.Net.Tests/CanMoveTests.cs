using Tavern.Net.Game;
using Tavern.Net.GameData.Models;

namespace Tavern.Net.Tests;

public class CanMoveTests
{
    private static readonly GameSession Session = new();

    private static CardInstance Card(ZoneType home = ZoneType.MainDeck, bool champion = false, bool token = false)
    {
        var types = new List<string>();
        if (champion) types.Add("Champion");
        if (token) types.Add("Token");
        return new CardInstance(new CardDto { Name = "X", Types = types }, home);
    }

    [Theory]
    [InlineData(ZoneType.Hand)]
    [InlineData(ZoneType.MainDeck)]
    [InlineData(ZoneType.Graveyard)]
    [InlineData(ZoneType.Memory)]
    public void MaterialCard_CannotGoToZonesTheBarrierBlocks(ZoneType destination)
    {
        Assert.False(Session.CanMove(Card(ZoneType.MaterialDeck), ZoneType.Field, destination));
    }

    [Fact]
    public void MaterialCard_CanStillGoToFieldAndBanishment()
    {
        Assert.True(Session.CanMove(Card(ZoneType.MaterialDeck), ZoneType.MaterialDeck, ZoneType.Field));
        Assert.True(Session.CanMove(Card(ZoneType.MaterialDeck), ZoneType.Field, ZoneType.Banishment));
    }

    [Fact]
    public void OnlyChampionsMayEnterTheChampionZone()
    {
        Assert.False(Session.CanMove(Card(), ZoneType.Hand, ZoneType.Champion));
        Assert.True(Session.CanMove(Card(ZoneType.MaterialDeck, champion: true), ZoneType.MaterialDeck, ZoneType.Champion));
    }

    [Theory]
    [InlineData(ZoneType.Field)]
    [InlineData(ZoneType.Hand)]
    [InlineData(ZoneType.Graveyard)]
    public void OrdinaryCards_CanNeverEnterTheTokensZone(ZoneType from)
    {
        Assert.False(Session.CanMove(Card(), from, ZoneType.Tokens));
    }

    [Fact]
    public void MoveCard_OfAnOrdinaryCardOntoTokens_IsIgnored()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var card = Card();
        player.GetZone(ZoneType.Field).Cards.Add(card);

        session.MoveCard(player, card, ZoneType.Field, ZoneType.Tokens);

        Assert.Empty(player.GetZone(ZoneType.Tokens).Cards);
        Assert.Contains(card, player.GetZone(ZoneType.Field).Cards);
    }

    [Fact]
    public void Tokens_OnlyMoveBetweenTheTokensCatalogAndTheField()
    {
        var token = Card(ZoneType.Tokens, token: true);

        Assert.True(Session.CanMove(token, ZoneType.Tokens, ZoneType.Field));
        Assert.True(Session.CanMove(token, ZoneType.Field, ZoneType.Tokens));
        Assert.False(Session.CanMove(token, ZoneType.Field, ZoneType.Graveyard));
        Assert.False(Session.CanMove(token, ZoneType.Tokens, ZoneType.Hand));
    }

    [Fact]
    public void SameZone_OnlyCountsOnTheField()
    {
        Assert.True(Session.CanMove(Card(), ZoneType.Field, ZoneType.Field));
        Assert.False(Session.CanMove(Card(), ZoneType.Hand, ZoneType.Hand));
    }
}
