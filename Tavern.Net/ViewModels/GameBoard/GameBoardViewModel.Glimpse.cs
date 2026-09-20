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

/// <summary>Glimpse: pulling the top of Main into a sortable overlay.</summary>
public sealed partial class GameBoardViewModel
{
    /// <summary>
    /// Glimpsing state — self-contained ViewModel-side lists, not domain zones, since a glimpsed
    /// card is genuinely removed from Main until <see cref="FinishGlimpse"/> puts every one of
    /// them back (see GameSession.GlimpseNextCard/FinishGlimpse). Staging holds cards drawn by 'G'
    /// that haven't been sorted yet; Top/Bottom are the two sortable piles they get dragged into.
    /// </summary>
    [ObservableProperty]
    private bool _isGlimpsing;

    public ObservableCollection<CardViewModel> GlimpseStaging { get; } = new();
    public ObservableCollection<CardViewModel> GlimpseTop { get; } = new();
    public ObservableCollection<CardViewModel> GlimpseBottom { get; } = new();

    // Captured the instant a glimpse starts pulling cards off Main, but not shown as an available
    // Undo until FinishGlimpse actually commits (see MoveGlimpseCard) — while still sorting, the
    // player has full visibility/control over Staging/Top/Bottom already, so surfacing "Undo" that
    // early is just confusing, and clicking it mid-sort would restore Main out from under cards
    // still sitting in Staging.
    private List<CardInstance>? _pendingGlimpseUndoSnapshot;

    /// <summary>Glimpse action: pulls up to count cards off the top of Main in one batch and opens
    /// the overlay already populated — replaces the old repeat-G-to-add-one flow now that Undo
    /// makes a mis-counted glimpse recoverable. Stops early, same as GameSession.GlimpseCards, once
    /// Main is empty.</summary>
    private void GlimpseCardsForChord(int count)
    {
        _pendingGlimpseUndoSnapshot = Player.GetZone(ZoneType.MainDeck).Cards.ToList();
        var glimpsed = _session.GlimpseCards(Player, count);
        if (glimpsed.Count == 0)
        {
            _pendingGlimpseUndoSnapshot = null;
            return;
        }

        IsGlimpsing = true;
        foreach (var card in glimpsed)
        {
            GlimpseStaging.Add(new CardViewModel(card, _apiClient, this));
        }
    }

    /// <summary>
    /// Handles every drag within the Glimpse overlay: removes the card from whichever of the
    /// three lists currently holds it, then inserts it into the target list — at InsertBefore's
    /// position if given, otherwise at the end. Auto-finishes (and closes) the instant Staging
    /// empties out, since that means every drawn card has been sorted.
    /// </summary>
    [RelayCommand]
    private void MoveGlimpseCard(GlimpseDropRequest? request)
    {
        if (request is null || request.Card == request.InsertBefore)
        {
            return;
        }

        GlimpseStaging.Remove(request.Card);
        GlimpseTop.Remove(request.Card);
        GlimpseBottom.Remove(request.Card);

        var targetList = request.Target == GlimpseTarget.Top ? GlimpseTop : GlimpseBottom;
        var insertIndex = request.InsertBefore is not null ? targetList.IndexOf(request.InsertBefore) : -1;
        if (insertIndex < 0)
        {
            targetList.Add(request.Card);
        }
        else
        {
            targetList.Insert(insertIndex, request.Card);
        }

        if (GlimpseStaging.Count == 0)
        {
            _session.FinishGlimpse(Player, GlimpseTop.Select(c => c.Instance).ToList(), GlimpseBottom.Select(c => c.Instance).ToList());
            GlimpseTop.Clear();
            GlimpseBottom.Clear();
            IsGlimpsing = false;

            if (_drawAfterGlimpsing)
            {
                _drawAfterGlimpsing = false;
                _session.DrawStartingHand(Player);
            }

            // Only the Glimpse action's own chord takes this snapshot — the very first, opening-hand
            // glimpse (triggered by New Game, not the player) has none, and shouldn't offer Undo.
            if (_pendingGlimpseUndoSnapshot is { } snapshot)
            {
                _pendingGlimpseUndoSnapshot = null;
                ArmUndo("Glimpse", () => _session.RestoreMainDeckOrder(Player, snapshot));
            }
        }
    }
}
