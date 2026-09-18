using Tavern.Net.GameData.Models;

namespace Tavern.Net.Game;

/// <summary>
/// One player's deck as a set of card lists rather than live zones: the Main deck, the Material
/// deck, and the sideboard — plus the "registered" arrangement (what the deck looked like when it
/// was imported) so a sideboarding session can always be reset back to it. Deliberately NOT part of
/// the player's zones: the sideboard isn't on the board, is never shown to the opponent, and isn't
/// broadcast (see GameSessionSerializer.CapturePlayer's includeDeck) — only a whole-game save
/// carries it. Each copy of a card is its own list entry (quantities expanded), matching how the
/// live zones hold them.
///
/// A "session" here is every game played without leaving for the menu; the current lists persist
/// from one game to the next, and <see cref="Reset"/> puts them back to the registered deck.
/// </summary>
public sealed class DeckArrangement
{
    public IReadOnlyList<CardDto> RegisteredMain { get; }

    public IReadOnlyList<CardDto> RegisteredMaterial { get; }

    public IReadOnlyList<CardDto> RegisteredSideboard { get; }

    public List<CardDto> Main { get; }

    public List<CardDto> Material { get; }

    public List<CardDto> Sideboard { get; }

    /// <param name="currentMain">Null (the default for a freshly imported deck) means "same as
    /// registered"; a save being restored mid-sideboarding passes its own current lists.</param>
    public DeckArrangement(
        IEnumerable<CardDto> registeredMain,
        IEnumerable<CardDto> registeredMaterial,
        IEnumerable<CardDto> registeredSideboard,
        IEnumerable<CardDto>? currentMain = null,
        IEnumerable<CardDto>? currentMaterial = null,
        IEnumerable<CardDto>? currentSideboard = null)
    {
        RegisteredMain = registeredMain.ToList();
        RegisteredMaterial = registeredMaterial.ToList();
        RegisteredSideboard = registeredSideboard.ToList();

        Main = (currentMain ?? RegisteredMain).ToList();
        Material = (currentMaterial ?? RegisteredMaterial).ToList();
        Sideboard = (currentSideboard ?? RegisteredSideboard).ToList();
    }

    /// <summary>StartNewGame materializes the Level 0 champion from Material as its very first
    /// step, so a Material list without one can't start a game — the one deck-legality rule this app
    /// enforces (every other rule is left to the players' good faith).</summary>
    public bool HasLevelZeroChampion => Material.Any(card => card.IsChampion && card.Level == 0);

    /// <summary>Every distinct card referenced anywhere in this arrangement — registered or current.</summary>
    public IEnumerable<CardDto> AllCards =>
        RegisteredMain.Concat(RegisteredMaterial).Concat(RegisteredSideboard)
            .Concat(Main).Concat(Material).Concat(Sideboard);

    /// <summary>Puts the current lists back to exactly the registered deck.</summary>
    public void Reset()
    {
        Main.Clear();
        Main.AddRange(RegisteredMain);
        Material.Clear();
        Material.AddRange(RegisteredMaterial);
        Sideboard.Clear();
        Sideboard.AddRange(RegisteredSideboard);
    }
}
