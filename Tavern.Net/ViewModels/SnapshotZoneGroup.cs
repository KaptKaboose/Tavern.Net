using Tavern.Net.Game;

namespace Tavern.Net.ViewModels;

/// <summary>One pile's worth of cards within a Major event's snapshot (Champion, Graveyard,
/// Banishment, Material, Main — the zones StackZoneView renders as piles on the live board).
/// Field/Hand/Memory are spread out directly instead and never wrapped in this. Always present
/// even when empty (a "0" box) — see GameBoardViewModel.PopulateViewedCollections.
/// <para>A redacted group (a live opponent's Material/Main) carries no cards, only a face-down
/// <see cref="RedactedCount"/>; it isn't clickable.</para></summary>
public sealed record SnapshotZoneGroup(
    ZoneType Zone,
    IReadOnlyList<CardSnapshotViewModel> Cards,
    bool IsRedacted = false,
    int RedactedCount = 0)
{
    public int Count => IsRedacted ? RedactedCount : Cards.Count;
}
