using Tavern.Net.Decklists;
using Tavern.Net.Game;
using Tavern.Net.GameData;
using Tavern.Net.GameData.Models;

namespace Tavern.Net.Tests;

public class DeckArrangementTests
{
    private static CardDto Card(string name, string? slug = null, bool champion = false, double? level = null) => new()
    {
        Name = name,
        Slug = slug ?? name.ToLowerInvariant().Replace(' ', '-'),
        Types = champion ? new List<string> { "Champion" } : new List<string>(),
        Level = level,
    };

    private static List<DeckSessionBuilder.Entry> SampleDeck() => new()
    {
        new(Card("Alpha"), DeckSection.Main, 2),
        new(Card("Beta"), DeckSection.Main, 1),
        new(Card("Base Champion", champion: true, level: 0), DeckSection.Material, 1),
        new(Card("Regalia One"), DeckSection.Material, 1),
        new(Card("Side Card"), DeckSection.Sideboard, 3),
    };

    private static Player LoadedPlayer()
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        DeckSessionBuilder.LoadDeckIntoPlayer(player, SampleDeck());
        return player;
    }

    [Fact]
    public void LoadDeckIntoPlayer_KeepsSideboardInTheArrangementButOutOfEveryZone()
    {
        var player = LoadedPlayer();

        Assert.Equal(3, player.GetZone(ZoneType.MainDeck).Cards.Count);
        Assert.Equal(2, player.GetZone(ZoneType.MaterialDeck).Cards.Count);
        Assert.Equal(3, player.Deck!.Sideboard.Count);
        Assert.All(player.Zones.Values.SelectMany(z => z.Cards), c => Assert.NotEqual("Side Card", c.Card.Name));
    }

    [Fact]
    public void ApplyArrangement_AfterASwap_RebuildsZonesFromTheCurrentLists()
    {
        var player = LoadedPlayer();
        var deck = player.Deck!;
        var swappedIn = deck.Sideboard[0];
        deck.Sideboard.RemoveAt(0);
        deck.Main.RemoveAt(0);
        deck.Main.Add(swappedIn);

        DeckSessionBuilder.ApplyArrangement(player);

        var mainNames = player.GetZone(ZoneType.MainDeck).Cards.Select(c => c.Card.Name).ToList();
        Assert.Contains("Side Card", mainNames);
        Assert.Equal(3, mainNames.Count);
        Assert.Equal(1, mainNames.Count(n => n == "Alpha"));
        Assert.All(player.GetZone(ZoneType.MainDeck).Cards, c => Assert.Equal(ZoneType.MainDeck, c.HomeZone));
        Assert.Equal(new[] { 0, 1, 2 }, player.GetZone(ZoneType.MainDeck).Cards.Select(c => c.HomeOrder));
    }

    [Fact]
    public void StartNewGame_AfterApplyingASwap_PlaysTheSwappedDeck()
    {
        var player = LoadedPlayer();
        var deck = player.Deck!;

        // Swap both "Alpha" copies out for two sideboard cards.
        deck.Main.RemoveAll(c => c.Name == "Alpha");
        deck.Main.Add(deck.Sideboard[0]);
        deck.Main.Add(deck.Sideboard[1]);

        var game = new GameSession();
        var freshPlayer = game.AddPlayer("Solo");
        freshPlayer.Deck = player.Deck;
        DeckSessionBuilder.ApplyArrangement(freshPlayer);
        game.StartNewGame();

        var allMainCards = freshPlayer.GetZone(ZoneType.MainDeck).Cards
            .Concat(freshPlayer.GetZone(ZoneType.Hand).Cards)
            .Select(c => c.Card.Name)
            .ToList();
        Assert.DoesNotContain("Alpha", allMainCards);
        Assert.Equal(2, allMainCards.Count(n => n == "Side Card"));
    }

    [Fact]
    public void Reset_PutsEveryListBackToTheRegisteredDeck()
    {
        var player = LoadedPlayer();
        var deck = player.Deck!;
        deck.Main.Add(deck.Sideboard[0]);
        deck.Sideboard.Clear();
        deck.Material.Clear();

        deck.Reset();

        Assert.Equal(deck.RegisteredMain, deck.Main);
        Assert.Equal(deck.RegisteredMaterial, deck.Material);
        Assert.Equal(deck.RegisteredSideboard, deck.Sideboard);
    }

    [Fact]
    public void HasLevelZeroChampion_IsFalseOnceTheBaseChampionIsSwappedOut()
    {
        var deck = LoadedPlayer().Deck!;
        Assert.True(deck.HasLevelZeroChampion);

        deck.Material.RemoveAll(c => c.IsChampion);

        Assert.False(deck.HasLevelZeroChampion);
    }

    [Fact]
    public void Capture_CarriesTheDeckForASave_ButThePerPlayerBroadcastDoesNot()
    {
        var player = LoadedPlayer();
        player.Deck!.Main.Add(player.Deck.Sideboard[0]);
        player.Deck.Sideboard.RemoveAt(0);

        var broadcast = GameSessionSerializer.CapturePlayer(player);
        var saved = GameSessionSerializer.CapturePlayer(player, includeDeck: true);

        Assert.Null(broadcast.Deck);
        Assert.NotNull(saved.Deck);
        Assert.Equal(2, saved.Deck!.Sideboard.Count);
        Assert.Equal(3, saved.Deck.RegisteredSideboard.Count);
    }

    [Fact]
    public async Task Restore_RebuildsTheSavedDeckArrangement()
    {
        var api = new GrandArchiveApiClient();
        var unique = Guid.NewGuid().ToString("N");
        var main = Card("Cached Main", $"cached-main-{unique}");
        var side = Card("Cached Side", $"cached-side-{unique}");
        var champion = Card("Cached Champ", $"cached-champ-{unique}", champion: true, level: 0);
        api.CacheCard(main);
        api.CacheCard(side);
        api.CacheCard(champion);

        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        player.Deck = new DeckArrangement(new[] { main }, new[] { champion }, new[] { side }, currentMain: new[] { main, side }, currentSideboard: Array.Empty<CardDto>());
        var saved = GameSessionSerializer.Capture("test", session);

        var (restored, restoredPlayer) = await GameSessionSerializer.RestoreAsync(saved, api);

        Assert.NotNull(restoredPlayer.Deck);
        Assert.Equal(new[] { main.Slug, side.Slug }, restoredPlayer.Deck!.Main.Select(c => c.Slug));
        Assert.Empty(restoredPlayer.Deck.Sideboard);
        Assert.Equal(new[] { side.Slug }, restoredPlayer.Deck.RegisteredSideboard.Select(c => c.Slug));
        Assert.NotNull(restored);
    }
}
