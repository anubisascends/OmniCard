using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OmniCard.Api.Contracts;
using OmniCard.Collection.Sales;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Inventory;
using OmniCard.Shared.Sales;
using OmniCard.Tests.Services.Ebay;
using OmniCard.Web.Api.Controllers;

namespace OmniCard.Tests.Web;

/// <summary>The order editor's add-card search and AddLine guard: only listed-for-sale cards, and only
/// copies not already on an open order.</summary>
public class OrdersSellableTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<OmniCardDbContext> _opts;
    private readonly OrderService _orders;
    private readonly OrdersController _controller;

    public OrdersSellableTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using (var ctx = new OmniCardDbContext(_opts))
            ctx.Database.EnsureCreated();

        var factory = new Factory(_opts);
        var settings = Mock.Of<ISalesSettingsService>();
        _orders = new OrderService(factory, new ListingService(factory, settings), new FakeEbayListingService(),
            NullLogger<OrderService>.Instance);
        _controller = new OrdersController(_orders, new CustomerService(factory), settings, null!, null!, factory);
    }

    public void Dispose() => _conn.Dispose();

    [Fact]
    public void Sellable_OffersListedCardsOnly()
    {
        var listed = SeedLot("Sol Ring", "C21", listedPrice: 2.5m);
        SeedLot("Sol Ring", "CMR", listedPrice: null); // in the collection, not for sale
        SeedLot("Lightning Bolt", "M10", listedPrice: 1m);

        var result = Sellable("sol ring");

        var card = Assert.Single(result);
        Assert.Equal(listed, card.LotId);
        Assert.Equal(2.5m, card.ListedPrice);
        Assert.Equal(1, card.Available);
    }

    [Fact]
    public void Sellable_MatchesEveryWord_AcrossNameAndSet()
    {
        var c21 = SeedLot("Sol Ring", "C21", listedPrice: 2m);
        SeedLot("Sol Ring", "CMR", listedPrice: 2m);

        Assert.Equal(c21, Assert.Single(Sellable("sol c21")).LotId);
        Assert.Empty(Sellable("50%"));
    }

    [Fact]
    public void Sellable_HidesCopiesAlreadyOnOpenOrders()
    {
        var lot = SeedLot("Sol Ring", "C21", listedPrice: 2m, quantity: 2);
        var order = SeedOrder();

        Assert.IsType<OrderLineDto>(_controller.AddLine(order, new AddOrderLineRequest { LotId = lot, UnitSalePrice = 2m }).Value);
        Assert.Equal(1, Assert.Single(Sellable("sol")).Available);

        Assert.IsType<OrderLineDto>(_controller.AddLine(order, new AddOrderLineRequest { LotId = lot, UnitSalePrice = 2m }).Value);
        Assert.Empty(Sellable("sol"));
    }

    [Fact]
    public void AddLine_RejectsUnlistedCard_AndOverselling()
    {
        var unlisted = SeedLot("Sol Ring", "CMR", listedPrice: null);
        var listed = SeedLot("Sol Ring", "C21", listedPrice: 2m);
        var order = SeedOrder();

        Assert.IsType<BadRequestObjectResult>(
            _controller.AddLine(order, new AddOrderLineRequest { LotId = unlisted, UnitSalePrice = 1m }).Result);
        Assert.IsType<OrderLineDto>(
            _controller.AddLine(order, new AddOrderLineRequest { LotId = listed, UnitSalePrice = 2m }).Value);
        Assert.IsType<ConflictObjectResult>(
            _controller.AddLine(SeedOrder(), new AddOrderLineRequest { LotId = listed, UnitSalePrice = 2m }).Result);
    }

    private IReadOnlyList<SellableListingDto> Sellable(string q) =>
        Assert.IsAssignableFrom<IReadOnlyList<SellableListingDto>>(_controller.Sellable(q, "Mtg").Value);

    private int SeedLot(string name, string setCode, decimal? listedPrice, int quantity = 1)
    {
        using var ctx = new OmniCardDbContext(_opts);
        var product = new Product { Game = CardGame.Mtg, Category = ProductCategory.Single, Name = name, SetCode = setCode, SetName = setCode };
        ctx.Products.Add(product);
        ctx.SaveChanges();
        var lot = new InventoryLot { ProductId = product.Id, Quantity = quantity, Condition = "NM" };
        ctx.Lots.Add(lot);
        ctx.SaveChanges();
        if (listedPrice is decimal price)
        {
            ctx.Listings.Add(new Listing { LotId = lot.Id, Status = ListingStatus.Listed, ListedPrice = price, Quantity = quantity });
            ctx.SaveChanges();
        }
        return lot.Id;
    }

    private int SeedOrder()
    {
        using var ctx = new OmniCardDbContext(_opts);
        var customer = ctx.Customers.FirstOrDefault() ?? ctx.Customers.Add(new Customer { Name = "Ada" }).Entity;
        ctx.SaveChanges();
        var order = new Order { CustomerId = customer.Id, Channel = SalesChannel.Manual, Status = OrderStatus.Created, StageKey = "created" };
        ctx.Orders.Add(order);
        ctx.SaveChanges();
        return order.Id;
    }

    private sealed class Factory(DbContextOptions<OmniCardDbContext> o) : IDbContextFactory<OmniCardDbContext>
    { public OmniCardDbContext CreateDbContext() => new(o); }
}
