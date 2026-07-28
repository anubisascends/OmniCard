using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface IEbaySellingSettingsService
{
    EbaySellingSettings Get();
    void Save(EbaySellingSettings settings);

    /// <summary>Location provisioned and fulfillment + return policies exist
    /// (payment policy is optional — sandbox managed-payments may block it).</summary>
    bool IsSetupComplete();
}
