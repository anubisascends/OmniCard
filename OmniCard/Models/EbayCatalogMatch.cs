namespace OmniCard.Models;

public class EbayCatalogMatch
{
    public string ItemId { get; set; } = "";
    public string Title { get; set; } = "";
    public string? ImageUrl { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public string? Condition { get; set; }
    public string? CategoryId { get; set; }
}
