using Tavern.Net.GameData.Models;

namespace Tavern.Net.Game;

/// <summary>
/// The display order for a <see cref="DeckArrangement"/>'s lists (see the sideboard panel):
/// Main by element then type; Material with champions first (in level order, so the base champion
/// leads), then everything else by element then type; Sideboard by type then element. Elements rank
/// Norm first, then the basic elements (Fire, Water, Wind) alphabetically, then every advanced one
/// alphabetically. The arrangement's own lists are re-ordered in place, so the order the panel
/// shows is also the order the Material deck is rebuilt in (ApplyArrangement's HomeOrder).
/// </summary>
public static class DeckSorting
{
    private static readonly HashSet<string> BasicElements = new(StringComparer.OrdinalIgnoreCase) { "Fire", "Water", "Wind" };

    public static string PrimaryElement(CardDto card) =>
        card.Elements.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e)) ?? "Norm";

    public static string PrimaryType(CardDto card) => card.Types.FirstOrDefault() ?? "";

    private static int ElementGroup(CardDto card)
    {
        var element = PrimaryElement(card);
        if (element.Equals("Norm", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return BasicElements.Contains(element) ? 1 : 2;
    }

    public static IEnumerable<CardDto> OrderMain(IEnumerable<CardDto> cards) => cards
        .OrderBy(ElementGroup)
        .ThenBy(PrimaryElement, StringComparer.OrdinalIgnoreCase)
        .ThenBy(PrimaryType, StringComparer.OrdinalIgnoreCase)
        .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase);

    // Champions are ordered by level first — so the Level 0 base champion always leads, whatever
    // element the higher levels are — and only then by element/type like everything else.
    public static IEnumerable<CardDto> OrderMaterial(IEnumerable<CardDto> cards) => cards
        .OrderBy(c => c.IsChampion ? 0 : 1)
        .ThenBy(c => c.IsChampion ? c.Level ?? double.MaxValue : 0)
        .ThenBy(ElementGroup)
        .ThenBy(PrimaryElement, StringComparer.OrdinalIgnoreCase)
        .ThenBy(PrimaryType, StringComparer.OrdinalIgnoreCase)
        .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<CardDto> OrderSideboard(IEnumerable<CardDto> cards) => cards
        .OrderBy(PrimaryType, StringComparer.OrdinalIgnoreCase)
        .ThenBy(ElementGroup)
        .ThenBy(PrimaryElement, StringComparer.OrdinalIgnoreCase)
        .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Re-orders the arrangement's current lists (not the registered ones) in place.</summary>
    public static void SortInPlace(DeckArrangement deck)
    {
        Replace(deck.Main, OrderMain(deck.Main));
        Replace(deck.Material, OrderMaterial(deck.Material));
        Replace(deck.Sideboard, OrderSideboard(deck.Sideboard));
    }

    private static void Replace(List<CardDto> list, IEnumerable<CardDto> ordered)
    {
        var sorted = ordered.ToList();
        list.Clear();
        list.AddRange(sorted);
    }
}
