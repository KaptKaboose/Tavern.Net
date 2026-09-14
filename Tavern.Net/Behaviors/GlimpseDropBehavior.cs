using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Tavern.Net.ViewModels;

namespace Tavern.Net.Behaviors;

/// <summary>
/// Attached behavior for drop targets inside the Glimpse overlay. Set on two kinds of element:
/// the zone container itself (Target only — a drop there with no InsertBefore appends to the end
/// of that pile), and, via ItemContainerStyle, each card already in a pile (Target + InsertBefore
/// bound to that card — a drop there inserts before it). The per-item Drop marks the event Handled
/// so it doesn't also bubble up and re-fire the zone-level container's own Drop as an append.
/// </summary>
public static class GlimpseDropBehavior
{
    private static readonly Brush HighlightBrush = new SolidColorBrush(Color.FromArgb(60, 30, 144, 255));
    private static readonly Dictionary<object, Brush?> OriginalBackgrounds = new();

    public static readonly DependencyProperty TargetProperty = DependencyProperty.RegisterAttached(
        "Target", typeof(GlimpseTarget?), typeof(GlimpseDropBehavior), new PropertyMetadata(null, OnTargetChanged));

    public static void SetTarget(DependencyObject element, GlimpseTarget? value) => element.SetValue(TargetProperty, value);
    public static GlimpseTarget? GetTarget(DependencyObject element) => (GlimpseTarget?)element.GetValue(TargetProperty);

    public static readonly DependencyProperty MoveCommandProperty = DependencyProperty.RegisterAttached(
        "MoveCommand", typeof(ICommand), typeof(GlimpseDropBehavior));

    public static void SetMoveCommand(DependencyObject element, ICommand value) => element.SetValue(MoveCommandProperty, value);
    public static ICommand? GetMoveCommand(DependencyObject element) => (ICommand?)element.GetValue(MoveCommandProperty);

    public static readonly DependencyProperty InsertBeforeProperty = DependencyProperty.RegisterAttached(
        "InsertBefore", typeof(CardViewModel), typeof(GlimpseDropBehavior));

    public static void SetInsertBefore(DependencyObject element, CardViewModel? value) => element.SetValue(InsertBeforeProperty, value);
    public static CardViewModel? GetInsertBefore(DependencyObject element) => (CardViewModel?)element.GetValue(InsertBeforeProperty);

    private static void OnTargetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        if (e.NewValue is GlimpseTarget)
        {
            element.AllowDrop = true;
            element.DragEnter += OnDragEnter;
            element.DragLeave += OnDragLeave;
            element.Drop += OnDrop;
        }
        else
        {
            element.AllowDrop = false;
            element.DragEnter -= OnDragEnter;
            element.DragLeave -= OnDragLeave;
            element.Drop -= OnDrop;
        }
    }

    private static void OnDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(CardDragPayload)))
        {
            SetHighlight(sender, HighlightBrush);
        }
    }

    private static void OnDragLeave(object sender, DragEventArgs e)
    {
        SetHighlight(sender, null);
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        SetHighlight(sender, null);

        if (sender is not DependencyObject element || !e.Data.GetDataPresent(typeof(CardDragPayload)))
        {
            return;
        }

        if (GetTarget(element) is not { } target)
        {
            return;
        }

        var payload = (CardDragPayload)e.Data.GetData(typeof(CardDragPayload))!;
        var request = new GlimpseDropRequest(payload.Card, target, GetInsertBefore(element));
        var command = GetMoveCommand(element);
        if (command is not null && command.CanExecute(request))
        {
            command.Execute(request);
        }

        // Stops this same drop from also bubbling up to (and re-firing on) an ancestor zone-level
        // target — without this, dropping on a specific card would insert-before AND append.
        e.Handled = true;
    }

    // Restores the exact original brush rather than ClearValue-ing it — see CardDropBehavior for
    // why that matters (ClearValue wipes out a local-value background like a zone's own fill).
    private static void SetHighlight(object sender, Brush? brush)
    {
        DependencyProperty backgroundProperty = sender switch
        {
            Border => Border.BackgroundProperty,
            Control => Control.BackgroundProperty,
            Panel => Panel.BackgroundProperty,
            _ => null!,
        };

        if (backgroundProperty is null || sender is not DependencyObject element)
        {
            return;
        }

        if (brush is not null)
        {
            if (!OriginalBackgrounds.ContainsKey(sender))
            {
                OriginalBackgrounds[sender] = (Brush?)element.GetValue(backgroundProperty);
            }

            element.SetValue(backgroundProperty, brush);
        }
        else if (OriginalBackgrounds.Remove(sender, out var original))
        {
            element.SetValue(backgroundProperty, original);
        }
    }
}
