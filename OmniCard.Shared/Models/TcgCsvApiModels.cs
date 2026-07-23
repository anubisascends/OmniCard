namespace OmniCard.Models;

// Response DTOs for tcgcsv.com. TCGCSV returns camelCase JSON — deserialize with a
// CamelCase JsonNamingPolicy. Envelope shape: { success, errors, results[] }.

public sealed class TcgCsvGroupsResponse
{
    public List<TcgCsvGroup> Results { get; set; } = [];
}

public sealed class TcgCsvGroup
{
    public int GroupId { get; set; }
    public string Name { get; set; } = "";
    public string? Abbreviation { get; set; }
}

public sealed class TcgCsvProductsResponse
{
    public List<TcgCsvProduct> Results { get; set; } = [];
}

public sealed class TcgCsvProduct
{
    public int ProductId { get; set; }
    public string Name { get; set; } = "";
    public string? CleanName { get; set; }
    public string? ImageUrl { get; set; }
    public int GroupId { get; set; }
    public string? Url { get; set; }
    public List<TcgCsvExtendedData> ExtendedData { get; set; } = [];
}

public sealed class TcgCsvExtendedData
{
    public string Name { get; set; } = "";
    public string? DisplayName { get; set; }
    public string Value { get; set; } = "";
}

public sealed class TcgCsvPricesResponse
{
    public List<TcgCsvPrice> Results { get; set; } = [];
}

public sealed class TcgCsvPrice
{
    public int ProductId { get; set; }
    public decimal? MarketPrice { get; set; }
    public string? SubTypeName { get; set; }
}
