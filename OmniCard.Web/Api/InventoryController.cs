using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OmniCard.Api.Contracts;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Web.Api;

/// <summary>Sealed-product inventory: products (non-single), their lots, and total valuation.</summary>
public sealed class InventoryController(
    IInventoryService inventory,
    IDbContextFactory<OmniCardDbContext> dbFactory) : ApiControllerBase
{
    /// <summary>Products, optionally filtered by game/category. Singles are excluded by default
    /// (they live in the Collection); pass <c>category=Single</c> to include them.</summary>
    [HttpGet("products")]
    public ActionResult<IReadOnlyList<ProductDto>> Products(
        [FromQuery] string? game, [FromQuery] string? category)
    {
        var g = LocationsController.ParseGame(game);
        var cat = ParseCategory(category);
        var products = inventory.GetProducts(g, cat);
        if (cat is null)
            products = products.Where(p => p.Category != ProductCategory.Single).ToList();

        // Total owned quantity per product, in one grouped query.
        var ids = products.Select(p => p.Id).ToList();
        using var ctx = dbFactory.CreateDbContext();
        var qtyByProduct = ctx.Lots.AsNoTracking()
            .Where(l => ids.Contains(l.ProductId))
            .GroupBy(l => l.ProductId)
            .Select(gr => new { ProductId = gr.Key, Qty = gr.Sum(l => l.Quantity) })
            .ToDictionary(x => x.ProductId, x => x.Qty);

        return products.Select(p => DtoMapping.ToDto(p, qtyByProduct.GetValueOrDefault(p.Id))).ToList();
    }

    [HttpGet("products/{id:int}/lots")]
    public ActionResult<IReadOnlyList<InventoryLotDto>> Lots(int id) =>
        inventory.GetLots(id).Select(DtoMapping.ToDto).ToList();

    [HttpGet("valuation")]
    public ActionResult<InventoryValuationDto> Valuation(
        [FromQuery] string? game, [FromQuery] string? category) =>
        DtoMapping.ToDto(inventory.GetValuation(LocationsController.ParseGame(game), ParseCategory(category)));

    // --- writes ---

    [HttpPost("products")]
    public ActionResult<ProductDto> CreateProduct([FromBody] ProductUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Name is required" });
        if (LocationsController.ParseGame(request.Game) is not { } game)
            return BadRequest(new { error = $"Unknown game '{request.Game}'" });
        var category = ParseCategory(request.Category) ?? ProductCategory.Box;
        if (category == ProductCategory.Single)
            return BadRequest(new { error = "Singles are managed in the Collection, not Inventory" });

        var created = inventory.CreateProduct(new Product
        {
            Game = game,
            Category = category,
            Name = request.Name.Trim(),
            SetName = request.SetName,
            SetCode = request.SetCode,
            Upc = request.Upc,
            LastMarketPrice = request.LastMarketPrice,
        });
        return CreatedAtAction(nameof(Lots), new { id = created.Id }, DtoMapping.ToDto(created, 0));
    }

    [HttpPut("products/{id:int}")]
    public IActionResult UpdateProduct(int id, [FromBody] ProductUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Name is required" });
        if (LocationsController.ParseGame(request.Game) is not { } game)
            return BadRequest(new { error = $"Unknown game '{request.Game}'" });
        var category = ParseCategory(request.Category) ?? ProductCategory.Box;

        // Load-then-patch so the SQL Server RowVersion concurrency token is honored.
        using var ctx = dbFactory.CreateDbContext();
        var existing = ctx.Products.FirstOrDefault(p => p.Id == id);
        if (existing is null)
            return NotFound();
        if (existing.Category == ProductCategory.Single)
            return BadRequest(new { error = "Singles are managed in the Collection, not Inventory" });

        existing.Game = game;
        existing.Category = category;
        existing.Name = request.Name.Trim();
        existing.SetName = request.SetName;
        existing.SetCode = request.SetCode;
        existing.Upc = request.Upc;
        existing.LastMarketPrice = request.LastMarketPrice;
        ctx.SaveChanges();
        return NoContent();
    }

    [HttpDelete("products/{id:int}")]
    public IActionResult DeleteProduct(int id)
    {
        inventory.DeleteProduct(id);
        return NoContent();
    }

    [HttpPost("products/{id:int}/lots")]
    public ActionResult<InventoryLotDto> AddLot(int id, [FromBody] LotUpsertRequest request)
    {
        if (request.Quantity <= 0)
            return BadRequest(new { error = "Quantity must be at least 1" });

        using (var ctx = dbFactory.CreateDbContext())
        {
            if (!ctx.Products.Any(p => p.Id == id))
                return NotFound();
        }

        var lot = inventory.AddLot(id, request.Quantity, request.UnitCost, request.LocationId, request.Source);
        return DtoMapping.ToDto(lot);
    }

    [HttpPut("lots/{id:int}")]
    public IActionResult UpdateLot(int id, [FromBody] LotUpsertRequest request)
    {
        if (request.Quantity <= 0)
            return BadRequest(new { error = "Quantity must be at least 1" });

        using var ctx = dbFactory.CreateDbContext();
        var lot = ctx.Lots.FirstOrDefault(l => l.Id == id);
        if (lot is null)
            return NotFound();

        lot.Quantity = request.Quantity;
        lot.UnitCost = request.UnitCost;
        lot.LocationId = request.LocationId;
        lot.Source = request.Source;
        ctx.SaveChanges();
        return NoContent();
    }

    [HttpDelete("lots/{id:int}")]
    public IActionResult DeleteLot(int id)
    {
        inventory.DeleteLot(id);
        return NoContent();
    }

    private static ProductCategory? ParseCategory(string? c) =>
        Enum.TryParse<ProductCategory>(c, ignoreCase: true, out var cat) ? cat : null;
}
