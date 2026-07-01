namespace OmniCard.Models;

public class SealedProductInstance
{
    public int Id { get; set; }
    public int TemplateId { get; set; }
    public SealedProductTemplate Template { get; set; } = null!;
    public decimal? PurchasePrice { get; set; }
    public DateTime DateAdded { get; set; } = DateTime.UtcNow;
}
