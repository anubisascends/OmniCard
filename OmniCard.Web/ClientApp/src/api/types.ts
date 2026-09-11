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
}

export interface UserDto {
  id: number;
  username: string;
  isSystem: boolean;
  isAdmin: boolean;
  createdAt: string;
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
}

/** Value-tier badge config for the scan page. `thresholds` is an ascending list of price ceilings
 * (four of them → five tiers); `currencyCode` is the ISO code they're denominated in. */
export interface ScanBadgeSettingsDto {
  currencyCode: string;
  thresholds: number[];
}

export interface CustomerDto {
  id: number;
  name: string;
  email?: string | null;
  phone?: string | null;
  city?: string | null;
  state?: string | null;
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
}

export interface CommitListResultDto {
  imported: number;
  listDeleted: boolean;
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
