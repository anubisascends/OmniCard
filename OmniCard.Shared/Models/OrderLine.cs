using System.ComponentModel.DataAnnotations.Schema;

namespace OmniCard.Models;

public class OrderLine
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    /// <summary>The lot this line sold (nullable — the lot may be removed once shipped).</summary>
    public int? LotId { get; set; }
    public int? ProductId { get; set; }
    public string NameSnapshot { get; set; } = "";
    public string? SetSnapshot { get; set; }
    public string? ConditionSnapshot { get; set; }
    public bool IsFoilSnapshot { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal UnitSalePrice { get; set; }

    /// <summary>Identifies lines that represent the same distinct item (for grouping identical
    /// lines in the UI/receipt) — not persisted.</summary>
    [NotMapped]
    public string GroupKey => $"{NameSnapshot}|{SetSnapshot}|{ConditionSnapshot}|{IsFoilSnapshot}|{UnitSalePrice}";
}
