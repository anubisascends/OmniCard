using OmniCard.Collection.Lists;

namespace OmniCard.Tests.Services.Lists;

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
        Assert.All(entries, e => Assert.Null(e.Finish));   // no finish field → non-foil
    }

    [Fact]
    public void ParseMoxfieldJson_ReadsFinish()
    {
        const string json = """
            {
              "name": "D",
              "mainboard": {
                "A": { "quantity": 1, "finish": "foil", "isFoil": true, "card": { "name": "A", "set": "x", "cn": "1" } },
                "B": { "quantity": 1, "finish": "etched", "isFoil": false, "card": { "name": "B", "set": "x", "cn": "2" } },
                "C": { "quantity": 1, "finish": "nonFoil", "isFoil": false, "card": { "name": "C", "set": "x", "cn": "3" } }
              }
            }
            """;

        var (_, entries) = DecklistService.ParseMoxfieldJson(json);

        Assert.Equal("Foil", entries.Single(e => e.CardName == "A").Finish);
        Assert.Equal("Etched", entries.Single(e => e.CardName == "B").Finish);
        Assert.Null(entries.Single(e => e.CardName == "C").Finish);
    }

    // Shape mirrors Archidekt's real /api/decks/{id}/ response (verified live): top-level categories carry
    // includedInDeck; each card has quantity, modifier, categories and a nested card/edition/oracleCard.
    [Fact]
    public void ParseArchidektJson_ReadsFinish_AndSkipsOutOfDeckCategories()
    {
        const string json = """
            {
              "name": "Arch Deck",
              "categories": [
                { "name": "Commander", "includedInDeck": true },
                { "name": "Maybeboard", "includedInDeck": false }
              ],
              "cards": [
                { "quantity": 1, "modifier": "Foil", "categories": ["Commander"],
                  "card": { "collectorNumber": "277", "edition": { "editioncode": "fra" }, "oracleCard": { "name": "Vraska" } } },
                { "quantity": 2, "modifier": "Normal", "categories": [],
                  "card": { "collectorNumber": "10", "edition": { "editioncode": "m10" }, "oracleCard": { "name": "Uncategorized" } } },
                { "quantity": 1, "modifier": "Normal", "categories": ["Maybeboard"],
                  "card": { "collectorNumber": "5", "edition": { "editioncode": "m10" }, "oracleCard": { "name": "Maybe" } } }
              ]
            }
            """;

        var (deckName, entries) = DecklistService.ParseArchidektJson(json);

        Assert.Equal("Arch Deck", deckName);
        Assert.Equal(2, entries.Count);
        var vraska = entries.Single(e => e.CardName == "Vraska");
        Assert.Equal(("FRA", "277", "Foil"), (vraska.SetCode, vraska.CollectorNumber, vraska.Finish));
        Assert.Null(entries.Single(e => e.CardName == "Uncategorized").Finish);
        Assert.DoesNotContain(entries, e => e.CardName == "Maybe");
    }
}
