using Tavern.Net.Game;
using Tavern.Net.GameData.Models;
using Tavern.Net.ViewModels;

namespace Tavern.Net.Tests;

public class SideboardTests
{
    private static CardDto Card(string name, string element = "NORM", string type = "Ally", bool champion = false, double? level = null, bool regalia = false)
    {
        var types = new List<string> { type };
        if (champion) types = new List<string> { "Champion" };
        if (regalia) types = new List<string> { "Regalia" };
        return new CardDto
        {
            Name = name,
            Slug = name.ToLowerInvariant().Replace(' ', '-'),
            Elements = new List<string> { element },
            Types = types,
            Level = level,
        };
    }

    private static DeckArrangement SampleDeck() => new(
        registeredMain: new[] { Card("Wind Ally", "WIND"), Card("Norm Ally"), Card("Fire Action", "FIRE", "Action") },
        registeredMaterial: new[] { Card("Base Champ", "FIRE", champion: true, level: 0), Card("Some Regalia", regalia: true) },
        registeredSideboard: new[] { Card("Side Ally", "WATER"), Card("Side Regalia", regalia: true) });

    private static SideboardViewModel Panel(DeckArrangement deck, Action? onReady = null, Action? onCancel = null) =>
        new(deck, onReady ?? (() => { }), onCancel);

    [Fact]
    public void MainSorting_IsNormThenBasicThenAdvancedElements_ThenType()
    {
        var sorted = DeckSorting.OrderMain(new[]
        {
            Card("Umbra Thing", "UMBRA"),
            Card("Wind B", "WIND", "Ally"),
            Card("Norm Action", "NORM", "Action"),
            Card("Arcane Thing", "ARCANE"),
            Card("Fire Thing", "FIRE"),
            Card("Norm Ally", "NORM", "Ally"),
            Card("Water Thing", "WATER"),
        }).Select(c => c.Name).ToList();

        Assert.Equal(
            new[] { "Norm Action", "Norm Ally", "Fire Thing", "Water Thing", "Wind B", "Arcane Thing", "Umbra Thing" },
            sorted);
    }

    [Fact]
    public void MaterialSorting_PutsChampionsFirstInLevelOrder_WhateverTheirElements()
    {
        var sorted = DeckSorting.OrderMaterial(new[]
        {
            Card("Regalia", "FIRE", regalia: true),
            Card("Norm Level One", "NORM", champion: true, level: 1),
            Card("Umbra Level Two", "UMBRA", champion: true, level: 2),
            Card("Fire Level Zero", "FIRE", champion: true, level: 0),
            Card("Fire Level Three", "FIRE", champion: true, level: 3),
        }).Select(c => c.Name).ToList();

        // The Level 0 champion leads even though a Norm champion would outrank it by element alone.
        Assert.Equal(
            new[] { "Fire Level Zero", "Norm Level One", "Umbra Level Two", "Fire Level Three", "Regalia" },
            sorted);
    }

    [Fact]
    public void SideboardSorting_IsTypeThenElement()
    {
        var sorted = DeckSorting.OrderSideboard(new[]
        {
            Card("B Item", "FIRE", "Item"),
            Card("A Ally Wind", "WIND", "Ally"),
            Card("A Ally Norm", "NORM", "Ally"),
        }).Select(c => c.Name).ToList();

        Assert.Equal(new[] { "A Ally Norm", "A Ally Wind", "B Item" }, sorted);
    }

