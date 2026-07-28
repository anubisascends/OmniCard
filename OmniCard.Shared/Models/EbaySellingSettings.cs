namespace OmniCard.Models;

public enum ReturnShippingPayer { Buyer, Seller }

public class EbaySellingSettings
{
    // Location
    public string MerchantLocationKey { get; set; } = "omnicard-primary";
    public bool LocationProvisioned { get; set; }
    public string? LocationName { get; set; } = "OmniCard Primary";
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; } // ISO 3166-1 alpha-2, e.g. "US"
    public string? Phone { get; set; }

    // Fulfillment (shipping) policy inputs
    public bool FreeShipping { get; set; } = true;
    public decimal ShippingCost { get; set; }
    public int HandlingTimeDays { get; set; } = 1;

    // Return policy inputs
    public bool ReturnsAccepted { get; set; } = true;
    public int ReturnWindowDays { get; set; } = 30;
    public ReturnShippingPayer ReturnShippingPaidBy { get; set; } = ReturnShippingPayer.Buyer;

    // Results (written by setup)
    public string? FulfillmentPolicyId { get; set; }
    public string? PaymentPolicyId { get; set; }
    public string? ReturnPolicyId { get; set; }
    public System.DateTime? SetupCompletedAt { get; set; }
}
