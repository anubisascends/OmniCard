namespace OmniCard.Models;

/// <summary>One row of a parsed TCGPlayer Shipping Export, plus how it maps onto existing data.</summary>
public class TcgOrderImportRow
{
    public string OrderNumber { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
    public DateTime OrderDate { get; set; }
    public decimal ShippingFeePaid { get; set; }
    public int ItemCount { get; set; }
    public decimal ValueOfProducts { get; set; }
    public string? TrackingNumber { get; set; }
    public string? Carrier { get; set; }

    /// <summary>Existing customer this row matched (name + postal), if any.</summary>
    public int? MatchedCustomerId { get; set; }
    public bool IsNewCustomer { get; set; }
    /// <summary>The order number already exists in the app — this row is skipped on commit.</summary>
    public bool IsDuplicateOrder { get; set; }
    /// <summary>Whether the user has this row selected for commit (defaults false for duplicates).</summary>
    public bool Include { get; set; } = true;

    /// <summary>Whether the Include checkbox may be toggled (duplicates are locked off).</summary>
    public bool CanInclude => !IsDuplicateOrder;

    /// <summary>Human-readable status shown in the preview grid.</summary>
    public string StatusText =>
        IsDuplicateOrder ? "Already imported"
        : IsNewCustomer ? "New customer · New order"
        : "Matched customer · New order";
}
