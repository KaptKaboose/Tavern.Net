using Tavern.Net.Game;
using Tavern.Net.GameData.Models;
using static Tavern.Net.Tests.SessionTestData;

namespace Tavern.Net.Tests;

/// <summary>Drawing and moving cards between zones (the movement rules, resets, Champion levels, tokens).</summary>
public class GameSessionMovesTests
{
    [Fact]
    public void DrawCard_MovesTopCardFromDeckToHand()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var card = MakeCard();
        player.GetZone(ZoneType.MainDeck).Cards.Add(card);

        var drew = session.DrawCard(player);

        Assert.True(drew);
        Assert.Empty(player.GetZone(ZoneType.MainDeck).Cards);
        Assert.Single(player.GetZone(ZoneType.Hand).Cards);
        Assert.Equal(1, player.Stats.CardsDrawnCount);
    }

    [Fact]
    public void DrawCard_FromEmptyZone_ReturnsFalseAndDoesNotThrow()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");

        var drew = session.DrawCard(player);

        Assert.False(drew);
        Assert.Equal(0, player.Stats.CardsDrawnCount);
    }

    [Fact]
    public void MoveCard_MovesBetweenZonesAndLogs()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var card = MakeCard("Winter's Wolf");
        player.GetZone(ZoneType.Hand).Cards.Add(card);

        session.MoveCard(player, card, ZoneType.Hand, ZoneType.Field);

        Assert.Empty(player.GetZone(ZoneType.Hand).Cards);
        Assert.Same(card, Assert.Single(player.GetZone(ZoneType.Field).Cards));
        Assert.Contains(player.Stats.PlayLog, m => m.Contains("Winter's Wolf") && m.Contains("Field"));
    }

    [Fact]
    public void MoveCard_ToAndFromSealed_NeedsNoSpecialCasing()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var card = MakeCard("Hidden Away");
        player.GetZone(ZoneType.Hand).Cards.Add(card);

        session.MoveCard(player, card, ZoneType.Hand, ZoneType.Sealed);
        Assert.Same(card, Assert.Single(player.GetZone(ZoneType.Sealed).Cards));

        session.MoveCard(player, card, ZoneType.Sealed, ZoneType.Field);
        Assert.Empty(player.GetZone(ZoneType.Sealed).Cards);
        Assert.Same(card, Assert.Single(player.GetZone(ZoneType.Field).Cards));
    }

    [Fact]
    public void MoveCard_ToStackZone_InsertsAtFront()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var older = MakeCard("Older");
        var newer = MakeCard("Newer");
        player.GetZone(ZoneType.Graveyard).Cards.Add(older);
        player.GetZone(ZoneType.Hand).Cards.Add(newer);

        session.MoveCard(player, newer, ZoneType.Hand, ZoneType.Graveyard);

        Assert.Equal(new[] { newer, older }, player.GetZone(ZoneType.Graveyard).Cards);
    }

    [Fact]
    public void MoveCard_ToNonStackZone_AppendsAtEnd()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var older = MakeCard("Older");
        var newer = MakeCard("Newer");
        player.GetZone(ZoneType.Hand).Cards.Add(older);
        player.GetZone(ZoneType.Field).Cards.Add(newer);

        session.MoveCard(player, newer, ZoneType.Field, ZoneType.Hand);

        Assert.Equal(new[] { older, newer }, player.GetZone(ZoneType.Hand).Cards);
    }

    [Fact]
    public void MoveCard_ToBottom_AppendsRatherThanInserts()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var existingTop = MakeCard("Existing Top");
        var incoming = MakeCard("Incoming");
        player.GetZone(ZoneType.MainDeck).Cards.Add(existingTop);
        player.GetZone(ZoneType.Hand).Cards.Add(incoming);

        session.MoveCard(player, incoming, ZoneType.Hand, ZoneType.MainDeck, toBottom: true);

        Assert.Equal(new[] { existingTop, incoming }, player.GetZone(ZoneType.MainDeck).Cards);
    }

    [Fact]
    public void MoveCard_ResetsFlippedState()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var card = MakeCard();
        card.IsFlipped = true;
        player.GetZone(ZoneType.Hand).Cards.Add(card);

        session.MoveCard(player, card, ZoneType.Hand, ZoneType.Field);

        Assert.False(card.IsFlipped);
    }

    [Fact]
    public void MoveCard_ToField_SetsFieldPosition()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var card = MakeCard();
        player.GetZone(ZoneType.Hand).Cards.Add(card);

        session.MoveCard(player, card, ZoneType.Hand, ZoneType.Field, fieldX: 120, fieldY: 80);

        Assert.Equal(120, card.FieldX);
        Assert.Equal(80, card.FieldY);
    }

    [Fact]
    public void MoveCard_ToNonFieldZone_IgnoresFieldPosition()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var card = MakeCard();
        player.GetZone(ZoneType.Hand).Cards.Add(card);

        session.MoveCard(player, card, ZoneType.Hand, ZoneType.Graveyard, fieldX: 120, fieldY: 80);

        Assert.Equal(0, card.FieldX);
        Assert.Equal(0, card.FieldY);
    }

    [Fact]
    public void MoveCard_ToField_PreservesCounterAndStatuses()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var card = MakeCard();
        card.Counter = 2;
        card.IsRanged = true;
        player.GetZone(ZoneType.Hand).Cards.Add(card);

        session.MoveCard(player, card, ZoneType.Hand, ZoneType.Field);

        Assert.Equal(2, card.Counter);
        Assert.True(card.IsRanged);
    }

    [Fact]
    public void MoveCard_ToChampion_PreservesCounterAndStatuses()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var champion = MakeChampion("Base", life: 8);
        champion.Counter = 3;
        champion.IsWarded = true;
        player.GetZone(ZoneType.Hand).Cards.Add(champion);

        session.MoveCard(player, champion, ZoneType.Hand, ZoneType.Champion);

        Assert.Equal(3, champion.Counter);
        Assert.True(champion.IsWarded);
    }

    [Theory]
    [InlineData(ZoneType.Graveyard)]
    [InlineData(ZoneType.Hand)]
    [InlineData(ZoneType.Banishment)]
    [InlineData(ZoneType.Memory)]
    public void MoveCard_AwayFromFieldToAnyOtherZone_ResetsCounterAndStatuses(ZoneType destination)
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var card = MakeCard();
        card.Counter = 5;
        card.IsImbued = true;
        card.IsRooted = true;
        player.GetZone(ZoneType.Field).Cards.Add(card);

        session.MoveCard(player, card, ZoneType.Field, destination);

        Assert.Equal(0, card.Counter);
        Assert.False(card.IsImbued);
        Assert.False(card.IsRooted);
    }

    [Fact]
    public void MoveCard_NonChampionCardToChampionZone_IsIgnored()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var card = MakeCard("Not A Champion");
        player.GetZone(ZoneType.Hand).Cards.Add(card);

        session.MoveCard(player, card, ZoneType.Hand, ZoneType.Champion);

        Assert.Empty(player.GetZone(ZoneType.Champion).Cards);
        Assert.Same(card, Assert.Single(player.GetZone(ZoneType.Hand).Cards));
    }

    [Fact]
    public void MoveCard_ChampionCardToChampionZone_StacksOnTop()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var older = MakeChampion("Older", life: 10);
        var newer = MakeChampion("Newer", life: 10);
        player.GetZone(ZoneType.Champion).Cards.Add(older);
        player.GetZone(ZoneType.Hand).Cards.Add(newer);

        session.MoveCard(player, newer, ZoneType.Hand, ZoneType.Champion);

        Assert.Equal(new[] { newer, older }, player.GetZone(ZoneType.Champion).Cards);
    }

    [Fact]
    public void MoveCard_ChampionEntersEmptyChampionZone_DoesNotChangeLife()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo", startingLife: 15);
        var champion = MakeChampion("Base", life: 8);
        player.GetZone(ZoneType.Hand).Cards.Add(champion);

        session.MoveCard(player, champion, ZoneType.Hand, ZoneType.Champion);

        Assert.Equal(15, player.Life);
    }

    [Fact]
    public void MoveCard_ChampionLeavesChampionZoneEmpty_DoesNotChangeLife()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo", startingLife: 15);
        var champion = MakeChampion("Base", life: 8);
        player.GetZone(ZoneType.Champion).Cards.Add(champion);

        session.MoveCard(player, champion, ZoneType.Champion, ZoneType.Graveyard);

        Assert.Equal(15, player.Life);
    }

    [Fact]
    public void MoveCard_ChampionLevelsUp_LifeIncreasesByDifference()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo", startingLife: 15);
        var baseForm = MakeChampion("Base", life: 8);
        var leveledForm = MakeChampion("Leveled", life: 12);
        player.GetZone(ZoneType.Champion).Cards.Add(baseForm);
        player.GetZone(ZoneType.Hand).Cards.Add(leveledForm);

        session.MoveCard(player, leveledForm, ZoneType.Hand, ZoneType.Champion);

        Assert.Equal(19, player.Life);
        Assert.Equal(new[] { leveledForm, baseForm }, player.GetZone(ZoneType.Champion).Cards);
    }

    [Fact]
    public void MoveCard_ChampionDeLevels_LifeDecreasesByDifference()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo", startingLife: 15);
        var leveledForm = MakeChampion("Leveled", life: 12);
        var baseForm = MakeChampion("Base", life: 8);
        player.GetZone(ZoneType.Champion).Cards.Add(leveledForm);
        player.GetZone(ZoneType.Hand).Cards.Add(baseForm);

        session.MoveCard(player, baseForm, ZoneType.Hand, ZoneType.Champion);

        Assert.Equal(11, player.Life);
    }

    [Fact]
    public void MoveCard_ChampionLevelsUp_TransfersCounterAndStatusesToNewTopAndClearsOldTop()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var baseForm = MakeChampion("Base", life: 8);
        baseForm.Counter = 3;
        baseForm.IsImbued = true;
        baseForm.IsRanged = true;
        baseForm.IsTapped = true;
        var leveledForm = MakeChampion("Leveled", life: 12);
        player.GetZone(ZoneType.Champion).Cards.Add(baseForm);
        player.GetZone(ZoneType.Hand).Cards.Add(leveledForm);

        session.MoveCard(player, leveledForm, ZoneType.Hand, ZoneType.Champion);

        Assert.Equal(3, leveledForm.Counter);
        Assert.True(leveledForm.IsImbued);
        Assert.True(leveledForm.IsRanged);
        Assert.Equal(0, baseForm.Counter);
        Assert.False(baseForm.IsImbued);
        Assert.False(baseForm.IsRanged);
        Assert.False(baseForm.IsTapped);
    }

    [Fact]
    public void MoveCard_ChampionLeavesChampionZoneToGraveyard_TransfersCounterAndStatusesToExposedCard()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var buried = MakeChampion("Buried", life: 8);
        buried.IsTapped = true;
        var dying = MakeChampion("Dying", life: 12);
        dying.Counter = 5;
        dying.IsWarded = true;
        player.GetZone(ZoneType.Champion).Cards.Add(dying);
        player.GetZone(ZoneType.Champion).Cards.Add(buried);

        session.MoveCard(player, dying, ZoneType.Champion, ZoneType.Graveyard);

        Assert.Equal(5, buried.Counter);
        Assert.True(buried.IsWarded);
        // Only the old top's tapped state resets — the newly-exposed top's own is left alone.
        Assert.True(buried.IsTapped);
        // The card that actually left is also cleared — same as any card leaving Champion.
        Assert.Equal(0, dying.Counter);
        Assert.False(dying.IsWarded);
    }

    [Fact]
    public void MoveCard_TokenFromTokensToField_SpawnsCopyAndLeavesCatalogEntryInPlace()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var catalogToken = MakeToken("Training Dummy");
        player.GetZone(ZoneType.Tokens).Cards.Add(catalogToken);

        session.MoveCard(player, catalogToken, ZoneType.Tokens, ZoneType.Field, fieldX: 30, fieldY: 40);

        Assert.Same(catalogToken, Assert.Single(player.GetZone(ZoneType.Tokens).Cards));
        var spawned = Assert.Single(player.GetZone(ZoneType.Field).Cards);
        Assert.NotSame(catalogToken, spawned);
        Assert.Equal("Training Dummy", spawned.Card.Name);
        Assert.Equal(30, spawned.FieldX);
        Assert.Equal(40, spawned.FieldY);
    }

    [Fact]
    public void MoveCard_TokenFromTokensToField_CanBeSpawnedMultipleTimes()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var catalogToken = MakeToken();
        player.GetZone(ZoneType.Tokens).Cards.Add(catalogToken);

        session.MoveCard(player, catalogToken, ZoneType.Tokens, ZoneType.Field);
        session.MoveCard(player, catalogToken, ZoneType.Tokens, ZoneType.Field);

        Assert.Equal(2, player.GetZone(ZoneType.Field).Cards.Count);
        Assert.Single(player.GetZone(ZoneType.Tokens).Cards);
    }

    [Fact]
    public void MoveCard_TokenFromFieldToTokens_DiscardsIt()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var spawned = MakeToken();
        player.GetZone(ZoneType.Field).Cards.Add(spawned);

        session.MoveCard(player, spawned, ZoneType.Field, ZoneType.Tokens);

        Assert.Empty(player.GetZone(ZoneType.Field).Cards);
        Assert.Empty(player.GetZone(ZoneType.Tokens).Cards);
    }

    [Fact]
    public void MoveCard_TokenToAnyOtherZone_IsIgnored()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var spawned = MakeToken();
        player.GetZone(ZoneType.Field).Cards.Add(spawned);

        session.MoveCard(player, spawned, ZoneType.Field, ZoneType.Graveyard);

        Assert.Empty(player.GetZone(ZoneType.Graveyard).Cards);
        Assert.Same(spawned, Assert.Single(player.GetZone(ZoneType.Field).Cards));
    }

    [Fact]
    public void RepositionOnField_UpdatesPositionWithoutChangingZoneOrLogging()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var card = MakeCard();
        player.GetZone(ZoneType.Field).Cards.Add(card);

        session.RepositionOnField(card, 50, 60);

        Assert.Equal(50, card.FieldX);
        Assert.Equal(60, card.FieldY);
        Assert.Same(card, Assert.Single(player.GetZone(ZoneType.Field).Cards));
        Assert.Empty(player.Stats.PlayLog);
    }

    [Fact]
    public void MainCardPutIntoMaterial_IsLogged_AndReturnsToItsOwnDeckOnNewGame()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        player.GetZone(ZoneType.MaterialDeck).Cards.Add(MakeBaseChampion());
        var preserved = new CardInstance(new CardDto { Name = "Preserved" }, ZoneType.MainDeck);
        player.GetZone(ZoneType.Field).Cards.Add(preserved);

        session.MoveCard(player, preserved, ZoneType.Field, ZoneType.MaterialDeck);

        Assert.Contains(preserved, player.GetZone(ZoneType.MaterialDeck).Cards);
        Assert.Contains(player.Stats.MajorEvents, e => e.Description == "Preserved was put into the Material Deck." && e.CardName == "Preserved");

        session.StartNewGame();

        // Its home is Main, so the reset rebuilds it there (or it was drawn into the opening hand),
        // never left in Material.
        Assert.DoesNotContain(preserved, player.GetZone(ZoneType.MaterialDeck).Cards);
        Assert.Contains(preserved, player.Zones.Values.SelectMany(z => z.Cards));
    }
}
