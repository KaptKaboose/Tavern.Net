using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Tavern.Net.ViewModels;

namespace Tavern.Net.Behaviors;

/// <summary>
/// Drag-and-drop between the sideboard panel's three lists (SideboardViewModel). Two attached
/// properties on a list's container mark it as a drop target — <c>ListKind</c> says which list it
/// is and <c>MoveCommand</c> is what runs on a drop — and <c>IsRowDragSource</c> on each row makes
/// that row draggable. A double-click on a row asks the same command for its natural quick move
/// (no explicit target). The command's own CanExecute decides whether a drop is legal (e.g. a
/// Material card can't go in Main), which also drives the drag cursor.
/// </summary>
public static class DeckListBehavior
{
    private const string DragFormat = "Tavern.Net.DeckCardDrag";

    private sealed record DragPayload(DeckCardGroup Group, DeckListKind Source);

    private static Point _dragStart;
    private static bool _dragPending;

    public static readonly DependencyProperty ListKindProperty = DependencyProperty.RegisterAttached(
        "ListKind", typeof(DeckListKind?), typeof(DeckListBehavior), new PropertyMetadata(null, OnListKindChanged));

    public static void SetListKind(DependencyObject element, DeckListKind? value) => element.SetValue(ListKindProperty, value);
    public static DeckListKind? GetListKind(DependencyObject element) => (DeckListKind?)element.GetValue(ListKindProperty);

    public static readonly DependencyProperty MoveCommandProperty = DependencyProperty.RegisterAttached(
        "MoveCommand", typeof(ICommand), typeof(DeckListBehavior));

    public static void SetMoveCommand(DependencyObject element, ICommand? value) => element.SetValue(MoveCommandProperty, value);
    public static ICommand? GetMoveCommand(DependencyObject element) => (ICommand?)element.GetValue(MoveCommandProperty);

    public static readonly DependencyProperty IsRowDragSourceProperty = DependencyProperty.RegisterAttached(
        "IsRowDragSource", typeof(bool), typeof(DeckListBehavior), new PropertyMetadata(false, OnIsRowDragSourceChanged));

    public static void SetIsRowDragSource(DependencyObject element, bool value) => element.SetValue(IsRowDragSourceProperty, value);
    public static bool GetIsRowDragSource(DependencyObject element) => (bool)element.GetValue(IsRowDragSourceProperty);

    private static void OnListKindChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        element.DragOver -= OnDragOver;
        element.Drop -= OnDrop;

        if (e.NewValue is not null)
        {
            element.AllowDrop = true;
            element.DragOver += OnDragOver;
            element.Drop += OnDrop;
        }
    }

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = TryBuildRequest(sender, e, out var request, out var command) && command!.CanExecute(request)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        if (TryBuildRequest(sender, e, out var request, out var command) && command!.CanExecute(request))
        {
            command.Execute(request);
        }

        e.Handled = true;
    }

    private static bool TryBuildRequest(object sender, DragEventArgs e, out DeckMoveRequest? request, out ICommand? command)
    {
        request = null;
        command = null;

        if (sender is not DependencyObject target
            || GetListKind(target) is not { } targetKind
            || GetMoveCommand(target) is not { } moveCommand
            || !e.Data.GetDataPresent(DragFormat)
            || e.Data.GetData(DragFormat) is not DragPayload payload)
        {
            return false;
        }

        request = new DeckMoveRequest(payload.Group, payload.Source, targetKind);
        command = moveCommand;
        return true;
    }

    private static void OnIsRowDragSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement row)
        {
            return;
        }

        row.PreviewMouseLeftButtonDown -= OnRowMouseDown;
        row.PreviewMouseMove -= OnRowMouseMove;
        row.PreviewMouseLeftButtonUp -= OnRowMouseUp;

        if ((bool)e.NewValue)
        {
            row.PreviewMouseLeftButtonDown += OnRowMouseDown;
            row.PreviewMouseMove += OnRowMouseMove;
            row.PreviewMouseLeftButtonUp += OnRowMouseUp;
        }
    }

    private static void OnRowMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            _dragPending = false;
            if (TryFindList(sender, out var group, out var kind, out var command))
            {
                var request = new DeckMoveRequest(group!, kind, null);
                if (command!.CanExecute(request))
                {
                    command.Execute(request);
                }
            }

            e.Handled = true;
            return;
        }

        _dragStart = e.GetPosition(null);
        _dragPending = true;
    }

    private static void OnRowMouseUp(object sender, MouseButtonEventArgs e) => _dragPending = false;

    private static void OnRowMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragPending || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var position = e.GetPosition(null);
        if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragPending = false;
        if (sender is DependencyObject row && TryFindList(sender, out var group, out var kind, out _))
        {
            var data = new DataObject();
            data.SetData(DragFormat, new DragPayload(group!, kind));
            DragDrop.DoDragDrop(row, data, DragDropEffects.Move);
        }
    }

    /// <summary>Finds the row's card group (its DataContext) and which list it's in — the nearest
    /// ancestor carrying a ListKind — plus that list's move command.</summary>
    private static bool TryFindList(object sender, out DeckCardGroup? group, out DeckListKind kind, out ICommand? command)
    {
        group = (sender as FrameworkElement)?.DataContext as DeckCardGroup;
        kind = default;
        command = null;

        var current = sender as DependencyObject;
        while (current is not null)
        {
            if (GetListKind(current) is { } found && GetMoveCommand(current) is { } found2)
            {
                kind = found;
                command = found2;
                return group is not null;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }
}
