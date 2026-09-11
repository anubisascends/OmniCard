namespace OmniCard.Api.Contracts;

/// <summary>A page of results plus the total count for the query (before paging).</summary>
public sealed record PagedResult<T>(int Total, int Skip, int Take, IReadOnlyList<T> Items);

/// <summary>A supported card game as exposed to the client. <see cref="Id"/> is the stable enum
/// name (e.g. "Mtg", "OnePiece"); <see cref="DisplayName"/> is the human label.</summary>
public sealed record GameDto(string Id, string DisplayName);

/// <summary>One third-party (or first-party) software component OmniCard ships or runs on, for the
/// Administration ▸ Components license inventory. <see cref="HomepageUrl"/> / <see cref="LicenseUrl"/>
/// may be null when no canonical link exists.</summary>
public sealed record ComponentDto(
    string Category,
    string Name,
    string Version,
    string License,
    string? HomepageUrl,
    string? LicenseUrl);

/// <summary>The searchable fields for a game, driving the search box's dynamic placeholder and syntax
/// help popover. <see cref="Game"/> is the enum name, or "all" for the game-agnostic core schema.</summary>
public sealed record SearchSchemaDto(string Game, IReadOnlyList<SearchFieldDto> Fields);

/// <summary>One searchable field: its canonical token, accepted aliases, value shorthands
/// (e.g. "f=Fire"), a human description, an example query, and its kind (core/game/special).</summary>
public sealed record SearchFieldDto(
    string Canonical,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string> ValueAliases,
    string Description,
    string Example,
    string Kind);

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
    /// <summary>When the row is a stack (grouped duplicates), the underlying lot ids it represents;
    /// empty for a single-lot row. Used by the client for bulk move/delete on a stack.</summary>
    public IReadOnlyList<int> StackedIds { get; init; } = [];
    public IReadOnlyList<string> Tags { get; init; } = [];
    public decimal? PurchasePrice { get; init; }
    public string? Note { get; init; }
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
    /// <summary>Active for-sale status of this lot — "Listed" or "Picked" — or null when it isn't on the
    /// market. For a stacked row it's only set when every underlying lot is listed, so partially-listed
    /// stacks stay listable. The client uses it to disable "List for sale" on cards already on the market.</summary>
    public string? ListingStatus { get; init; }
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

    /// <summary>Assigned game system (enum id, e.g. "Mtg"), or null. Only ever set for deck boxes.</summary>
    public string? Game { get; init; }
    /// <summary>Assigned deck type (format) id, or null. Only ever set for deck boxes.</summary>
    public int? DeckTypeId { get; init; }
    /// <summary>Assigned deck type display name, or null.</summary>
    public string? DeckTypeName { get; init; }
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
    /// <summary>Required when <see cref="Type"/> is DeckBox: the game system (enum id, e.g. "Mtg").</summary>
    public string? Game { get; init; }
    /// <summary>Optional deck type (format) id for a DeckBox.</summary>
    public int? DeckTypeId { get; init; }
}

/// <summary>Assign/reassign a deck box's game system and deck type.</summary>
public sealed record SetDeckBoxRequest
{
    /// <summary>Game system enum id, e.g. "Mtg".</summary>
    public string Game { get; init; } = "";
    public int? DeckTypeId { get; init; }
}

// --- Deck types ---

/// <summary>A deck format for a game, with its (advisory) construction rules.</summary>
public sealed record DeckTypeDto
{
    public int Id { get; init; }
    public string Game { get; init; } = "";
    public string Name { get; init; } = "";
    public bool IsBuiltIn { get; init; }
    public int? DeckSizeMin { get; init; }
    public int? DeckSizeMax { get; init; }
    public int? MaxCopiesPerCard { get; init; }
    public bool Singleton { get; init; }
    public bool BasicLandsExempt { get; init; }
    public int CommanderSlots { get; init; }
}

/// <summary>Create/update a custom deck type. Game is required on create and ignored on update.</summary>
public sealed record DeckTypeUpsertRequest
{
    public string Game { get; init; } = "";
    public string Name { get; init; } = "";
    public int? DeckSizeMin { get; init; }
    public int? DeckSizeMax { get; init; }
    public int? MaxCopiesPerCard { get; init; }
    public bool Singleton { get; init; }
    public bool BasicLandsExempt { get; init; }
    public int CommanderSlots { get; init; }
}

/// <summary>A legacy deck box with no game assigned, plus the game inferred from its cards.</summary>
public sealed record DeckBoxNeedsGameDto
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    /// <summary>Inferred game (enum id) when the box's cards are all one game; null if empty/mixed.</summary>
    public string? InferredGame { get; init; }
    /// <summary>Distinct game enum ids present among the box's cards.</summary>
    public IReadOnlyList<string> CardGames { get; init; } = [];
}

