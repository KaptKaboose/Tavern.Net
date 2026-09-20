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

/// <summary>Online plumbing: incoming messages, applying the opponent's state, whose turn it is.</summary>
public sealed partial class GameBoardViewModel
{
    /// <summary>Fires every ~300ms while connected — broadcasts this player's full current state.
    /// Unconditional (not just on change) so this doubles as the connection's keepalive; see
    /// GameConnection's own doc comment.</summary>
    private void OnBroadcastTick()
    {
        var state = GameSessionSerializer.CapturePlayer(Player);
        _ = _connection!.SendAsync(new OnlineMessage { Kind = OnlineMessageKind.PlayerState, PlayerState = state });
    }

    private void OnMessageReceived(OnlineMessage message)
    {
        switch (message.Kind)
        {
            case OnlineMessageKind.PlayerState when message.PlayerState is not null:
                _ = ApplyOpponentStateAsync(message.PlayerState);
                break;
            case OnlineMessageKind.GameState when message.Phase is not null && message.ActivePlayerNumber is not null:
                ApplyRemoteGameState(message.Phase.Value, message.ActivePlayerNumber.Value);
                break;
            case OnlineMessageKind.RevealCards when message.RevealedCards is { Count: > 0 } entries:
                _ = EnqueueRevealAsync(entries, message.RevealId, message.RevealSeq);
                break;
            case OnlineMessageKind.TransferCard when message.TransferCardSlugs is { Count: > 0 } slugs && message.TransferTargetZone is not null:
                _ = ReceiveTransferAsync(slugs, message.TransferTargetZone.Value, message.TransferSourceLabel);
                break;
            case OnlineMessageKind.UndoNotice:
                ShowToast(message.UndoActionLabel is { } label ? $"Opponent used Undo ({label})." : "Opponent used Undo.");
                break;
            case OnlineMessageKind.NewGameRequest:
                ReceiveNewGameRequest();
                break;
            case OnlineMessageKind.NewGameAgree:
                ReceiveNewGameAgree();
                break;
            case OnlineMessageKind.NewGameReady:
                IsOpponentNewGameReady = message.Ready;
                break;
            case OnlineMessageKind.NewGameStart:
                PerformOnlineNewGame(message.FirstPlayerNumber);
                break;
            case OnlineMessageKind.NewGameCancel:
                if (IsNewGameAgreementOpen || SideboardPanel is not null)
                {
                    ShowToast("Opponent cancelled the new game.");
                }

                CloseNewGameAgreement();
                CloseNewGameReadyUp();
                break;
        }
    }

    /// <summary>Resolves a slug to a CardDto for a card that isn't (and for Reveal, never will be)
    /// in any local zone — reuses the same per-connection cache ApplyOpponentStateAsync already
    /// keeps warm, since Reveal/Give overwhelmingly reference cards the opponent's own broadcasts
    /// have already resolved once.</summary>
    private async Task<CardDto> ResolveOpponentCardAsync(string slug)
    {
        if (_opponentCardCache.TryGetValue(slug, out var cached))
        {
            return cached;
        }

        var card = await _apiClient.GetCardBySlugAsync(slug)
            ?? throw new InvalidOperationException($"Couldn't resolve \"{slug}\" for a Reveal/Give.");
        _opponentCardCache[slug] = card;
        return card;
    }

    // Only the newest opponent state matters, and applying one is async (it clears and rebuilds
    // their whole mirror, resolving any card not yet cached) — so a state arriving mid-apply is
    // parked here and applied next, rather than starting a second apply that would interleave with
    // the first and leave the mirror half of each.
    private SavedPlayer? _pendingOpponentState;
    private bool _isApplyingOpponentState;

    private async Task ApplyOpponentStateAsync(SavedPlayer state)
    {
        if (OpponentPlayer is null)
        {
            return;
        }

        _pendingOpponentState = state;
        if (_isApplyingOpponentState)
        {
            return;
        }

        _isApplyingOpponentState = true;
        try
        {
            while (_pendingOpponentState is { } next)
            {
                _pendingOpponentState = null;
                await GameSessionSerializer.ApplyToPlayer(OpponentPlayer, next, _apiClient, _opponentCardCache);
                RefreshOpponentPanel();
            }
        }
        finally
        {
            _isApplyingOpponentState = false;
        }
    }

    /// <summary>Applies a GameState update from the active side of the handoff. The sender always
    /// broadcasts WakeUp as the landing phase (its own mirror of me never tracks whether this is
    /// genuinely my first turn — only my own authoritative session does), so when this makes ME
    /// newly active, MY session's own first-turn bookkeeping decides what actually happens here —
    /// see the two branches below.</summary>
    private void ApplyRemoteGameState(TurnPhase phase, int activePlayerNumber)
    {
        var activePlayer = activePlayerNumber == Player.PlayerNumber ? Player : OpponentPlayer;
        if (activePlayer is null)
        {
            return;
        }

        // True when this side lands on a different phase than the one the sender broadcast (the
        // first-turn skip below) — the sender is still showing its own WakeUp and has to be told.
        var landedOnDifferentPhase = false;

        if (activePlayer == Player && _session.ActivePlayer != Player)
        {
            if (_session.IsAwaitingFirstTurn(Player))
            {
                // Genuinely my first turn ever — land on Materialization instead of the sender's
                // broadcast WakeUp, and don't bump TurnCount, so this whole turn still displays as
                // "Turn 1" rather than "Turn 2". My own next AdvancePhase call (GameSession's own
                // fast-forward branch) handles skipping straight to Draw from here.
                phase = TurnPhase.Materialization;
                landedOnDifferentPhase = true;
            }
            else
            {
                // The handoff's own NextTurn call (inside GameSession.AdvancePhase, on the departing
                // side) only ever runs against that side's own mirror of me — inert, since my own
                // broadcasts overwrite it anyway. So this is the one place MY OWN TurnCount/
                // DeadTurnsCount/"Turn X started" log — and WakeUp's untap, which the departing
                // side's own SetPhase call likewise only ever ran against that same inert mirror —
                // actually happens for my own authoritative Player.
                _session.NextTurn(Player);
                _session.WakeUp(Player);
            }
        }

        _session.ApplyRemoteGameState(phase, activePlayer);
        CurrentPhase = _session.CurrentPhase;
        UpdateIsMyTurn();

        // The passive side normally never answers a GameState, but here the sender's tracker would
        // otherwise keep reading WakeUp until this side's first advance — while this side already
        // skipped ahead. Reply once with where this side actually landed; the sender just mirrors it
        // (I am not newly active from its point of view, so it does not answer back).
        if (landedOnDifferentPhase && IsOnline)
        {
            _ = _connection!.SendAsync(new OnlineMessage
            {
                Kind = OnlineMessageKind.GameState,
                Phase = _session.CurrentPhase,
                ActivePlayerNumber = activePlayer.PlayerNumber,
            });
        }
    }

    /// <summary>Recomputes IsMyTurn from GameSession's shared ActivePlayer and, only on an actual
    /// transition, auto-toggles the opponent panel — open when it just became their turn, closed
    /// when it just became mine. A manual toggle (see ToggleOpponentPanelCommand) still works
    /// independently of this at any other time.</summary>
    private void UpdateIsMyTurn()
    {
        var wasMyTurn = IsMyTurn;
        IsMyTurn = !IsOnline || _session.ActivePlayer == Player;

        if (IsOnline && wasMyTurn != IsMyTurn)
        {
            IsOpponentPanelOpen = !IsMyTurn;
        }
    }

    private void OnConnectionLost() => ConnectionLostMessage = "Connection to your opponent was lost.";
}
