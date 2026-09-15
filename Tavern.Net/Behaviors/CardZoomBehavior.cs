using System.Windows;
using System.Windows.Input;
using Tavern.Net.ViewModels;

namespace Tavern.Net.Behaviors;

/// <summary>
/// Attached behavior that turns any element whose DataContext is a <see cref="CardViewModel"/>
/// (live board) or <see cref="CardSnapshotViewModel"/> (read-only snapshot viewer) into a
/// right-click zoom trigger, routing to that DataContext's own Board and zoom command. Usage: set
/// <c>behaviors:CardZoomBehavior.IsZoomable="True"</c> on the element in XAML.
/// </summary>
public static class CardZoomBehavior
{
    public static readonly DependencyProperty IsZoomableProperty = DependencyProperty.RegisterAttached(
        "IsZoomable", typeof(bool), typeof(CardZoomBehavior), new PropertyMetadata(false, OnIsZoomableChanged));

    public static void SetIsZoomable(DependencyObject element, bool value) => element.SetValue(IsZoomableProperty, value);
    public static bool GetIsZoomable(DependencyObject element) => (bool)element.GetValue(IsZoomableProperty);

    private static void OnIsZoomableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            element.PreviewMouseRightButtonDown += OnPreviewMouseRightButtonDown;
        }
        else
        {
            element.PreviewMouseRightButtonDown -= OnPreviewMouseRightButtonDown;
        }
    }

    private static void OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        e.Handled = true;

        switch (element.DataContext)
        {
            case CardViewModel card when card.Board.ZoomCardCommand.CanExecute(card):
                card.Board.ZoomCardCommand.Execute(card);
                break;
            case CardSnapshotViewModel snapshot when snapshot.Board.ZoomSnapshotCardCommand.CanExecute(snapshot):
                snapshot.Board.ZoomSnapshotCardCommand.Execute(snapshot);
                break;
        }
    }
}
