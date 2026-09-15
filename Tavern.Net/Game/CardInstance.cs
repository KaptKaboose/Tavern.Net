using CommunityToolkit.Mvvm.ComponentModel;
using Tavern.Net.GameData.Models;

namespace Tavern.Net.Game;

/// <summary>
/// One physical copy of a card as it exists in a game session. Multiple
/// instances can wrap the same <see cref="CardDto"/> (e.g. 4 copies of the
/// same card in a deck).
/// </summary>
public sealed partial class CardInstance : ObservableObject
{
    public Guid InstanceId { get; } = Guid.NewGuid();

    public CardDto Card { get; }

    /// <summary>
    /// Which starting deck this copy belongs to (MainDeck or MaterialDeck) and its position within
    /// that deck's original decklist order. Set once at import time and never changed afterward —
    /// GameSession.StartNewGame uses it to figure out where a card that's since wandered off to
    /// Hand/Field/Graveyard/etc. belongs when rebuilding the two decks for a fresh game.
    /// </summary>
    public ZoneType HomeZone { get; }

    public int HomeOrder { get; }

    [ObservableProperty]
    private bool _isTapped;

    /// <summary>Showing its other face (double-click) — a real flip for a double-faced card
    /// (e.g. a Fatestone), or just the generic card back for an ordinary one.</summary>
    [ObservableProperty]
    private bool _isFlipped;

    /// <summary>Position on the Field's freeform canvas. Meaningless outside the Field zone.</summary>
    [ObservableProperty]
    private double _fieldX;

    [ObservableProperty]
    private double _fieldY;

    /// <summary>
    /// A single generic +/- counter (buff, enlightenment, prep, whatever — the game has too many
    /// to model individually, so this is deliberately unlabeled; the player knows from context
    /// what it represents). Only meaningful on the Field or in the Champion zone — see
    /// GameSession.MoveCard, which resets this back to 0 the instant a card goes anywhere else.
    /// </summary>
    [ObservableProperty]
    private int _counter;

    // Status keywords: a fixed, small set backed by the token images in GameData/Images/Tokens —
    // written out individually (matching GameBoardView's own "fixed set of 6, write it out
    // directly" precedent for the turn-phase tracker) rather than a generic name/collection, since
    // unlike Counter these have real curated art rather than being arbitrary/freeform. Same
    // Field/Champion-only lifetime as Counter.
    [ObservableProperty]
    private bool _isEphemeral;

    [ObservableProperty]
    private bool _isIgnited;

    [ObservableProperty]
    private bool _isImbued;

    [ObservableProperty]
    private bool _isRanged;

    [ObservableProperty]
    private bool _isRooted;

    [ObservableProperty]
    private bool _isWarded;

    public CardInstance(CardDto card, ZoneType homeZone = ZoneType.MainDeck, int homeOrder = 0)
    {
        Card = card;
        HomeZone = homeZone;
        HomeOrder = homeOrder;
    }

    /// <summary>Clears the counter and every status — called whenever a card leaves the Field/
    /// Champion zone, or is flipped, per GameSession.MoveCard and GameSession.FlipCard.</summary>
    public void ResetCounterAndStatuses()
    {
        Counter = 0;
        IsEphemeral = false;
        IsIgnited = false;
        IsImbued = false;
        IsRanged = false;
        IsRooted = false;
        IsWarded = false;
    }

    /// <summary>Toggles one of the six fixed statuses by name (matching the token image file names,
    /// capitalized) — a single dispatch point so callers (GameSession.ToggleStatus) don't need
    /// their own copy of this switch.</summary>
    public void ToggleStatus(string name)
    {
        switch (name)
        {
            case "Ephemeral":
                IsEphemeral = !IsEphemeral;
                break;
            case "Ignited":
                IsIgnited = !IsIgnited;
                break;
            case "Imbued":
                IsImbued = !IsImbued;
                break;
            case "Ranged":
                IsRanged = !IsRanged;
                break;
            case "Rooted":
                IsRooted = !IsRooted;
                break;
            case "Warded":
                IsWarded = !IsWarded;
                break;
        }
    }
}
