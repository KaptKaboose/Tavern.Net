using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Tavern.Net.ViewModels;

namespace Tavern.Net.Behaviors;

/// <summary>
/// Attached behavior that turns any element whose DataContext is a
/// <see cref="CardViewModel"/> into a drag source. Usage: set
/// <c>behaviors:CardDragBehavior.IsDraggable="True"</c> on the element in XAML.
/// While dragging, a snapshot of the element itself follows the cursor
/// (via the window's adorner layer) instead of the default OS drag cursor.
/// </summary>
public static class CardDragBehavior
{
    public static readonly DependencyProperty IsDraggableProperty = DependencyProperty.RegisterAttached(
        "IsDraggable", typeof(bool), typeof(CardDragBehavior), new PropertyMetadata(false, OnIsDraggableChanged));

    public static void SetIsDraggable(DependencyObject element, bool value) => element.SetValue(IsDraggableProperty, value);
    public static bool GetIsDraggable(DependencyObject element) => (bool)element.GetValue(IsDraggableProperty);

    // WPF's own drag threshold constants (SystemParameters.Minimum*DragDistance) — a plain
    // click shouldn't start a drag, only a deliberate press-and-move.
    private static Point _dragStartScreenPoint;
    private static Point _grabOffsetInElement;

    // Set once OnPreviewMouseMove actually kicks off DoDragDrop, so the paired MouseLeftButtonUp
    // can tell a real drag apart from a plain click (which taps/untaps the card instead).
    private static bool _dragStarted;

    // ClickCount is read from the DOWN event, not the paired UP event — WPF's double-click
    // tracking is tied to button-down, and reading it on MouseLeftButtonUp (as this originally
    // did) reported 1 for every click, so a double-click never registered as anything but two
    // separate taps. This flag carries that DOWN-event reading forward to the matching UP event.
    private static bool _isDoubleClick;

    // The adorner for whichever drag is currently in progress (only one drag can be active at
    // a time), updated from GiveFeedback rather than DragOver — see OnGiveFeedback for why.
    private static DragAdorner? _activeAdorner;
    private static UIElement? _activeAdornerRoot;
    private static bool _loggedFirstFeedbackThisDrag;
    private static bool _loggedMissingAdornerThisDrag;

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    /// <summary>
    /// The cursor's position, converted into <paramref name="relativeTo"/>'s local coordinates.
    /// <see cref="Mouse.GetPosition"/> is documented to go stale during an active OS-level
    /// <see cref="DragDrop.DoDragDrop"/> operation (confirmed: it froze at a garbage, offscreen
    /// value for the rest of the drag after the very first tick) — asking Windows directly via
    /// GetCursorPos, then converting with the purely-geometric <see cref="Visual.PointFromScreen"/>,
    /// sidesteps that stale input-tracking state entirely.
    /// </summary>
    private static Point GetCursorPosition(Visual relativeTo)
    {
        GetCursorPos(out var screenPoint);
        return relativeTo.PointFromScreen(new Point(screenPoint.X, screenPoint.Y));
    }

