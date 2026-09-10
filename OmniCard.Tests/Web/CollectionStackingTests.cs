using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Collection;
using OmniCard.Web.Api.Controllers;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Inventory;

namespace OmniCard.Tests.Web;

/// <summary>
/// Guards the collection "Stack duplicates" grouping. A stack is one unique printing — name + set +
/// collector number + foil — NOT one per name (the earlier bug collapsed different sets/printings/foils
/// of the same card into a single row).
/// </summary>
public class CollectionStackingTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<OmniCardDbContext> _opts;

    public CollectionStackingTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        _opts = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using var ctx = new OmniCardDbContext(_opts);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _conn.Dispose();

    private void AddLot(string name, string setCode, string number, bool foil, string condition = "NM", int quantity = 1)
    {
        using var ctx = new OmniCardDbContext(_opts);
        var product = new Product
        {
            Game = CardGame.Pokemon,
            Category = ProductCategory.Single,
            GameCardId = $"{name}-{setCode}-{number}-{foil}".ToLowerInvariant(),
            Foil = foil,
            Name = name,
            SetCode = setCode,
            SetName = setCode + " Set",
            CollectorNumber = number,
        };
        ctx.Products.Add(product);
        ctx.SaveChanges();
        ctx.Lots.Add(new InventoryLot { ProductId = product.Id, Condition = condition, Quantity = quantity });
        ctx.SaveChanges();
    }

    private (int Total, System.Collections.Generic.List<Shared.Collection.CollectionCard> Cards) Stack()
    {
        using var ctx = new OmniCardDbContext(_opts);
        var query = CollectionQueryBuilder.BuildFilteredQuery(ctx, "", null, null, null);
        return CollectionController.PageStacked(query, 0, 100);
    }

    [Fact]
    public void DifferentSets_StaySeparate()
    {
        AddLot("Pikachu", "BASE", "58", foil: false);
        AddLot("Pikachu", "JUNGLE", "60", foil: false);

        var (total, cards) = Stack();

        Assert.Equal(2, total);
        Assert.Equal(2, cards.Count);
    }

    [Fact]
    public void DifferentFoil_StaySeparate()
    {
        AddLot("Charizard", "BASE", "4", foil: false);
        AddLot("Charizard", "BASE", "4", foil: true);

        var (total, cards) = Stack();

        Assert.Equal(2, total);
    }

    [Fact]
    public void DifferentCollectorNumber_StaySeparate()
    {
        AddLot("Energy", "BASE", "98", foil: false);
        AddLot("Energy", "BASE", "99", foil: false);

        var (total, _) = Stack();

        Assert.Equal(2, total);
    }

    [Fact]
    public void IdenticalPrintings_StackAndSumQuantities()
    {
        AddLot("Squirtle", "BASE", "63", foil: false, quantity: 2);
        AddLot("Squirtle", "BASE", "63", foil: false, quantity: 3);

        var (total, cards) = Stack();

        Assert.Equal(1, total);
        var row = Assert.Single(cards);
        Assert.Equal(5, row.Quantity);
        Assert.Equal(2, row.StackedIds!.Count);
    }
}
