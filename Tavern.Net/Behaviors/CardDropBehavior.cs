using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Tavern.Net.Game;
using Tavern.Net.ViewModels;

namespace Tavern.Net.Behaviors;

/// <summary>
/// Attached behavior that turns any <see cref="UIElement"/> into a card drop target — a
/// <see cref="Control"/> (GroupBox, ItemsControl) or a plain <see cref="Border"/>/<see cref="Panel"/>.
/// Usage: set <c>behaviors:CardDropBehavior.TargetZone="Field"</c> and
/// <c>behaviors:CardDropBehavior.MoveCommand="{Binding MoveCardToCommand}"</c>
/// on the zone's container in XAML.
/// </summary>
public static class CardDropBehavior
{
    private static readonly Brush HighlightBrush = new SolidColorBrush(Color.FromArgb(60, 30, 144, 255));

    // Background as it was before we overlaid the highlight, so we can put it back exactly —
    // ClearValue() is wrong here because the zone's own Background (e.g. StackZoneView's "#222"
    // pile fill) is itself a local value; clearing it wipes it out permanently instead of
    // reverting to it, leaving the zone transparent after the first drag-enter/leave or drop.
    private static readonly Dictionary<object, Brush?> OriginalBackgrounds = new();

    public static readonly DependencyProperty TargetZoneProperty = DependencyProperty.RegisterAttached(
        "TargetZone", typeof(ZoneType?), typeof(CardDropBehavior), new PropertyMetadata(null, OnTargetZoneChanged));

    public static void SetTargetZone(DependencyObject element, ZoneType? value) => element.SetValue(TargetZoneProperty, value);
    public static ZoneType? GetTargetZone(DependencyObject element) => (ZoneType?)element.GetValue(TargetZoneProperty);

    public static readonly DependencyProperty MoveCommandProperty = DependencyProperty.RegisterAttached(
        "MoveCommand", typeof(ICommand), typeof(CardDropBehavior), new PropertyMetadata(null));

    public static void SetMoveCommand(DependencyObject element, ICommand value) => element.SetValue(MoveCommandProperty, value);
    public static ICommand? GetMoveCommand(DependencyObject element) => (ICommand?)element.GetValue(MoveCommandProperty);

    private static void OnTargetZoneChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // NOTE: this used to require `d is Control`, which silently did nothing for a plain
        // Border (e.g. StackZoneView's pile widget) — Border doesn't derive from Control.
        // AllowDrop/DragEnter/DragLeave/Drop are all defined on UIElement, so that's the right
        // bound here; only the "highlight background" logic needs to know about specific types.
        if (d is not UIElement element)
        {
            return;
        }

        if (e.NewValue is ZoneType)
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

    private static void OnDragEnter(object sender, DragEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[DragDrop] DragEnter zone={(sender is DependencyObject d1 ? GetTargetZone(d1) : null)} hasPayload={e.Data.GetDataPresent(typeof(CardDragPayload))}");

        if (e.Data.GetDataPresent(typeof(CardDragPayload)))
        {
            SetHighlight(sender, HighlightBrush);
        }
    }

    private static void OnDragLeave(object sender, DragEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[DragDrop] DragLeave from zone={(sender is DependencyObject d3 ? GetTargetZone(d3) : null)}");

        SetHighlight(sender, null);
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[DragDrop] Drop zone={(sender is DependencyObject d2 ? GetTargetZone(d2) : null)} hasPayload={e.Data.GetDataPresent(typeof(CardDragPayload))}");

        SetHighlight(sender, null);

        if (sender is not UIElement element)
        {
            return;
        }

        if (!e.Data.GetDataPresent(typeof(CardDragPayload)) || GetTargetZone(element) is not { } zone)
        {
            return;
        }

        var payload = (CardDragPayload)e.Data.GetData(typeof(CardDragPayload))!;
        // Anchor the drop so the point the user grabbed lands back under the cursor, matching the drag ghost.
        var dropPoint = e.GetPosition(element);
        var topLeft = new Point(dropPoint.X - payload.GrabOffset.X, dropPoint.Y - payload.GrabOffset.Y);

        var request = new MoveCardRequest(payload.Card, zone, topLeft.X, topLeft.Y);
        var command = GetMoveCommand(element);
        if (command is not null && command.CanExecute(request))
        {
            command.Execute(request);
        }
    }
}
