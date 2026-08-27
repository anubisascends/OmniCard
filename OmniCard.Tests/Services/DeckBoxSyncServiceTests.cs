using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Tests.Fakes;

namespace OmniCard.Tests.Services;

public class DeckBoxSyncServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<OmniCardDbContext> _options;
    private readonly int _deckBoxId;

    public DeckBoxSyncServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_connection).Options;
        using var ctx = new OmniCardDbContext(_options);
        ctx.Database.EnsureCreated();

        var deck = new StorageContainer { Name = "Deck A", ContainerType = ContainerType.DeckBox };
        ctx.StorageContainers.Add(deck);
        ctx.SaveChanges();
        _deckBoxId = deck.Id;
    }

    public void Dispose() => _connection.Dispose();

    private sealed class Factory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }

    private int AddContainer(string name, ContainerType type = ContainerType.Box)
    {
        using var ctx = new OmniCardDbContext(_options);
        var c = new StorageContainer { Name = name, ContainerType = type };
        ctx.StorageContainers.Add(c);
        ctx.SaveChanges();
        return c.Id;
    }

    /// <summary>Adds a single-card lot; returns its lot id.</summary>
    private int AddLot(string name, int? locationId, int quantity = 1, string setCode = "cmm", string cn = "1", bool foil = false)
    {
        using var ctx = new OmniCardDbContext(_options);
        var product = new Product
        {
            Game = CardGame.Mtg,
            Category = ProductCategory.Single,
            GameCardId = $"{name}-{setCode}-{cn}",
            Name = name,
            SetCode = setCode,
            CollectorNumber = cn,
            Foil = foil,
        };
        ctx.Products.Add(product);
        ctx.SaveChanges();
        var lot = new InventoryLot { ProductId = product.Id, Quantity = quantity, LocationId = locationId, Condition = "NM" };
        ctx.Lots.Add(lot);
        ctx.SaveChanges();
        return lot.Id;
    }

    private DeckBoxSyncService CreateService(out RecordingCardService cardService, out RecordingTagService tagService)
    {
        cardService = new RecordingCardService(new ConfigurableGameService());
        tagService = new RecordingTagService();
        return new DeckBoxSyncService(new Factory(_options), cardService, tagService);
    }

    private static DecklistEntry Entry(string name, int qty = 1, string? set = null, string? cn = null)
        => new(qty, name, set, cn);

    [Fact]
    public void BuildPlan_ComputesCutAddKeep()
    {
        var bulkId = AddContainer("Bulk", ContainerType.Bulk);
        // Deck box currently holds Sol Ring, Llanowar Elves, Counterspell.
        AddLot("Sol Ring", _deckBoxId);
        AddLot("Llanowar Elves", _deckBoxId);
        AddLot("Counterspell", _deckBoxId);
        // Brainstorm lives in Bulk, ready to pull in.
        AddLot("Brainstorm", bulkId);

        var svc = CreateService(out _, out _);
        var plan = svc.BuildPlan(_deckBoxId,
            [Entry("Sol Ring"), Entry("Brainstorm"), Entry("Counterspell")],
            CardGame.Mtg);

        // Llanowar Elves is no longer wanted → cut.
        Assert.Single(plan.Cuts);
        Assert.Equal("Llanowar Elves", plan.Cuts[0].CardName);
        // Brainstorm is wanted but absent → add, with Bulk as a source.
        Assert.Single(plan.Adds);
        Assert.Equal("Brainstorm", plan.Adds[0].CardName);
        Assert.Contains(plan.Adds[0].Sources, s => s.ContainerName == "Bulk");
        // Sol Ring + Counterspell already present and wanted → kept.
        Assert.Equal(2, plan.KeepCount);
    }

    [Fact]
    public void BuildPlan_OrdersAddSources_ExactPrintingFirst()
    {
        var boxA = AddContainer("Box A");
        var bulk = AddContainer("Bulk", ContainerType.Bulk);
        // Two printings of Brainstorm owned; the target asks specifically for the MMQ printing.
        AddLot("Brainstorm", bulk, setCode: "ema", cn: "40");
        AddLot("Brainstorm", boxA, setCode: "mmq", cn: "60");

        var svc = CreateService(out _, out _);
        var plan = svc.BuildPlan(_deckBoxId, [Entry("Brainstorm", set: "mmq", cn: "60")], CardGame.Mtg);

        Assert.Single(plan.Adds);
        var sources = plan.Adds[0].Sources;
        Assert.Equal("mmq", sources[0].SetCode);
        Assert.True(sources[0].IsExactMatch);
        Assert.False(sources[1].IsExactMatch);
    }

    [Fact]
    public void BuildPlan_OverQuantity_CutsOnlySurplus()
    {
        // Deck box has 3 Sol Rings in one lot; the list wants only 1.
        AddLot("Sol Ring", _deckBoxId, quantity: 3);

        var svc = CreateService(out _, out _);
        var plan = svc.BuildPlan(_deckBoxId, [Entry("Sol Ring", qty: 1)], CardGame.Mtg);

        Assert.Single(plan.Cuts);
        Assert.Equal(2, plan.Cuts[0].Quantity); // surplus only
        Assert.Equal(1, plan.KeepCount);
        Assert.Empty(plan.Adds);
    }

    [Fact]
    public void BuildPlan_AlreadyMatches_NoCutsNoAdds()
    {
        AddLot("Sol Ring", _deckBoxId);
        var svc = CreateService(out _, out _);
        var plan = svc.BuildPlan(_deckBoxId, [Entry("Sol Ring")], CardGame.Mtg);

        Assert.Empty(plan.Cuts);
        Assert.Empty(plan.Adds);
        Assert.Equal(1, plan.KeepCount);
    }

    [Fact]
    public void ApplySync_Adds_MoveFromSource_Cuts_SideboardTagsAndMoveRelocates()
    {
        var destId = AddContainer("Box C");
        var svc = CreateService(out var cards, out var tags);

        var request = new DeckBoxSyncCommitRequest(
            _deckBoxId,
            Cuts:
            [
                new DeckBoxCutDecision(LotId: 7, Quantity: 1, Sideboard: true, DestinationContainerId: null),
                new DeckBoxCutDecision(LotId: 8, Quantity: 1, Sideboard: false, DestinationContainerId: destId),
            ],
            Adds:
            [
                new DeckBoxAddDecision(SourceLotId: 5, Quantity: 2),
            ]);

        svc.ApplySync(request);

        // Add pulled 2 copies from lot 5 into the deck box.
        Assert.Contains(cards.MovedQuantities, m => m.LotId == 5 && m.Quantity == 2 && m.ContainerId == _deckBoxId);
        // Cut-to-location moved lot 8 into Box C.
        Assert.Contains(cards.MovedQuantities, m => m.LotId == 8 && m.Quantity == 1 && m.ContainerId == destId);
        // Sideboard lot 7 was tagged and NOT moved.
        Assert.DoesNotContain(cards.MovedQuantities, m => m.LotId == 7);
        Assert.Contains(tags.Added, t => t.LotId == 7 && t.TagName == DeckBoxSyncService.SideboardTag);
    }
}
