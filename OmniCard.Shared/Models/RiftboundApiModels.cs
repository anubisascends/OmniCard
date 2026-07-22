namespace OmniCard.Models;

// Response DTOs for api.riftcodex.com.
// Deserialized with JsonNamingPolicy.SnakeCaseLower (no [JsonPropertyName] needed).
// The API's `new` (C# keyword) and `cardmarket_id` (string|array) fields are intentionally
// unmapped — System.Text.Json ignores unmapped JSON properties by default.

public sealed class RiftboundCardListResponse
{
    public List<RiftboundApiCard> Items { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; }
    public int Size { get; set; }
    public int Pages { get; set; }
}

public sealed class RiftboundApiCard
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string RiftboundId { get; set; } = "";
    public string? TcgplayerId { get; set; }
    public int CollectorNumber { get; set; }
    public RiftboundApiAttributes Attributes { get; set; } = new();
    public RiftboundApiClassification Classification { get; set; } = new();
    public RiftboundApiText Text { get; set; } = new();
    public RiftboundApiCardSet Set { get; set; } = new();
    public RiftboundApiMedia Media { get; set; } = new();
    public string Orientation { get; set; } = "portrait";
    public RiftboundApiMetadata Metadata { get; set; } = new();
}

public sealed class RiftboundApiAttributes
{
    public int? Energy { get; set; }
    public int? Might { get; set; }
    public int? Power { get; set; }
}

public sealed class RiftboundApiClassification
{
    public string Type { get; set; } = "";
    public string? Supertype { get; set; }
    public string? Rarity { get; set; }
    public List<string> Domain { get; set; } = [];
}

public sealed class RiftboundApiText
{
    public string? Rich { get; set; }
    public string? Plain { get; set; }
    public string? Flavour { get; set; }
}

public sealed class RiftboundApiCardSet
{
    public string SetId { get; set; } = "";
    public string Label { get; set; } = "";
}

public sealed class RiftboundApiMedia
{
    public string? ImageUrl { get; set; }
    public string? Artist { get; set; }
    public string? AccessibilityText { get; set; }
}

public sealed class RiftboundApiMetadata
{
    public string? CleanName { get; set; }
    public string? UpdatedOn { get; set; }
    public bool AlternateArt { get; set; }
    public bool Overnumbered { get; set; }
    public bool Signature { get; set; }
}

public sealed class RiftboundSetListResponse
{
    public List<RiftboundApiSetSummary> Items { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; }
    public int Size { get; set; }
    public int Pages { get; set; }
}

public sealed class RiftboundApiSetSummary
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string SetId { get; set; } = "";
    public int CardCount { get; set; }
    public string? TcgplayerId { get; set; }
    public DateTimeOffset? PublishedOn { get; set; }
}
