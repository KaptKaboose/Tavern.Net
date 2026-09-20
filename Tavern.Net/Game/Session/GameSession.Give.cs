using Tavern.Net.GameData.Models;

namespace Tavern.Net.Game;

/// <summary>Give: removing a card to hand across, and receiving one from the opponent.</summary>
public sealed partial class GameSession
{
    /// <summary>Removes <paramref name="card"/> from <paramref name="from"/> entirely, for Give —
    /// unlike MoveCard there's no local destination: the card is headed to the opponent's own board
    /// via a TransferCard message, which their own client applies against their own authoritative
    /// Player (see ReceiveGivenCard). A no-op if it's already gone (e.g. a stale reference).</summary>
    public void RemoveCardForGive(Player player, CardInstance card, ZoneType from)
    {
        if (!player.GetZone(from).Cards.Remove(card))
        {
            return;
        }

        player.Stats.Log($"Gave {card.Card.Name} from {from} to the opponent.");
    }

    /// <summary>Blind top-N removal from Main for Give's 'P' chord — the cards are unknown to the
    /// player choosing to send them, same reasoning as Mill's own blind top-N.</summary>
    public List<CardInstance> TakeTopCardsForGive(Player player, int count)
    {
        var taken = player.GetZone(ZoneType.MainDeck).Cards.Take(count).ToList();
        foreach (var card in taken)
        {
            RemoveCardForGive(player, card, ZoneType.MainDeck);
        }

        return taken;
    }

    /// <summary>Receiver side of Give: materializes a card the opponent sent directly into this
    /// player's own Field or Sealed zone. HomeZone is set to <paramref name="destination"/> itself
    /// (not MainDeck/MaterialDeck) — deliberate: this card has no home in the receiver's own deck,
    /// so StartNewGame's reset sweep (which only rebuilds Main/Material from HomeZone) naturally
    /// discards it on the next New Game, with no special-case cleanup needed here.</summary>
    public CardInstance ReceiveGivenCard(Player player, CardDto cardDto, ZoneType destination, string? sourceLabel)
    {
        var instance = new CardInstance(cardDto, destination);
        player.GetZone(destination).Cards.Add(instance);
        player.Stats.Log($"Received {cardDto.Name} from the opponent ({sourceLabel ?? "?"}) into {destination}.");

        if (destination == ZoneType.Field)
        {
            RecordMajorEvent(player, $"{cardDto.Name} arrived on the Field via Give.", cardName: cardDto.Name, cardZone: ZoneType.Field);
        }

        return instance;
    }
}
