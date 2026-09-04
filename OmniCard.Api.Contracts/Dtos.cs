namespace OmniCard.Api.Contracts;

/// <summary>A page of results plus the total count for the query (before paging).</summary>
public sealed record PagedResult<T>(int Total, int Skip, int Take, IReadOnlyList<T> Items);

/// <summary>A supported card game as exposed to the client. <see cref="Id"/> is the stable enum
/// name (e.g. "Mtg", "OnePiece"); <see cref="DisplayName"/> is the human label.</summary>
public sealed record GameDto(string Id, string DisplayName);

/// <summary>A single collection card (one owned lot of a single printing) for list/detail views.</summary>
public sealed record CardDto
{
    public int Id { get; init; }
    public string Game { get; init; } = "";
    public string GameCardId { get; init; } = "";
    public string Name { get; init; } = "";
    public string SetName { get; init; } = "";
    public string SetCode { get; init; } = "";
    public string Number { get; init; } = "";
    public string Rarity { get; init; } = "";
    public string? ImageUri { get; init; }
    public string? ScanImagePath { get; init; }
    public string Condition { get; init; } = "NM";
    public bool IsFoil { get; init; }
    public string? FoilType { get; init; }
    public int Quantity { get; init; } = 1;
    public IReadOnlyList<string> Tags { get; init; } = [];
    public decimal? PurchasePrice { get; init; }
    public decimal MarketPrice { get; init; }
    public int? ContainerId { get; init; }
    public string? ContainerName { get; init; }
    public int? Page { get; init; }
    public int? Slot { get; init; }
    public string? Section { get; init; }
    public string? Color { get; init; }
    public string? CardType { get; init; }
    public bool IsMissing { get; init; }
    public bool IsTraded { get; init; }
}

/// <summary>A storage location tile for the collection/overview screens.</summary>
public sealed record LocationSummaryDto
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Type { get; init; } = "";
    public bool IsSystem { get; init; }
    public bool IsAlwaysAvailable { get; init; }
    public int CardCount { get; init; }
    public int UniquePrintCount { get; init; }
    public decimal TotalMarketValue { get; init; }
    public decimal TotalPurchaseCost { get; init; }
    public decimal PriceDelta { get; init; }
    public double PriceDeltaPercent { get; init; }
    public string? CoverImageUri { get; init; }
}

/// <summary>One row of a valuation breakdown (by game / category / location).</summary>
public sealed record ValuationLineDto(string Key, int Units, decimal Cost, decimal Market);

/// <summary>Dashboard holdings + realized P&amp;L summary.</summary>
public sealed record DashboardDto
{
    public int TotalUnits { get; init; }
    public decimal TotalCost { get; init; }
    public decimal TotalMarket { get; init; }
    public decimal UnrealizedDelta { get; init; }
    public IReadOnlyList<ValuationLineDto> ByGame { get; init; } = [];
    public IReadOnlyList<ValuationLineDto> ByCategory { get; init; } = [];
    public IReadOnlyList<ValuationLineDto> ByLocation { get; init; } = [];
    public RealizedDto Realized { get; init; } = new();
}

/// <summary>Realized profit summary from completed sales.</summary>
public sealed record RealizedDto
{
    public int TotalSold { get; init; }
    public decimal TotalProceeds { get; init; }
    public decimal TotalCost { get; init; }
    public decimal TotalFees { get; init; }
    public decimal Profit { get; init; }
}

// --- Sets ---

/// <summary>A set/expansion available for a game.</summary>
public sealed record SetInfoDto(string SetCode, string SetName);

/// <summary>A set-completion checklist: every printing with owned quantity + prices.</summary>
public sealed record SetChecklistDto
{
    public string Game { get; init; } = "";
    public string SetCode { get; init; } = "";
    public string SetName { get; init; } = "";
    public int OwnedCount { get; init; }
    public int TotalCount { get; init; }
    public int OwnedPhysicalCount { get; init; }
    public double CompletionPercent { get; init; }
    public IReadOnlyList<SetChecklistCardDto> Cards { get; init; } = [];
}

public sealed record SetChecklistCardDto
{
    public string GameCardId { get; init; } = "";
    public string CollectorNumber { get; init; } = "";
    public string Name { get; init; } = "";
    public string Rarity { get; init; } = "";
    public string? ImageUri { get; init; }
    public int OwnedQuantity { get; init; }
    public decimal? NormalPrice { get; init; }
    public decimal? FoilPrice { get; init; }
    public bool HasFoil { get; init; }
}

