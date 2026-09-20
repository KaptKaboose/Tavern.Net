using Tavern.Net.GameData.Models;

namespace Tavern.Net.Game;

/// <summary>Actions on the Main Deck: shuffle, restore order, generate copies, banish, mill.</summary>
public sealed partial class GameSession
{
    public void Shuffle(Player player, ZoneType zoneType = ZoneType.MainDeck)
    {
        var zone = player.GetZone(zoneType);
        var shuffled = zone.Cards.OrderBy(_ => _random.Next()).ToList();
        zone.Cards.Clear();
        foreach (var card in shuffled)
        {
            zone.Cards.Add(card);
        }

        player.Stats.Log($"Shuffled {zoneType}.");
    }

    /// <summary>Restores Main's exact card order from a pre-action snapshot — GameBoardViewModel's
    /// single-level Undo for blind Main Deck actions (Mill/Bottom/the R-P-M-G chords). Same Clear +
    /// re-Add idiom as Shuffle.</summary>
    public void RestoreMainDeckOrder(Player player, IReadOnlyList<CardInstance> order)
    {
        var deck = player.GetZone(ZoneType.MainDeck);
        deck.Cards.Clear();
        foreach (var card in order)
        {
            deck.Cards.Add(card);
        }

        player.Stats.Log("Undid the last blind Main Deck action.");
    }

    /// <summary>
    /// Creates a brand-new copy of <paramref name="cardDto"/> at Main's bottom — for card effects
    /// that generate extra copies of a card already in the deck (drawn/played exactly like any
    /// other Main card; unlike a Token, this is a real card, not a board marker). The source card
    /// might not be reachable anywhere on the board to zoom (every remaining copy could be buried
    /// in Main, or the player could have started with zero copies), hence a name search
    /// (GameBoardViewModel's Generate flow) rather than requiring an existing instance to duplicate.
    /// HomeZone is MainDeck like a real deck card (so it behaves identically for e.g. the
    /// MaterialDeck zone barrier), but IsSessionGenerated keeps StartNewGame's rebuild sweep from
    /// treating it as part of the original decklist.
    /// </summary>
    public CardInstance GenerateCard(Player player, CardDto cardDto)
    {
        var card = new CardInstance(cardDto, ZoneType.MainDeck, isSessionGenerated: true);
        player.GetZone(ZoneType.MainDeck).Cards.Add(card);
        player.Stats.Log($"Generated a copy of {cardDto.Name} into the Main Deck.");
        RecordMajorEvent(player, $"Generated a copy of {cardDto.Name} into the Main Deck.", cardName: cardDto.Name);
        return card;
    }

    /// <summary>
    /// Banishes up to <paramref name="count"/> random cards from Memory — fewer if Memory doesn't
    /// have that many.
    /// </summary>
    public void Banish(Player player, int count)
    {
        var memory = player.GetZone(ZoneType.Memory);
        var chosen = memory.Cards.OrderBy(_ => _random.Next()).Take(count).ToList();
        foreach (var card in chosen)
        {
            MoveCard(player, card, ZoneType.Memory, ZoneType.Banishment);
        }
    }

    /// <summary>Moves up to <paramref name="count"/> cards off the top of Main straight to the
    /// Graveyard — the Mill action's blind top-N mill. Purely local: Graveyard is already public/
    /// synced, so this needs no online message of its own, just the normal PlayerState broadcast
    /// tick reflecting the result.</summary>
    /// <returns>The exact cards milled, in the order they left Main — GameBoardViewModel's own Undo
    /// needs these specific instances back to correctly reverse a mill (pull them back out of
    /// Graveyard, not just restore Main's own card list, which would otherwise leave the same
    /// CardInstance sitting in both zones at once).</returns>
    public List<CardInstance> Mill(Player player, int count)
    {
        var deck = player.GetZone(ZoneType.MainDeck);
        var milled = deck.Cards.Take(count).ToList();
        foreach (var card in milled)
        {
            MoveCard(player, card, ZoneType.MainDeck, ZoneType.Graveyard);
        }

        return milled;
    }
}
