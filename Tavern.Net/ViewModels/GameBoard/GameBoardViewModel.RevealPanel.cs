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

/// <summary>The Reveal panel (Actions > Reveal): dragging revealed cards out, Next, Top/Bottom.</summary>
public sealed partial class GameBoardViewModel
{
    // --- Reveal panel: the Actions menu's Reveal pulls the top N off Main into this panel (like
    // Glimpse, the cards are in no zone while it's open) and — online — shows them to the opponent.
    // From here you drag any card to any zone a Main card may go (the panel fades while you drag, so
    // the board's drop targets are reachable), "Next" pulls one more (for "reveal until X"), and
    // Top/Bottom put whatever is left back into Main — shuffled first if Randomize is on. The panel
    // is modal until the last card leaves it, like Glimpse, and has no Undo: a blind Main-deck
    // restore would duplicate any card already dragged out, and the opponent has seen the cards anyway.

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RevealNextCommand), nameof(RevealToTopCommand), nameof(RevealToBottomCommand))]
    private bool _isRevealPanelOpen;

    partial void OnIsRevealPanelOpenChanged(bool value)
    {
        if (value)
        {
            CloseActionsMenu();
        }
    }

    public ObservableCollection<CardViewModel> RevealPanelCards { get; } = new();

    /// <summary>True while a card is being dragged out of the Reveal panel — the panel goes nearly
    /// transparent and stops catching the mouse, so what's under it can receive the drop.</summary>
    [ObservableProperty]
    private bool _isDraggingFromRevealPanel;

    /// <summary>Armed by the panel's switch; only takes effect when Top or Bottom is clicked.</summary>
    [ObservableProperty]
    private bool _revealRandomize;

    // Everything revealed since the panel opened, in reveal order — including cards since dragged
    // out, since the opponent's view shows the whole run. Resent in full (same id, rising sequence)
    // on each "Next"; see OnlineMessage.RevealId.
    private readonly List<CardInstance> _revealedThisSession = new();
    private string _revealSessionId = "";
    private int _revealSessionSeq;

    private void StartRevealPanel(int count)
    {
        if (IsRevealPanelOpen)
        {
            return;
        }

        // A pending Undo (Mill/Bottom/...) would otherwise try to restore Main around cards that are
        // now sitting in the panel.
        DiscardUndo();

        var pulled = _session.GlimpseCards(Player, count);
        if (pulled.Count == 0)
        {
            return;
        }

        RevealRandomize = false;
        _revealedThisSession.Clear();
        _revealSessionId = Guid.NewGuid().ToString("N");
        _revealSessionSeq = 0;

        foreach (var card in pulled)
        {
            _revealedThisSession.Add(card);
            RevealPanelCards.Add(new CardViewModel(card, _apiClient, this));
        }

        Player.Stats.Log($"Revealed {pulled.Count} card(s) from the Main Deck.");
        IsRevealPanelOpen = true;
        SendRevealSession();
    }

    private void SendRevealSession()
    {
        if (!IsOnline || _revealedThisSession.Count == 0)
        {
            return;
        }

        _revealSessionSeq++;
        _ = _connection!.SendAsync(new OnlineMessage
        {
            Kind = OnlineMessageKind.RevealCards,
            RevealedCards = _revealedThisSession.Select(c => new RevealedCardEntry(c.Card.Slug, c.IsFlipped)).ToList(),
            RevealId = _revealSessionId,
            RevealSeq = _revealSessionSeq,
        });
    }

    private bool CanUseRevealPanel() => IsRevealPanelOpen;

    private bool CanRevealNext() => IsRevealPanelOpen && Player.GetZone(ZoneType.MainDeck).Cards.Count > 0;

    /// <summary>Reveals one more card off the top of Main into the panel and resends the whole set.</summary>
    [RelayCommand(CanExecute = nameof(CanRevealNext))]
    private void RevealNext()
    {
        var card = _session.GlimpseNextCard(Player);
        if (card is null)
        {
            return;
        }

        _revealedThisSession.Add(card);
        RevealPanelCards.Add(new CardViewModel(card, _apiClient, this));
        Player.Stats.Log($"Revealed {card.Card.Name} from the Main Deck.");
        SendRevealSession();
        RevealNextCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanUseRevealPanel))]
    private void RevealToTop() => FinishRevealPanel(toTop: true);

    [RelayCommand(CanExecute = nameof(CanUseRevealPanel))]
    private void RevealToBottom() => FinishRevealPanel(toTop: false);

    private void FinishRevealPanel(bool toTop)
    {
        var remaining = RevealPanelCards.Select(c => c.Instance).ToList();
        if (RevealRandomize)
        {
            // Fisher-Yates, on just what's left in the panel.
            for (var i = remaining.Count - 1; i > 0; i--)
            {
                var j = Random.Shared.Next(i + 1);
                (remaining[i], remaining[j]) = (remaining[j], remaining[i]);
            }
        }

        _session.ReturnRevealedCards(Player, remaining, toTop);
        CloseRevealPanel();
    }

    private void CloseRevealPanel()
    {
        RevealPanelCards.Clear();
        _revealedThisSession.Clear();
        IsDraggingFromRevealPanel = false;
        RevealRandomize = false;
        IsRevealPanelOpen = false;
    }

    /// <summary>A new game can't start with cards stranded outside every zone — anything still in the
    /// panel goes back on top of Main first (the reset then rebuilds Main from each card's home).</summary>
    private void AbortRevealPanel()
    {
        if (!IsRevealPanelOpen)
        {
            return;
        }

        _session.ReturnRevealedCards(Player, RevealPanelCards.Select(c => c.Instance).ToList(), toTop: true);
        CloseRevealPanel();
    }

    /// <summary>Called by CardDragBehavior right before a drag starts / after it ends — fades the
    /// Reveal panel out for the duration when the dragged card came from it.</summary>
    internal void BeginCardDrag(CardViewModel card) => IsDraggingFromRevealPanel = RevealPanelCards.Contains(card);

    internal void EndCardDrag() => IsDraggingFromRevealPanel = false;
}
