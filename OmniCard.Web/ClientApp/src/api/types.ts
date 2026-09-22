// Mirrors OmniCard.Api.Contracts DTOs. Kept in sync by hand for now; can be replaced with a
// generated client from /openapi/v1.json later.

export interface PagedResult<T> {
  total: number;
  skip: number;
  take: number;
  items: T[];
}

export interface GameDto {
  id: string;
  displayName: string;
}

export interface ComponentDto {
  category: string;
  name: string;
  version: string;
  license: string;
  homepageUrl: string | null;
  licenseUrl: string | null;
}

export interface SearchFieldDto {
  canonical: string;
  aliases: string[];
  valueAliases: string[]; // "f=Fire" display strings
  description: string;
  example: string;
  kind: 'core' | 'game' | 'special';
}

export interface SearchSchemaDto {
  game: string; // enum name, or "all" for the core schema
  fields: SearchFieldDto[];
}

export interface CardDto {
  id: number;
  game: string;
  gameCardId: string;
  name: string;
  setName: string;
  setCode: string;
  number: string;
  rarity: string;
  imageUri?: string | null;
  scanImagePath?: string | null;
  condition: string;
  isFoil: boolean;
  foilType?: string | null;
  quantity: number;
  stackedIds: number[];
  tags: string[];
  purchasePrice?: number | null;
  note?: string | null;
  marketPrice: number;
  containerId?: number | null;
  containerName?: string | null;
  page?: number | null;
  slot?: number | null;
  section?: string | null;
  color?: string | null;
  cardType?: string | null;
  isMissing: boolean;
  isTraded: boolean;
  /** "Listed" or "Picked" when this lot is already on the market, else null. Used to block re-listing. */
  listingStatus?: string | null;
}

export interface LocationSummaryDto {
  id: number;
  name: string;
  type: string;
  isSystem: boolean;
  isAlwaysAvailable: boolean;
  cardCount: number;
  uniquePrintCount: number;
  totalMarketValue: number;
  totalPurchaseCost: number;
  priceDelta: number;
  priceDeltaPercent: number;
  coverImageUri?: string | null;
  /** Assigned game system (enum id, e.g. "Mtg"); only ever set for deck boxes. */
  game?: string | null;
  /** Assigned deck type (format) id; only ever set for deck boxes. */
  deckTypeId?: number | null;
  /** Assigned deck type display name. */
  deckTypeName?: string | null;
}

export interface DeckTypeDto {
  id: number;
  game: string;
  name: string;
  isBuiltIn: boolean;
  deckSizeMin?: number | null;
  deckSizeMax?: number | null;
  maxCopiesPerCard?: number | null;
  singleton: boolean;
  basicLandsExempt: boolean;
  copiesCountByCollectorNumber: boolean;
  commanderSlots: number;
}

export interface DeckTypeUpsertRequest {
  game: string;
  name: string;
  deckSizeMin?: number | null;
  deckSizeMax?: number | null;
  maxCopiesPerCard?: number | null;
  singleton: boolean;
  basicLandsExempt: boolean;
  copiesCountByCollectorNumber: boolean;
  commanderSlots: number;
}

export interface DeckBoxNeedsGameDto {
  id: number;
  name: string;
  /** Inferred game (enum id) when all the box's cards are one game; null if empty/mixed. */
  inferredGame?: string | null;
  cardGames: string[];
}

export interface DeckLegalityWarningDto {
  code: string;
  message: string;
}

export interface DeckLegalityDto {
  ok: boolean;
  deckTypeName?: string | null;
  mainDeckCount: number;
  commanderCount: number;
  /** Whole-deck count used for size rules: main deck + command zone (99 + 1 = 100 for Commander). */
  totalDeckCount: number;
  warnings: DeckLegalityWarningDto[];
}

export interface ValuationLineDto {
  key: string;
  units: number;
  cost: number;
  market: number;
}

export interface RealizedDto {
  totalSold: number;
  totalProceeds: number;
  totalCost: number;
  totalFees: number;
  profit: number;
}

export interface DashboardDto {
  totalUnits: number;
  totalCost: number;
  totalMarket: number;
  unrealizedDelta: number;
  byGame: ValuationLineDto[];
  byCategory: ValuationLineDto[];
  byLocation: ValuationLineDto[];
  realized: RealizedDto;
}

export interface AuthStatusDto {
  authRequired: boolean;
  authenticated: boolean;
  username?: string | null;
  isAdmin?: boolean;
  /** Effective permission keys for the signed-in user (admins hold every key). */
  permissions?: string[] | null;
}

