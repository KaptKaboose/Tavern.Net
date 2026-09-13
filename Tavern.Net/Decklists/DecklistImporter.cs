using Tavern.Net.GameData;
using Tavern.Net.GameData.Models;

namespace Tavern.Net.Decklists;

public enum ResolutionStatus
{
    Matched,
    Ambiguous,
    NotFound,
}

public sealed record ResolvedDecklistEntry(
    ParsedDecklistLine SourceLine,
    DeckSection FinalSection,
    ResolutionStatus Status,
    CardDto? Card,
    IReadOnlyList<CardDto> Candidates);

/// <summary>
/// Resolves parsed decklist lines against the live Grand Archive card API.
/// </summary>
public sealed class DecklistImporter
{
    private readonly GrandArchiveApiClient _client;

    public DecklistImporter(GrandArchiveApiClient client)
    {
        _client = client;
    }

    public async Task<List<ResolvedDecklistEntry>> ResolveAsync(
        IEnumerable<ParsedDecklistLine> lines,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ResolvedDecklistEntry>();

        foreach (var line in lines)
        {
            var response = await _client.SearchCardsAsync(line.CardName, pageSize: 10, cancellationToken: cancellationToken);

            var exactMatch = response.Data.FirstOrDefault(
                c => string.Equals(c.Name, line.CardName, StringComparison.OrdinalIgnoreCase));

            if (exactMatch is not null)
            {
                var section = ResolveSection(line.Section, exactMatch);
                results.Add(new ResolvedDecklistEntry(line, section, ResolutionStatus.Matched, exactMatch, response.Data));
            }
            else if (response.Data.Count > 0)
            {
                results.Add(new ResolvedDecklistEntry(line, line.Section, ResolutionStatus.Ambiguous, null, response.Data));
            }
            else
            {
                results.Add(new ResolvedDecklistEntry(line, line.Section, ResolutionStatus.NotFound, null, Array.Empty<CardDto>()));
            }
        }

        return results;
    }

    private static DeckSection ResolveSection(DeckSection given, CardDto card)
    {
        if (given != DeckSection.Unspecified)
        {
            return given;
        }

        return card.IsChampionOrRegalia ? DeckSection.Material : DeckSection.Main;
    }
}