/// <summary>Advisory deck-legality result for a deck box.</summary>
public sealed record DeckLegalityDto
{
    public bool Ok { get; init; } = true;
    public string? DeckTypeName { get; init; }
    public int MainDeckCount { get; init; }
    public int CommanderCount { get; init; }
    public int TotalDeckCount { get; init; }
    public IReadOnlyList<DeckLegalityWarningDto> Warnings { get; init; } = [];
}

public sealed record DeckLegalityWarningDto(string Code, string Message);

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
    public string? Note { get; init; }
}

public sealed record MoveCardsRequest
{
    public IReadOnlyList<int> CardIds { get; init; } = [];
    public int ContainerId { get; init; }
    public string? Section { get; init; }
}

/// <summary>Bulk-edit selected cards (lots). Each <c>SetX</c> flag opts that field into the change —
/// only ticked fields are applied, so unticked fields keep their per-card values. Tags apply either
/// additively (union) or as a full replacement, per <see cref="TagsMode"/>.</summary>
public sealed record BulkUpdateCardsRequest
{
    public IReadOnlyList<int> CardIds { get; init; } = [];

    public bool SetCondition { get; init; }
    public string? Condition { get; init; }

    public bool SetFoil { get; init; }
    public bool IsFoil { get; init; }

    public bool SetQuantity { get; init; }
    public int Quantity { get; init; } = 1;

    public bool SetPurchasePrice { get; init; }
    public decimal? PurchasePrice { get; init; }

    public bool SetNote { get; init; }
    public string? Note { get; init; }

