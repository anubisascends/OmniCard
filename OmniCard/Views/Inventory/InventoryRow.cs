using OmniCard.Models;

namespace OmniCard.Views.Inventory;

/// <summary>
/// View-only aggregate: a Product plus its owned quantity/cost/market value summed across all lots.
/// </summary>
public sealed record InventoryRow(Product Product, int OwnedQuantity, decimal TotalCost, decimal TotalMarket)
{
    public string Name => Product.Name;
    public CardGame Game => Product.Game;
    public ProductCategory Category => Product.Category;
    public string? SetCode => Product.SetCode;
    public decimal? UnitCost => OwnedQuantity > 0 ? TotalCost / OwnedQuantity : null;
}
