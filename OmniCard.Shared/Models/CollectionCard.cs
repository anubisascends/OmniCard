using System.ComponentModel.DataAnnotations.Schema;

namespace OmniCard.Models;

public class CollectionCard
{
    public int Id { get; set; }
    public CardGame Game { get; set; }
    public string GameCardId { get; set; } = "";
    public string Name { get; set; } = "";
    public string SetName { get; set; } = "";
    public string SetCode { get; set; } = "";
    public string Number { get; set; } = "";
    public string Rarity { get; set; } = "";
    public string? ImageUri { get; set; }
    public string? ScanImagePath { get; set; }
    public string Condition { get; set; } = "NM";
    public bool IsFoil { get; set; }
    public decimal? PurchasePrice { get; set; }
    public DateTime DateAdded { get; set; } = DateTime.UtcNow;

    public int? ContainerId { get; set; }
    public StorageContainer? Container { get; set; }
    public int? Page { get; set; }
    public int? Slot { get; set; }
    public string? Section { get; set; }
    public string? Color { get; set; }
    public string? CardType { get; set; }
    public EbayListing? EbayListing { get; set; }

    /// <summary>Cached market price for display and sorting. Not persisted.</summary>
    [NotMapped]
    public decimal MarketPrice { get; set; }

    /// <summary>Display-only quantity when stacking identical cards. Not persisted.</summary>
    [NotMapped]
    public int Quantity { get; set; } = 1;

    /// <summary>All card IDs in this stack (including this card). Only populated when stacked.</summary>
    [NotMapped]
    public List<int>? StackedIds { get; set; }
}