export interface UserDto {
  id: number;
  username: string;
  isSystem: boolean;
  isAdmin: boolean;
  createdAt: string;
  roleId?: number | null;
  grant?: string[] | null;
  deny?: string[] | null;
}

/** A reusable permission bundle. System roles can't be deleted. */
export interface RoleDto {
  id: number;
  name: string;
  isSystem: boolean;
  permissions: string[];
}

/** The permission catalog for the admin checklist UI: sections, each with their permissions. */
export interface PermissionCatalogDto {
  groups: PermissionGroupDto[];
}
export interface PermissionGroupDto {
  key: string;
  label: string;
  permissions: PermissionItemDto[];
}
export interface PermissionItemDto {
  key: string;
  action: string;
  label: string;
}

export interface WorkflowLaneDto {
  key: string;
  name: string;
  color: string;
  behavior: string;
}

export interface OrderDto {
  id: number;
  customerId: number;
  customerName?: string | null;
  channel: string;
  orderNumber?: string | null;
  orderDate: string;
  status: string;
  stageKey?: string | null;
  lineItemCount: number;
  lineTotal: number;
  shippingChargedToBuyer: number;
  shippingCost: number;
  marketplaceFees: number;
  trackingNumber?: string | null;
  notes?: string | null;
}

// --- Order CSV import ---

export interface OrderImportTemplateDto {
  id: string;
  name: string;
  isBuiltIn: boolean;
  channel: string;
  columnMappings: Record<string, string>;
}

export interface OrderImportRowDto {
  orderNumber: string;
  customerName: string;
  addressLine1?: string | null;
  addressLine2?: string | null;
  city?: string | null;
  state?: string | null;
  postalCode?: string | null;
  country?: string | null;
  orderDate: string;
  shippingFeePaid: number;
  itemCount: number;
  valueOfProducts: number;
  trackingNumber?: string | null;
  carrier?: string | null;
  matchedCustomerId?: number | null;
  isNewCustomer: boolean;
  isDuplicateOrder: boolean;
  include: boolean;
  canInclude: boolean;
  statusText: string;
}

export interface OrderImportPreviewDto {
  rows: OrderImportRowDto[];
  warnings: string[];
}

export interface ActiveListingDto {
  lotId: number;
  name: string;
  setName: string;
  setCode: string;
  condition?: string | null;
  isFoil: boolean;
  listedPrice: number;
  status: string;
}

export interface ListingDetailDto {
  id: number;
  lotId: number;
  name: string;
  setName: string;
  setCode: string;
  condition?: string | null;
  isFoil: boolean;
  channel: string;
  status: string;
  listedPrice: number;
  quantity: number;
  note?: string | null;
}

export interface SalesSettingsDto {
  forSaleLocationId?: number | null;
  movePickedToForSaleLocation: boolean;
}

/** Value-tier badge config for the scan page. `thresholds` is an ascending list of price ceilings
 * (four of them → five tiers); `currencyCode` is the ISO code they're denominated in. */
export interface ScanBadgeSettingsDto {
  currencyCode: string;
  thresholds: number[];
}

/** Store/company identity printed on receipts. `logoPath` is the stored relative path (managed via the
 * logo upload/delete endpoints); `logoUrl` is a ready-to-render URL (with cache-busting) or null. */
export interface CompanyProfileDto {
  name?: string | null;
  addressLine1?: string | null;
  addressLine2?: string | null;
  city?: string | null;
  state?: string | null;
  postalCode?: string | null;
  country?: string | null;
  email?: string | null;
  phone?: string | null;
  logoPath?: string | null;
  logoUrl?: string | null;
}

/** Physical layout of a printed receipt. `widthMm` is the roll/paper width — the PDF is generated at
 * exactly this width so it prints on a thermal printer without scaling. */
export interface ReceiptLayoutDto {
  widthMm: number;
  marginMm: number;
  fontPointSize: number;
  showPrices: boolean;
  footerText?: string | null;
}

export interface ReceiptConfigDto {
  company: CompanyProfileDto;
  receipt: ReceiptLayoutDto;
}

export interface LogoUploadResultDto {
  company: CompanyProfileDto;
}

export interface CustomerDto {
  id: number;
  name: string;
  email?: string | null;
  phone?: string | null;
  addressLine1?: string | null;
  addressLine2?: string | null;
  city?: string | null;
  state?: string | null;
  postalCode?: string | null;
  country?: string | null;
}

export interface ProductDto {
  id: number;
  game: string;
  category: string;
  name: string;
  setName?: string | null;
  setCode?: string | null;
  upc?: string | null;
  lastMarketPrice?: number | null;
  totalQuantity: number;
}