    private static void OnIsDraggableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            element.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
            element.PreviewMouseMove += OnPreviewMouseMove;
            element.PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
            element.GiveFeedback += OnGiveFeedback;
        }
        else
        {
            element.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
            element.PreviewMouseMove -= OnPreviewMouseMove;
            element.PreviewMouseLeftButtonUp -= OnPreviewMouseLeftButtonUp;
            element.GiveFeedback -= OnGiveFeedback;
        }
    }

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartScreenPoint = e.GetPosition(null);
        _grabOffsetInElement = e.GetPosition((IInputElement)sender);
        _dragStarted = false;
        _isDoubleClick = e.ClickCount >= 2;
    }

    /// <summary>
    /// A press-and-release that never crossed the drag threshold is a click: toggle tapped, or on
    /// the second click of a double-click (per _isDoubleClick, read from the paired down event),
    /// flip instead. A double click on a Field card therefore taps it once, as an accidental side
    /// effect of the first click already committing before the second one arrives, then flips it —
    /// tolerated rather than delaying every single click to see if a second one is coming, which
    /// would make the much more common single-click tap feel laggy.
    /// </summary>
    private static void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStarted || sender is not FrameworkElement { DataContext: CardViewModel card })
        {
            return;
        }

        if (_isDoubleClick)
        {
            if (card.Board.FlipCardCommand.CanExecute(card))
            {
                card.Board.FlipCardCommand.Execute(card);
            }

            return;
        }

        if (card.Board.ToggleTappedCommand.CanExecute(card))
        {
            card.Board.ToggleTappedCommand.Execute(card);
        }
    }

    private static void OnGiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        // We draw our own drag visual via the adorner layer, so hide the OS's move/no-drop cursor.
        e.UseDefaultCursors = false;
        Mouse.SetCursor(Cursors.Hand);
        e.Handled = true;

        // GiveFeedback is raised on the drag SOURCE continuously for the whole drag, regardless
        // of what's currently underneath the cursor — unlike DragOver/PreviewDragOver, which are
        // raised on whatever element is being hovered and can have gaps exactly at the boundary
        // between two different drop targets (that's what made the ghost freeze crossing zones).
        var adorner = _activeAdorner;
        var root = _activeAdornerRoot;
        if (adorner is null || root is null)
        {
            if (!_loggedMissingAdornerThisDrag)
            {
                _loggedMissingAdornerThisDrag = true;
                System.Diagnostics.Debug.WriteLine("[DragDrop] GiveFeedback fired but adorner/root is null — nothing to move.");
            }

            return;
        }

        // Deferring this update via Dispatcher.BeginInvoke turned out not to work — it appears
        // the OLE drag message pump doesn't reliably drain queued dispatcher operations, so the
        // update never actually ran. Back to updating inline, but guarded: an unhandled exception
        // thrown from inside GiveFeedback can silently abort the whole drag operation, which is
        // what caused the "no ghost AND no drops work at all" regression previously.
        try
        {
            adorner.UpdatePosition(GetCursorPosition(root));
            if (!_loggedFirstFeedbackThisDrag)
            {
                _loggedFirstFeedbackThisDrag = true;
                System.Diagnostics.Debug.WriteLine("[DragDrop] GiveFeedback updated the adorner successfully.");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DragDrop] Adorner update threw: {ex}");
        }
    }

    private static void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not FrameworkElement { DataContext: CardViewModel card } element)
        {
            return;
        }

        var position = e.GetPosition(null);
        var movedHorizontally = Math.Abs(position.X - _dragStartScreenPoint.X);
        var movedVertically = Math.Abs(position.Y - _dragStartScreenPoint.Y);
        if (movedHorizontally < SystemParameters.MinimumHorizontalDragDistance && movedVertically < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragStarted = true;
        _loggedFirstFeedbackThisDrag = false;
        _loggedMissingAdornerThisDrag = false;

        var window = Window.GetWindow(element);
        var adornerRoot = window?.Content as UIElement;
        var adornerLayer = adornerRoot is null ? null : AdornerLayer.GetAdornerLayer(adornerRoot);

        System.Diagnostics.Debug.WriteLine($"[DragDrop] Starting drag of '{card.Name}'. window={(window is not null)} adornerRoot={(adornerRoot is not null)} adornerLayer={(adornerLayer is not null)} element.ActualWidth={element.ActualWidth} element.ActualHeight={element.ActualHeight}");

        DragAdorner? adorner = null;

        if (adornerRoot is not null && adornerLayer is not null)
        {
            // A frozen bitmap, not a live VisualBrush(element) — the card being dragged may live
            // inside the peek overlay (see below), which closes the instant the drag starts. A
            // live VisualBrush mirrors its source's current rendering, so it would go blank the
            // moment that source stops being rendered; a frozen snapshot keeps following the
            // cursor unaffected, matching "anchor stays on mouse" regardless of what closing the
            // peek does to the card's original visual container.
            // The adorner layer lives outside MainWindow's Viewbox, in real window pixels, while the
            // element is measured in the Viewbox's fixed design units — so the ghost (and the grab
            // offset that keeps it anchored under the cursor) has to be scaled by however much the
            // window is currently scaling the board, or it stays default-sized when the window
            // shrinks/grows.
            var origin = element.TransformToVisual(adornerRoot).Transform(new Point(0, 0));
            var unitX = element.TransformToVisual(adornerRoot).Transform(new Point(1, 0));
            var scale = Math.Max(0.01, (unitX - origin).Length);

            var ghostWidth = element.ActualWidth * scale;
            var ghostHeight = element.ActualHeight * scale;
            var pixelWidth = Math.Max(1, (int)Math.Ceiling(ghostWidth));
            var pixelHeight = Math.Max(1, (int)Math.Ceiling(ghostHeight));
            var renderTarget = new RenderTargetBitmap(pixelWidth, pixelHeight, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            renderTarget.Render(element);
            renderTarget.Freeze();

            var snapshot = new Rectangle
            {
                Width = ghostWidth,
                Height = ghostHeight,
                Fill = new ImageBrush(renderTarget) { Stretch = Stretch.Fill },
                Opacity = 0.85,
                IsHitTestVisible = false,
            };

            var scaledGrabOffset = new Point(_grabOffsetInElement.X * scale, _grabOffsetInElement.Y * scale);
            adorner = new DragAdorner(adornerRoot, snapshot, scaledGrabOffset);
            adornerLayer.Add(adorner);

            System.Diagnostics.Debug.WriteLine($"[DragDrop] Adorner added. IsVisible={adorner.IsVisible} ActualWidth={adorner.ActualWidth} ActualHeight={adorner.ActualHeight}");

            _activeAdorner = adorner;
            _activeAdornerRoot = adornerRoot;
        }

        // Close any overlay that covers the whole board (Peek, and now the Sealed panel) now that
        // the ghost has its own frozen snapshot to follow the cursor with — without closing it here,
        // a card dragged from inside one of these would have nowhere real to be dropped.
        card.Board.CloseOverlaysThatBlockDragTarget();

        try
        {
            var payload = new CardDragPayload(card, _grabOffsetInElement);
            var data = new DataObject(typeof(CardDragPayload), payload);
            var result = DragDrop.DoDragDrop(element, data, DragDropEffects.Move);
            System.Diagnostics.Debug.WriteLine($"[DragDrop] DoDragDrop returned {result}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DragDrop] DoDragDrop threw: {ex}");
            throw;
        }
        finally
        {
            if (adorner is not null && adornerLayer is not null)
            {
                adornerLayer.Remove(adorner);
            }

            _activeAdorner = null;
            _activeAdornerRoot = null;
        }
    }
}
