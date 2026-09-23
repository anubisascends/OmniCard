using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using OmniCard.Api.Contracts;
using OmniCard.Data;
using OmniCard.Shared.Inventory;
using OmniCard.Web.Api.Mapping;

namespace OmniCard.Web.Mcp.Tools;

/// <summary>Read-only MCP tools over sealed-product inventory (boxes, cases, packs — non-single
/// product), mirroring the read paths of <c>InventoryController</c>.</summary>
[McpServerToolType]
public sealed class InventoryTools(
    IInventoryService inventory,
    IDbContextFactory<OmniCardDbContext> dbFactory)
{
    [McpServerTool(Name = "list_inventory_products")]
    [Description("List sealed-product inventory (boxes, cases, packs, bundles) with total owned quantity. " +
        "Singles live in the collection and are excluded unless category=Single is passed.")]
    public IReadOnlyList<ProductDto> ListInventoryProducts(
        [Description("Optional game filter: mtg, pokemon, optcg, riftbound, yugioh, finalfantasy.")]
        string? game = null,
        [Description("Optional category: Single, Case, Box, Pack, Deck, Bundle, Other.")]
        string? category = null)
    {
        var g = McpGameParsing.ParseGame(game);
        var cat = ParseCategory(category);
        var products = inventory.GetProducts(g, cat);
        if (cat is null)
            products = products.Where(p => p.Category != ProductCategory.Single).ToList();

        var ids = products.Select(p => p.Id).ToList();
        using var ctx = dbFactory.CreateDbContext();
        var qtyByProduct = ctx.Lots.AsNoTracking()
            .Where(l => ids.Contains(l.ProductId))
            .GroupBy(l => l.ProductId)
            .Select(gr => new { ProductId = gr.Key, Qty = gr.Sum(l => l.Quantity) })
            .ToDictionary(x => x.ProductId, x => x.Qty);

        return products.Select(p => DtoMapping.ToDto(p, qtyByProduct.GetValueOrDefault(p.Id))).ToList();
    }

    [McpServerTool(Name = "list_inventory_lots")]
    [Description("List the individual inventory lots (acquisitions) for one product, with quantity, " +
        "unit cost, source, and location.")]
    public IReadOnlyList<InventoryLotDto> ListInventoryLots(
        [Description("The product id.")] int productId) =>
        inventory.GetLots(productId).Select(DtoMapping.ToDto).ToList();

    [McpServerTool(Name = "inventory_valuation")]
    [Description("Total sealed-product inventory valuation: unit count, total cost, and current market value.")]
    public InventoryValuationDto InventoryValuation(
        [Description("Optional game filter: mtg, pokemon, optcg, riftbound, yugioh, finalfantasy.")]
        string? game = null,
        [Description("Optional category: Single, Case, Box, Pack, Deck, Bundle, Other.")]
        string? category = null) =>
        DtoMapping.ToDto(inventory.GetValuation(McpGameParsing.ParseGame(game), ParseCategory(category)));

    private static ProductCategory? ParseCategory(string? c) =>
        Enum.TryParse<ProductCategory>(c, ignoreCase: true, out var cat) ? cat : null;
}
