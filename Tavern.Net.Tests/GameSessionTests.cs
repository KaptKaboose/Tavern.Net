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
}
