using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Tavern.Net.Behaviors;

/// <summary>
/// Renders a single visual (typically a snapshot of the element being dragged)
/// floating at the mouse position, on top of everything else in the window.
/// </summary>
internal sealed class DragAdorner : Adorner
{
    private readonly UIElement _visual;
    private readonly Point _grabOffset;
    private Point _position;

    public DragAdorner(UIElement adornedElement, UIElement visual, Point grabOffset)
        : base(adornedElement)
    {
        _visual = visual;
        _grabOffset = grabOffset;
        IsHitTestVisible = false;
        AddVisualChild(visual);
        System.Diagnostics.Debug.WriteLine($"[DragAdorner] Constructed. adornedElement={adornedElement.GetType().Name} visual size={(visual as FrameworkElement)?.Width}x{(visual as FrameworkElement)?.Height}");
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) => _visual;

    /// <summary>Moves the adorner so the same point the user grabbed stays under the cursor.</summary>
    public void UpdatePosition(Point cursorPosition)
    {
        _position = new Point(cursorPosition.X - _grabOffset.X, cursorPosition.Y - _grabOffset.Y);
        var layer = Parent as AdornerLayer;
        System.Diagnostics.Debug.WriteLine($"[DragAdorner] UpdatePosition to {_position}. Parent is AdornerLayer: {layer is not null}");
        layer?.Update(AdornedElement);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        _visual.Measure(constraint);
        System.Diagnostics.Debug.WriteLine($"[DragAdorner] MeasureOverride constraint={constraint} visual.DesiredSize={_visual.DesiredSize}");
        return _visual.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _visual.Arrange(new Rect(finalSize));
        System.Diagnostics.Debug.WriteLine($"[DragAdorner] ArrangeOverride finalSize={finalSize}");
        return finalSize;
    }

    public override GeneralTransform GetDesiredTransform(GeneralTransform transform)
    {
        System.Diagnostics.Debug.WriteLine($"[DragAdorner] GetDesiredTransform applying position={_position}");
        var group = new GeneralTransformGroup();
        group.Children.Add(base.GetDesiredTransform(transform));
        group.Children.Add(new TranslateTransform(_position.X, _position.Y));
        return group;
    }
}
