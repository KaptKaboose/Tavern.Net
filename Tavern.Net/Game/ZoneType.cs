namespace Tavern.Net.Game;

public enum ZoneType
{
    MainDeck,
    MaterialDeck,
    Hand,
    Field,
    Graveyard,
    Banishment,
    Memory,
    Champion,
    Tokens,

    // Must stay last: GameConnection's JsonSerializerOptions has no JsonStringEnumConverter, so
    // ZoneType travels as a raw int on every online broadcast — inserting a new member anywhere
    // but the end would silently renumber every value after it.
    Sealed,
}
