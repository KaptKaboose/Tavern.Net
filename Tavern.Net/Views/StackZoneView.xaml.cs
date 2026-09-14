using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Tavern.Net.Game;
using Tavern.Net.ViewModels;

namespace Tavern.Net.Views;

/// <summary>
/// A collapsed "pile" widget (just a card count) for zones that don't need every
/// card visible at once — Material Deck, Main Deck, Graveyard, Banishment. Clicking
/// it asks the board (via <see cref="PeekCommand"/>) to fan the actual cards out in its
/// screen-wide peek overlay (draggable, right-clickable, scrollable, using the same
/// CardTemplate as everywhere else).
/// </summary>
public partial class StackZoneView : UserControl
{
    public static readonly DependencyProperty ZoneProperty =
        DependencyProperty.Register(nameof(Zone), typeof(ZoneViewModel), typeof(StackZoneView));

    public ZoneViewModel? Zone
    {
        get => (ZoneViewModel?)GetValue(ZoneProperty);
        set => SetValue(ZoneProperty, value);
    }

    public static readonly DependencyProperty HeaderTextProperty =
        DependencyProperty.Register(nameof(HeaderText), typeof(string), typeof(StackZoneView));

    public string? HeaderText
    {
        get => (string?)GetValue(HeaderTextProperty);
        set => SetValue(HeaderTextProperty, value);
    }

    public static readonly DependencyProperty ShowBorderProperty =
        DependencyProperty.Register(nameof(ShowBorder), typeof(bool), typeof(StackZoneView), new PropertyMetadata(false));

    public bool ShowBorder
    {
        get => (bool)GetValue(ShowBorderProperty);
        set => SetValue(ShowBorderProperty, value);
    }

    public static readonly DependencyProperty IsSidewaysProperty =
        DependencyProperty.Register(nameof(IsSideways), typeof(bool), typeof(StackZoneView), new PropertyMetadata(false));

    public bool IsSideways
    {
        get => (bool)GetValue(IsSidewaysProperty);
        set => SetValue(IsSidewaysProperty, value);
    }

    /// <summary>True for a face-down pile (Material/Main), which shows a generic card back instead
    /// of the top card's own artwork.</summary>
    public static readonly DependencyProperty IsFaceDownProperty =
        DependencyProperty.Register(nameof(IsFaceDown), typeof(bool), typeof(StackZoneView), new PropertyMetadata(false));

    public bool IsFaceDown
    {
        get => (bool)GetValue(IsFaceDownProperty);
        set => SetValue(IsFaceDownProperty, value);
    }

    public static readonly DependencyProperty TargetZoneProperty =
        DependencyProperty.Register(nameof(TargetZone), typeof(ZoneType?), typeof(StackZoneView));

    public ZoneType? TargetZone
    {
        get => (ZoneType?)GetValue(TargetZoneProperty);
        set => SetValue(TargetZoneProperty, value);
    }

    public static readonly DependencyProperty MoveCommandProperty =
        DependencyProperty.Register(nameof(MoveCommand), typeof(ICommand), typeof(StackZoneView));

    public ICommand? MoveCommand
    {
        get => (ICommand?)GetValue(MoveCommandProperty);
        set => SetValue(MoveCommandProperty, value);
    }

    public static readonly DependencyProperty PeekCommandProperty =
        DependencyProperty.Register(nameof(PeekCommand), typeof(ICommand), typeof(StackZoneView));

    public ICommand? PeekCommand
    {
        get => (ICommand?)GetValue(PeekCommandProperty);
        set => SetValue(PeekCommandProperty, value);
    }

    public StackZoneView()
    {
        InitializeComponent();
    }

    private void OnPileClicked(object sender, MouseButtonEventArgs e)
    {
        if (Zone is null || Zone.Cards.Count == 0)
        {
            return;
        }

        var request = new PeekedZoneInfo(Zone, HeaderText ?? Zone.Type.ToString());
        if (PeekCommand is not null && PeekCommand.CanExecute(request))
        {
            PeekCommand.Execute(request);
        }
    }
}
