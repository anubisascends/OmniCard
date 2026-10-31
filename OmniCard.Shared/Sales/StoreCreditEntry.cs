namespace OmniCard.Shared.Sales;

/// <summary>
/// One movement on a customer's store-credit ledger: credit issued (positive — e.g. trading in cards at
/// intake) or spent / taken back (negative). The balance is the sum of a customer's entries; entries are
/// never edited or deleted, so the ledger doubles as the audit trail.
/// </summary>
public class StoreCreditEntry
{
    public const int MaxReasonLength = 300;

    public int Id { get; set; }
    public int CustomerId { get; set; }
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public string CreatedByName { get; set; } = "";
    public string Reason { get; set; } = "";
    /// <summary>The intake that issued this credit, if any.</summary>
    public int? IntakeId { get; set; }
}
