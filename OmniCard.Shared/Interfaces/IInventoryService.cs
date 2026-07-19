using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IInventoryService
{
    List<Product> GetProducts(CardGame? game = null, ProductCategory? category = null);
    Product? FindProductByUpc(string upc);
    Product CreateProduct(Product product);
    void UpdateProduct(Product product);
    void DeleteProduct(int productId);
    List<InventoryLot> GetLots(int productId);
    InventoryLot AddLot(int productId, int quantity, decimal? unitCost, int? locationId, string? source);
    void UpdateLot(InventoryLot lot);
    void DeleteLot(int lotId);
    void OpenUnits(int lotId, int quantity, string? note);
    IReadOnlyList<InventoryMovement> GetMovements(int productId);
    InventoryValuation GetValuation(CardGame? game = null, ProductCategory? category = null);
}
