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
  tags: string[];
  purchasePrice?: number | null;
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
  scanHash: string;
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
  quantity: number;
  purchasePrice?: number | null;
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
