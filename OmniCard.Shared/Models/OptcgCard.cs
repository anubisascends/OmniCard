namespace OmniCard.Models;

// Persistence entity for OPTCG cards. One row per card variant (printing).
// Populated by OptcgService from api.poneglyph.one DTOs (see OptcgApiModels).
public class OptcgCard
{
    // Variant uid: bare card number for the base printing (index 0),
    // "{CardNumber}_p{index}" for alternate arts.
    public string CardSetId { get; set; } = "";

    // Printed collector number, e.g. "OP01-001" (shared across variants).
    public string CardNumber { get; set; } = "";

    public int VariantIndex { get; set; }
    public string? VariantLabel { get; set; }
    public string? Artist { get; set; }

    public string CardName { get; set; } = "";
    public string SetId { get; set; } = "";
    public string SetName { get; set; } = "";
    public string Rarity { get; set; } = "";
    public string CardColor { get; set; } = "";
    public string CardType { get; set; } = "";
    public string? CardCost { get; set; }
    public string? CardPower { get; set; }
    public string? Life { get; set; }
    public string? CardText { get; set; }
    public string? SubTypes { get; set; }
    public string? Attribute { get; set; }
    public int? CounterAmount { get; set; }
    public decimal? InventoryPrice { get; set; }
    public decimal? MarketPrice { get; set; }
    public string? CardImageId { get; set; }
    public string? CardImageUri { get; set; }
    public string? DateScraped { get; set; }

    // Computed locally, not from API
    public ulong? ImageHash { get; set; }
    public string? LocalImagePath { get; set; }
}
