using System.Text.Json.Serialization;

namespace OmniCard.Models;

public class OptcgCard
{
    [JsonPropertyName("card_set_id")]
    public string CardSetId { get; set; } = "";

    [JsonPropertyName("card_name")]
    public string CardName { get; set; } = "";

    [JsonPropertyName("set_id")]
    public string SetId { get; set; } = "";

    [JsonPropertyName("set_name")]
    public string SetName { get; set; } = "";

    [JsonPropertyName("rarity")]
    public string Rarity { get; set; } = "";

    [JsonPropertyName("card_color")]
    public string CardColor { get; set; } = "";

    [JsonPropertyName("card_type")]
    public string CardType { get; set; } = "";

    [JsonPropertyName("card_cost")]
    public string? CardCost { get; set; }

    [JsonPropertyName("card_power")]
    public string? CardPower { get; set; }

    [JsonPropertyName("life")]
    public string? Life { get; set; }

    [JsonPropertyName("card_text")]
    public string? CardText { get; set; }

    [JsonPropertyName("sub_types")]
    public string? SubTypes { get; set; }

    [JsonPropertyName("attribute")]
    public string? Attribute { get; set; }

    [JsonPropertyName("counter_amount")]
    public int? CounterAmount { get; set; }

    [JsonPropertyName("inventory_price")]
    public decimal? InventoryPrice { get; set; }

    [JsonPropertyName("market_price")]
    public decimal? MarketPrice { get; set; }

    [JsonPropertyName("card_image_id")]
    public string? CardImageId { get; set; }

    [JsonPropertyName("card_image")]
    public string? CardImageUri { get; set; }

    [JsonPropertyName("date_scraped")]
    public string? DateScraped { get; set; }

    // Computed locally, not from API
    [JsonIgnore]
    public ulong? ImageHash { get; set; }
}
