using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Web.Services;
using Xunit;

namespace OmniCard.Tests.Web;

/// <summary>
/// Covers the writable binder-edit service used by the web companion. Same in-memory SQLite pattern
/// as the desktop service tests (keep the connection open for the fixture's lifetime).
/// </summary>
public class WebBinderCardServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<OmniCardDbContext> _opts;
    private readonly IDbContextFactory<OmniCardDbContext> _factory;
    private readonly WebBinderCardService _service;
    private readonly StorageContainerService _containers;
    private readonly int _binderId;

    public WebBinderCardServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using (var ctx = new OmniCardDbContext(_opts)) ctx.Database.EnsureCreated();

        _factory = new MockFactory(_opts);
        _service = new WebBinderCardService(_factory, new StubDataPath());
        _containers = new StorageContainerService(_factory);
        _binderId = _containers.Create("Binder A", ContainerType.Binder).Id;
    }

    public void Dispose() => _conn.Dispose();

    private int AddLot(string name, int? page = null, int? slot = null, bool foil = false, string condition = "NM")
    {
        using var ctx = new OmniCardDbContext(_opts);
        var product = new Product
        {
            Game = CardGame.Pokemon,
            Category = ProductCategory.Single,
            GameCardId = name.Replace(" ", "").ToLowerInvariant(),
            Foil = foil,
            Name = name,
            SetCode = "SET",
            SetName = "Set Name",
        };
        ctx.Products.Add(product);
        ctx.SaveChanges();
        var lot = new InventoryLot
        {
            ProductId = product.Id,
            LocationId = _binderId,
            Page = page,
            Slot = slot,
            Condition = condition,
        };
        ctx.Lots.Add(lot);
        ctx.SaveChanges();
        return lot.Id;
    }

    [Fact]
    public void GetUnplacedBinderCards_ReturnsOnlyUnplacedInContainer()
    {
        AddLot("Placed Card", page: 1, slot: 0);
        var unplacedId = AddLot("Unplaced Card");

        var result = _service.GetUnplacedBinderCards(_binderId, null);

        Assert.Single(result);
        Assert.Equal(unplacedId, result[0].Id);
    }

    [Fact]
    public void GetUnplacedBinderCards_AppliesScryfallFilter()
    {
        AddLot("Charizard");
        AddLot("Blastoise");

        var result = _service.GetUnplacedBinderCards(_binderId, new FilterPreset { Query = "name:char" });

        Assert.Single(result);
        Assert.Equal("Charizard", result[0].Name);
    }

    [Fact]
    public void MoveCardsToContainer_ClearsPlacementAndMoves()
    {
        var lotId = AddLot("Mover", page: 1, slot: 2);
        var targetId = _containers.Create("Box B", ContainerType.Box).Id;

        _service.MoveCardsToContainer([lotId], targetId);

        using var ctx = new OmniCardDbContext(_opts);
        var lot = ctx.Lots.Single(l => l.Id == lotId);
        Assert.Equal(targetId, lot.LocationId);
        Assert.Null(lot.Page);
        Assert.Null(lot.Slot);
    }

    [Fact]
    public void SetFoil_MovesLotToFoilProduct()
    {
        var lotId = AddLot("Foiler", foil: false);

        _service.SetFoil([lotId], true);

        using var ctx = new OmniCardDbContext(_opts);
        var lot = ctx.Lots.Include(l => l.Product).Single(l => l.Id == lotId);
        Assert.True(lot.Product.Foil);
    }

    [Fact]
    public void SetCondition_UpdatesLot()
    {
        var lotId = AddLot("Condish", condition: "NM");

        _service.SetCondition([lotId], "LP");

        using var ctx = new OmniCardDbContext(_opts);
        Assert.Equal("LP", ctx.Lots.Single(l => l.Id == lotId).Condition);
    }

    [Fact]
    public void DeleteCollectionCard_RemovesLot()
    {
        var lotId = AddLot("Doomed");

        _service.DeleteCollectionCard(lotId);

        using var ctx = new OmniCardDbContext(_opts);
        Assert.False(ctx.Lots.Any(l => l.Id == lotId));
    }

    [Fact]
    public void AddMissingCardToSlot_PlacesCardAndSwapsOccupant()
    {
        var occupantId = AddLot("Occupant", page: 1, slot: 0);
        var match = new CardMatch
        {
            GameSpecificId = "newcard",
            Name = "New Card",
            SetCode = "SET",
            SetName = "Set Name",
            CollectorNumber = "42",
            Rarity = "Rare",
        };

        _service.AddMissingCardToSlot(match, CardGame.Pokemon, "NM", false, null, null, _binderId, 1, 0);

        using var ctx = new OmniCardDbContext(_opts);
        // Occupant displaced back to the unplaced pool.
        var occupant = ctx.Lots.Single(l => l.Id == occupantId);
        Assert.Null(occupant.Page);
        Assert.Null(occupant.Slot);
        // New card now occupies page 1, slot 0.
        var placed = ctx.Lots.Include(l => l.Product)
            .Single(l => l.LocationId == _binderId && l.Page == 1 && l.Slot == 0);
        Assert.Equal("New Card", placed.Product.Name);
    }

    private sealed class MockFactory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }

    private sealed class StubDataPath : IDataPathService
    {
        public string DataDirectory => Path.GetTempPath();
        public string ScansDirectory => Path.GetTempPath();
        public string TempScansDirectory => Path.GetTempPath();
        public string SymbolsCacheDirectory => Path.GetTempPath();
        public string LogsDirectory => Path.GetTempPath();
        public string TradesDirectory => Path.GetTempPath();
        public string? PendingDataDirectory => null;
        public bool IsMigrationPending => false;
        public void SetPendingDataDirectory(string path) { }
        public void CommitMigration() { }
        public void CancelPendingMigration() { }
    }
}