    public bool SetTags { get; init; }
    /// <summary>"add" (union onto each card) or "replace" (set the exact tag list on each card).</summary>
    public string TagsMode { get; init; } = "add";
    public IReadOnlyList<string> Tags { get; init; } = [];
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

public sealed record OrderLineDto(
    int Id, int? LotId, string Name, string? Set, string? Condition, bool IsFoil,
    int Quantity, decimal UnitSalePrice);

/// <summary>An order's header (<see cref="Order"/>) plus its line items, for the detail/edit view.</summary>
public sealed record OrderDetailDto(OrderDto Order, IReadOnlyList<OrderLineDto> Lines);

public sealed record CreateOrderRequest
{
    public int CustomerId { get; init; }
    public string Channel { get; init; } = "Manual";
    public string? OrderNumber { get; init; }
}

/// <summary>Editable header fields for a pre-ship order.</summary>
public sealed record UpdateOrderRequest
{
    public string Channel { get; init; } = "Manual";
    public string? OrderNumber { get; init; }
    public string? TrackingNumber { get; init; }
    public string? Carrier { get; init; }
    public decimal ShippingChargedToBuyer { get; init; }
    public decimal ShippingCost { get; init; }
    public decimal MarketplaceFees { get; init; }
    public string? Notes { get; init; }
}

public sealed record AddOrderLineRequest
{
    public int LotId { get; init; }
    public decimal UnitSalePrice { get; init; }
}

public sealed record ActiveListingDto(
    int LotId, string Name, string SetName, string SetCode,
    string? Condition, bool IsFoil, decimal ListedPrice, string Status);

/// <summary>Full detail of a Listed/Picked listing for the Manage Listings screen, including the
/// listing <see cref="Id"/> and every editable sale property.</summary>
public sealed record ListingDetailDto(
    int Id, int LotId, string Name, string SetName, string SetCode,
    string? Condition, bool IsFoil, string Channel, string Status,
    decimal ListedPrice, int Quantity, string? Note);

/// <summary>Editable sale properties of an active listing.</summary>
public sealed record UpdateListingRequest
{
    public decimal ListedPrice { get; init; }
    public string Channel { get; init; } = "Manual";
    public int Quantity { get; init; } = 1;
    public string? Note { get; init; }
}

/// <summary>List a single lot for sale. When <see cref="Quantity"/> is less than the lot's quantity the
/// lot is split so only the listed copies move when picked.</summary>
public sealed record CreateListingRequest
{
    public int LotId { get; init; }
    public int Quantity { get; init; } = 1;
    public decimal Price { get; init; }
    public string Channel { get; init; } = "Manual";
    public string? Note { get; init; }
}

/// <summary>List several whole lots for sale at once, each at its own price.</summary>
public sealed record BulkListingItem(int LotId, decimal Price);

public sealed record BulkListingRequest
{
    public IReadOnlyList<BulkListingItem> Items { get; init; } = [];
    public string Channel { get; init; } = "Manual";
    public string? Note { get; init; }
}

/// <summary>Lot ids to act on (e.g. mark picked).</summary>
public sealed record LotIdsRequest
{
    public IReadOnlyList<int> LotIds { get; init; } = [];
}

/// <summary>App/sales settings surfaced to the SPA.</summary>
public sealed record SalesSettingsDto(int? ForSaleLocationId);

public sealed record UpdateSalesSettingsRequest
{
    public int? ForSaleLocationId { get; init; }
}

/// <summary>Value-tier badge configuration for the scan page: the ISO currency code the thresholds are
/// denominated in, and the ascending price ceilings that split cards into value tiers. Four thresholds
/// define five tiers — a card priced at or below <c>Thresholds[0]</c> shows one currency sign, above
/// the last threshold shows five.</summary>
public sealed record ScanBadgeSettingsDto(string CurrencyCode, IReadOnlyList<decimal> Thresholds);

public sealed record UpdateScanBadgeSettingsRequest
{
    public string CurrencyCode { get; init; } = "USD";
    public IReadOnlyList<decimal> Thresholds { get; init; } = [];
}

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

// --- Customer / inventory writes ---

/// <summary>Create or edit a customer. On edit, only these fields are patched; address/notes not
/// exposed here are left untouched.</summary>
public sealed record CustomerUpsertRequest
{
    public string Name { get; init; } = "";
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
}

/// <summary>Create or edit a sealed-product catalog entry.</summary>
public sealed record ProductUpsertRequest
{
    public string Game { get; init; } = "";
    public string Category { get; init; } = "Box";
    public string Name { get; init; } = "";
    public string? SetName { get; init; }
    public string? SetCode { get; init; }
    public string? Upc { get; init; }
    public decimal? LastMarketPrice { get; init; }
}

/// <summary>Add or edit an inventory lot (owned quantity of a product at a location).</summary>
public sealed record LotUpsertRequest
{
    public int Quantity { get; init; } = 1;
    public decimal? UnitCost { get; init; }
    public int? LocationId { get; init; }
    public string? Source { get; init; }
}

// --- Trades ---

/// <summary>One outgoing card within a trade.</summary>
public sealed record TradeCardDto(
    string Game, string CardName, string? SetCode, string? SetName,
    string? CollectorNumber, bool Foil, bool IsOffDatabase, decimal? EstimatedValue);

/// <summary>A recorded trade session (cards traded away for a received note/photo/value), newest
/// first, with how many replacement lots have since been linked to it.</summary>
public sealed record TradeSummaryDto(
    int Id, string Label, string Note, string CreatedAt, decimal OutgoingValue,
    decimal? ReceivedValue, decimal? ValueDelta, int ReplacementCount, bool HasPhoto,
    IReadOnlyList<TradeCardDto> OutgoingCards);

// --- Card lists ---

public sealed record CardListDto(int Id, string Name, string Game, string? Notes, int ItemCount);

public sealed record CardListItemDto(
    int Id, string GameCardId, string CardName, string? SetCode, string? CollectorNumber,
    bool IsFoil, string? FoilType, int Quantity, decimal? MarketPrice, bool IsUnpriced);

public sealed record CreateListRequest
{
    public string Name { get; init; } = "";
    public string Game { get; init; } = "Mtg";
}

public sealed record CommitListRequest
{
    public int ContainerId { get; init; }
    public string Condition { get; init; } = "NM";
}

public sealed record CommitListResultDto(int Imported, bool ListDeleted);

public sealed record SetQuantityRequest
{
    public int Quantity { get; init; } = 1;
}

/// <summary>Import a decklist from a Moxfield/Archidekt URL into a card list. When <see cref="ListId"/> is
/// null a new list is created (named after the fetched deck); otherwise the deck's cards are appended to the
/// existing list. <see cref="Game"/> is only used when creating a new list.</summary>
public sealed record ImportListUrlRequest
{
    public string Url { get; init; } = "";
    public int? ListId { get; init; }
    public string Game { get; init; } = "Mtg";
}

/// <summary>Outcome of a URL import. <see cref="ListId"/>/<see cref="ListName"/> identify the target list
/// (created or existing); <see cref="UnresolvedNames"/> are deck cards that couldn't be matched to a printing.</summary>
public sealed record ImportListResultDto(
    int ListId, string ListName, bool ListCreated, int AddedCount, IReadOnlyList<string> UnresolvedNames);

// --- Catalog refresh ---

/// <summary>One catalog-refresh job (running or recent). <see cref="State"/> is running|succeeded|failed;
/// <see cref="Operation"/> is prices|bulk|hashes.</summary>
public sealed record CatalogJobDto(
    string Game, string Operation, string State, string Message, string StartedAt, string? FinishedAt);

public sealed record CatalogStatusDto(CatalogJobDto? Running, IReadOnlyList<CatalogJobDto> Recent);

public sealed record CatalogRefreshRequest
{
    public string Game { get; init; } = "";
    /// <summary>prices | bulk | hashes.</summary>
    public string Operation { get; init; } = "prices";
}

// --- eBay ---

/// <summary>eBay connection state for the settings screen. <see cref="Connected"/> = valid OAuth
/// tokens on file; <see cref="Configured"/> = the app credentials needed to attempt a connection are
/// all present (<see cref="MissingConfig"/> lists any that aren't).</summary>
public sealed record EbayStatusDto(bool Connected, bool Configured, IReadOnlyList<string> MissingConfig);

public sealed record EbaySetupResultDto(bool Success, string? Message);

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

// --- Scan (server-side image matching) ---

/// <summary>The result of matching one uploaded card image against a game's catalog. When
/// <see cref="Matched"/> is false the identity fields are null and the client falls through to the
/// correction search. <see cref="ScanHash"/> is the scan's 64-bit pHash as a decimal string so it
/// round-trips through JSON/JS without the 53-bit precision loss a JS number would incur.</summary>
public sealed record ScanMatchDto
{
    public bool Matched { get; init; }
    public string Game { get; init; } = "";
    public string? GameCardId { get; init; }
    public string? Name { get; init; }
    public string? SetName { get; init; }
    public string? SetCode { get; init; }
    public string? CollectorNumber { get; init; }
    public string? Rarity { get; init; }
    public string? ImageUri { get; init; }
    public double? Confidence { get; init; }
    /// <summary>The card's current market price (from the game catalog), when matched; null if unknown.
    /// Drives the scan tile's value-tier "currency sign" badge. Denominated in the catalog's currency
    /// (USD); display formatting/localization happens client-side.</summary>
    public decimal? MarketPrice { get; init; }
    /// <summary>True when no lot of this card (by game + card id, any finish) yet exists in the
    /// collection — i.e. this scan is a card you don't already own. Drives the "new card" gold-star
    /// badge. False when unmatched.</summary>
    public bool IsNew { get; init; }
    public string ScanHash { get; init; } = "";
    /// <summary>A browser-renderable (JPEG data-URI) preview of the uploaded scan, populated by the
    /// server only when the upload's own format can't be shown in an <c>&lt;img&gt;</c> (e.g. TIFF).
    /// Null for JPEG/PNG, where the client previews its local copy directly.</summary>
    public string? ScanPreviewDataUri { get; init; }
    /// <summary>Set when matching could not run at all (e.g. game catalog unavailable); distinct
    /// from a clean "no match" (<see cref="Matched"/> false, no error).</summary>
    public string? Error { get; init; }
}

/// <summary>One catalog card returned by the correction search (<c>GET /api/scan/search</c>).</summary>
public sealed record ScanSearchResultDto(
    string GameCardId, string Name, string SetCode, string SetName,
    string CollectorNumber, string Rarity, string? ImageUri);

/// <summary>One card the user confirmed from a scan, to be written to inventory as an owned lot.</summary>
public sealed record ScanCommitItem
{
    public string Game { get; init; } = "";
    public string GameCardId { get; init; } = "";
    public string Name { get; init; } = "";
    public string SetCode { get; init; } = "";
    public string SetName { get; init; } = "";
    public string CollectorNumber { get; init; } = "";
    public string Rarity { get; init; } = "";
    public string? ImageUri { get; init; }
    public string Condition { get; init; } = "NM";
    public bool IsFoil { get; init; }
    public string? FoilType { get; init; }
    public int Quantity { get; init; } = 1;
    public decimal? PurchasePrice { get; init; }
    public string? Note { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
}

/// <summary>Commit a batch of confirmed scans into a storage location.</summary>
public sealed record ScanCommitRequest
{
    public int ContainerId { get; init; }
    public IReadOnlyList<ScanCommitItem> Items { get; init; } = [];
}

public sealed record ScanCommitResultDto(int Imported);

// --- Auth ---

/// <summary>
/// Current authentication state for the SPA. <see cref="AuthRequired"/> is always true now (the app
/// uses per-user accounts) but is kept so older clients that branch on it keep working. When
/// <see cref="Authenticated"/>, <see cref="Username"/>/<see cref="IsAdmin"/> describe the signed-in user.
/// </summary>
public sealed record AuthStatusDto(bool AuthRequired, bool Authenticated, string? Username = null, bool IsAdmin = false);

public sealed record LoginRequest
{
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";
    public bool RememberMe { get; init; }
}

/// <summary>A user account as exposed to the Administration UI (never carries the password hash).</summary>
public sealed record UserDto(int Id, string Username, bool IsSystem, bool IsAdmin, DateTime CreatedAt);

public sealed record CreateUserRequest
{
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";
    public bool IsAdmin { get; init; }
}

/// <summary>Self-service password change — the current password is required to set a new one.</summary>
public sealed record ChangePasswordRequest
{
    public string CurrentPassword { get; init; } = "";
    public string NewPassword { get; init; } = "";
}

/// <summary>Admin password reset for another user (no current password needed).</summary>
public sealed record ResetPasswordRequest
{
    public string NewPassword { get; init; } = "";
}
