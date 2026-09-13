using Tavern.Net.Game;

namespace Tavern.Net.ViewModels;

/// <summary>
/// Carries a drag-and-drop (or other single-command) card move to <see cref="GameBoardViewModel"/>.
/// FieldX/FieldY are the drop position in the Field's coordinate space — ignored for any other destination.
/// </summary>
public sealed record MoveCardRequest(CardViewModel Card, ZoneType Destination, double? FieldX = null, double? FieldY = null);
