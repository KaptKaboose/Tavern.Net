using Tavern.Net.Decklists;

namespace Tavern.Net.Tests;

public class DecklistParserTests
{
    [Fact]
    public void Parse_FlatListWithNoHeaders_DefaultsToUnspecifiedSection()
    {
        var text = "4 Silvie, Slime Sovereign\n3x Crescent Glaive\nWinter's Wolf";

        var lines = DecklistParser.Parse(text);

        Assert.Equal(3, lines.Count);
        Assert.Equal(new ParsedDecklistLine(4, "Silvie, Slime Sovereign", DeckSection.Unspecified), lines[0]);
        Assert.Equal(new ParsedDecklistLine(3, "Crescent Glaive", DeckSection.Unspecified), lines[1]);
        Assert.Equal(new ParsedDecklistLine(1, "Winter's Wolf", DeckSection.Unspecified), lines[2]);
    }

    [Fact]
    public void Parse_WithSectionHeaders_AssignsSectionsUntilNextHeader()
    {
        var text = """
            Champion:
            1 Silvie, Slime Sovereign

            Main:
            4 Card A
            3 Card B

            Material:
            1 Regalia Thing
            """;

        var lines = DecklistParser.Parse(text);

        Assert.Equal(4, lines.Count);
        Assert.Equal(DeckSection.Champion, lines[0].Section);
        Assert.Equal(DeckSection.Main, lines[1].Section);
        Assert.Equal(DeckSection.Main, lines[2].Section);
        Assert.Equal(DeckSection.Material, lines[3].Section);
    }

    [Fact]
    public void Parse_IgnoresBlankLines()
    {
        var text = "4 Card A\n\n\n3 Card B\n";

        var lines = DecklistParser.Parse(text);

        Assert.Equal(2, lines.Count);
    }

    [Fact]
    public void Parse_OmnidexMarkdownHeaders_AssignsSectionsUntilNextHeader()
    {
        var text = """
            # Material Deck
            1 Spirit of Wind
            1 Blinding Orb

            # Main Deck
            3 Accepted Contract
            4 Angel Attendant

            # Sideboard
            1 Censer of Restful Peace
            """;

        var lines = DecklistParser.Parse(text);

        Assert.Equal(5, lines.Count);
        Assert.Equal(DeckSection.Material, lines[0].Section);
        Assert.Equal(DeckSection.Material, lines[1].Section);
        Assert.Equal(DeckSection.Main, lines[2].Section);
        Assert.Equal(DeckSection.Main, lines[3].Section);
        Assert.Equal(DeckSection.Sideboard, lines[4].Section);
        Assert.Equal(new ParsedDecklistLine(1, "Spirit of Wind", DeckSection.Material), lines[0]);
    }

    [Fact]
    public void Parse_FullOmnidexExport_ParsesEveryLineWithCorrectQuantities()
    {
        var text = """
            # Material Deck
            1 Spirit of Wind
            1 Tristan, Underhanded
            1 Tristan, Hired Blade
            1 Tristan, Shadowdancer
            1 Tristan, Shadowreaver
            1 Blinding Orb
            1 Dummy Trainer
            1 Lost Providence
            1 Purifying Thurible
            1 Windwalker Boots
            1 Blight's Ring
            1 Shadow's Claw

            # Main Deck
            3 Accepted Contract
            4 Angel Attendant
            4 Dungeon Guide
            1 Heavenly Guide
            4 Incapacitate
            2 Mastermind Scheme
            3 Sadi, Blood Harvester
            2 Aella, Zephyr's Hand
            4 Beseech the Winds
            3 Calming Breeze
            2 Ensnaring Fumes
            4 Fairy Whispers
            2 Innervate Agility
            2 Stifling Trap
            4 Surveil the Winds
            3 Winbless Lookout
            2 Windmill Engineer
            2 Chasing Shadows
            2 Grim Foreboeseech the Winds
            3 Calming Bre
            4 Fairy Whispers
            2 Innervate Agility
            2 Stifling Trap
            4 Surveil the Winds
            3 Winbless Lookout
            2 Windmill Engineer
            2 Chasing Shadows
            2 Grim Foreboding
            2 Penumbral Waltz
            2 Revenant's Scourge
            3 Shadowstrike
            2 Sinister Composure
            
            # Sideboard
            1 Censer of Restful Peace
            1 Viridian Protective Trinket
            1 Evaporation Synchron
            1 Peer Beyond
            1 Provoking Stand
            3 Slice and Dice
            1 Strategic Planning
            2 Topsy Decreeeze
            2 Ensnaring Fumesctive Trinket
            1 Evaporation Synchron
            1 Peer Beyond
            1 Provoking Stand
            3 Slice and Dice
            1 Strategic Planning
            2 Topsy Decree
            2 Dream Fairy
            """;

        var lines = DecklistParser.Parse(text);

        var material = lines.Where(l => l.Section == DeckSection.Material).ToList();
        var main = lines.Where(l => l.Section == DeckSection.Main).ToList();
        var sideboard = lines.Where(l => l.Section == DeckSection.Sideboard).ToList();

        Assert.Equal(12, material.Count);
        Assert.Equal(23, main.Count);
        Assert.Equal(9, sideboard.Count);
        Assert.Equal(62, main.Sum(l => l.Quantity));
        Assert.Equal(12, material.Sum(l => l.Quantity));
        Assert.Contains(material, l => l.CardName == "Blight's Ring" && l.Quantity == 1);
        Assert.Contains(main, l => l.CardName == "Aella, Zephyr's Hand" && l.Quantity == 2);
        Assert.Contains(sideboard, l => l.CardName == "Dream Fairy" && l.Quantity == 2);
    }
}
