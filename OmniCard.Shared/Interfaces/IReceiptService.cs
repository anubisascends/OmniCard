using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IReceiptService
{
    /// <summary>Builds the receipt content model for an order. Throws if the order is not found.</summary>
    ReceiptDocument BuildReceipt(int orderId);
}
