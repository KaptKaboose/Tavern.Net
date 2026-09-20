using Tavern.Net.Game;
using Tavern.Net.GameData.Models;
using static Tavern.Net.Tests.SessionTestData;

namespace Tavern.Net.Tests;

/// <summary>Starting a new game: the reset and first-turn fast-forward.</summary>
public class GameSessionNewGameTests
{
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
}
