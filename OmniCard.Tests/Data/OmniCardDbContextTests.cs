using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Tests.Data;

public class OmniCardDbContextTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<OmniCardDbContext> _options;

    public OmniCardDbContextTests()
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

    [Fact]
    public void Product_CategoryAndGame_RoundTripAsStrings()
    {
        int productId;
        using (var ctx = new OmniCardDbContext(_options))
        {
            var product = new Product
            {
                Game = CardGame.OnePiece,
                Category = ProductCategory.Bundle,
                Name = "Booster Bundle",
            };
            ctx.Products.Add(product);
            ctx.SaveChanges();
            productId = product.Id;
        }

        // Confirm the raw column values are the enum names (string), not numeric codes.
        using (var command = _connection.CreateCommand())
        {
            command.CommandText = "SELECT Game, Category FROM Products WHERE Id = $id";
            command.Parameters.AddWithValue("$id", productId);
            using var reader = command.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal("OnePiece", reader.GetString(0));
            Assert.Equal("Bundle", reader.GetString(1));
        }

        using var readCtx = new OmniCardDbContext(_options);
        var reloaded = readCtx.Products.AsNoTracking().Single(p => p.Id == productId);
        Assert.Equal(CardGame.OnePiece, reloaded.Game);
        Assert.Equal(ProductCategory.Bundle, reloaded.Category);
    }

    [Fact]
    public void Movement_Type_RoundTripsAsString()
    {
        int movementId;
        using (var ctx = new OmniCardDbContext(_options))
        {
            var product = new Product { Game = CardGame.Mtg, Category = ProductCategory.Box, Name = "Test Box" };
            ctx.Products.Add(product);
            ctx.SaveChanges();

            var movement = new InventoryMovement
            {
                ProductId = product.Id,
                Type = MovementType.Adjust,
                Quantity = 2,
            };
            ctx.Movements.Add(movement);
            ctx.SaveChanges();
            movementId = movement.Id;
        }

        using (var command = _connection.CreateCommand())
        {
            command.CommandText = "SELECT Type FROM Movements WHERE Id = $id";
            command.Parameters.AddWithValue("$id", movementId);
            using var reader = command.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal("Adjust", reader.GetString(0));
        }

        using var readCtx = new OmniCardDbContext(_options);
        var reloaded = readCtx.Movements.AsNoTracking().Single(m => m.Id == movementId);
        Assert.Equal(MovementType.Adjust, reloaded.Type);
    }

    [Fact]
    public void DeletingProduct_CascadesLots_ViaForeignKey()
    {
        int productId;
        int lotId;
        using (var ctx = new OmniCardDbContext(_options))
        {
            var product = new Product { Game = CardGame.Mtg, Category = ProductCategory.Box, Name = "Test Box" };
            ctx.Products.Add(product);
            ctx.SaveChanges();
            productId = product.Id;

            var lot = new InventoryLot { ProductId = productId, Quantity = 3 };
            ctx.Lots.Add(lot);
            ctx.SaveChanges();
            lotId = lot.Id;
        }

        // Delete the product without touching the lot explicitly; the FK's cascade delete
        // behavior (configured in OmniCardDbContext.OnModelCreating) should remove it too.
        using (var ctx = new OmniCardDbContext(_options))
        {
            var product = ctx.Products.Single(p => p.Id == productId);
            ctx.Products.Remove(product);
            ctx.SaveChanges();
        }

        using var readCtx = new OmniCardDbContext(_options);
        Assert.False(readCtx.Products.Any(p => p.Id == productId));
        Assert.False(readCtx.Lots.Any(l => l.Id == lotId));
    }

    [Fact]
    public void DeletingProduct_DoesNotCascadeMovements_NoForeignKeyConfigured()
    {
        int productId;
        int movementId;
        using (var ctx = new OmniCardDbContext(_options))
        {
            var product = new Product { Game = CardGame.Mtg, Category = ProductCategory.Box, Name = "Test Box" };
            ctx.Products.Add(product);
            ctx.SaveChanges();
            productId = product.Id;

            var movement = new InventoryMovement
            {
                ProductId = productId,
                Type = MovementType.Acquire,
                Quantity = 1,
            };
            ctx.Movements.Add(movement);
            ctx.SaveChanges();
            movementId = movement.Id;
        }

        // Movements has no FK relationship to Product in OnModelCreating, so deleting the
        // product must not fail (no FK constraint) and must not remove the movement row —
        // callers (InventoryService.DeleteProduct) are responsible for cleaning those up explicitly.
        using (var ctx = new OmniCardDbContext(_options))
        {
            var product = ctx.Products.Single(p => p.Id == productId);
            ctx.Products.Remove(product);
            ctx.SaveChanges();
        }

        using var readCtx = new OmniCardDbContext(_options);
        Assert.False(readCtx.Products.Any(p => p.Id == productId));

        var reloadedMovement = readCtx.Movements.AsNoTracking().Single(m => m.Id == movementId);
        Assert.Equal(productId, reloadedMovement.ProductId);
        Assert.Equal(MovementType.Acquire, reloadedMovement.Type);
    }
}
