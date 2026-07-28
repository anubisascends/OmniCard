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

    // Shape mirrors Moxfield's real v2 API: boards are objects keyed by card name, each with
    // quantity + a nested card { name, set, cn }. (Verified against the live api2.moxfield.com response.)
    private const string MoxfieldJson = """
        {
          "name": "Villains of Darkness",
          "mainboard": {
            "Island": { "quantity": 9, "card": { "name": "Island", "set": "fin", "cn": "299" } },
            "Sol Ring": { "quantity": 1, "card": { "name": "Sol Ring", "set": "scd", "cn": "276" } }
          },
          "commanders": {
            "Some Commander": { "quantity": 1, "card": { "name": "Some Commander", "set": "fin", "cn": "1" } }
          },
          "sideboard": {},
          "companions": {}
        }
        """;

    [Fact]
    public void ParseMoxfieldJson_ReadsNameAndAllBoards()
    {
        var (deckName, entries) = DecklistService.ParseMoxfieldJson(MoxfieldJson);

        Assert.Equal("Villains of Darkness", deckName);
        Assert.Equal(3, entries.Count);   // 2 mainboard + 1 commander; empty boards contribute nothing

        var island = Assert.Single(entries, e => e.CardName == "Island");
        Assert.Equal(9, island.Quantity);
        Assert.Equal("FIN", island.SetCode);        // upper-cased
        Assert.Equal("299", island.CollectorNumber);
        Assert.Contains(entries, e => e.CardName == "Some Commander");
    }
}
