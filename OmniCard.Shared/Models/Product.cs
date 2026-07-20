using System.ComponentModel.DataAnnotations.Schema;

namespace OmniCard.Models;

public class Product
{
    public int Id { get; set; }
    public CardGame Game { get; set; }
    public ProductCategory Category { get; set; }
    public string Name { get; set; } = "";
    public string? SetCode { get; set; }
    public string? Upc { get; set; }
    // Single-oriented fields (unused in Phase 1; present so Phase 2 needs no schema change).
    public string? GameCardId { get; set; }
    public string? CollectorNumber { get; set; }
    public string? Rarity { get; set; }
    public bool Foil { get; set; }
    public string? ImageUri { get; set; }
    // Added in the Phase 2a unified-store migration (Task 2) to carry the remaining
    // CollectionCard fields faithfully — the Phase-1 model omitted these.
    public string? SetName { get; set; }
    public string? Color { get; set; }
    public string? CardType { get; set; }

    /// <summary>Cached market price for display/valuation. Not persisted.</summary>
    [NotMapped] public decimal MarketPrice { get; set; }
}
