using OmniCard.Collection;
using OmniCard.Models;
using Xunit;

namespace OmniCard.Tests.Services;

public class DecklistServiceTests
{
    private static DecklistService NewService() => new(null!, null!, null!);

    private const string Sample = """
        // a comment
        Deck
        1 Isperia, Supreme Judge (SCD) 4 *E*
        4 Island (SCD) 337
        4 Island (SCD) 338
        1 Island (SCD) 338
        1 Sol Ring (SCD) 276
        1 Lightning Bolt
        """;

    [Fact]
    public void ParseDecklistPrintings_ParsesSetAndCollector_IgnoringTrailingText()
    {
        var entries = NewService().ParseDecklistPrintings(Sample);

        var isperia = Assert.Single(entries, e => e.CardName == "Isperia, Supreme Judge");
        Assert.Equal("SCD", isperia.SetCode);
        Assert.Equal("4", isperia.CollectorNumber);   // *E* dropped
        Assert.Equal(1, isperia.Quantity);
    }

    [Fact]
    public void ParseDecklistPrintings_KeepsDistinctPrintings_AndSumsExactDuplicates()
    {
        var entries = NewService().ParseDecklistPrintings(Sample);

        var islands = entries.Where(e => e.CardName == "Island").ToList();
        Assert.Equal(2, islands.Count);                                  // 337 and 338 distinct
        Assert.Equal(4, islands.Single(e => e.CollectorNumber == "337").Quantity);
        Assert.Equal(5, islands.Single(e => e.CollectorNumber == "338").Quantity); // 4 + 1 summed
    }

    [Fact]
    public void ParseDecklistPrintings_SkipsCommentsAndHeaders_AndAllowsNameOnly()
    {
        var entries = NewService().ParseDecklistPrintings(Sample);

        Assert.DoesNotContain(entries, e => e.CardName.StartsWith("//"));
        Assert.DoesNotContain(entries, e => e.CardName == "Deck");
        var bolt = Assert.Single(entries, e => e.CardName == "Lightning Bolt");
        Assert.Null(bolt.SetCode);
        Assert.Null(bolt.CollectorNumber);
    }
}
