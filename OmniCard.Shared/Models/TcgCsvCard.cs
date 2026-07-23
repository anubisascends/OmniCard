namespace OmniCard.Models;

// Shared persistence entity for all TCGCSV-backed games (Pokémon, Yu-Gi-Oh!, Final Fantasy TCG).
// One row per printing (per TCGplayer productId). Populated by TcgCsvGameService subclasses.
public class TcgCsvCard
{
    // TCGplayer productId — unique per printing. Primary key. Exposed as GameCardId (ToString()).
    public int ProductId { get; set; }

    public CardGame Game { get; set; }

    public string Name { get; set; } = "";
    public string? CleanName { get; set; }

    public int GroupId { get; set; }              // TCGCSV group (set) id, used for API fetches
    public string SetCode { get; set; } = "";      // group abbreviation, or GroupId as string when blank
    public string SetName { get; set; } = "";

    public string CollectorNumber { get; set; } = ""; // extendedData "Number" (e.g. "123/198", "1-001H")
    public string Rarity { get; set; } = "";
    public string CardType { get; set; } = "";

    public string? ImageUrl { get; set; }
    public string? Url { get; set; }

    // Full extendedData array serialized verbatim as JSON — retains every game-specific attribute.
    public string? ExtendedDataJson { get; set; }

    // Computed locally, not from API.
    public ulong? ImageHash { get; set; }
    public ulong? EdgeHash { get; set; }
    public string? LocalImagePath { get; set; }

    // Pricing — populated from TCGCSV prices, keyed by ProductId.
    public decimal? MarketPrice { get; set; }        // "Normal" subtype market price
    public decimal? FoilMarketPrice { get; set; }    // game's principal foil subtype market price
    public DateTime? PriceUpdatedAt { get; set; }     // UTC
}
