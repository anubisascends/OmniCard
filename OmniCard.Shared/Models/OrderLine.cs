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
}
