namespace Tavern.Net.ViewModels;

/// <summary>
/// Carries a "peek at this stack" request from a <see cref="Views.StackZoneView"/> pile click to
/// <see cref="GameBoardViewModel"/>. HeaderText travels with it because it's set per-instance in
/// XAML (e.g. "Graveyard") rather than living on the underlying <see cref="ZoneViewModel"/>.
/// </summary>
public sealed record PeekedZoneInfo(ZoneViewModel Zone, string HeaderText);
