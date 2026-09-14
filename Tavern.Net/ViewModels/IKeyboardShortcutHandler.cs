using System.Windows.Input;

namespace Tavern.Net.ViewModels;

/// <summary>
/// Implemented by whichever view-model is currently active (<see cref="MainViewModel.CurrentView"/>)
/// to react to raw key presses — e.g. Space advancing the turn phase — routed here from
/// <see cref="MainWindow"/>'s PreviewKeyDown, which tunnels from the window down regardless of what
/// currently has focus. That matters because a focused Button treats Space/Enter as "click me" in
/// its own (later) bubble-phase handling; intercepting during the earlier tunnel phase means a
/// keyboard shortcut can never be accidentally swallowed by whatever the user last clicked. See
/// <see cref="global::Tavern.Net.MainWindow"/>.
/// </summary>
public interface IKeyboardShortcutHandler
{
    /// <summary>Handles <paramref name="key"/> if it maps to a command. Returns whether it did, so the caller can mark the routed event Handled.</summary>
    bool HandleKey(Key key);
}
