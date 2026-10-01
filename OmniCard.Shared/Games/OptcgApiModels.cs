namespace OmniCard.Shared.Games;

// Response DTOs for the api.poneglyph.one v1 endpoints.
// Deserialized with JsonNamingPolicy.SnakeCaseLower (no [JsonPropertyName] needed).

public sealed class OptcgSetListResponse
{
    public List<OptcgSetSummary> Data { get; set; } = [];
}

public sealed class OptcgSetSummary
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTimeOffset? ReleasedAt { get; set; }
    public int CardCount { get; set; }
}

/// <summary><c>GET /v1/search</c> page — used to fetch a set's cards in a non-English language
/// (<c>/v1/sets/{code}</c> has no <c>lang</c> parameter). Pages cap at 100 cards.</summary>
public sealed class OptcgSearchResponse
{
    public List<OptcgApiCard> Data { get; set; } = [];
    public OptcgPagination Pagination { get; set; } = new();
}

public sealed class OptcgPagination
{
    public int Page { get; set; }
    public int Limit { get; set; }
    public int Total { get; set; }
    public bool HasMore { get; set; }
}

public sealed class OptcgSetDetailResponse
{
    public OptcgSetDetail Data { get; set; } = new();
}

public sealed class OptcgSetDetail
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int CardCount { get; set; }
    public List<OptcgApiCard> Cards { get; set; } = [];
}

public sealed class OptcgApiCard
{
    public string CardNumber { get; set; } = "";
    public string Name { get; set; } = "";
    public string Set { get; set; } = "";
    public string SetName { get; set; } = "";
    public string CardType { get; set; } = "";
    public string? Rarity { get; set; }
    public List<string> Color { get; set; } = [];
    public int? Cost { get; set; }
    public int? Power { get; set; }
    public int? Counter { get; set; }
    public int? Life { get; set; }
    public List<string>? Attribute { get; set; }
    public List<string> Types { get; set; } = [];
    public string? Effect { get; set; }
    public List<OptcgApiVariant> Variants { get; set; } = [];
}

public sealed class OptcgApiVariant
{
    public int Index { get; set; }
    public string? Label { get; set; }
    public string? Artist { get; set; }
    public OptcgApiImages Images { get; set; } = new();
    public OptcgApiMarket Market { get; set; } = new();
}

public sealed class OptcgApiImages
{
    public OptcgApiStockImages Stock { get; set; } = new();
    public OptcgApiScanImages Scan { get; set; } = new();
}

public sealed class OptcgApiStockImages
{
    public string? Full { get; set; }
    public string? Thumb { get; set; }
}

public sealed class OptcgApiScanImages
{
    public string? Display { get; set; }
    public string? Full { get; set; }
    public string? Thumb { get; set; }
}

public sealed class OptcgApiMarket
{
    public string? MarketPrice { get; set; }
    public string? LowPrice { get; set; }
    public string? MidPrice { get; set; }
    public string? HighPrice { get; set; }
}