export interface InventoryValuationDto {
  totalUnits: number;
  totalCost: number;
  totalMarket: number;
}

export interface InventoryLotDto {
  id: number;
  productId: number;
  quantity: number;
  unitCost?: number | null;
  locationId?: number | null;
  source?: string | null;
  acquisitionDate?: string | null;
}

export interface DecklistEntryDto {
  cardName: string;
  quantityNeeded: number;
  setCode?: string | null;
  marketPrice?: number | null;
  imageUri?: string | null;
}

export interface DecklistCheckDto {
  deckName: string;
  totalOwned: number;
  totalMissing: number;
  totalCards: number;
  estimatedCost: number;
  owned: DecklistEntryDto[];
  missing: DecklistEntryDto[];
}

export interface CsvImportResultDto {
  imported: number;
  totalRows: number;
  detectedFormat: string;
  warnings: string[];
}

export interface SetInfoDto {
  setCode: string;
  setName: string;
}

export interface SetChecklistCardDto {
  gameCardId: string;
  collectorNumber: string;
  name: string;
  rarity: string;
  imageUri?: string | null;
  ownedQuantity: number;
  normalPrice?: number | null;
  foilPrice?: number | null;
  hasFoil: boolean;
}

export interface SetChecklistDto {
  game: string;
  setCode: string;
  setName: string;
  ownedCount: number;
  totalCount: number;
  ownedPhysicalCount: number;
  completionPercent: number;
  cards: SetChecklistCardDto[];
}

// --- Binder (matches OmniCard.Web.Services.BinderStateDto) ---

export interface BinderCardDto {
  id: number;
  game: number;
  name: string;
  setName: string;
  setCode: string;
  number: string;
  rarity: string;
  color?: string | null;
  cardType?: string | null;
  foil: boolean;
  foilType?: string | null;
  condition: string;
  purchasePrice?: number | null;
  price?: string | null;
  marketPriceRaw: number;
  imageUrl?: string | null;
  isTraded: boolean;
  tags: string[];
  page?: number | null;
  slot?: number | null;
  containerId?: number | null;
  tcgPlayerUrl: string;
  /** Active-listing badges — null unless the card is on the market. */
  listingStatus?: string | null; // "Listed" | "Picked"
  listingChannel?: string | null; // "Manual" | "TcgPlayer" | "Ebay"
  listedPriceRaw?: number | null;
  listedPrice?: string | null; // pre-formatted currency
}

export interface BinderSlotDto {
  slotIndex: number;
  card: BinderCardDto | null;
  reverseGame?: number | null;
}

export interface SpreadTabDto {
  index: number;
  label: string;
  isCurrent: boolean;
}

export interface BinderStateDto {
  containerName: string;
  slotsPerPage: number;
  columns: number;
  totalPages: number;
  spreadIndex: number;
  totalSpreads: number;
  leftPageNumber?: number | null;
  rightPageNumber?: number | null;
  pageRangeLabel: string;
  leftSlots: BinderSlotDto[];
  rightSlots: BinderSlotDto[];
  spreadTabs: SpreadTabDto[];
}

// --- Scan (server-side image matching; mirrors OmniCard.Api.Contracts scan DTOs) ---

export interface ScanMatchDto {
  matched: boolean;
  game: string;
  gameCardId?: string | null;
  name?: string | null;
  setName?: string | null;
  setCode?: string | null;
  collectorNumber?: string | null;
  rarity?: string | null;
  imageUri?: string | null;
  confidence?: number | null;
  /** Current market price of the matched card (catalog currency, USD), or null if unknown. Drives the
   * value-tier "currency sign" badge. */
  marketPrice?: number | null;
  /** True when the collection holds no copy of this card yet — drives the "new card" gold-star badge. */
  isNew?: boolean;
  scanHash: string;
  /** Server-rendered JPEG data URI, set only when the upload's own format (e.g. TIFF) can't render
   * in a browser <img>. Null for JPEG/PNG. */
  scanPreviewDataUri?: string | null;
  error?: string | null;
  /** Printed edition read from the scan (Yu-Gi-Oh! only): "1st Edition" / "Limited Edition", or null
   * when no edition text is printed (Unlimited) or not applicable. Informational at review time. */
  edition?: string | null;
  /** True when the MTG Planeswalker glyph was detected, marking this as a The List (plst) reprint —
   * a distinct, cheaper printing. When set, the match is remapped to the plst printing. Drives the
   * "The List" badge. MTG only. */
  isListReprint?: boolean;
  /** True when the List glyph was detected but the plst printing wasn't found in the catalog, so the
   * match fell back to the original (pricier) printing — the price should be sanity-checked. */
  listReprintUnresolved?: boolean;
}

