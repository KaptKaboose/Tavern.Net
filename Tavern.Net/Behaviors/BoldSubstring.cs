using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Tavern.Net.Behaviors;

/// <summary>
/// Attached properties for a TextBlock showing a play-log line: <c>Prefix</c> + <c>Text</c>, with
/// the first occurrence of <c>Bold</c> inside Text (the card's name) rendered in bold. Inlines can't
/// be bound directly, so this rebuilds them whenever any of the three change. Usage:
/// <c>behaviors:BoldSubstring.Prefix="..." behaviors:BoldSubstring.Text="..." behaviors:BoldSubstring.Bold="..."</c>.
/// </summary>
public static class BoldSubstring
{
    public static readonly DependencyProperty PrefixProperty = DependencyProperty.RegisterAttached(
        "Prefix", typeof(string), typeof(BoldSubstring), new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(BoldSubstring), new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty BoldProperty = DependencyProperty.RegisterAttached(
        "Bold", typeof(string), typeof(BoldSubstring), new PropertyMetadata(null, OnChanged));

    /// <summary>Optional colors: <c>PrefixBrush</c> for the Prefix run ("Turn 3:"), <c>BoldBrush</c> for the bolded name.</summary>
    public static readonly DependencyProperty PrefixBrushProperty = DependencyProperty.RegisterAttached(
        "PrefixBrush", typeof(Brush), typeof(BoldSubstring), new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty BoldBrushProperty = DependencyProperty.RegisterAttached(
        "BoldBrush", typeof(Brush), typeof(BoldSubstring), new PropertyMetadata(null, OnChanged));

    public static void SetPrefixBrush(DependencyObject element, Brush? value) => element.SetValue(PrefixBrushProperty, value);
    public static Brush? GetPrefixBrush(DependencyObject element) => (Brush?)element.GetValue(PrefixBrushProperty);
    public static void SetBoldBrush(DependencyObject element, Brush? value) => element.SetValue(BoldBrushProperty, value);
    public static Brush? GetBoldBrush(DependencyObject element) => (Brush?)element.GetValue(BoldBrushProperty);

    public static void SetPrefix(DependencyObject element, string? value) => element.SetValue(PrefixProperty, value);
    public static string? GetPrefix(DependencyObject element) => (string?)element.GetValue(PrefixProperty);
    public static void SetText(DependencyObject element, string? value) => element.SetValue(TextProperty, value);
    public static string? GetText(DependencyObject element) => (string?)element.GetValue(TextProperty);
    public static void SetBold(DependencyObject element, string? value) => element.SetValue(BoldProperty, value);
    public static string? GetBold(DependencyObject element) => (string?)element.GetValue(BoldProperty);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock textBlock)
        {
            return;
        }

        var prefix = GetPrefix(textBlock) ?? "";
        var text = GetText(textBlock) ?? "";
        var bold = GetBold(textBlock);

        textBlock.Inlines.Clear();
        var prefixRun = new Run(prefix);
        if (GetPrefixBrush(textBlock) is { } prefixBrush)
        {
            prefixRun.Foreground = prefixBrush;
        }

        textBlock.Inlines.Add(prefixRun);

        var index = string.IsNullOrEmpty(bold) ? -1 : text.IndexOf(bold, StringComparison.Ordinal);
        if (index < 0)
        {
            textBlock.Inlines.Add(new Run(text));
            return;
        }

        textBlock.Inlines.Add(new Run(text[..index]));
        var boldRun = new Run(bold) { FontWeight = FontWeights.Bold };
        if (GetBoldBrush(textBlock) is { } boldBrush)
        {
            boldRun.Foreground = boldBrush;
        }

        textBlock.Inlines.Add(boldRun);
        textBlock.Inlines.Add(new Run(text[(index + bold!.Length)..]));
    }
}