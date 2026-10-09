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
  /** Printed language code of this copy ("en", "ja", …). */
  language: string;
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

/**
 * A group header in a grouped card list. `key` is the raw value ("MH3", "true", "Listed", "" = none);
 * `label` is display text for data values (set / location / game names) and equals `key` otherwise.
 * `rows` counts grid rows (stacks when stacking), `quantity` copies, `value` their market value.
 * `continued` marks a header repeated at the top of a page that starts mid-group.
 */
export interface CardGroupDto {
  id: string;
  level: number;
  field: string;
  key: string;
  label: string;
  rows: number;
  quantity: number;
  value: number;
  collapsed: boolean;
  continued: boolean;
}

/** One row of a grouped card list page: a group header or a card. */
export interface GroupedCardRowDto {
  group?: CardGroupDto | null;
  card?: CardDto | null;
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
  /** The site (major physical location — a home, a shop) this location sits in. */
  siteId: number;
  siteName?: string | null;
  /** False when the current user only has read access to the location's site. */
  canWrite: boolean;
  /** Lists (find in collection, owned counts, putting a list away) and decklist checks ignore this
   * location's cards — e.g. a sales binder or a deck in use. */
  ignoredForLists: boolean;
}

/** A site: a MAJOR physical location (a home, a shop) holding many storage locations. `access` is
 * the current user's level on it. The default site is always present and visible to everyone. */
export interface SiteDto {
  id: number;
  name: string;
  description?: string | null;
  isDefault: boolean;
  sortOrder: number;
  access: 'Read' | 'Write';
  locationCount: number;
}

/** One access grant on a site (admin). `level` "None" removes the grant when saving. */
export interface SiteGrantDto {
  principalType: 'User' | 'Role';
  principalId: number;
  level: 'None' | 'Read' | 'Write';
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
  email?: string | null;
  /** True while the user still has to sign in with an admin-issued setup key and choose a password. */
  setupPending?: boolean;
}

/** The signed-in user's own account details (Account page). */
export interface AccountDto {
  id: number;
  username: string;
  email?: string | null;
  isAdmin: boolean;
  createdAt: string;
}