export interface ScanSearchResultDto {
  gameCardId: string;
  name: string;
  setCode: string;
  setName: string;
  collectorNumber: string;
  rarity: string;
  imageUri?: string | null;
}

export interface ScanCommitItem {
  game: string;
  gameCardId: string;
  name: string;
  setCode: string;
  setName: string;
  collectorNumber: string;
  rarity: string;
  imageUri?: string | null;
  condition: string;
  isFoil: boolean;
  foilType?: string | null;
  quantity: number;
  purchasePrice?: number | null;
  note?: string | null;
  tags?: string[];
  /** The scan's perceptual hash (from ScanMatchDto.scanHash); lets the server record a
   * scan-hash → this-card mapping so future scans of the same card auto-match. */
  scanHash?: string | null;
}

export interface ScanCommitResultDto {
  imported: number;
}

// --- eBay ---

export interface EbayStatusDto {
  connected: boolean;
  configured: boolean;
  missingConfig: string[];
}

export interface EbaySetupResultDto {
  success: boolean;
  message?: string | null;
}

// --- Catalog refresh ---

export interface CatalogJobDto {
  game: string;
  operation: string;
  state: string;
  message: string;
  startedAt: string;
  finishedAt?: string | null;
}

export interface CatalogStatusDto {
  running?: CatalogJobDto | null;
  recent: CatalogJobDto[];
}

// --- Trades ---

export interface TradeCardDto {
  game: string;
  cardName: string;
  setCode?: string | null;
  setName?: string | null;
  collectorNumber?: string | null;
  foil: boolean;
  isOffDatabase: boolean;
  estimatedValue?: number | null;
}

export interface TradeSummaryDto {
  id: number;
  label: string;
  note: string;
  createdAt: string;
  outgoingValue: number;
  receivedValue?: number | null;
  valueDelta?: number | null;
  replacementCount: number;
  hasPhoto: boolean;
  outgoingCards: TradeCardDto[];
}

// Trade builder (in-progress draft session)
export interface TradeSessionItem {
  index: number;
  lotId?: number | null;
  isOffDatabase: boolean;
  game: number;
  cardName: string;
  setCode?: string | null;
  setName?: string | null;
  collectorNumber?: string | null;
  foil: boolean;
  estimatedValue?: number | null;
  imageUrl?: string | null;
  tcgPlayerUrl?: string | null;
}

export interface TradeSessionState {
  sessionId: string;
  note: string;
  receivedValue?: number | null;
  outgoingTotal: number;
  items: TradeSessionItem[];
}

export interface TradeSearchResult {
  lotId: number;
  name: string;
  setName: string;
  setCode: string;
  number: string;
  isFoil: boolean;
  condition: string;
  marketPrice?: number | null;
  imageUrl?: string | null;
}

// --- Card lists ---

export interface CardListDto {
  id: number;
  name: string;
  game: string;
  notes?: string | null;
  itemCount: number;
}

export interface CardListItemDto {
  id: number;
  gameCardId: string;
  cardName: string;
  setCode?: string | null;
  collectorNumber?: string | null;
  isFoil: boolean;
  foilType?: string | null;
  quantity: number;
  marketPrice?: number | null;
  isUnpriced: boolean;
  /** True when the collection already owns at least one copy of this printing (by game + card id). */
  inCollection: boolean;
  /** Best display art (local cache when downloaded, else catalog CDN) for hover previews. */
  imageUri?: string | null;
}

export interface CommitListResultDto {
  imported: number;
  listDeleted: boolean;
}

/** Add a single catalog card ("a wholly new card") to a list. Records a frozen printing only — it never
 * creates a lot or touches the collection. */
export interface AddListItemRequest {
  gameCardId: string;
  name: string;
  setCode?: string | null;
  setName?: string | null;
  collectorNumber?: string | null;
  rarity?: string | null;
  imageUri?: string | null;
  isFoil: boolean;
  foilType?: string | null;
  quantity: number;
}

export interface ImportListResultDto {
  listId: number;
  listName: string;
  listCreated: boolean;
  addedCount: number;
  unresolvedNames: string[];
}

// --- Order detail / lines ---

export interface OrderLineDto {
  id: number;
  lotId?: number | null;
  name: string;
  set?: string | null;
  condition?: string | null;
  isFoil: boolean;
  quantity: number;
  unitSalePrice: number;
}

export interface OrderDetailDto {
  order: OrderDto;
  lines: OrderLineDto[];
}
