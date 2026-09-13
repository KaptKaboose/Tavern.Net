using CommunityToolkit.Mvvm.ComponentModel;
using Tavern.Net.Decklists;
using Tavern.Net.GameData.Models;

namespace Tavern.Net.ViewModels;

/// <summary>One line of a pasted decklist, plus the card the user has confirmed it resolves to.</summary>
public sealed partial class ResolvedLineViewModel : ObservableObject
{
    public ParsedDecklistLine SourceLine { get; }

    public ResolutionStatus Status { get; }

    public IReadOnlyList<CardDto> Candidates { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsResolved))]
    [NotifyPropertyChangedFor(nameof(EffectiveSection))]
    private CardDto? _selectedCard;

    public bool IsResolved => SelectedCard is not null;

    public bool ShowCandidatePicker => Status == ResolutionStatus.Ambiguous && Candidates.Count > 1;

    public DeckSection EffectiveSection => SourceLine.Section != DeckSection.Unspecified
        ? SourceLine.Section
        : SelectedCard?.IsChampionOrRegalia == true ? DeckSection.Material : DeckSection.Main;

    public ResolvedLineViewModel(ResolvedDecklistEntry entry)
    {
        SourceLine = entry.SourceLine;
        Status = entry.Status;
        Candidates = entry.Candidates;
        SelectedCard = entry.Card ?? entry.Candidates.FirstOrDefault();
    }
}