/** What the sign-in screen asks for after the username/email step. */
export interface SignInStepDto {
  step: 'password' | 'setupKey' | 'locked';
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
  ebayItemId?: string | null;
  ebayStatus?: string | null;
  ebayViewUrl?: string | null;
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

/** An owned copy allocated to a decklist entry: which lot to pull, how many copies, and where it is. */
export interface DecklistPickDto {
  lotId: number;
  locationId?: number | null;
  locationName: string;
  locationType?: string | null;
  page?: number | null;
  slot?: number | null;
  section?: string | null;
  setCode: string;
  collectorNumber: string;
  isFoil: boolean;
  condition: string;
  quantity: number;
  isListed: boolean;
}

export interface DecklistEntryDto {
  cardName: string;
  quantityNeeded: number;
  setCode?: string | null;
  marketPrice?: number | null;
  imageUri?: string | null;
  collectorNumber?: string | null;
  /** Owned entries only: the copies chosen to fill this entry. */
  picks?: DecklistPickDto[] | null;
}

/** The decklist source for a check (and for re-running it to print): a URL, or pasted text. */
export interface DecklistCheckRequest {
  url?: string;
  text?: string;
  game: string;
}

export interface DecklistCheckDto {
  deckName: string;
  game: string;
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
  /** New cards the tag rules tagged. */
  ruleTagged?: number;
}

/** One problem (or note) about an import line. `row` is the CSV spreadsheet row (header = row 1);
 *  null for deck-URL imports and whole-file problems. */
export interface LocationImportIssueDto {
  row: number | null;
  card: string | null;
  message: string;
}

/** A successful all-or-nothing location import. `format` is the detected CSV format (null for URLs). */
export interface LocationImportResultDto {
  source: string;
  format: string | null;
  lines: number;
  copies: number;
  substitutions: LocationImportIssueDto[];
  /** New cards the tag rules tagged. */
  ruleTagged?: number;
}

/** A rejected location import (HTTP 422): nothing was written. */
export interface LocationImportFailureDto {
  error: string;
  errors: LocationImportIssueDto[];
}

export interface ImportUrlResultDto {
  deckName: string;
  imported: number;
  skipped: number;
  totalCards: number;
  unresolvedNames: string[];
  substitutedNames: string[];
  /** New cards the tag rules tagged. */
  ruleTagged?: number;
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
  /** The copy's printed language: read off the card (MTG "• JP", Yu-Gi-Oh! "-DE"), else the session's
   * chosen language, else the matched catalog row's. Null when unmatched. */
  language?: string | null;
  /** True when `language` was read from the card rather than assumed. */
  languageDetected?: boolean;
}

export interface ScanSearchResultDto {
  gameCardId: string;
  name: string;
  setCode: string;
  setName: string;
  collectorNumber: string;
  rarity: string;
  imageUri?: string | null;
  /** Catalog row language, for catalogs that hold several (MTG / One Piece / Pokémon Japan). */
  language?: string | null;
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
  /** Printed language code of this copy; omitted/unknown ⇒ English. */
  language?: string | null;
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
  /** Cards the tag rules tagged (only when the commit asked for `applyTagRules`). */
  ruleTagged?: number;
}

/** One card line in an audit summary bucket. */
export interface AuditLineDto {
  name: string;
  setCode: string;
  collectorNumber: string;
  condition?: string | null;
  isFoil: boolean;
  quantity: number;
}

/** Outcome of committing a location audit. `notFound` lots were deleted; `added` lots were created;
 * `updatedCount` counts matched copies whose condition/foil was overwritten from the scan. */
export interface AuditCommitResultDto {
  matched: AuditLineDto[];
  notFound: AuditLineDto[];
  added: AuditLineDto[];
  updatedCount: number;
}

// --- Scan batches (watched scan folders, matched in the background) ---

export type ScanBatchStatus = 'Collecting' | 'Matching' | 'Ready' | 'Committed' | 'Discarded';

export interface ScanBatchSummaryDto {
  id: number;
  name: string;
  game: string;
  status: ScanBatchStatus;
  /** Items still open for review. */
  total: number;
  pending: number;
  matched: number;
  errors: number;
  committed: number;
  claimedBy?: string | null;
  claimedByMe: boolean;
  createdUtc: string;
  lastFileUtc: string;
  readyUtc?: string | null;
  closedUtc?: string | null;
  defaultContainerId?: number | null;
}

export interface ScanBatchItemDto {
  id: number;
  sequence: number;
  fileName: string;
  imageUrl: string;
  status: 'Pending' | 'Matched' | 'Error';
  match?: ScanMatchDto | null;
  override?: ScanSearchResultDto | null;
  error?: string | null;
  include: boolean;
  verified: boolean;
  condition: string;
  language?: string | null;
  isFoil: boolean;
  foilType?: string | null;
  quantity: number;
  purchasePrice?: number | null;
  tags: string[];
  note?: string | null;
}

export interface ScanBatchDto {
  summary: ScanBatchSummaryDto;
  isFoil: boolean;
  condition?: string | null;
  language?: string | null;
  setCodes: string[];
  items: ScanBatchItemDto[];
}

export interface ScanBatchItemEdit {
  id: number;
  include: boolean;
  verified: boolean;
  override?: ScanSearchResultDto | null;
  condition: string;
  language?: string | null;
  isFoil: boolean;
  foilType?: string | null;
  quantity: number;
  purchasePrice?: number | null;
  tags: string[];
  note?: string | null;
}

export interface ScanBatchCommitResultDto {
  imported: number;
  /** True once the batch has no open items left. */
  batchClosed: boolean;
}

export interface ScanFolderConfigDto {
  game: string;
  path: string;
  enabled: boolean;
  isFoil: boolean;
  condition: string;
  language?: string | null;
  setCodes: string[];
  defaultContainerId?: number | null;
}

export interface ScanFolderSettingsDto {
  enabled: boolean;
  quietPeriodSeconds: number;
  retentionDays: number;
  folders: ScanFolderConfigDto[];
}

export interface ScanFolderStatusDto {
  game: string;
  exists: boolean;
  writable: boolean;
  lastError?: string | null;
}

export interface ScanFolderSettingsResponse {
  settings: ScanFolderSettingsDto;
  status: ScanFolderStatusDto[];
}

// --- eBay ---

export interface EbayStatusDto {
  connected: boolean;
  configured: boolean;
  missingConfig: string[];
}

export interface EbayCategoryOptionDto {
  categoryId: string;
  title: string;
  price?: number | null;
}

export interface EbayListingDraftDto {
  suggestedTitle: string;
  suggestedDescription: string;
  condition: string;
  isFoil: boolean;
  game: string;
  categories: EbayCategoryOptionDto[];
}

export interface EbayListingResultDto {
  lotId: number;
  success: boolean;
  ebayItemId?: string | null;
  error?: string | null;
}

export interface EbaySetupResultDto {
  success: boolean;
  message?: string | null;
}

export interface EbaySellingSettingsDto {
  // Inventory location (editable)
  locationName?: string | null;
  addressLine1?: string | null;
  addressLine2?: string | null;
  city?: string | null;
  state?: string | null;
  postalCode?: string | null;
  country?: string | null; // ISO 3166-1 alpha-2, e.g. "US"
  phone?: string | null;
  // Shipping policy (editable)
  freeShipping: boolean;
  shippingCost: number;
  handlingTimeDays: number;
  shippingServiceCode: string;
  // Return policy (editable)
  returnsAccepted: boolean;
  returnWindowDays: number;
  returnShippingPaidBy: 'Buyer' | 'Seller';
  // Setup results (read-only)
  locationProvisioned: boolean;
  fulfillmentPolicyId?: string | null;
  paymentPolicyId?: string | null;
  returnPolicyId?: string | null;
  setupCompletedAt?: string | null;
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

/** A game's catalog language options (Settings ▸ Catalog data). */
export interface CatalogLanguagesDto {
  game: string;
  /** Languages the game's source can download (English first). */
  downloadable: string[];
  /** Saved download selection (always includes "en"). */
  selected: string[];
  /** Every language an owned copy can be tagged with. */
  cardLanguages: string[];
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
  /** Forced card language (a language code), or null/absent for any language. */
  language?: string | null;
  /** The Moxfield / Archidekt URL the list was imported from ("update from URL" re-fetches it). */
  sourceUrl?: string | null;
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
  /** True when the collection covers at least one copy (`ownedQuantity > 0`). */
  inCollection: boolean;
  /** Best display art (local cache when downloaded, else catalog CDN) for hover previews. */
  imageUri?: string | null;
  /** How many of `quantity` the collection already covers with this exact printing (readable sites). */
  ownedQuantity: number;
  /** The owned copies were already moved by a fulfillment; what's left is to buy. */
  awaitingPurchase: boolean;
  /** An owned copy of another printing, approved to stand in for a card the collection didn't have. */
  isSubstitute: boolean;
  /** Copies of this printing that don't count: in a location ignored for lists, or listed for sale. */
  ignoredQuantity: number;
  /** The printing's catalog language; only set when the list forces a language and the game's catalog serves
   * other languages. Differs from the list's language when the card wasn't printed (or downloaded) in it. */
  language?: string | null;
}

/** Which part of a list an export covers: every card, the copies still to buy, or the copies already owned. */
export type ListExportScope = 'all' | 'buy' | 'owned';
export type ListExportFormat = 'text' | 'csv';

/** One difference between a list and a fresh fetch of its deck URL. Applying it sets the printing's
 * quantity on the list to `newQuantity` (0 removes it). */
export interface ListUpdateRowDto {
  kind: 'Add' | 'Remove' | 'Change';
  gameCardId: string;
  cardName: string;
  setCode?: string | null;
  setName?: string | null;
  collectorNumber?: string | null;
  rarity?: string | null;
  imageUri?: string | null;
  isFoil: boolean;
  oldQuantity: number;
  newQuantity: number;
  price?: number | null;
  /** A removal of a card that wasn't imported from a URL (added by hand or as a stand-in). */
  handAdded: boolean;
  /** For an added card: copies the collection already covers. */
  ownedQuantity: number;
}

export interface ListUpdatePreviewDto {
  deckName: string;
  url: string;
  rows: ListUpdateRowDto[];
  unchangedCount: number;
  unresolvedNames: string[];
}

/** An owned copy of another printing that could stand in for a missing list card. */
export interface ListSubstituteCandidateDto {
  lotId: number;
  gameCardId: string;
  cardName: string;
  setCode?: string | null;
  collectorNumber?: string | null;
  isFoil: boolean;
  language: string;
  condition: string;
  locationName: string;
  page?: number | null;
  slot?: number | null;
  section?: string | null;
  available: number;
  suggested: number;
  imageUri?: string | null;
  /** Set when this copy is shown for information only: its location is ignored for lists, or it's listed for sale. */
  ignoredReason?: 'Location' | 'Listed' | null;
}

export interface ListItemSubstitutesDto {
  itemId: number;
  cardName: string;
  setCode?: string | null;
  collectorNumber?: string | null;
  isFoil: boolean;
  missing: number;
  candidates: ListSubstituteCandidateDto[];
}

export interface ListSubstitutionDto {
  itemId: number;
  lotId: number;
  quantity: number;
}

/** One-click list fulfillment: either location may be omitted to do only that half. */
export interface FulfillListRequest {
  moveToContainerId?: number | null;
  addToContainerId?: number | null;
  condition: string;
}

export interface FulfillListResultDto {
  moved: number;
  added: number;
  remaining: number;
  listDeleted: boolean;
  /** New cards the tag rules tagged. */
  ruleTagged?: number;
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

// --- Saved views (Collection / Location pages) ---

/** Which card list a saved view belongs to. `AllLocations` views are shared (admin-published) only. */
export type SavedViewPage = 'Collection' | 'Location' | 'AllLocations';

/** The layout a saved view restores. `display` / `groupBy` only apply on Location pages. */
export interface SavedViewStateDto {
  q: string;
  sort: string;
  dir: 'asc' | 'desc';
  pageSize: number;
  stacked: boolean;
  hiddenColumns: string[];
  columnOrder: string[];
  display?: 'table' | 'stacks' | null;
  groupBy?: 'type' | 'tag' | null;
  /** Table row-grouping columns, outermost first (grid field names). Absent on older views. */
  groupColumns?: string[];
}

export interface SavedViewDto {
  id: number;
  name: string;
  page: SavedViewPage;
  containerId?: number | null;
  /** A game id, "all" (the All Games selection) or null (any game). */
  game?: string | null;
  /** Published by an administrator for everyone; read-only unless `canEdit`. */
  shared: boolean;
  canEdit: boolean;
  isMyDefault: boolean;
  isEveryoneDefault: boolean;
  state: SavedViewStateDto;
}

export interface SavedViewListDto {
  views: SavedViewDto[];
  /** The view the page opens with; null = the built-in layout. */
  defaultViewId?: number | null;
}

export interface CreateSavedViewRequest {
  name: string;
  page: SavedViewPage;
  containerId?: number | null;
  game?: string | null;
  shared: boolean;
  state: SavedViewStateDto;
}

// ---- Tag rules (Settings ▸ Tag rules) ----

/** An admin-defined auto-tagging rule: cards of `game` matching `query` get every tag in `tags`.
 * Additive only — rules never remove tags. */
export interface TagRuleDto {
  id: number;
  name: string;
  game: string;
  query: string;
  tags: string[];
  enabled: boolean;
  updatedAt: string;
}

export interface TagRuleInput {
  name: string;
  game: string;
  query: string;
  tags: string[];
  enabled: boolean;
}

export interface TagRuleValidationDto {
  errors: string[];
}

export interface TagRulePreviewCardDto {
  id: number;
  name: string;
  setCode: string;
  collectorNumber: string;
  location: string | null;
  condition: string;
  isFoil: boolean;
  missingTags: string[];
}

/** `matchCount` owned cards match; `changeCount` of them lack at least one of the rule's tags. */
export interface TagRulePreviewDto {
  matchCount: number;
  changeCount: number;
  sample: TagRulePreviewCardDto[];
  errors: string[];
}

export interface TagRuleRunResultDto {
  cardsTagged: number;
}

/** One unsaved scanned card to check against the tag rules; `key` is echoed back. */
export interface ScanTagRuleItem {
  key: string;
  gameCardId: string;
  name: string;
  setCode: string;
  collectorNumber: string;
  rarity: string;
  condition: string;
  language?: string | null;
  isFoil: boolean;
  foilType?: string | null;
}

export interface ScanTagRuleResultDto {
  key: string;
  tags: string[];
}
