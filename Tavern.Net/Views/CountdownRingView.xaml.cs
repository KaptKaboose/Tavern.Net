using System.Windows;
using System.Windows.Controls;
using Tavern.Net.Converters;

namespace Tavern.Net.Views;

/// <summary>A reusable depleting countdown ring — used by the Reveal overlay (cancellable, "Keep
/// open" pauses it) and the Undo badge (flat, non-cancellable 5s). RemainingFraction drives how
/// much of the ring is still drawn; the caller's own DispatcherTimer owns the actual countdown.</summary>
public partial class CountdownRingView : UserControl
{
    private static readonly RemainingFractionToDashArrayConverter DashArrayConverter = new();

    public static readonly DependencyProperty RemainingFractionProperty = DependencyProperty.Register(
        nameof(RemainingFraction), typeof(double), typeof(CountdownRingView),
        new PropertyMetadata(1.0, OnVisualPropertyChanged));

    public double RemainingFraction
    {
        get => (double)GetValue(RemainingFractionProperty);
        set => SetValue(RemainingFractionProperty, value);
    }

    public static readonly DependencyProperty DiameterProperty = DependencyProperty.Register(
        nameof(Diameter), typeof(double), typeof(CountdownRingView),
        new PropertyMetadata(48.0, OnVisualPropertyChanged));

    public double Diameter
    {
        get => (double)GetValue(DiameterProperty);
        set => SetValue(DiameterProperty, value);
    }

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(CountdownRingView),
        new PropertyMetadata(5.0, OnVisualPropertyChanged));

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public CountdownRingView()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateDashArray();
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((CountdownRingView)d).UpdateDashArray();

    private void UpdateDashArray()
    {
        if (ProgressRing is null)
        {
            return;
        }

        // StrokeDashArray units are relative to StrokeThickness, not absolute pixels — the ring's
        // own radius, in those units, is (Diameter - Thickness - margin) / 2 / Thickness (the -2
        // margin matches CountdownRingView.xaml's own fixed Margin="2" inset on each Ellipse).
        var radiusInThicknessUnits = (Diameter - Thickness - 4) / 2 / Math.Max(Thickness, 0.01);
        var circumference = 2 * Math.PI * Math.Max(radiusInThicknessUnits, 0);
        ProgressRing.StrokeDashArray = (System.Windows.Media.DoubleCollection)DashArrayConverter.Convert(
            RemainingFraction, typeof(System.Windows.Media.DoubleCollection), circumference, System.Globalization.CultureInfo.InvariantCulture);
    }
}
