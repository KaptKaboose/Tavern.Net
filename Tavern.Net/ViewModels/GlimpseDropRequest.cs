namespace Tavern.Net.ViewModels;

/// <summary>
/// Carries a drag-and-drop move within the Glimpse overlay (Staging, Top, and Bottom are plain
/// ViewModel-side lists, not domain zones, so this doesn't reuse MoveCardRequest/ZoneType).
/// <paramref name="InsertBefore"/> is null to append at the end of <paramref name="Target"/>'s
/// list (dropped on empty space in the zone), or a specific card already in that list to insert
/// before it (dropped directly on that card).
/// </summary>
public sealed record GlimpseDropRequest(CardViewModel Card, GlimpseTarget Target, CardViewModel? InsertBefore);
