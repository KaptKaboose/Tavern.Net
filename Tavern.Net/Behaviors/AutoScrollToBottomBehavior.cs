using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace Tavern.Net.Behaviors;

/// <summary>
/// Attached behavior for a ListBox whose ItemsSource grows over time (the play log): keeps the
/// view pinned to the newest entry as long as the player was already at the bottom, but leaves it
/// alone if they've scrolled up to read older entries — a new entry shouldn't yank the screen out
/// from under them. Usage: <c>behaviors:AutoScrollToBottomBehavior.IsEnabled="True"</c> on the
/// ListBox itself.
/// </summary>
public static class AutoScrollToBottomBehavior
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(AutoScrollToBottomBehavior), new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    // Tracked per-ListBox from the last ScrollChanged, read right before a new item lands (see
    // OnItemsChanged) — this has to be read *before* the add, since the add itself changes
    // ExtentHeight and would otherwise always read back "not at bottom".
    private static readonly DependencyProperty WasAtBottomProperty = DependencyProperty.RegisterAttached(
        "WasAtBottom", typeof(bool), typeof(AutoScrollToBottomBehavior), new PropertyMetadata(true));

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox listBox || !(bool)e.NewValue)
        {
            return;
        }

        listBox.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrollChanged));

        // ItemContainerGenerator exists immediately (no need to wait for Loaded/template
        // application), and fires for every ItemsSource change regardless of when the ItemsSource
        // binding itself actually resolves.
        listBox.ItemContainerGenerator.ItemsChanged += (_, args) => OnItemsChanged(listBox, args);
    }

    private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not ListBox listBox)
        {
            return;
        }

        const double epsilon = 4;
        var atBottom = e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - epsilon;
        listBox.SetValue(WasAtBottomProperty, atBottom);
    }

    private static void OnItemsChanged(ListBox listBox, ItemsChangedEventArgs args)
    {
        if (args.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Add || !(bool)listBox.GetValue(WasAtBottomProperty))
        {
            return;
        }

        // The new item hasn't been measured/arranged yet — defer until layout catches up so
        // ScrollToBottom's ExtentHeight actually reflects it.
        listBox.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => FindScrollViewer(listBox)?.ScrollToBottom()));
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer scrollViewer)
            {
                return scrollViewer;
            }

            if (FindScrollViewer(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
