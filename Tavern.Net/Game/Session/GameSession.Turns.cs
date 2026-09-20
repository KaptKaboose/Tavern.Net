using Tavern.Net.GameData.Models;

namespace Tavern.Net.Game;

/// <summary>Turn structure: the current phase, whose turn it is, and advancing through the phases.</summary>
public sealed partial class GameSession
{
    private static readonly TurnPhase[] PhaseOrder =
    {
        TurnPhase.WakeUp,
        TurnPhase.Materialization,
        TurnPhase.Recollection,
        TurnPhase.Draw,
        TurnPhase.Main,
        TurnPhase.End,
    };

    // Set (per player) by InitializePlayerForNewGame, consumed by that player's very first
    // AdvancePhase call afterward, whenever it happens to come (immediately for whoever goes first;
    // after their own first End-of-turn handoff arrives for whoever goes second, in an online game).
    private readonly HashSet<Player> _pendingFirstTurnFastForward = new();

    // Which phase _pendingFirstTurnFastForward's consumption should jump straight to for a given
    // player — Main (skip Recollection+Draw) for whoever is actually the game's first mover, Draw
    // (skip only Recollection) for whoever's first turn arrives via a handoff from someone else's
    // turn. Deliberately NOT inferred from TurnCount at consumption time: TurnCount must stay 0
    // for a player's entire first turn (online or not) so it still displays as "Turn 1" rather than
    // "Turn 2" — see GameBoardViewModel.ApplyRemoteGameState, which is why this can no longer double
    // as the "have I gone before" signal the way it briefly did. Set explicitly by StartNewGame
    // (solo) and by the online lobby (via SetFirstTurnTarget) once the agreed first player is known;
    // removed the moment AdvancePhase consumes it.
    private readonly Dictionary<Player, TurnPhase> _firstTurnTarget = new();

    public TurnPhase CurrentPhase { get; private set; } = TurnPhase.WakeUp;

    /// <summary>Whose turn it currently is — the one player (in an online game) allowed to advance
    /// the shared phase; irrelevant for solo, where it's always the only player. Defaults to the
    /// first player added. See AdvancePhase's End-of-turn handoff for where this changes.</summary>
    public Player? ActivePlayer { get; private set; }

    /// <summary>Sets the current phase directly, without running WakeUp/Recollect/Draw's entry
    /// actions the way SetPhase does — used only by GameSessionSerializer.RestoreAsync, where those
    /// actions already happened and are already reflected in the restored zones/stats.</summary>
    internal void SetPhaseForRestore(TurnPhase phase) => CurrentPhase = phase;

    /// <summary>Applies a shared phase/active-player update received from the network — used only
    /// by the passive side of an online game, mirroring whatever the active side already computed
    /// and ran SetPhase's own side effects for locally. No side effects re-run here: the active
    /// side's own broadcast PlayerState already reflects their WakeUp/Recollect/Draw outcome.</summary>
    internal void ApplyRemoteGameState(TurnPhase phase, Player activePlayer)
    {
        CurrentPhase = phase;
        ActivePlayer = activePlayer;
    }

    /// <summary>Whether <paramref name="player"/> hasn't taken their very first turn yet — true from
    /// InitializePlayerForNewGame until their own first AdvancePhase call consumes it. Used online
    /// (GameBoardViewModel.ApplyRemoteGameState) to tell whether becoming newly active is genuinely
    /// a player's first-ever turn (land on Materialization, don't bump TurnCount) or an ordinary
    /// later one (land on WakeUp for real, with its untap effect, and bump TurnCount as usual).</summary>
    public bool IsAwaitingFirstTurn(Player player) => _pendingFirstTurnFastForward.Contains(player);

    /// <summary>Registers which phase <paramref name="player"/>'s own first AdvancePhase call should
    /// jump straight to — see _firstTurnTarget's own comment. Used by the online lobby once the
    /// agreed first player is known; StartNewGame (solo) sets this for itself directly.</summary>
    public void SetFirstTurnTarget(Player player, TurnPhase target) => _firstTurnTarget[player] = target;

    public void NextTurn(Player player)
    {
        if (!player.Stats.PlayedCardThisTurn)
        {
            player.Stats.DeadTurnsCount++;
        }

        player.Stats.PlayedCardThisTurn = false;

        player.Stats.TurnCount++;
        player.Stats.Log("New turn.");
        RecordMajorEvent(player, $"Turn {player.Stats.TurnCount + 1} started.");
    }

    /// <summary>
    /// Advances to the next phase of <paramref name="player"/>'s turn, running whichever automatic
    /// behavior belongs to the phase being entered. Advancing past End hands the turn to the next
    /// player in <see cref="Players"/> (wrapping around) and starts their turn at WakeUp — for solo
    /// (one player), that next player is always the same one, so this is a no-op change from the
    /// old goldfish-only behavior; for an online 2-player game it correctly alternates.
    /// </summary>
    public void AdvancePhase(Player player)
    {
        // Fires exactly once per player, on their first AdvancePhase call after being initialized
        // for a new game — see _pendingFirstTurnFastForward's own comment. Which phase it jumps to
        // is looked up from _firstTurnTarget, set explicitly back when this player was landed on
        // Materialization in the first place (StartNewGame for solo, the online lobby via
        // SetFirstTurnTarget for online) rather than inferred here — TurnCount can't be used for
        // that anymore now that it's kept at 0 for a player's *entire* first turn, online included.
        if (_pendingFirstTurnFastForward.Remove(player))
        {
            var target = _firstTurnTarget.Remove(player, out var explicitTarget) ? explicitTarget : TurnPhase.Main;
            SetPhase(target, player);
            return;
        }

        var nextIndex = Array.IndexOf(PhaseOrder, CurrentPhase) + 1;
        if (nextIndex >= PhaseOrder.Length)
        {
            var nextPlayer = Players[(Players.IndexOf(player) + 1) % Players.Count];
            NextTurn(nextPlayer);
            ActivePlayer = nextPlayer;
            SetPhase(PhaseOrder[0], nextPlayer);
            return;
        }

        SetPhase(PhaseOrder[nextIndex], player);
    }

    /// <summary>Untaps every card on the Field. Runs automatically at the start of the Wake Up phase.</summary>
    public void WakeUp(Player player)
    {
        foreach (var card in player.GetZone(ZoneType.Field).Cards)
        {
            card.IsTapped = false;
        }

        foreach (var card in player.GetZone(ZoneType.Champion).Cards)
        {
            card.IsTapped = false;
        }

        player.Stats.Log("Wake Up: untapped the Field.");
    }

    /// <summary>
    /// Returns every card in Memory to Hand. Runs automatically at the start of the Recollection
    /// phase, but is also its own public method so a future card effect can call it independently
    /// of the phase it happens to be resolved in.
    /// </summary>
    public void Recollect(Player player)
    {
        var memory = player.GetZone(ZoneType.Memory);
        foreach (var card in memory.Cards.ToList())
        {
            MoveCard(player, card, ZoneType.Memory, ZoneType.Hand);
        }
    }
}
