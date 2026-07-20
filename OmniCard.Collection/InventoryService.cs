using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

public class InventoryService(IDbContextFactory<OmniCardDbContext> dbContextFactory) : IInventoryService
{
    public List<Product> GetProducts(CardGame? game = null, ProductCategory? category = null)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var query = ctx.Products.AsNoTracking().AsQueryable();
        if (game.HasValue) query = query.Where(p => p.Game == game.Value);
        if (category.HasValue) query = query.Where(p => p.Category == category.Value);
        return query.OrderBy(p => p.Name).ToList();
    }

    public Product? FindProductByUpc(string upc)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        return ctx.Products.AsNoTracking().FirstOrDefault(p => p.Upc == upc);
    }

    public Product CreateProduct(Product product)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        ctx.Products.Add(product);
        ctx.SaveChanges();
        return product;
    }

    public void UpdateProduct(Product product)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        ctx.Products.Update(product);
        ctx.SaveChanges();
    }

    public void DeleteProduct(int productId)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var product = ctx.Products.Find(productId);
        if (product is null) return;

        var movements = ctx.Movements.Where(m => m.ProductId == productId);
        ctx.Movements.RemoveRange(movements);

        // Lots cascade-delete via the FK, but EF still needs to know about them
        // when the tracked product is removed in the same context.
        ctx.Products.Remove(product);
        ctx.SaveChanges();
    }

    public List<InventoryLot> GetLots(int productId)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        return ctx.Lots.AsNoTracking()
            .Where(l => l.ProductId == productId)
            .OrderBy(l => l.AcquisitionDate)
            .ToList();
    }

    public InventoryLot AddLot(int productId, int quantity, decimal? unitCost, int? locationId, string? source, DateTime? acquisitionDate = null)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var lot = new InventoryLot
        {
            ProductId = productId,
            Quantity = quantity,
            UnitCost = unitCost,
            LocationId = locationId,
            Source = source,
            AcquisitionDate = acquisitionDate ?? DateTime.UtcNow,
        };
        ctx.Lots.Add(lot);
        ctx.SaveChanges();

        // The Acquire movement timestamp always reflects "now" (when the entry was recorded),
        // independent of the lot's (possibly backdated) AcquisitionDate.
        ctx.Movements.Add(new InventoryMovement
        {
            ProductId = productId,
            LotId = lot.Id,
            Type = MovementType.Acquire,
            Quantity = quantity,
            UnitValue = unitCost,
        });
        ctx.SaveChanges();

        return lot;
    }

    public void UpdateLot(InventoryLot lot)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        ctx.Lots.Update(lot);
        ctx.SaveChanges();
    }

    public void DeleteLot(int lotId)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var lot = ctx.Lots.Find(lotId);
        if (lot is null) return;
        ctx.Lots.Remove(lot);
        ctx.SaveChanges();
    }

    public void OpenUnits(int lotId, int quantity, string? note)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var lot = ctx.Lots.Find(lotId);
        if (lot is null) return;
        if (quantity <= 0 || quantity > lot.Quantity)
            throw new InvalidOperationException($"Cannot open {quantity} units; only {lot.Quantity} available.");

        var productId = lot.ProductId;
        lot.Quantity -= quantity;

        if (lot.Quantity <= 0)
        {
            ctx.Lots.Remove(lot);
        }

        ctx.Movements.Add(new InventoryMovement
        {
            ProductId = productId,
            LotId = lotId,
            Type = MovementType.Open,
            Quantity = quantity,
            Note = note,
        });

        ctx.SaveChanges();
    }

    public IReadOnlyList<InventoryMovement> GetMovements(int productId)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        return ctx.Movements.AsNoTracking()
            .Where(m => m.ProductId == productId)
            .OrderBy(m => m.Timestamp)
            .ToList();
    }

    public InventoryValuation GetValuation(CardGame? game = null, ProductCategory? category = null)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var query = ctx.Lots.AsNoTracking().Include(l => l.Product).AsQueryable();
        if (game.HasValue) query = query.Where(l => l.Product.Game == game.Value);
        if (category.HasValue) query = query.Where(l => l.Product.Category == category.Value);

        var lots = query.ToList();
        var totalUnits = lots.Sum(l => l.Quantity);
        var totalCost = lots.Sum(l => l.Quantity * (l.UnitCost ?? 0m));
        var totalMarket = lots.Sum(l => l.Quantity * l.Product.MarketPrice);

        return new InventoryValuation(totalUnits, totalCost, totalMarket);
    }
}
