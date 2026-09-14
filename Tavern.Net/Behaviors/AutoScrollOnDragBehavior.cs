using System.Windows;
using System.Windows.Controls;

namespace Tavern.Net.Behaviors;

/// <summary>
/// Attached behavior that auto-scrolls a ScrollViewer while a drag hovers near its top/bottom edge
/// — normal scrolling (wheel, scrollbar) doesn't work during an active OS-level drag, so without
/// this, a ScrollViewer taller than the screen (e.g. the Glimpse overlay after drawing many cards)
/// has no way to reach content below the fold while mid-drag. Usage:
/// <c>behaviors:AutoScrollOnDragBehavior.IsEnabled="True"</c> on the ScrollViewer itself.
/// </summary>
public static class AutoScrollOnDragBehavior
{
    // How close to the top/bottom edge (in pixels) triggers scrolling, and how far each DragOver
    // tick scrolls — DragOver fires roughly as often as the mouse moves, so this is fast enough to
    // feel responsive without being twitchy.
    private const double EdgeMargin = 48;
    private const double ScrollStep = 18;

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(AutoScrollOnDragBehavior), new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer scrollViewer)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            // Preview (tunnel), not the bubbling DragOver — that fires on whatever's directly
            // under the cursor and can gap out crossing between drop targets nested inside this
            // ScrollViewer (see CardDragBehavior.OnGiveFeedback for the same issue elsewhere);
            // Preview fires on this element every time the drag tunnels through it regardless.
            scrollViewer.PreviewDragOver += OnPreviewDragOver;
        }
        else
        {
            scrollViewer.PreviewDragOver -= OnPreviewDragOver;
        }
    }

    private static void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        var y = e.GetPosition(scrollViewer).Y;
        if (y < EdgeMargin)
        {
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - ScrollStep);
        }
        else if (y > scrollViewer.ActualHeight - EdgeMargin)
        {
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + ScrollStep);
        }
    }
}
