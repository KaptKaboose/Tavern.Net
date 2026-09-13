using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Tavern.Net.Game;
using Tavern.Net.GameData;

namespace Tavern.Net.ViewModels;

/// <summary>
/// Mirrors a domain <see cref="Zone"/> as an observable collection of
/// <see cref="CardViewModel"/>, kept in sync via the zone's CollectionChanged event.
/// </summary>
public sealed class ZoneViewModel
{
    private readonly Zone _zone;
    private readonly GrandArchiveApiClient _apiClient;
    private readonly GameBoardViewModel _board;
    private readonly Dictionary<CardInstance, CardViewModel> _wrappers = new();

    public ZoneType Type => _zone.Type;

    public ObservableCollection<CardViewModel> Cards { get; } = new();

    public ZoneViewModel(Zone zone, GrandArchiveApiClient apiClient, GameBoardViewModel board)
    {
        _zone = zone;
        _apiClient = apiClient;
        _board = board;

        foreach (var card in _zone.Cards)
        {
            AddWrapper(card);
        }

        _zone.Cards.CollectionChanged += OnZoneCardsChanged;
    }

    private void OnZoneCardsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                foreach (CardInstance card in e.NewItems!)
                {
                    AddWrapper(card);
                }
                break;

            case NotifyCollectionChangedAction.Remove:
                foreach (CardInstance card in e.OldItems!)
                {
                    RemoveWrapper(card);
                }
                break;

            default:
                // Reset/Move/Replace (e.g. a shuffle) — simplest correct thing is a full rebuild.
                Cards.Clear();
                _wrappers.Clear();
                foreach (var card in _zone.Cards)
                {
                    AddWrapper(card);
                }
                break;
        }
    }

    private void AddWrapper(CardInstance card)
    {
        var viewModel = new CardViewModel(card, _apiClient, _board);
        _wrappers[card] = viewModel;
        Cards.Add(viewModel);
    }

    private void RemoveWrapper(CardInstance card)
    {
        if (_wrappers.Remove(card, out var viewModel))
        {
            Cards.Remove(viewModel);
        }
    }
}
