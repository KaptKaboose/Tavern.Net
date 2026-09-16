using Tavern.Net.Game;
using Tavern.Net.GameData.Models;

namespace Tavern.Net.Decklists;

/// <summary>
/// Builds a solo <see cref="GameSession"/> from a resolved deck — the plumbing shared by
/// DeckImportViewModel.StartGame (a freshly resolved decklist) and StartMenuViewModel.Solo (a saved
/// deck's entries re-resolved by slug), so both paths build the session identically.
/// </summary>
public static class DeckSessionBuilder
{
    public readonly record struct Entry(CardDto Card, DeckSection Section, int Quantity);

    public static (GameSession Session, Player Player) BuildSoloSession(IEnumerable<Entry> entries)
    {
        var session = new GameSession();
        var player = session.AddPlayer("You");
        var mainOrder = 0;
        var materialOrder = 0;

        foreach (var entry in entries)
        {
            // Sideboards aren't meaningful in a solo goldfish session — parsed and shown, not played.
            if (entry.Section == DeckSection.Sideboard)
            {
                continue;
            }

            // Champions start in the Material Deck alongside Regalia, same as Omnidex decklists —
            // materialize your starting champion onto the Field yourself once the game begins.
            var zoneType = entry.Section == DeckSection.Main ? ZoneType.MainDeck : ZoneType.MaterialDeck;

            for (var i = 0; i < entry.Quantity; i++)
            {
                // HomeOrder records this copy's place in the original decklist so
                // GameSession.StartNewGame can rebuild Material in its original order later.
                var homeOrder = zoneType == ZoneType.MainDeck ? mainOrder++ : materialOrder++;
                player.GetZone(zoneType).Cards.Add(new CardInstance(entry.Card, zoneType, homeOrder));
            }
        }

        session.Shuffle(player, ZoneType.MainDeck);
        return (session, player);
    }
}
