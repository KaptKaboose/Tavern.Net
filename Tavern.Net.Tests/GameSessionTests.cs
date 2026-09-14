using Tavern.Net.Game;
using Tavern.Net.GameData.Models;

namespace Tavern.Net.Tests;

public class GameSessionTests
{
    private static CardInstance MakeCard(string name = "Test Card") => new(new CardDto { Name = name });

    // StartNewGame requires a base (Level 0) Champion in Material to materialize onto the Field.
    private static CardInstance MakeBaseChampion(string name = "Base Champion", int homeOrder = 0) =>
        new(new CardDto { Name = name, Types = new List<string> { "Champion" }, Level = 0 }, ZoneType.MaterialDeck, homeOrder);

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
    public void AdjustLife_UpdatesLifeAndRecordsHistory()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo", startingLife: 20);

        session.AdjustLife(player, -3);

        Assert.Equal(17, player.Life);
        Assert.Equal(new LifeHistoryEntry(1, 17), Assert.Single(player.Stats.LifeHistory));
    }

    [Fact]
    public void NextTurn_IncrementsTurnCount()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");

        session.NextTurn(player);
        session.NextTurn(player);

        Assert.Equal(3, player.Stats.TurnCount);
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
        Assert.Equal(1, player.Stats.TurnCount);

        session.AdvancePhase(player);

        Assert.Equal(TurnPhase.WakeUp, session.CurrentPhase);
        Assert.Equal(2, player.Stats.TurnCount);
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

        Assert.Equal(20, player.Life);
        Assert.Equal(0, player.Stats.TurnCount);
        Assert.False(champion.IsTapped);
        Assert.Equal(0, champion.FieldX);
        Assert.Equal(0, champion.FieldY);
        Assert.Equal(new[] { champion, regalia }, player.GetZone(ZoneType.MaterialDeck).Cards);
        // StartNewGame's own base-champion materialization puts baseChampion on the Field.
        Assert.Same(baseChampion, Assert.Single(player.GetZone(ZoneType.Field).Cards));
        // Opening hand: drawn after the deck is rebuilt, so it comes out of all 10 Main cards
        // (shuffled), not just whichever ones hadn't wandered off to Hand/Field before the reset.
        Assert.Equal(GameSession.DefaultOpeningHandSize, player.GetZone(ZoneType.Hand).Cards.Count);
        Assert.Equal(10 - GameSession.DefaultOpeningHandSize, player.GetZone(ZoneType.MainDeck).Cards.Count);
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
}
