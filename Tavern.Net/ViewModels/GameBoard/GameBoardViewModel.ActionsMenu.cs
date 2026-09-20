using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tavern.Net.Game;
using Tavern.Net.GameData;
using Tavern.Net.GameData.Models;
using Tavern.Net.Online;

namespace Tavern.Net.ViewModels;

/// <summary>The Actions menu: choosing Banish/Reveal/Glimpse/Mill/Give/Shuffle and a count.</summary>
public sealed partial class GameBoardViewModel
{
    // --- Actions menu: click the header's Actions button, pick Banish/Reveal/Glimpse/Mill/Give
    // (Reveal/Give hidden solo — see CanUseOnlineAction), then set a count and confirm. Replaces
    // the old keyboard arm-then-digit chords (B/R/P/M/G) entirely — a visible menu instead of an
    // invisible armed state, and a real counter instead of a single 1-9 keypress, since some of
    // these (Glimpse especially) genuinely need double-digit counts.

    /// <summary>Which action is currently selected for count entry — None means the menu is still
    /// showing the top-level list (see IsShowingActionsList/IsEnteringActionCount).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShowingActionsList), nameof(IsEnteringActionCount), nameof(SelectedActionLabel))]
    private ArmedChord _selectedAction;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShowingActionsList), nameof(IsEnteringActionCount))]
    private bool _isActionsMenuOpen;

    [ObservableProperty]
    private int _actionCount = 1;

    // Whether the count still reads its opening default (1) or the player has actually typed a
    // digit yet — decides whether the next digit *replaces* it (so typing "5" reads 5, not 15) or
    // *appends* to it (so typing "1" then "0" reads 10). Reset every time a new action is selected.
    private bool _hasTypedActionCount;

    public bool IsShowingActionsList => IsActionsMenuOpen && SelectedAction == ArmedChord.None;

    public bool IsEnteringActionCount => IsActionsMenuOpen && SelectedAction != ArmedChord.None;

    public string SelectedActionLabel => SelectedAction switch
    {
        ArmedChord.Banish => "Banish",
        ArmedChord.Reveal => "Reveal",
        ArmedChord.Give => "Give",
        ArmedChord.Mill => "Mill",
        ArmedChord.Glimpse => "Glimpse",
        ArmedChord.Shuffle => "Shuffle",
        _ => "",
    };

    /// <summary>Give is online-only — solo has no opponent to give to, so the menu hides it there
    /// rather than showing a permanently-disabled option. (Reveal works solo too: the panel is useful
    /// on its own; there is just no opponent to show it to.)</summary>
    public bool CanUseOnlineAction => IsOnline;

    [RelayCommand]
    private void OpenActionsMenu()
    {
        IsActionsMenuOpen = true;
        SelectedAction = ArmedChord.None;
    }

    [RelayCommand]
    private void SelectAction(ArmedChord action)
    {
        // Shuffle takes no count — run it immediately rather than sending the player through the
        // count-entry view for nothing.
        if (action == ArmedChord.Shuffle)
        {
            CloseActionsMenu();
            RunAction(action, 0);
            return;
        }

        SelectedAction = action;
        ActionCount = 1;
        _hasTypedActionCount = false;
    }

    [RelayCommand]
    private void BackToActionsList() => SelectedAction = ArmedChord.None;

    [RelayCommand]
    private void CloseActionsMenu()
    {
        IsActionsMenuOpen = false;
        SelectedAction = ArmedChord.None;
    }

    [RelayCommand]
    private void IncreaseActionCount() => ActionCount = Math.Min(ActionCount + 1, 99);

    [RelayCommand]
    private void DecreaseActionCount() => ActionCount = Math.Max(ActionCount - 1, 1);

    [RelayCommand]
    private void ConfirmAction()
    {
        var action = SelectedAction;
        var count = ActionCount;
        CloseActionsMenu();
        RunAction(action, count);
    }

    private void RunAction(ArmedChord action, int count)
    {
        switch (action)
        {
            case ArmedChord.Banish:
                _session.Banish(Player, count);
                break;
            case ArmedChord.Mill:
                var milled = _session.Mill(Player, count);
                ArmUndo("Mill", () =>
                {
                    var graveyard = Player.GetZone(ZoneType.Graveyard);
                    foreach (var card in milled)
                    {
                        graveyard.Cards.Remove(card);
                    }

                    // Insert back-to-front so milled[0] (the original top card) ends up on top
                    // again — ObservableCollection has no InsertRange.
                    var deck = Player.GetZone(ZoneType.MainDeck);
                    for (var i = milled.Count - 1; i >= 0; i--)
                    {
                        deck.Cards.Insert(0, milled[i]);
                    }
                });
                break;
            case ArmedChord.Glimpse:
                GlimpseCardsForChord(count);
                break;
            case ArmedChord.Reveal:
                StartRevealPanel(count);
                break;
            case ArmedChord.Give:
                ArmGiveBlind(count);
                break;
            case ArmedChord.Shuffle:
                var beforeShuffle = Player.GetZone(ZoneType.MainDeck).Cards.ToList();
                _session.Shuffle(Player);
                ArmUndo("Shuffle", () => _session.RestoreMainDeckOrder(Player, beforeShuffle));
                break;
        }
    }
}
