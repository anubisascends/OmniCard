using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace OmniCard.Models;

public class Card
{
    // Core
    public Guid Id { get; set; }
    public Guid OracleId { get; set; }
    public List<int>? MultiverseIds { get; set; }
    public int? MtgoId { get; set; }
    public int? MtgoFoilId { get; set; }
    public int? ArenaId { get; set; }
    public int? TcgplayerId { get; set; }
    public int? TcgplayerEtchedId { get; set; }
    public int? CardmarketId { get; set; }
    public string Name { get; set; } = "";
    public string Lang { get; set; } = "";
    public string ReleasedAt { get; set; } = "";
    public string Uri { get; set; } = "";
    public string ScryfallUri { get; set; } = "";
    public string Layout { get; set; } = "";
    public bool HighresImage { get; set; }
    public string ImageStatus { get; set; } = "";
    public string? ResourceId { get; set; }

    // Gameplay
    public string? ManaCost { get; set; }
    public double Cmc { get; set; }
    public string TypeLine { get; set; } = "";
    public string? OracleText { get; set; }
    public string? Power { get; set; }
    public string? Toughness { get; set; }
    public string? Loyalty { get; set; }
    public string? Defense { get; set; }
    public List<string>? Colors { get; set; }
    public List<string> ColorIdentity { get; set; } = [];
    public List<string>? ColorIndicator { get; set; }
    public List<string> Keywords { get; set; } = [];
    public List<string>? ProducedMana { get; set; }

    // Print
    public List<string> Games { get; set; } = [];
    public bool Reserved { get; set; }
    public bool GameChanger { get; set; }
    public bool Foil { get; set; }
    public bool Nonfoil { get; set; }
    public List<string> Finishes { get; set; } = [];
    public bool Oversized { get; set; }
    public bool Promo { get; set; }
    public List<string>? PromoTypes { get; set; }
    public bool Reprint { get; set; }
    public bool Variation { get; set; }
    public Guid SetId { get; set; }
    [JsonPropertyName("set")]
    public string SetCode { get; set; } = "";
    public string SetName { get; set; } = "";
    public string SetType { get; set; } = "";
    public string SetUri { get; set; } = "";
    public string SetSearchUri { get; set; } = "";
    public string ScryfallSetUri { get; set; } = "";
    public string RulingsUri { get; set; } = "";
    public string PrintsSearchUri { get; set; } = "";
    public string CollectorNumber { get; set; } = "";
    public bool Digital { get; set; }
    public string Rarity { get; set; } = "";
    public string? FlavorText { get; set; }
    public string? FlavorName { get; set; }
    public Guid? CardBackId { get; set; }
    public string? Artist { get; set; }
    public List<Guid>? ArtistIds { get; set; }
    public Guid? IllustrationId { get; set; }
    public string BorderColor { get; set; } = "";
    public string Frame { get; set; } = "";
    public List<string>? FrameEffects { get; set; }
    public string? SecurityStamp { get; set; }
    public bool FullArt { get; set; }
    public bool Textless { get; set; }
    public bool Booster { get; set; }
    public bool StorySpotlight { get; set; }
    public string? Watermark { get; set; }
    public bool? ContentWarning { get; set; }
    public List<int>? AttractionLights { get; set; }

    // Non-English
    public string? PrintedName { get; set; }
    public string? PrintedTypeLine { get; set; }
    public string? PrintedText { get; set; }

    // Ranking
    public int? EdhrecRank { get; set; }
    public int? PennyRank { get; set; }

    // Vanguard
    public string? HandModifier { get; set; }
    public string? LifeModifier { get; set; }

    // Owned types (JSON columns in DB)
    public ImageUris? ImageUris { get; set; }
    public Prices? Prices { get; set; }
    [JsonPropertyName("preview")]
    public CardPreview? Preview { get; set; }

    // Dictionary JSON columns
    public Dictionary<string, string> Legalities { get; set; } = new();
    public Dictionary<string, string>? RelatedUris { get; set; }
    public Dictionary<string, string>? PurchaseUris { get; set; }

    // Perceptual hash of the card image (computed locally, not from Scryfall)
    [JsonIgnore]
    public ulong? ImageHash { get; set; }

    // Perceptual hash of the card art (computed locally, not from Scryfall)
    [JsonIgnore]
    public ulong? ArtHash { get; set; }

    // Tracks which IllustrationId was used to compute ImageHash,
    // so we can detect art changes and recompute.
    [JsonIgnore]
    public Guid? HashedIllustrationId { get; set; }

    // Relative path to locally cached card art (e.g., "art/mh3/001.jpg")
    [JsonIgnore]
    public string? LocalImagePath { get; set; }

    // Navigation property (EF Core only, not from Scryfall JSON)
    [JsonIgnore]
    public List<RelatedCard> RelatedCards { get; set; } = [];

    // Transient deserialization properties (not stored in DB)
    [NotMapped]
    public List<CardFace>? CardFaces { get; set; }

    [NotMapped]
    public List<AllPartsEntry>? AllParts { get; set; }
}
