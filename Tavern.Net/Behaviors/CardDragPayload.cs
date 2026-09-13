using System.Windows;
using Tavern.Net.ViewModels;

namespace Tavern.Net.Behaviors;

/// <summary>
/// What actually travels inside the OS drag-and-drop <see cref="System.Windows.DataObject"/>.
/// GrabOffset is where within the card the user grabbed it, so a drop can position the card's
/// top-left such that the same point ends up back under the cursor — matching the drag ghost exactly.
/// </summary>
internal sealed record CardDragPayload(CardViewModel Card, Point GrabOffset);
