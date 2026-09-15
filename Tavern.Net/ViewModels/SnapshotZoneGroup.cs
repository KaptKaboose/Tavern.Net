using Tavern.Net.Game;

namespace Tavern.Net.ViewModels;

/// <summary>One pile's worth of cards within a Major event's snapshot (Champion, Graveyard,
/// Banishment, Material, Main — the zones StackZoneView renders as piles on the live board).
/// Field/Hand/Memory are spread out directly instead and never wrapped in this. Empty piles are
/// simply omitted rather than included with a zero-length Cards list — see
/// GameBoardViewModel.OnViewedMajorEventChanged.</summary>
public sealed record SnapshotZoneGroup(ZoneType Zone, IReadOnlyList<CardSnapshotViewModel> Cards);
