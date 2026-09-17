using Tavern.Net.Game;
using Tavern.Net.GameData.Models;

namespace Tavern.Net.Tests;

public class GameSessionTests
{
    private static CardInstance MakeCard(string name = "Test Card") => new(new CardDto { Name = name });

    // StartNewGame requires a base (Level 0) Champion in Material to materialize onto the Field.
    private static CardInstance MakeBaseChampion(string name = "Base Champion", int homeOrder = 0) =>
        new(new CardDto { Name = name, Types = new List<string> { "Champion" }, Level = 0 }, ZoneType.MaterialDeck, homeOrder);

    private static CardInstance MakeChampion(string name, double life) =>
        new(new CardDto { Name = name, Types = new List<string> { "Champion" }, Life = life });

    private static CardInstance MakeToken(string name = "Test Token") =>
        new(new CardDto { Name = name, Types = new List<string> { "Token" } }, ZoneType.Tokens);

    private static Player MakePlayerWithDeck(int deckSize)
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        for (var i = 0; i < deckSize; i++)
        {
            player.GetZone(ZoneType.MainDeck).Cards.Add(MakeCard($"Card {i}"));
        }

        return player;
    }

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

    [Fact]
    public void StartNewGame_ResetsCounterAndStatusesOnEveryCard()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var baseChampion = MakeBaseChampion();
        player.GetZone(ZoneType.MaterialDeck).Cards.Add(baseChampion);
        var scattered = MakeCard("Scattered");
        scattered.Counter = 4;
        scattered.IsIgnited = true;
        player.GetZone(ZoneType.Field).Cards.Add(scattered);

        session.StartNewGame();

        Assert.Equal(0, scattered.Counter);
        Assert.False(scattered.IsIgnited);
    }

    [Fact]
    public void StartNewGame_LeavesTokensCatalogUntouchedAndDiscardsSpawnedFieldTokens()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var baseChampion = MakeBaseChampion();
        player.GetZone(ZoneType.MaterialDeck).Cards.Add(baseChampion);
        var catalogToken = MakeToken("Training Dummy");
        player.GetZone(ZoneType.Tokens).Cards.Add(catalogToken);
        var spawnedToken = MakeToken("Training Dummy");
        player.GetZone(ZoneType.Field).Cards.Add(spawnedToken);

        session.StartNewGame();

        Assert.Same(catalogToken, Assert.Single(player.GetZone(ZoneType.Tokens).Cards));
        Assert.DoesNotContain(spawnedToken, player.GetZone(ZoneType.Field).Cards);
    }

    [Fact]
    public void StartNewGame_RebuildsMainAndMaterialFromWhereverCardsEnded_UpAndClearsOtherZones()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var mainCards = new List<CardInstance>();
        for (var i = 0; i < 10; i++)
        {
            var card = new CardInstance(new CardDto { Name = $"Main {i}" }, ZoneType.MainDeck, homeOrder: i);
            mainCards.Add(card);
            player.GetZone(ZoneType.MainDeck).Cards.Add(card);
        }
        var champion = new CardInstance(new CardDto { Name = "Champion" }, ZoneType.MaterialDeck, homeOrder: 0);
        var regalia = new CardInstance(new CardDto { Name = "Regalia" }, ZoneType.MaterialDeck, homeOrder: 1);
        player.GetZone(ZoneType.MaterialDeck).Cards.Add(champion);
        player.GetZone(ZoneType.MaterialDeck).Cards.Add(regalia);
        // A base champion is required for StartNewGame to materialize onto the Field; give it a
        // later HomeOrder so it doesn't disturb the champion/regalia ordering asserted below.
        var baseChampion = MakeBaseChampion(homeOrder: 2);
        player.GetZone(ZoneType.MaterialDeck).Cards.Add(baseChampion);
        // Scatter cards around the board the way a played game would.
        player.GetZone(ZoneType.Hand).Cards.Add(mainCards[0]);
        player.GetZone(ZoneType.MainDeck).Cards.Remove(mainCards[0]);
        champion.IsTapped = true;
        champion.FieldX = 42;
        champion.FieldY = 17;
        player.GetZone(ZoneType.Field).Cards.Add(champion);
        player.GetZone(ZoneType.MaterialDeck).Cards.Remove(champion);
        player.Life = 5;

        session.StartNewGame();

        Assert.Equal(15, player.Life);
        Assert.Equal(0, player.Stats.TurnCount);
        Assert.False(champion.IsTapped);
        Assert.Equal(0, champion.FieldX);
        Assert.Equal(0, champion.FieldY);
        Assert.Equal(new[] { champion, regalia }, player.GetZone(ZoneType.MaterialDeck).Cards);
        // StartNewGame's own base-champion materialization puts baseChampion in the Champion zone.
        Assert.Same(baseChampion, Assert.Single(player.GetZone(ZoneType.Champion).Cards));
        // Opening hand: drawn after the deck is rebuilt, so it comes out of all 10 Main cards
        // (shuffled), not just whichever ones hadn't wandered off to Hand/Field before the reset.
        Assert.Equal(player.StartingHandSize, player.GetZone(ZoneType.Hand).Cards.Count);
        Assert.Equal(10 - player.StartingHandSize, player.GetZone(ZoneType.MainDeck).Cards.Count);
        var handAndDeck = player.GetZone(ZoneType.Hand).Cards.Concat(player.GetZone(ZoneType.MainDeck).Cards);
        Assert.Equal(mainCards.ToHashSet(), handAndDeck.ToHashSet());
        Assert.Equal(TurnPhase.Materialization, session.CurrentPhase);
    }

    [Fact]
    public void StartNewGame_DiscardsSessionGeneratedCards()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        player.GetZone(ZoneType.MainDeck).Cards.Add(MakeCard("Original Card"));
        var baseChampion = MakeBaseChampion();
        player.GetZone(ZoneType.MaterialDeck).Cards.Add(baseChampion);

        var generated = session.GenerateCard(player, new CardDto { Name = "Generated Extra" });
        Assert.Contains(generated, player.GetZone(ZoneType.MainDeck).Cards);

        session.StartNewGame();

        var allMainAndHand = player.GetZone(ZoneType.MainDeck).Cards.Concat(player.GetZone(ZoneType.Hand).Cards);
        Assert.DoesNotContain(generated, allMainAndHand);
    }

    [Fact]
    public void AdvancePhase_AfterStartNewGame_FastForwardsOnceThenResumesNormalProgression()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        player.GetZone(ZoneType.MainDeck).Cards.Add(MakeCard());
        player.GetZone(ZoneType.MaterialDeck).Cards.Add(MakeBaseChampion());
        session.StartNewGame(); // Materialization, TurnCount 0, PlayerNumber 0.

        session.AdvancePhase(player); // Fast-forward: Materialization -> Main.
        Assert.Equal(TurnPhase.Main, session.CurrentPhase);
        Assert.Equal(0, player.Stats.TurnCount);

        session.AdvancePhase(player); // Normal progression resumes: Main -> End.
        Assert.Equal(TurnPhase.End, session.CurrentPhase);
        Assert.Equal(0, player.Stats.TurnCount);

        session.AdvancePhase(player); // End -> next turn, WakeUp.
        Assert.Equal(TurnPhase.WakeUp, session.CurrentPhase);
        Assert.Equal(1, player.Stats.TurnCount);

        session.AdvancePhase(player); // Confirm it isn't still fast-forwarding.
        Assert.Equal(TurnPhase.Materialization, session.CurrentPhase);
    }

    [Fact]
    public void AdvancePhase_FirstTurnFastForward_UsesExplicitTargetNotPlayerNumber()
    {
        var session = new GameSession();
        var host = session.AddPlayer("Host");   // PlayerNumber 0
        var guest = session.AddPlayer("Guest"); // PlayerNumber 1

        host.GetZone(ZoneType.MainDeck).Cards.Add(MakeCard());
        host.GetZone(ZoneType.MaterialDeck).Cards.Add(MakeBaseChampion());
        guest.GetZone(ZoneType.MainDeck).Cards.Add(MakeCard());
        guest.GetZone(ZoneType.MaterialDeck).Cards.Add(MakeBaseChampion());

        // Both initialized independently, as each side of an online game does for just its own
        // player — then the guest (PlayerNumber 1), not PlayerNumber 0, is the one who actually
        // goes first, same as a dice roll could decide either way. Mirrors OnlineLobbyViewModel.
        // TryBuildAndStartAsync's own call sequence.
        session.StartNewGameForPlayer(host);
        session.StartNewGameForPlayer(guest);
        session.ApplyRemoteGameState(TurnPhase.Materialization, guest);
        session.SetFirstTurnTarget(guest, TurnPhase.Main);
        session.SetFirstTurnTarget(host, TurnPhase.Draw);

        // Guest actually goes first — fast-forwards to Main despite being PlayerNumber 1.
        session.AdvancePhase(guest);
        Assert.Equal(TurnPhase.Main, session.CurrentPhase);
        Assert.Equal(0, guest.Stats.TurnCount);

        // Host receiving the handoff for their own first turn (GameBoardViewModel.
        // ApplyRemoteGameState lands them on Materialization and deliberately does NOT call
        // NextTurn — see its own comment) stays at TurnCount 0 too, so this whole turn still
        // displays as "Turn 1".
        Assert.Equal(0, host.Stats.TurnCount);

        // Host's own first AdvancePhase call still fast-forwards (still pending), but to Draw —
        // despite being PlayerNumber 0 — per the explicit target set above, not TurnCount.
        session.AdvancePhase(host);
        Assert.Equal(TurnPhase.Draw, session.CurrentPhase);
        Assert.Equal(0, host.Stats.TurnCount);
    }

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

    [Fact]
    public void AdjustLife_ConsecutiveSameDirectionChanges_CoalesceIntoOneMajorEvent()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo", startingLife: 15);

        session.AdjustLife(player, -1);
        session.AdjustLife(player, -1);
        session.AdjustLife(player, -1);

        var entry = Assert.Single(player.Stats.MajorEvents);
        Assert.Equal(MajorEventKind.LifeChanged, entry.Kind);
        Assert.Equal(-3, entry.NetDelta);
        Assert.Equal("Life decreased by 3.", entry.Description);
        Assert.Equal(12, entry.Snapshot.Life);
    }

    [Fact]
    public void AdjustLife_DirectionChange_StartsNewMajorEvent()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo", startingLife: 15);

        session.AdjustLife(player, -1);
        session.AdjustLife(player, -1);
        session.AdjustLife(player, 1);

        Assert.Equal(2, player.Stats.MajorEvents.Count);
        Assert.Equal("Life decreased by 2.", player.Stats.MajorEvents[0].Description);
        Assert.Equal("Life increased by 1.", player.Stats.MajorEvents[1].Description);
    }

    [Fact]
    public void AdjustDamageDealt_DifferentKindThanLife_DoesNotCoalesceWithIt()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");

        session.AdjustLife(player, -2);
        session.AdjustDamageDealt(player, 3);

        Assert.Equal(2, player.Stats.MajorEvents.Count);
        Assert.Equal(MajorEventKind.LifeChanged, player.Stats.MajorEvents[0].Kind);
        Assert.Equal(MajorEventKind.DamageDealt, player.Stats.MajorEvents[1].Kind);
    }

    [Fact]
    public void ToggleTapped_BreaksDamageCoalescingStreak()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var attacker = MakeCard("Attacker");
        player.GetZone(ZoneType.Field).Cards.Add(attacker);

        session.AdjustDamageDealt(player, 2);
        session.ToggleTapped(player, attacker);
        session.AdjustDamageDealt(player, 4);

        Assert.Equal(2, player.Stats.MajorEvents.Count);
        Assert.Equal(2, player.Stats.MajorEvents[0].NetDelta);
        Assert.Equal(4, player.Stats.MajorEvents[1].NetDelta);
    }

    [Fact]
    public void FlipCard_BreaksLifeCoalescingStreak()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var card = MakeCard();
        player.GetZone(ZoneType.Hand).Cards.Add(card);

        session.AdjustLife(player, -1);
        session.FlipCard(player, card);
        session.AdjustLife(player, -1);

        Assert.Equal(2, player.Stats.MajorEvents.Count);
    }

    [Fact]
    public void ToggleTapped_LogsAndTogglesState()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var card = MakeCard("Guardian");
        player.GetZone(ZoneType.Field).Cards.Add(card);

        session.ToggleTapped(player, card);

        Assert.True(card.IsTapped);
        Assert.Contains(player.Stats.PlayLog, m => m.Contains("Tapped Guardian"));
    }

    [Fact]
    public void MoveCard_ToField_RecordsMajorEventWithSnapshot()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var card = MakeCard("Winter's Wolf");
        player.GetZone(ZoneType.Hand).Cards.Add(card);

        session.MoveCard(player, card, ZoneType.Hand, ZoneType.Field);

        var entry = Assert.Single(player.Stats.MajorEvents);
        Assert.Equal("Played Winter's Wolf to the Field.", entry.Description);
        Assert.Contains(entry.Snapshot.Cards, c => c.Card.Name == "Winter's Wolf" && c.Zone == ZoneType.Field);
    }

    [Fact]
    public void MoveCard_ToGraveyard_RecordsMajorEvent()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var card = MakeCard("Fallen Knight");
        player.GetZone(ZoneType.Field).Cards.Add(card);

        session.MoveCard(player, card, ZoneType.Field, ZoneType.Graveyard);

        var entry = Assert.Single(player.Stats.MajorEvents);
        Assert.Equal("Fallen Knight went to the Graveyard.", entry.Description);
    }

    [Fact]
    public void Banish_RecordsMajorEventPerBanishedCard()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var card = MakeCard("Lost Memory");
        player.GetZone(ZoneType.Memory).Cards.Add(card);

        session.Banish(player, 1);

        var entry = Assert.Single(player.Stats.MajorEvents);
        Assert.Equal("Banished Lost Memory.", entry.Description);
    }

    [Fact]
    public void MoveToken_Spawn_RecordsMajorEvent()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var catalogToken = MakeToken("Training Dummy");
        player.GetZone(ZoneType.Tokens).Cards.Add(catalogToken);

        session.MoveCard(player, catalogToken, ZoneType.Tokens, ZoneType.Field);

        var entry = Assert.Single(player.Stats.MajorEvents);
        Assert.Equal("Summoned Training Dummy token.", entry.Description);
    }

    [Fact]
    public void MoveToken_Discard_RecordsMajorEvent()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var spawned = MakeToken("Training Dummy");
        player.GetZone(ZoneType.Field).Cards.Add(spawned);

        session.MoveCard(player, spawned, ZoneType.Field, ZoneType.Tokens);

        var entry = Assert.Single(player.Stats.MajorEvents);
        Assert.Equal("Discarded Training Dummy token.", entry.Description);
    }

    [Fact]
    public void NextTurn_RecordsMajorEvent()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");

        session.NextTurn(player);

        var entry = Assert.Single(player.Stats.MajorEvents);
        Assert.Equal("Turn 2 started.", entry.Description);
    }

    [Fact]
    public void StartNewGame_RecordsGameStartedAndClearsPreviousMajorEvents()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var baseChampion = MakeBaseChampion();
        player.GetZone(ZoneType.MaterialDeck).Cards.Add(baseChampion);
        session.AdjustLife(player, -1); // a stale entry from "before" the reset

        session.StartNewGame();

        // "Game started." must be first — even though it's recorded (with its full, final
        // snapshot) after the base champion's own materialization event further down in
        // StartNewGame, it's inserted at the front rather than appended.
        Assert.Equal("Game started.", player.Stats.MajorEvents[0].Description);
        Assert.DoesNotContain(player.Stats.MajorEvents, e => e.Kind == MajorEventKind.LifeChanged);
    }

    [Fact]
    public void StartNewGame_GameStartedSnapshotReflectsFullyMaterializedState()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var baseChampion = MakeBaseChampion();
        player.GetZone(ZoneType.MaterialDeck).Cards.Add(baseChampion);

        session.StartNewGame();

        // The snapshot attached to "Game started." must reflect state AFTER champion
        // materialization/opening hand — not the empty board at the instant zones were cleared.
        var gameStarted = player.Stats.MajorEvents[0];
        Assert.Contains(gameStarted.Snapshot.Cards, c => c.Card.Name == baseChampion.Card.Name && c.Zone == ZoneType.Champion);
    }

    [Fact]
    public void TakeSnapshot_ExcludesTokensZone()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        player.GetZone(ZoneType.Tokens).Cards.Add(MakeToken());
        var card = MakeCard();
        player.GetZone(ZoneType.Hand).Cards.Add(card);

        session.MoveCard(player, card, ZoneType.Hand, ZoneType.Field);

        var entry = Assert.Single(player.Stats.MajorEvents);
        Assert.DoesNotContain(entry.Snapshot.Cards, c => c.Zone == ZoneType.Tokens);
    }
}
