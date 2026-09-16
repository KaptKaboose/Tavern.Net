namespace Tavern.Net.Decklists;

/// <summary>One resolved line of a saved deck — the exact card (by slug, so a reload never has to
/// guess between candidates again), not just the name the player originally typed.</summary>
public sealed record SavedDeckEntry(string Slug, string CardName, int Quantity, DeckSection Section);

/// <summary>
/// A deck the player named and saved. <see cref="Entries"/> is the source of truth for rebuilding a
/// game (each slug round-trips through <see cref="GameData.GrandArchiveApiClient.GetCardBySlugAsync"/>,
/// which is disk-cached from the original import) so loading never needs to re-resolve ambiguous
/// names; <see cref="DecklistText"/> is kept only so the paste box can show what was originally
/// imported.
/// </summary>
public sealed record SavedDeck(string Name, string DecklistText, DateTime SavedAtUtc, List<SavedDeckEntry> Entries);