// --- Tags ---

public sealed record TagDto(int Id, string Name, int UsageCount);

// --- Write requests ---

public sealed record CreateLocationRequest
{
    public string Name { get; init; } = "";
    /// <summary>ContainerType name: Binder, Box, DeckBox, DisplayCase (Bulk not allowed).</summary>
    public string Type { get; init; } = "Box";
    public int SlotsPerPage { get; init; } = 9;
}

public sealed record RenameRequest
{
    public string Name { get; init; } = "";
}

public sealed record BoolValueRequest
{
    public bool Value { get; init; }
}

public sealed record NameAvailableDto(bool Available);

public sealed record UpdateCardRequest
{
    public string Condition { get; init; } = "NM";
    public bool IsFoil { get; init; }
    public string? FoilType { get; init; }
    public int Quantity { get; init; } = 1;
    public decimal? PurchasePrice { get; init; }
}

public sealed record MoveCardsRequest
{
    public IReadOnlyList<int> CardIds { get; init; } = [];
    public int ContainerId { get; init; }
    public string? Section { get; init; }
}

public sealed record SetTagsRequest
{
    public IReadOnlyList<string> Tags { get; init; } = [];
}

// --- Sales & inventory ---

public sealed record WorkflowLaneDto(string Key, string Name, string Color, string Behavior);

public sealed record OrderDto
{
    public int Id { get; init; }
    public int CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public string Channel { get; init; } = "";
    public string? OrderNumber { get; init; }
    public string OrderDate { get; init; } = "";
    public string Status { get; init; } = "";
    public string? StageKey { get; init; }
    public int LineItemCount { get; init; }
    public decimal LineTotal { get; init; }
    public decimal ShippingChargedToBuyer { get; init; }
    public decimal ShippingCost { get; init; }
    public decimal MarketplaceFees { get; init; }
    public string? TrackingNumber { get; init; }
    public string? Notes { get; init; }
}

public sealed record SetOrderStatusRequest
{
    public string Status { get; init; } = "";
    public string? StageKey { get; init; }
}

public sealed record ActiveListingDto(
    int LotId, string Name, string SetName, string SetCode,
    string? Condition, bool IsFoil, decimal ListedPrice, string Status);

public sealed record CustomerDto
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
}

public sealed record ProductDto
{
    public int Id { get; init; }
    public string Game { get; init; } = "";
    public string Category { get; init; } = "";
    public string Name { get; init; } = "";
    public string? SetName { get; init; }
    public string? SetCode { get; init; }
    public string? Upc { get; init; }
    public decimal? LastMarketPrice { get; init; }
    public int TotalQuantity { get; init; }
}

public sealed record InventoryLotDto
{
    public int Id { get; init; }
    public int ProductId { get; init; }
    public int Quantity { get; init; }
    public decimal? UnitCost { get; init; }
    public int? LocationId { get; init; }
    public string? Source { get; init; }
    public string? AcquisitionDate { get; init; }
}

public sealed record InventoryValuationDto(int TotalUnits, decimal TotalCost, decimal TotalMarket);

// --- Decklist check ---

public sealed record DecklistCheckRequest
{
    public string? Url { get; init; }
    public string? Text { get; init; }
    public string Game { get; init; } = "Mtg";
}

public sealed record DecklistEntryDto(string CardName, int QuantityNeeded, string? SetCode, decimal? MarketPrice, string? ImageUri);

public sealed record DecklistCheckDto
{
    public string DeckName { get; init; } = "";
    public int TotalOwned { get; init; }
    public int TotalMissing { get; init; }
    public int TotalCards { get; init; }
    public decimal EstimatedCost { get; init; }
    public IReadOnlyList<DecklistEntryDto> Owned { get; init; } = [];
    public IReadOnlyList<DecklistEntryDto> Missing { get; init; } = [];
}

// --- Import result ---

public sealed record CsvImportResultDto(int Imported, int TotalRows, string DetectedFormat, IReadOnlyList<string> Warnings);

// --- Auth ---

/// <summary>Whether the site requires a passphrase and whether this session is authenticated.</summary>
public sealed record AuthStatusDto(bool AuthRequired, bool Authenticated);

public sealed record LoginRequest
{
    public string Passphrase { get; init; } = "";
}
