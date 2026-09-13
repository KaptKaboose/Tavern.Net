using System.Text.RegularExpressions;

namespace Tavern.Net.Decklists;

public enum DeckSection
{
    /// <summary>Section wasn't given an explicit header; caller should classify by card type.</summary>
    Unspecified,
    Champion,
    Main,
    Material,
    /// <summary>Not used in a goldfish session — parsed and shown, but not loaded into any zone.</summary>
    Sideboard,
}

public sealed record ParsedDecklistLine(int Quantity, string CardName, DeckSection Section);

/// <summary>
/// Parses pasted decklist text into (quantity, name, section) triples.
/// Recognizes both colon-style headers ("Champion:" / "Main:" / "Material:" /
/// "Main Deck:" / "Material Deck:" / "Reserve:") and Omnidex-style Markdown
/// headers ("# Main Deck" / "# Material Deck" / "# Sideboard"). Lines outside
/// any header, or in a list with no headers at all, are Unspecified — the
/// importer classifies those by the resolved card's type (Champion/Regalia
/// -&gt; Material, else Main).
/// </summary>
public static partial class DecklistParser
{
    [GeneratedRegex(@"^\s*(\d+)\s*x?\s+(.+?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex QuantityLineRegex();

    public static List<ParsedDecklistLine> Parse(string decklistText)
    {
        var lines = new List<ParsedDecklistLine>();
        var currentSection = DeckSection.Unspecified;

        foreach (var rawLine in decklistText.Split('\n'))
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            if (TryParseSectionHeader(line, out var section))
            {
                currentSection = section;
                continue;
            }

            var match = QuantityLineRegex().Match(line);
            if (!match.Success)
            {
                // No leading quantity - assume 1 copy.
                lines.Add(new ParsedDecklistLine(1, line, currentSection));
                continue;
            }

            var quantity = int.Parse(match.Groups[1].Value);
            var name = match.Groups[2].Value;
            lines.Add(new ParsedDecklistLine(quantity, name, currentSection));
        }

        return lines;
    }

    private static bool TryParseSectionHeader(string line, out DeckSection section)
    {
        // Strips both colon-style ("Main:") and Omnidex Markdown-style ("# Main Deck") headers down
        // to a bare section name before comparing.
        var normalized = line.TrimStart('#').Trim().TrimEnd(':').Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "champion":
            case "champion deck":
                section = DeckSection.Champion;
                return true;
            case "main":
            case "main deck":
                section = DeckSection.Main;
                return true;
            case "material":
            case "material deck":
            case "reserve":
                section = DeckSection.Material;
                return true;
            case "sideboard":
            case "side deck":
                section = DeckSection.Sideboard;
                return true;
            default:
                section = DeckSection.Unspecified;
                return false;
        }
    }
}
