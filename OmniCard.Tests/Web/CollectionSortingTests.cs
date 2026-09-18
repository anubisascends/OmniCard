using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Collection;
using OmniCard.Web.Api.Controllers;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
using OmniCard.Shared.Inventory;

namespace OmniCard.Tests.Web;

/// <summary>
/// Guards server-side collection sorting. The bug this locks in: sorting was done client-side over just
/// the current page, so "Market Price desc" only ever surfaced the priciest card on page 1 (names near
/// "A"), not the priciest in the whole collection. Sorting must now order the entire filtered set before
/// paging — including market price, which isn't a DB column and is hydrated over the full set.
/// </summary>
public class CollectionSortingTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<OmniCardDbContext> _opts;

    public CollectionSortingTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using var ctx = new OmniCardDbContext(_opts);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _conn.Dispose();

    private void AddLot(string name, int quantity = 1)
    {
        using var ctx = new OmniCardDbContext(_opts);
        var product = new Product
        {
            Game = CardGame.Pokemon,
            Category = ProductCategory.Single,
            GameCardId = name.ToLowerInvariant(),
            Foil = false,
            Name = name,
            SetCode = "SET",
            SetName = "Set",
            CollectorNumber = "1",
        };
        ctx.Products.Add(product);
        ctx.SaveChanges();
        ctx.Lots.Add(new InventoryLot { ProductId = product.Id, Condition = "NM", Quantity = quantity });
        ctx.SaveChanges();
    }

    private IQueryable<CollectionCard> Query(OmniCardDbContext ctx) =>
        CollectionQueryBuilder.BuildFilteredQuery(ctx, "", null, null, null);

    // Stand-in for the game-catalog price lookup: price each card by the numeric suffix in its name so
    // the priciest card ("Card 99") is deterministic and sits last alphabetically — i.e. off page 1.
    private static void PriceByNameSuffix(IReadOnlyCollection<CollectionCard> cards)
    {
        foreach (var c in cards)
        {
            var digits = new string(c.Name.Where(char.IsDigit).ToArray());
            c.MarketPrice = int.TryParse(digits, out var n) ? n : 0m;
        }
    }

    [Fact]
    public void MarketPriceDesc_ReturnsGlobalTop_NotPageTop()
    {
        // 30 cards; the priciest ("Card 29") sorts alphabetically last, so a page-local sort would miss it.
        for (var i = 0; i < 30; i++)
            AddLot($"Card {i:00}");

        using var ctx = new OmniCardDbContext(_opts);
        var (total, page) = CollectionController.PageFlat(
            Query(ctx), skip: 0, take: 5, sort: "marketprice", desc: true, hydratePrices: PriceByNameSuffix);

        Assert.Equal(30, total);
        Assert.Equal(29m, page[0].MarketPrice);
        Assert.Equal("Card 29", page[0].Name);
        // Strictly descending across the page.
        Assert.True(page.Zip(page.Skip(1)).All(pair => pair.First.MarketPrice >= pair.Second.MarketPrice));
    }

    [Fact]
    public void MarketPriceDesc_Stacked_ReturnsGlobalTop()
    {
        for (var i = 0; i < 30; i++)
            AddLot($"Card {i:00}");

        using var ctx = new OmniCardDbContext(_opts);
        var (total, page) = CollectionController.PageStacked(
            Query(ctx), skip: 0, take: 5, sort: "marketprice", desc: true, hydratePrices: PriceByNameSuffix);

        Assert.Equal(30, total);
        Assert.Equal("Card 29", page[0].Name);
        Assert.Equal(29m, page[0].MarketPrice);
    }

    [Fact]
    public void QuantityDesc_OrdersByQuantityAcrossWholeSet()
    {
        AddLot("Aardvark", quantity: 1);
        AddLot("Middle", quantity: 9);
        AddLot("Zebra", quantity: 4);

        using var ctx = new OmniCardDbContext(_opts);
        var (_, page) = CollectionController.PageFlat(Query(ctx), skip: 0, take: 3, sort: "quantity", desc: true);

        Assert.Equal("Middle", page[0].Name);
        Assert.Equal("Zebra", page[1].Name);
        Assert.Equal("Aardvark", page[2].Name);
    }
}
