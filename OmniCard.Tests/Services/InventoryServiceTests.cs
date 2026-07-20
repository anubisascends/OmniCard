using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Collection;

namespace OmniCard.Tests.Services;

public class InventoryServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<OmniCardDbContext> _options;

    public InventoryServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<OmniCardDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var ctx = new OmniCardDbContext(_options);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private IInventoryService CreateService() =>
        new InventoryService(new MockFactory(_options));

    [Fact]
    public void CreateProduct_RoundTrips()
    {
        var service = CreateService();
        var product = service.CreateProduct(new Product
        {
            Game = CardGame.Mtg,
            Category = ProductCategory.Box,
            Name = "Bloomburrow Booster Box",
            SetCode = "blb",
        });

        var loaded = service.GetProducts();
        Assert.Single(loaded);
        Assert.Equal("Bloomburrow Booster Box", loaded[0].Name);
        Assert.Equal(product.Id, loaded[0].Id);
    }

    [Fact]
    public void FindProductByUpc_ReturnsMatch()
    {
        var service = CreateService();
        service.CreateProduct(new Product
        {
            Game = CardGame.Mtg,
            Category = ProductCategory.Box,
            Name = "Test",
            Upc = "ABC123",
        });

        Assert.NotNull(service.FindProductByUpc("ABC123"));
        Assert.Null(service.FindProductByUpc("NOTFOUND"));
    }

    [Fact]
    public void GetProducts_FiltersByGameAndCategory()
    {
        var service = CreateService();
        service.CreateProduct(new Product
        {
            Game = CardGame.Mtg,
            Category = ProductCategory.Box,
            Name = "MTG Box",
        });
        service.CreateProduct(new Product
        {
            Game = CardGame.OnePiece,
            Category = ProductCategory.Pack,
            Name = "OP Pack",
        });

        var mtgOnly = service.GetProducts(game: CardGame.Mtg);
        Assert.Single(mtgOnly);
        Assert.Equal("MTG Box", mtgOnly[0].Name);

        var packsOnly = service.GetProducts(category: ProductCategory.Pack);
        Assert.Single(packsOnly);
        Assert.Equal("OP Pack", packsOnly[0].Name);

        var mtgBoxOnly = service.GetProducts(game: CardGame.Mtg, category: ProductCategory.Box);
        Assert.Single(mtgBoxOnly);

        var mtgPacks = service.GetProducts(game: CardGame.Mtg, category: ProductCategory.Pack);
        Assert.Empty(mtgPacks);
    }

    [Fact]
    public void AddLot_WritesAcquireMovement_AndComputesTotals()
    {
        var service = CreateService();
        var product = service.CreateProduct(new Product
        {
            Game = CardGame.Mtg,
            Category = ProductCategory.Box,
            Name = "Test Box",
        });

        service.AddLot(product.Id, 3, 10m, null, null);

        var lots = service.GetLots(product.Id);
        Assert.Single(lots);
        Assert.Equal(3, lots[0].Quantity);

        var movements = service.GetMovements(product.Id);
        Assert.Single(movements);
        Assert.Equal(MovementType.Acquire, movements[0].Type);
        Assert.Equal(3, movements[0].Quantity);
        Assert.Equal(10m, movements[0].UnitValue);
    }

    [Fact]
    public void OpenUnits_DecrementsLot_WritesOpenMovement()
    {
        var service = CreateService();
        var product = service.CreateProduct(new Product
        {
            Game = CardGame.Mtg,
            Category = ProductCategory.Box,
            Name = "Test Box",
        });
        var lot = service.AddLot(product.Id, 2, 10m, null, null);

        service.OpenUnits(lot.Id, 1, "pulled a foil");

        var lots = service.GetLots(product.Id);
        Assert.Single(lots);
        Assert.Equal(1, lots[0].Quantity);

        var movements = service.GetMovements(product.Id);
        Assert.Contains(movements, m => m.Type == MovementType.Open && m.Quantity == 1 && m.Note == "pulled a foil");
    }

    [Fact]
    public void OpenUnits_DeletesLot_AtZero()
    {
        var service = CreateService();
        var product = service.CreateProduct(new Product
        {
            Game = CardGame.Mtg,
            Category = ProductCategory.Box,
            Name = "Test Box",
        });
        var lot = service.AddLot(product.Id, 1, 10m, null, null);

        service.OpenUnits(lot.Id, 1, null);

        Assert.Empty(service.GetLots(product.Id));
    }

    [Fact]
    public void DeleteProduct_CascadesLots()
    {
        var service = CreateService();
        var product = service.CreateProduct(new Product
        {
            Game = CardGame.Mtg,
            Category = ProductCategory.Box,
            Name = "Test Box",
        });
        service.AddLot(product.Id, 1, 10m, null, null);

        service.DeleteProduct(product.Id);

        Assert.Empty(service.GetLots(product.Id));
        Assert.Empty(service.GetProducts());
    }

    [Fact]
    public void GetValuation_SumsCostAndMarket_AcrossLots()
    {
        var service = CreateService();
        var productA = service.CreateProduct(new Product
        {
            Game = CardGame.Mtg,
            Category = ProductCategory.Box,
            Name = "Product A",
        });
        var productB = service.CreateProduct(new Product
        {
            Game = CardGame.Mtg,
            Category = ProductCategory.Pack,
            Name = "Product B",
        });

        service.AddLot(productA.Id, 2, 5m, null, null);
        service.AddLot(productA.Id, 1, 8m, null, null);
        service.AddLot(productB.Id, 4, 2m, null, null);

        var valuation = service.GetValuation();
        Assert.Equal(7, valuation.TotalUnits);
        Assert.Equal(2 * 5m + 1 * 8m + 4 * 2m, valuation.TotalCost);
        // Sealed products with no LastMarketPrice set yet (Task 1, Phase 3) value at 0.
        Assert.Equal(0m, valuation.TotalMarket);

        var boxOnly = service.GetValuation(category: ProductCategory.Box);
        Assert.Equal(3, boxOnly.TotalUnits);
        Assert.Equal(2 * 5m + 1 * 8m, boxOnly.TotalCost);
    }

    [Fact]
    public void GetValuation_SealedProducts_UseLastMarketPrice_NotNotMappedMarketPrice()
    {
        var service = CreateService();
        var box = service.CreateProduct(new Product
        {
            Game = CardGame.Mtg,
            Category = ProductCategory.Box,
            Name = "Bloomburrow Booster Box",
            MarketPrice = 999m, // [NotMapped]; must be ignored for sealed valuation
        });
        service.AddLot(box.Id, 2, 20m, null, null);

        box.LastMarketPrice = 45.50m;
        service.UpdateProduct(box);

        var valuation = service.GetValuation();
        Assert.Equal(2, valuation.TotalUnits);
        Assert.Equal(40m, valuation.TotalCost);
        Assert.Equal(2 * 45.50m, valuation.TotalMarket);
    }

    private class MockFactory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }
}
