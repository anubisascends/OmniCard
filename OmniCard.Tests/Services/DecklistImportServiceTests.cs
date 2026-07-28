using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Models;
using OmniCard.Services;
using OmniCard.Tests.Fakes;
using OmniCard.Views.DecklistImport;
using Xunit;

namespace OmniCard.Tests.Services;

public class DecklistImportServiceTests
{
    private static CardMatch M(string id) => new() { GameSpecificId = id, Name = "Island", SetCode = "SCD", CollectorNumber = "337" };

    private static (DecklistImportService svc, ConfigurableGameService gs, RecordingCardService cards,
        RecordingListService lists, FakeDecklistParseService decks) Build()
    {
        var gs = new ConfigurableGameService();
        var cards = new RecordingCardService(gs);
        var lists = new RecordingListService();
        var decks = new FakeDecklistParseService();
        var svc = new DecklistImportService(decks, cards, lists, NullLogger<DecklistImportService>.Instance);
        return (svc, gs, cards, lists, decks);
    }

    [Fact]
    public void ResolveFile_ReturnsRows_WithResolvedAndUnresolved()
    {
        var (svc, gs, _, _, decks) = Build();
        decks.Printings =
        [
            new DecklistEntry(4, "Island", "SCD", "337"),
            new DecklistEntry(1, "Nonesuch", "SCD", "999"),
        ];
        gs.OnSearchCards = (q, _) => q.Contains("337") ? [M("a")] : [];

        var rows = svc.ResolveFile("ignored");

        Assert.Equal(2, rows.Count);
        Assert.True(rows[0].IsResolved);
        Assert.Equal(4, rows[0].Quantity);
        Assert.False(rows[1].IsResolved);
    }

    [Fact]
    public void CommitToList_CallsAddPrinting_FileSource_NonFoil_ReturnsQuantitySum()
    {
        var (svc, _, _, lists, _) = Build();
        var rows = new List<DecklistImportRow>
        {
            new() { Quantity = 4, Name = "Island", Match = M("a") },
            new() { Quantity = 2, Name = "Plains", Match = M("b") },
        };

        var added = svc.CommitToList(42, rows);

        Assert.Equal(6, added);
        Assert.Equal(2, lists.Printings.Count);
        Assert.All(lists.Printings, p => Assert.Equal(42, p.ListId));
        Assert.All(lists.Printings, p => Assert.False(p.IsFoil));
        Assert.All(lists.Printings, p => Assert.Equal(ListItemSource.File, p.Source));
    }

    [Fact]
    public void CommitToLocation_CallsAddCardToCollection_NearMint_NonFoil_NoPrice()
    {
        var (svc, _, cards, _, _) = Build();
        var box = new StorageContainer { Id = 7, Name = "Deck Box" };
        var rows = new List<DecklistImportRow> { new() { Quantity = 3, Name = "Island", Match = M("a") } };

        var added = svc.CommitToLocation(box, rows);

        Assert.Equal(3, added);
        var call = Assert.Single(cards.Added);
        Assert.Equal("Near Mint", call.Condition);
        Assert.False(call.IsFoil);
        Assert.Null(call.PurchasePrice);
        Assert.Equal(3, call.Quantity);
        Assert.Equal(7, call.Container!.Id);
    }

    [Fact]
    public void ResolveEntries_ResolvesDirectly_WithoutParsing()
    {
        var (svc, gs, _, _, _) = Build();
        gs.OnSearchCards = (q, _) => q.Contains("337") ? [M("a")] : [];
        var entries = new[]
        {
            new DecklistEntry(4, "Island", "SCD", "337"),
            new DecklistEntry(1, "Nonesuch", "SCD", "999"),
        };

        var rows = svc.ResolveEntries(entries);

        Assert.Equal(2, rows.Count);
        Assert.True(rows[0].IsResolved);
        Assert.Equal(4, rows[0].Quantity);
        Assert.False(rows[1].IsResolved);
    }

    [Fact]
    public void ResolveFile_DelegatesToResolveEntries_ViaParser()
    {
        var (svc, gs, _, _, decks) = Build();
        decks.Printings = [new DecklistEntry(2, "Island", "SCD", "337")];
        gs.OnSearchCards = (_, _) => [M("a")];

        var rows = svc.ResolveFile("ignored");

        var row = Assert.Single(rows);
        Assert.True(row.IsResolved);
        Assert.Equal(2, row.Quantity);
    }
}
