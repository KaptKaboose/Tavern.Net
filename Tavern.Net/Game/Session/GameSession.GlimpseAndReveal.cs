using Tavern.Net.GameData.Models;

namespace Tavern.Net.Game;

/// <summary>Glimpse and the Reveal panel: pulling cards off Main and putting them back.</summary>
public sealed partial class GameSession
{
    /// <summary>
    /// Pulls the next card off the top of Main for glimpsing. The card leaves Main entirely —
    /// not tracked in any zone — until <see cref="FinishGlimpse"/> puts every glimpsed card back.
    /// Returns null if Main is empty.
    /// </summary>
    public CardInstance? GlimpseNextCard(Player player)
    {
        var deck = player.GetZone(ZoneType.MainDeck);
        if (deck.Cards.Count == 0)
        {
            return null;
        }

        var card = deck.Cards[0];
        deck.Cards.RemoveAt(0);
        return card;
    }

    /// <summary>Pulls up to <paramref name="count"/> cards off the top of Main in one batch — the
    /// 'G' chord's up-front count, replacing the old one-at-a-time repeated-G flow. Stops early if
    /// Main runs dry; loops GlimpseNextCard rather than duplicating it, so StartNewGame's own
    /// base-champion-glimpse path (which calls GlimpseNextCard directly) is unaffected.</summary>
    public List<CardInstance> GlimpseCards(Player player, int count)
    {
        var drawn = new List<CardInstance>();
        for (var i = 0; i < count; i++)
        {
            var card = GlimpseNextCard(player);
            if (card is null)
            {
                break;
            }

            drawn.Add(card);
        }

        return drawn;
    }

    /// <summary>
    /// Reinserts every glimpsed card into Main once all of them have been sorted into these two
    /// piles. <paramref name="top"/>[0] ends up on top of the deck (lowest index), the rest follow
    /// in order below it; <paramref name="bottom"/> is appended in order, so its last entry ends up
    /// at the very bottom of the deck.
    /// </summary>
    public void FinishGlimpse(Player player, IReadOnlyList<CardInstance> top, IReadOnlyList<CardInstance> bottom)
    {
        var deck = player.GetZone(ZoneType.MainDeck);
        for (var i = 0; i < top.Count; i++)
        {
            deck.Cards.Insert(i, top[i]);
        }

        foreach (var card in bottom)
        {
            deck.Cards.Add(card);
        }

        player.Stats.Log($"Glimpsed {top.Count + bottom.Count} card(s): {top.Count} to the top, {bottom.Count} to the bottom.");
    }

    /// <summary>
    /// Reveal panel: puts a card that was pulled off Main (and is sitting in the panel, in no zone)
    /// into <paramref name="to"/> — as an ordinary move out of Main, so it logs, counts as a draw for
    /// Hand/Memory, records Major events and applies the Champion rules exactly like any other. Does
    /// nothing (returns false) if a Main card may not go there (see <see cref="CanMove(CardInstance, ZoneType, ZoneType)"/>).
    /// </summary>
    public bool MoveRevealedCard(Player player, CardInstance card, ZoneType to, double? fieldX = null, double? fieldY = null)
    {
        if (!CanMove(card, ZoneType.MainDeck, to))
        {
            return false;
        }

        // MoveCard takes a card out of a zone, so give it one to come out of: back on top of Main
        // for the instant before it moves on.
        player.GetZone(ZoneType.MainDeck).Cards.Insert(0, card);
        MoveCard(player, card, ZoneType.MainDeck, to, fieldX, fieldY);
        return true;
    }

    /// <summary>Reveal panel: puts whatever is still in the panel back into Main — on top (the first
    /// card ends up highest) or appended to the bottom, in the order given.</summary>
    public void ReturnRevealedCards(Player player, IReadOnlyList<CardInstance> cards, bool toTop)
    {
        var deck = player.GetZone(ZoneType.MainDeck);
        if (toTop)
        {
            for (var i = 0; i < cards.Count; i++)
            {
                deck.Cards.Insert(i, cards[i]);
            }
        }
        else
        {
            foreach (var card in cards)
            {
                deck.Cards.Add(card);
            }
        }

        if (cards.Count > 0)
        {
            player.Stats.Log($"Put {cards.Count} revealed card(s) on the {(toTop ? "top" : "bottom")} of the Main Deck.");
        }
    }
}
