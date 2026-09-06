using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Api.Contracts;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Models;
using OmniCard.Web.Api;
using Xunit;

namespace OmniCard.Tests.Web;

/// <summary>
/// Covers the SPA's customer + sealed-inventory write endpoints. Uses the same in-memory SQLite
/// pattern as the other web service tests, with the real CustomerService/InventoryService so the
/// controller's create-via-service / update-via-load-patch / delete-via-service split is exercised
/// end-to-end. (RowVersion concurrency is a SQL-Server-only concern and absent here by design; the
/// load-patch path is what keeps it correct in production and is still exercised for behavior.)
/// </summary>
public class InventoryCustomerWriteTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<OmniCardDbContext> _opts;
    private readonly MockFactory _factory;
    private readonly CustomersController _customersController;
    private readonly InventoryController _inventoryController;

    public InventoryCustomerWriteTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using (var ctx = new OmniCardDbContext(_opts)) ctx.Database.EnsureCreated();

        _factory = new MockFactory(_opts);
        _customersController = new CustomersController(new CustomerService(_factory), _factory);
        _inventoryController = new InventoryController(new InventoryService(_factory), _factory);
    }

    public void Dispose() => _conn.Dispose();

    private static T Value<T>(ActionResult<T> r) =>
        r.Result is ObjectResult o ? (T)o.Value! : r.Value!;

    // --- Customers ---

    [Fact]
    public void Customer_Create_Then_GetOne_RoundTrips()
    {
        var created = Value(_customersController.Create(new CustomerUpsertRequest
        {
            Name = "Ada Lovelace", Email = "ada@example.com", City = "London", State = "ENG",
        }));
        Assert.True(created.Id > 0);

        var fetched = Value(_customersController.GetOne(created.Id));
        Assert.Equal("Ada Lovelace", fetched.Name);
        Assert.Equal("ada@example.com", fetched.Email);
        Assert.Equal("London", fetched.City);
    }

    [Fact]
    public void Customer_Create_BlankName_Returns400()
    {
        var result = _customersController.Create(new CustomerUpsertRequest { Name = "  " });
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public void Customer_Update_Patches_And_PreservesUnexposedFields()
    {
        // Seed a customer with an address + notes the request DTO does not expose.
        int id;
        using (var ctx = new OmniCardDbContext(_opts))
        {
            var c = new Customer { Name = "Grace", AddressLine1 = "1 Navy Way", Notes = "VIP" };
            ctx.Customers.Add(c);
            ctx.SaveChanges();
            id = c.Id;
        }

        var result = _customersController.Update(id, new CustomerUpsertRequest { Name = "Grace Hopper", Phone = "555" });
        Assert.IsType<NoContentResult>(result);

        using var verify = new OmniCardDbContext(_opts);
        var updated = verify.Customers.Single(c => c.Id == id);
        Assert.Equal("Grace Hopper", updated.Name);
        Assert.Equal("555", updated.Phone);
        Assert.Equal("1 Navy Way", updated.AddressLine1); // untouched
        Assert.Equal("VIP", updated.Notes); // untouched
    }

    [Fact]
    public void Customer_Update_Missing_Returns404()
    {
        var result = _customersController.Update(9999, new CustomerUpsertRequest { Name = "Nobody" });
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public void Customer_Delete_Removes()
    {
        var created = Value(_customersController.Create(new CustomerUpsertRequest { Name = "Temp" }));
        Assert.IsType<NoContentResult>(_customersController.Delete(created.Id));

        using var ctx = new OmniCardDbContext(_opts);
        Assert.False(ctx.Customers.Any(c => c.Id == created.Id));
    }

    // --- Inventory products ---

    [Fact]
    public void Product_Create_Then_Update_Then_Delete()
    {
        var created = Value(_inventoryController.CreateProduct(new ProductUpsertRequest
        {
            Game = "Mtg", Category = "Box", Name = "Foundations Play Booster Box", Upc = "195166",
        }));
        Assert.True(created.Id > 0);
        Assert.Equal("Foundations Play Booster Box", created.Name);

        var upd = _inventoryController.UpdateProduct(created.Id, new ProductUpsertRequest
        {
            Game = "Mtg", Category = "Case", Name = "Foundations Case", LastMarketPrice = 1200m,
        });
        Assert.IsType<NoContentResult>(upd);

        using (var ctx = new OmniCardDbContext(_opts))
        {
            var p = ctx.Products.Single(x => x.Id == created.Id);
            Assert.Equal(ProductCategory.Case, p.Category);
            Assert.Equal("Foundations Case", p.Name);
            Assert.Equal(1200m, p.LastMarketPrice);
        }

        Assert.IsType<NoContentResult>(_inventoryController.DeleteProduct(created.Id));
        using var verify = new OmniCardDbContext(_opts);
        Assert.False(verify.Products.Any(x => x.Id == created.Id));
    }

    [Fact]
    public void Product_Create_RejectsSingle()
    {
        var result = _inventoryController.CreateProduct(new ProductUpsertRequest
        {
            Game = "Mtg", Category = "Single", Name = "A single",
        });
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public void Product_Create_UnknownGame_Returns400()
    {
        var result = _inventoryController.CreateProduct(new ProductUpsertRequest { Game = "Nope", Name = "X" });
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    // --- Inventory lots ---

    [Fact]
    public void Lot_Add_Update_Delete_RoundTrips()
    {
        var product = Value(_inventoryController.CreateProduct(new ProductUpsertRequest
        {
            Game = "Pokemon", Category = "Box", Name = "151 Booster Box",
        }));

        var lot = Value(_inventoryController.AddLot(product.Id, new LotUpsertRequest
        {
            Quantity = 3, UnitCost = 120m, Source = "distributor",
        }));
        Assert.Equal(3, lot.Quantity);

        var lots = _inventoryController.Lots(product.Id).Value!;
        Assert.Single(lots);

        Assert.IsType<NoContentResult>(
            _inventoryController.UpdateLot(lot.Id, new LotUpsertRequest { Quantity = 5, UnitCost = 110m }));
        using (var ctx = new OmniCardDbContext(_opts))
            Assert.Equal(5, ctx.Lots.Single(l => l.Id == lot.Id).Quantity);

        Assert.IsType<NoContentResult>(_inventoryController.DeleteLot(lot.Id));
        using var verify = new OmniCardDbContext(_opts);
        Assert.False(verify.Lots.Any(l => l.Id == lot.Id));
    }

    [Fact]
    public void Lot_Add_ToMissingProduct_Returns404()
    {
        var result = _inventoryController.AddLot(9999, new LotUpsertRequest { Quantity = 1 });
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public void Lot_Add_ZeroQuantity_Returns400()
    {
        var product = Value(_inventoryController.CreateProduct(new ProductUpsertRequest
        {
            Game = "Mtg", Category = "Box", Name = "Box",
        }));
        var result = _inventoryController.AddLot(product.Id, new LotUpsertRequest { Quantity = 0 });
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    private sealed class MockFactory(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }
}
