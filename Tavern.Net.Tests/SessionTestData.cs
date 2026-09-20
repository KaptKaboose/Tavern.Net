using Tavern.Net.Game;
using Tavern.Net.GameData.Models;

namespace Tavern.Net.Tests;

/// <summary>Card / player builders shared by the GameSession test classes.</summary>
internal static class SessionTestData
{
    internal static CardInstance MakeCard(string name = "Test Card") => new(new CardDto { Name = name });

    // StartNewGame requires a base (Level 0) Champion in Material to materialize onto the Field.
    internal static CardInstance MakeBaseChampion(string name = "Base Champion", int homeOrder = 0) =>
        new(new CardDto { Name = name, Types = new List<string> { "Champion" }, Level = 0 }, ZoneType.MaterialDeck, homeOrder);

    internal static CardInstance MakeChampion(string name, double life) =>
        new(new CardDto { Name = name, Types = new List<string> { "Champion" }, Life = life });

    internal static CardInstance MakeToken(string name = "Test Token") =>
        new(new CardDto { Name = name, Types = new List<string> { "Token" } }, ZoneType.Tokens);

    internal static Player MakePlayerWithDeck(int deckSize)
    {
        var session = new GameSession();
        var player = session.AddPlayer("Solo");
        for (var i = 0; i < deckSize; i++)
        {
            player.GetZone(ZoneType.MainDeck).Cards.Add(MakeCard($"Card {i}"));
        }

        return player;
    }
}
