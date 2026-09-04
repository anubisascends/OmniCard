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

    private static ProductCategory? ParseCategory(string? c) =>
        Enum.TryParse<ProductCategory>(c, ignoreCase: true, out var cat) ? cat : null;
}
