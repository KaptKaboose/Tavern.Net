namespace Tavern.Net.Game;

/// <summary>
/// Orchestrates a play session. Modeled around a list of players (rather
/// than a hardcoded pair) so a future networked mode can add a second,
/// remotely-controlled player without reshaping this class.
/// </summary>
public sealed class GameSession
{
    private readonly Random _random;

    public List<Player> Players { get; } = new();

    public GameSession(Random? random = null)
    {
        _random = random ?? new Random();
    }

    public Player AddPlayer(string name, int startingLife = 20)
    {
        var player = new Player(name, startingLife);
        Players.Add(player);
        return player;
    }

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

    /// <summary>Moves the top card of <paramref name="from"/> to <paramref name="to"/>. Returns false if the source zone was empty.</summary>
    public bool DrawCard(Player player, ZoneType from = ZoneType.MainDeck, ZoneType to = ZoneType.Hand)
    {
        var source = player.GetZone(from);
        if (source.Cards.Count == 0)
        {
            player.Stats.Log($"Tried to draw from {from} but it was empty.");
            return false;
        }

        var card = source.Cards[0];
        source.Cards.RemoveAt(0);
        player.GetZone(to).Cards.Add(card);

        if (to == ZoneType.Hand)
        {
            player.Stats.CardsDrawnCount++;
        }

        player.Stats.Log($"Drew {card.Card.Name} from {from} to {to}.");
        return true;
    }

    public void MoveCard(Player player, CardInstance card, ZoneType from, ZoneType to, double? fieldX = null, double? fieldY = null)
    {
        var source = player.GetZone(from);
        if (!source.Cards.Remove(card))
        {
            throw new InvalidOperationException($"{card.Card.Name} was not found in {from}.");
        }

        player.GetZone(to).Cards.Add(card);

        if (to == ZoneType.Field && fieldX is not null && fieldY is not null)
        {
            card.FieldX = fieldX.Value;
            card.FieldY = fieldY.Value;
        }

        player.Stats.Log($"Moved {card.Card.Name} from {from} to {to}.");
    }

    /// <summary>Repositions a card already on the Field, without any zone change or log entry — used while dragging within the Field.</summary>
    public void RepositionOnField(CardInstance card, double x, double y)
    {
        card.FieldX = x;
        card.FieldY = y;
    }

    /// <summary>Shuffles the whole hand back into the main deck, reshuffles, then draws a fresh hand of the same size.</summary>
    public void Mulligan(Player player)
    {
        var hand = player.GetZone(ZoneType.Hand);
        var deck = player.GetZone(ZoneType.MainDeck);
        var handSize = hand.Cards.Count;

        foreach (var card in hand.Cards.ToList())
        {
            hand.Cards.Remove(card);
            deck.Cards.Add(card);
        }

        Shuffle(player);

        for (var i = 0; i < handSize; i++)
        {
            DrawCard(player);
        }

        player.Stats.MulliganCount++;
        player.Stats.Log($"Took a mulligan (hand size {handSize}).");
    }

    public void AdjustLife(Player player, int delta)
    {
        player.Life += delta;
        player.Stats.RecordLife(player.Life);
        player.Stats.Log($"Life changed by {delta:+0;-0} to {player.Life}.");
    }

    public void NextTurn(Player player)
    {
        player.Stats.TurnCount++;
        player.Stats.Log("New turn.");
    }
}
