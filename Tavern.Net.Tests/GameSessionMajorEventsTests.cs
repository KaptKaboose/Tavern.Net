using Tavern.Net.Game;
using Tavern.Net.GameData.Models;
using static Tavern.Net.Tests.SessionTestData;

namespace Tavern.Net.Tests;

/// <summary>Play-log Major events, Life/Damage coalescing and snapshots.</summary>
public class GameSessionMajorEventsTests
{
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

    [Theory]
    [InlineData(ZoneType.Field)]
    [InlineData(ZoneType.Graveyard)]
    [InlineData(ZoneType.Banishment)]
    public void MajorEvents_RecordWhichZoneTheirCardWentTo(ZoneType destination)
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        var card = new CardInstance(new CardDto { Name = "Mover" }, ZoneType.MainDeck);
        player.GetZone(ZoneType.Hand).Cards.Add(card);

        session.MoveCard(player, card, ZoneType.Hand, destination);

        var entry = Assert.Single(player.Stats.MajorEvents, e => e.CardName == "Mover");
        Assert.Equal(destination, entry.CardZone);
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