    [Theory]
    [InlineData(1, 0xF7, 0xFA, 0xFC)]
    [InlineData(2, 0x90, 0xCD, 0xF4)]
    [InlineData(3, 0xFA, 0xF0, 0x89)]
    [InlineData(4, 0xF6, 0xAD, 0x55)]
    [InlineData(7, 0xF6, 0xAD, 0x55)]
    public void CopyCountBrush_ScalesWhiteBlueYellowOrange_AndKeepsFourOrMoreTheSame(int count, byte r, byte g, byte b)
    {
        var brush = (System.Windows.Media.SolidColorBrush)new Tavern.Net.Converters.CopyCountToBrushConverter()
            .Convert(count, typeof(System.Windows.Media.Brush), null, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(System.Windows.Media.Color.FromRgb(r, g, b), brush.Color);
    }

    [Fact]
    public void Move_FromSideboardToMain_MovesOneCopy()
    {
        var deck = SampleDeck();
        var panel = Panel(deck);
        var sideAlly = panel.SideboardCards.Single(g => g.Name == "Side Ally");

        panel.MoveDeckCardCommand.Execute(new DeckMoveRequest(sideAlly, DeckListKind.Sideboard, DeckListKind.Main));

        Assert.Contains(deck.Main, c => c.Name == "Side Ally");
        Assert.DoesNotContain(deck.Sideboard, c => c.Name == "Side Ally");
        Assert.Equal(4, panel.MainCount);
        Assert.Equal(1, panel.SideboardCount);
    }

    [Fact]
    public void MaterialCards_CannotEnterMain_AndMainCardsCannotEnterMaterial()
    {
        var panel = Panel(SampleDeck());
        var regalia = panel.SideboardCards.Single(g => g.Name == "Side Regalia");
        var ally = panel.SideboardCards.Single(g => g.Name == "Side Ally");

        Assert.False(panel.MoveDeckCardCommand.CanExecute(new DeckMoveRequest(regalia, DeckListKind.Sideboard, DeckListKind.Main)));
        Assert.True(panel.MoveDeckCardCommand.CanExecute(new DeckMoveRequest(regalia, DeckListKind.Sideboard, DeckListKind.Material)));
        Assert.False(panel.MoveDeckCardCommand.CanExecute(new DeckMoveRequest(ally, DeckListKind.Sideboard, DeckListKind.Material)));
    }

    [Fact]
    public void DoubleClickQuickMove_GoesToTheCardsNaturalSide_AndBackToSideboard()
    {
        var deck = SampleDeck();
        var panel = Panel(deck);
        var regalia = panel.SideboardCards.Single(g => g.Name == "Side Regalia");

        panel.MoveDeckCardCommand.Execute(new DeckMoveRequest(regalia, DeckListKind.Sideboard, null));
        Assert.Contains(deck.Material, c => c.Name == "Side Regalia");

        var inMaterial = panel.MaterialCards.Single(g => g.Name == "Side Regalia");
        panel.MoveDeckCardCommand.Execute(new DeckMoveRequest(inMaterial, DeckListKind.Material, null));
        Assert.Contains(deck.Sideboard, c => c.Name == "Side Regalia");
    }

    private static SideboardViewModel PanelWithThreeCopies(out DeckArrangement deck)
    {
        var copy = Card("Triple Ally", "FIRE");
        deck = new DeckArrangement(
            registeredMain: new[] { Card("Norm Ally") },
            registeredMaterial: new[] { Card("Base Champ", champion: true, level: 0) },
            registeredSideboard: new[] { copy, copy, copy });
        return Panel(deck);
    }

    [Fact]
    public void DoubleClickOnAMultiCopyRow_AsksHowManyInsteadOfMovingOne()
    {
        var panel = PanelWithThreeCopies(out var deck);
        var row = panel.SideboardCards.Single(g => g.Name == "Triple Ally");

        panel.MoveDeckCardCommand.Execute(new DeckMoveRequest(row, DeckListKind.Sideboard, null));

        Assert.True(panel.IsPromptingMoveCount);
        Assert.Equal(3, panel.MoveCountMax);
        Assert.Equal(3, deck.Sideboard.Count);

        panel.MoveCount = 2;
        panel.ConfirmMoveCommand.Execute(null);

        Assert.False(panel.IsPromptingMoveCount);
        Assert.Equal(1, deck.Sideboard.Count);
        Assert.Equal(2, deck.Main.Count(c => c.Name == "Triple Ally"));
    }

    [Fact]
    public void MovePrompt_TypedDigitsClampToWhatIsAvailable_AndEscapeCancels()
    {
        var panel = PanelWithThreeCopies(out var deck);
        var row = panel.SideboardCards.Single(g => g.Name == "Triple Ally");
        panel.MoveDeckCardCommand.Execute(new DeckMoveRequest(row, DeckListKind.Sideboard, null));

        panel.HandleKey(System.Windows.Input.Key.D9);
        Assert.Equal(3, panel.MoveCount);

        panel.HandleKey(System.Windows.Input.Key.Escape);
        Assert.False(panel.IsPromptingMoveCount);
        Assert.Equal(3, deck.Sideboard.Count);
    }

    [Fact]
    public void DragDropToAnExplicitTarget_StillMovesASingleCopyWithoutPrompting()
    {
        var panel = PanelWithThreeCopies(out var deck);
        var row = panel.SideboardCards.Single(g => g.Name == "Triple Ally");

        panel.MoveDeckCardCommand.Execute(new DeckMoveRequest(row, DeckListKind.Sideboard, DeckListKind.Main));

        Assert.False(panel.IsPromptingMoveCount);
        Assert.Equal(2, deck.Sideboard.Count);
    }

    [Fact]
    public void Ready_IsBlockedWithoutALevelZeroChampion_AndRunsOtherwise()
    {
        var deck = SampleDeck();
        var readied = 0;
        var panel = Panel(deck, onReady: () => readied++);
        var champ = panel.MaterialCards.Single(g => g.Name == "Base Champ");

        panel.MoveDeckCardCommand.Execute(new DeckMoveRequest(champ, DeckListKind.Material, DeckListKind.Sideboard));
        Assert.False(panel.ReadyCommand.CanExecute(null));
        Assert.NotNull(panel.ValidationMessage);

        var inSideboard = panel.SideboardCards.Single(g => g.Name == "Base Champ");
        panel.MoveDeckCardCommand.Execute(new DeckMoveRequest(inSideboard, DeckListKind.Sideboard, DeckListKind.Material));
        Assert.True(panel.ReadyCommand.CanExecute(null));
        Assert.Null(panel.ValidationMessage);

        panel.ReadyCommand.Execute(null);
        Assert.Equal(1, readied);
    }

    [Fact]
    public void Reset_RestoresTheRegisteredDeck()
    {
        var deck = SampleDeck();
        var panel = Panel(deck);
        var sideAlly = panel.SideboardCards.Single(g => g.Name == "Side Ally");
        panel.MoveDeckCardCommand.Execute(new DeckMoveRequest(sideAlly, DeckListKind.Sideboard, DeckListKind.Main));

        panel.ResetCommand.Execute(null);

        Assert.DoesNotContain(panel.MainCards, g => g.Name == "Side Ally");
        Assert.Contains(panel.SideboardCards, g => g.Name == "Side Ally");
        Assert.Equal(3, panel.MainCount);
    }

    [Fact]
    public void WhileLocked_EditsAndResetAreBlocked_UntilUnlocked()
    {
        var panel = Panel(SampleDeck());
        var sideAlly = panel.SideboardCards.Single(g => g.Name == "Side Ally");
        var request = new DeckMoveRequest(sideAlly, DeckListKind.Sideboard, DeckListKind.Main);

        panel.IsLocked = true;
        Assert.False(panel.MoveDeckCardCommand.CanExecute(request));
        Assert.False(panel.ResetCommand.CanExecute(null));

        panel.IsLocked = false;
        Assert.True(panel.MoveDeckCardCommand.CanExecute(request));
        Assert.True(panel.ResetCommand.CanExecute(null));
    }

    [Fact]
    public void BackToMenu_IsOnlyOfferedWhenThereIsNothingToCancelBackTo()
    {
        Assert.True(new SideboardViewModel(SampleDeck(), () => { }, onCancel: null, onBackToMenu: () => { }).CanBackToMenu);
        Assert.False(new SideboardViewModel(SampleDeck(), () => { }, onCancel: () => { }, onBackToMenu: () => { }).CanBackToMenu);
        Assert.False(Panel(SampleDeck()).CanBackToMenu);
    }

    [Fact]
    public void Cancel_OnlyExistsWhenThereIsAGameToGoBackTo()
    {
        var cancelled = 0;
        Assert.False(Panel(SampleDeck()).CanCancel);

        var panel = Panel(SampleDeck(), onCancel: () => cancelled++);
        Assert.True(panel.CanCancel);
        panel.CancelCommand.Execute(null);
        Assert.Equal(1, cancelled);
    }

    [Fact]
    public void SortedMaterialOrder_IsWhatTheGameIsBuiltFrom()
    {
        var deck = SampleDeck();
        _ = Panel(deck);
        var player = new GameSession().AddPlayer("Solo");
        player.Deck = deck;

        Decklists.DeckSessionBuilder.ApplyArrangement(player);

        var material = player.GetZone(ZoneType.MaterialDeck).Cards;
        Assert.Equal("Base Champ", material[0].Card.Name);
        Assert.Equal(0, material[0].HomeOrder);
    }
}
