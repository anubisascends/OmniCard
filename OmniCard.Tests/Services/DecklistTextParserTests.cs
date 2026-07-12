using OmniCard.Collection;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class DecklistTextParserTests
{
    [Fact]
    public void ParseDecklistText_SimpleNameOnly()
    {
        var service = CreateService();
        var (name, entries) = service.ParseDecklistText("1 Lightning Bolt\n2 Mountain");

        Assert.Equal("Pasted Decklist", name);
        Assert.Equal(2, entries.Count);
        Assert.Equal(new DecklistEntry(1, "Lightning Bolt", null, null), entries[0]);
        Assert.Equal(new DecklistEntry(2, "Mountain", null, null), entries[1]);
    }

    [Fact]
    public void ParseDecklistText_WithSetAndCollectorNumber()
    {
        var service = CreateService();
        var (_, entries) = service.ParseDecklistText("1 Lightning Bolt (M11) 149");

        var entry = Assert.Single(entries);
        Assert.Equal("Lightning Bolt", entry.CardName);
        Assert.Equal("M11", entry.SetCode);
        Assert.Equal("149", entry.CollectorNumber);
        Assert.Equal(1, entry.Quantity);
    }

    [Fact]
    public void ParseDecklistText_IgnoresCommentsAndBlankLines()
    {
        var service = CreateService();
        var text = "// Creatures\n1 Ragavan, Nimble Pilferer\n\n// Lands\n2 Mountain";
        var (_, entries) = service.ParseDecklistText(text);

        Assert.Equal(2, entries.Count);
        Assert.Equal("Ragavan, Nimble Pilferer", entries[0].CardName);
        Assert.Equal("Mountain", entries[1].CardName);
    }

    [Fact]
    public void ParseDecklistText_AggregatesDuplicateEntries()
    {
        var service = CreateService();
        var text = "2 Lightning Bolt\n1 Lightning Bolt";
        var (_, entries) = service.ParseDecklistText(text);

        var entry = Assert.Single(entries);
        Assert.Equal(3, entry.Quantity);
        Assert.Equal("Lightning Bolt", entry.CardName);
    }

    [Fact]
    public void ParseDecklistText_EmptyInput_ReturnsEmptyList()
    {
        var service = CreateService();
        var (_, entries) = service.ParseDecklistText("");

        Assert.Empty(entries);
    }

    [Fact]
    public void ParseDecklistText_IgnoresSectionHeaders()
    {
        var service = CreateService();
        var text = "Deck\n1 Lightning Bolt\nSideboard\n1 Pyroblast";
        var (_, entries) = service.ParseDecklistText(text);

        Assert.Equal(2, entries.Count);
    }

    private static DecklistService CreateService()
    {
        return new DecklistService(null!, null!, null!);
    }
}
