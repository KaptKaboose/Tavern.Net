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

/// <summary>Give: sending a card (or a blind top-N) to the opponent, and receiving one.</summary>
public sealed partial class GameBoardViewModel
{
    // --- Give: self-initiated, no request/approval handshake — whoever's card effect calls for a
    // transfer is the one who reads it and performs it themselves, same "manual bookkeeping, no
    // rules engine" philosophy as every other action in this app. Both entry points (a specific
    // card via the Zoom overlay, or a blind top-N off Main via the 'P' chord) open the same small
    // Field/Sealed target-picker overlay; nothing actually moves until one of those two is clicked.

    private CardViewModel? _giveCandidateCard;
    private int? _giveBlindCount;

    [ObservableProperty]
    private bool _isGiveTargetPickerOpen;

    partial void OnIsGiveTargetPickerOpenChanged(bool value)
    {
        if (value)
        {
            CloseActionsMenu();
        }
    }

    public bool CanGiveZoomedCard => IsOnline && ZoomedCard is not null;

    [RelayCommand(CanExecute = nameof(CanGiveZoomedCard))]
    private void OpenGiveTargetPickerForZoomedCard()
    {
        if (ZoomedCard is null)
        {
            return;
        }

        _giveCandidateCard = ZoomedCard;
        _giveBlindCount = null;
        ZoomedCard = null;
        IsGiveTargetPickerOpen = true;
    }

    private void ArmGiveBlind(int count)
    {
        if (!IsOnline)
        {
            return;
        }

        _giveCandidateCard = null;
        _giveBlindCount = count;
        IsGiveTargetPickerOpen = true;
    }

    [RelayCommand]
    private void ConfirmGiveToField() => ConfirmGive(ZoneType.Field);

    [RelayCommand]
    private void ConfirmGiveToSealed() => ConfirmGive(ZoneType.Sealed);

    [RelayCommand]
    private void CancelGive()
    {
        IsGiveTargetPickerOpen = false;
        _giveCandidateCard = null;
        _giveBlindCount = null;
    }

    private void ConfirmGive(ZoneType destination)
    {
        IsGiveTargetPickerOpen = false;

        List<CardInstance> given;
        string sourceLabel;

        if (_giveCandidateCard is { } single)
        {
            var from = Player.Zones.First(kv => kv.Value.Cards.Contains(single.Instance)).Key;
            _session.RemoveCardForGive(Player, single.Instance, from);
            given = new List<CardInstance> { single.Instance };
            sourceLabel = from.ToString();
        }
        else if (_giveBlindCount is { } count)
        {
            // Deliberately no Undo entry here — by the time this shows, the TransferCard message
            // below has already reached the opponent, so a local-only undo would duplicate the
            // cards instead of actually taking them back (see the Undo section's own comment).
            given = _session.TakeTopCardsForGive(Player, count);
            sourceLabel = "Main Deck (blind)";
        }
        else
        {
            given = new List<CardInstance>();
            sourceLabel = "";
        }

        if (given.Count > 0 && IsOnline)
        {
            _ = _connection!.SendAsync(new OnlineMessage
            {
                Kind = OnlineMessageKind.TransferCard,
                TransferCardSlugs = given.Select(c => c.Card.Slug).ToList(),
                TransferTargetZone = destination,
                TransferSourceLabel = sourceLabel,
            });
        }

        _giveCandidateCard = null;
        _giveBlindCount = null;
    }

    private async Task ReceiveTransferAsync(IReadOnlyList<string> slugs, ZoneType destination, string? sourceLabel)
    {
        foreach (var slug in slugs)
        {
            _session.ReceiveGivenCard(Player, await ResolveOpponentCardAsync(slug), destination, sourceLabel);
        }
    }
}
