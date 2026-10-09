import type {
  AccountDto,
  ActiveListingDto,
  AuthStatusDto,
  BinderCardDto,
  BinderStateDto,
  CardDto,
  GroupedCardRowDto,
  ComponentDto,
  CsvImportResultDto,
  ImportUrlResultDto,
  LocationImportResultDto,
  AddListItemRequest,
  CardListDto,
  CardListItemDto,
  ListExportFormat,
  ListExportScope,
  CatalogLanguagesDto,
  CatalogStatusDto,
  FulfillListRequest,
  FulfillListResultDto,
  ListItemSubstitutesDto,
  ListSubstitutionDto,
  ListUpdatePreviewDto,
  ListUpdateRowDto,
  CustomerDto,
  DashboardDto,
  DeckBoxNeedsGameDto,
  DeckLegalityDto,
  DeckTypeDto,
  DeckTypeUpsertRequest,
  ImportListResultDto,
  EbayListingDraftDto,
  EbayListingResultDto,
  EbaySellingSettingsDto,
  EbaySetupResultDto,
  EbayStatusDto,
  InventoryLotDto,
  DecklistCheckDto,
  DecklistCheckRequest,
  GameDto,
  InventoryValuationDto,
  ListingDetailDto,
  LocationSummaryDto,
  SiteDto,
  SiteGrantDto,
  OrderDetailDto,
  OrderDto,
  OrderImportPreviewDto,
  OrderImportRowDto,
  OrderImportTemplateDto,
  OrderLineDto,
  ShipScanResultDto,
  PagedResult,
  ProductDto,
  SalesSettingsDto,
  ScanBadgeSettingsDto,
  CompanyProfileDto,
  ReceiptConfigDto,
  ReceiptLayoutDto,
  CreateSavedViewRequest,
  SavedViewDto,
  SavedViewListDto,
  SavedViewPage,
  SavedViewStateDto,
  LogoUploadResultDto,
  ScanCommitItem,
  ScanCommitResultDto,
  ScanBatchCommitResultDto,
  ScanBatchDto,
  ScanBatchItemEdit,
  ScanBatchSummaryDto,
  ScanFolderSettingsDto,
  ScanFolderSettingsResponse,
  AuditCommitResultDto,
  ScanMatchDto,
  ScanSearchResultDto,
  ScanTagRuleItem,
  ScanTagRuleResultDto,
  SearchSchemaDto,
  TagRuleDto,
  TagRuleInput,
  TagRulePreviewDto,
  TagRuleRunResultDto,
  TagRuleValidationDto,
  SetChecklistDto,
  SetInfoDto,
  TradeSearchResult,
  TradeSessionState,
  TradeSummaryDto,
  SignInStepDto,
  UserDto,
  RoleDto,
  PermissionCatalogDto,
  WorkflowLaneDto,
} from './types';

/** Write-request bodies (mirror the Contracts upsert records). */
export interface CustomerFields {
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
export interface ListingFields {
  listedPrice: number;
  channel: string;
  quantity: number;
  note?: string | null;
}
export interface CreateListingBody {
  lotId: number;
  quantity: number;
  price: number;
  channel: string;
  note?: string | null;
}
export interface BulkListingBody {
  items: { lotId: number; price: number }[];
  channel: string;
  note?: string | null;
}
export interface CreateEbayListingBody {
  lotId: number;
  quantity: number;
  price: number;
  note?: string | null;
  title: string;
  description: string;
  condition: string;
  listingType: string;
  auctionDuration?: number | null;
  categoryId?: string | null;
}
export interface ReviseEbayListingBody {
  listingId: number;
  lotId: number;
  price: number;
  title: string;
  description: string;
  condition: string;
  listingType: string;
  auctionDuration?: number | null;
  categoryId?: string | null;
}
export interface ProductFields {
  game: string;
  category: string;
  name: string;
  setName?: string | null;
  setCode?: string | null;
  upc?: string | null;
  lastMarketPrice?: number | null;
}
export interface LotFields {
  quantity: number;
  unitCost?: number | null;
  locationId?: number | null;
  source?: string | null;
}

/** Thrown for non-2xx responses; carries the HTTP status so callers can special-case 401, and the
 *  parsed JSON error body (when there was one) for endpoints that return structured errors. */
export class ApiError extends Error {
  constructor(
    public status: number,
    message: string,
    public body?: unknown,
  ) {
    super(message);
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(path, {
    ...init,
    headers: { 'Content-Type': 'application/json', ...(init?.headers ?? {}) },
    // Session cookie carries the passphrase-unlock state.
    credentials: 'same-origin',
  });
  if (!res.ok) {
    let message = res.statusText;
    let body: unknown;
    try {
      body = await res.json();
      if ((body as { error?: string })?.error) message = (body as { error: string }).error;
    } catch {
      /* non-JSON error body */
    }
    throw new ApiError(res.status, message, body);
  }
  if (res.status === 204) return undefined as T;
  return (await res.json()) as T;
}

/** POST a multipart/form-data body (for endpoints that accept file uploads). */
async function postForm<T>(path: string, form: FormData): Promise<T> {
  const res = await fetch(path, { method: 'POST', body: form, credentials: 'same-origin' });
  if (!res.ok) {
    let message = res.statusText;
    let b: { error?: string } | undefined;
    try {
      b = await res.json();
      if (b?.error) message = b.error;
    } catch {
      /* non-JSON error body */
    }
    throw new ApiError(res.status, message, b);
  }
  if (res.status === 204) return undefined as T;
  return (await res.json()) as T;
}

/** POST a JSON body and save the response as a file (name from Content-Disposition, else `fallbackName`). */
function postDownload(path: string, body: unknown, fallbackName: string): Promise<void> {
  return download(path, fallbackName, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
}

/** Fetch `path` (GET unless `init` says otherwise) and save the response as a file (name from
 * Content-Disposition, else `fallbackName`). Server errors surface as an ApiError, not a broken file. */
async function download(path: string, fallbackName: string, init?: RequestInit): Promise<void> {
  const res = await fetch(path, { ...init, credentials: 'same-origin' });
  if (!res.ok) {
    let message = res.statusText;
    try {
      const b = await res.json();
      if (b?.error) message = b.error;
    } catch {
      /* non-JSON error body */
    }
    throw new ApiError(res.status, message);
  }
  const blob = await res.blob();
  const disposition = res.headers.get('Content-Disposition') ?? '';
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = match ? decodeURIComponent(match[1]) : fallbackName;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}

/** GET `path` as plain text. Server errors surface as an ApiError. */
async function requestText(path: string): Promise<string> {
  const res = await fetch(path, { credentials: 'same-origin' });
  if (!res.ok) {
    let message = res.statusText;
    try {
      const b = await res.json();
      if (b?.error) message = b.error;
    } catch {
      /* non-JSON error body */
    }
    throw new ApiError(res.status, message);
  }
  return res.text();
}

function qs(params: Record<string, string | number | boolean | undefined | null>): string {
  const sp = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (v !== undefined && v !== null && v !== '') sp.set(k, String(v));
  }
  const s = sp.toString();
  return s ? `?${s}` : '';
}

export const api = {
  // Auth
  authStatus: () => request<AuthStatusDto>('/api/auth/status'),
  login: (username: string, password: string, rememberMe: boolean) =>
    request<AuthStatusDto>('/api/auth/login', {
      method: 'POST',
      body: JSON.stringify({ username, password, rememberMe }),
    }),
  signInStep: (login: string) =>
    request<SignInStepDto>('/api/auth/sign-in-step', {
      method: 'POST',
      body: JSON.stringify({ login }),
    }),
  completeSetup: (login: string, setupKey: string, newPassword: string, rememberMe: boolean) =>
    request<AuthStatusDto>('/api/auth/complete-setup', {
      method: 'POST',
      body: JSON.stringify({ login, setupKey, newPassword, rememberMe }),
    }),
  logout: () => request<AuthStatusDto>('/api/auth/logout', { method: 'POST' }),
  changePassword: (currentPassword: string, newPassword: string) =>
    request<void>('/api/auth/change-password', {
      method: 'POST',
      body: JSON.stringify({ currentPassword, newPassword }),
    }),

  // Account (the signed-in user's own details — any signed-in user)
  account: () => request<AccountDto>('/api/account'),
  accountChangeEmail: (email: string | null, currentPassword: string) =>
    request<AccountDto>('/api/account/email', {
      method: 'PUT',
      body: JSON.stringify({ email, currentPassword }),
    }),

  // Users (Administration ▸ Users — admin only)
  users: () => request<UserDto[]>('/api/users'),
  userCreate: (body: {
    username: string;
    email?: string | null;
    setupKey: string;
    isAdmin: boolean;
    roleId?: number | null;
    grant?: string[];
    deny?: string[];
  }) => request<UserDto>('/api/users', { method: 'POST', body: JSON.stringify(body) }),
  userUpdate: (
    id: number,
    body: { email: string | null; roleId: number | null; grant: string[]; deny: string[]; isAdmin: boolean },
  ) => request<UserDto>(`/api/users/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  userDelete: (id: number) => request<void>(`/api/users/${id}`, { method: 'DELETE' }),
  userRequirePasswordReset: (id: number, setupKey: string) =>
    request<void>(`/api/users/${id}/require-password-reset`, {
      method: 'POST',
      body: JSON.stringify({ setupKey }),
    }),

  // Roles (Administration ▸ Roles — admin only)
  roles: () => request<RoleDto[]>('/api/roles'),
  roleCreate: (body: { name: string; permissions: string[] }) =>
    request<RoleDto>('/api/roles', { method: 'POST', body: JSON.stringify(body) }),
  roleUpdate: (id: number, body: { name: string; permissions: string[] }) =>
    request<RoleDto>(`/api/roles/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  roleDelete: (id: number) => request<void>(`/api/roles/${id}`, { method: 'DELETE' }),

  // Meta
  games: () => request<GameDto[]>('/api/meta/games'),
  components: () => request<ComponentDto[]>('/api/meta/components'),
  permissionCatalog: () => request<PermissionCatalogDto>('/api/meta/permissions'),
  searchFields: (game?: string) => request<SearchSchemaDto>(`/api/meta/search-fields${qs({ game })}`),
  /** Languages an owned copy of each game can be tagged with, keyed by game id. */
  cardLanguages: () => request<Record<string, string[]>>('/api/meta/card-languages'),

  // Dashboard
  dashboard: () => request<DashboardDto>('/api/dashboard'),

  // Locations
  /** Locations in the sites the user can read; `siteId` narrows to one site. */
  locations: (game?: string, siteId?: number) =>
    request<LocationSummaryDto[]>(`/api/locations${qs({ game, siteId })}`),
  locationSetSite: (id: number, siteId: number) =>
    request<void>(`/api/locations/${id}/site`, { method: 'PUT', body: JSON.stringify({ siteId }) }),

  // Saved views of the Collection / Location pages. `game` is the selected game (none = All Games).
  savedViews: (page: SavedViewPage, containerId: number | undefined, game: string | undefined) =>
    request<SavedViewListDto>(`/api/views${qs({ page, containerId, game })}`),
  savedView: (id: number) => request<SavedViewDto>(`/api/views/${id}`),
  savedViewCreate: (body: CreateSavedViewRequest) =>
    request<SavedViewDto>('/api/views', { method: 'POST', body: JSON.stringify(body) }),
  savedViewUpdate: (id: number, body: { name?: string; state?: SavedViewStateDto }) =>
    request<SavedViewDto>(`/api/views/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  savedViewDelete: (id: number) => request<void>(`/api/views/${id}`, { method: 'DELETE' }),
  /** Make a view the default on the current page — the user's own, or (`everyone`, admins) everyone's.
   * `game` is the game on screen (an any-game default replaces that game's default). */
  savedViewSetDefault: (
    id: number,
    page: SavedViewPage,
    containerId: number | undefined,
    game: string | undefined,
    everyone: boolean,
    on: boolean,
  ) =>
    request<void>(`/api/views/${id}/default${qs({ page, containerId, game, everyone })}`, {
      method: on ? 'PUT' : 'DELETE',
    }),
  savedViewCopy: (id: number, containerIds: number[], setDefault: boolean) =>
    request<{ copied: number }>(`/api/views/${id}/copy`, {
      method: 'POST',
      body: JSON.stringify({ containerIds, setDefault }),
    }),

  // Sites (major physical locations). Listing returns only the sites the user can see; the rest is admin-only.
  sites: () => request<SiteDto[]>('/api/sites'),
  siteCreate: (body: { name: string; description?: string | null }) =>
    request<SiteDto>('/api/sites', { method: 'POST', body: JSON.stringify(body) }),
  siteUpdate: (id: number, body: { name: string; description?: string | null }) =>
    request<SiteDto>(`/api/sites/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  siteDelete: (id: number, moveToSiteId?: number) =>
    request<void>(`/api/sites/${id}${qs({ moveToSiteId })}`, { method: 'DELETE' }),
  siteGrants: (id: number) => request<SiteGrantDto[]>(`/api/sites/${id}/grants`),
  siteSetGrants: (id: number, grants: SiteGrantDto[]) =>
    request<void>(`/api/sites/${id}/grants`, { method: 'PUT', body: JSON.stringify({ grants }) }),
  location: (id: number) => request<LocationSummaryDto>(`/api/locations/${id}`),

  // Sets
  sets: (game: string) => request<SetInfoDto[]>(`/api/sets${qs({ game })}`),
  setChecklist: (game: string, setCode: string) =>
    request<SetChecklistDto>(`/api/sets/${encodeURIComponent(game)}/${encodeURIComponent(setCode)}`),

  // Binder
  binder: (id: number, spread: number) =>
    request<BinderStateDto>(`/api/binder/${id}${qs({ spread })}`),

  // Binder editing (BinderEditController — gated by the binder-edit passphrase, open when unset)
  binderUnplaced: (containerId: number, filter?: string) =>
    request<{ cards: BinderCardDto[] }>(`/api/binder/unplaced${qs({ containerId, filter })}`).then((r) => r.cards),
  binderAssign: (lotId: number, containerId: number, page: number, slot: number) =>
    request<void>('/api/binder/assign', { method: 'POST', body: JSON.stringify({ lotId, containerId, page, slot }) }),
  binderUnassign: (lotId: number) =>
    request<void>('/api/binder/unassign', { method: 'POST', body: JSON.stringify({ lotId }) }),
  binderAddPage: (containerId: number, mode: 'single' | 'double') =>
    request<{ spreadIndex: number }>('/api/binder/page/add', { method: 'POST', body: JSON.stringify({ containerId, mode }) }),
  binderRemovePage: (containerId: number, page: number) =>
    request<void>('/api/binder/page/remove', { method: 'POST', body: JSON.stringify({ containerId, page }) }),
  binderLayout: (containerId: number, slotsPerPage: number, columns: number) =>
    request<void>('/api/binder/layout', { method: 'POST', body: JSON.stringify({ containerId, slotsPerPage, columns }) }),
  /** Relocate one owned copy of a lot from wherever it lives into a binder pocket (splits a stack). */
  binderPlaceOwned: (lotId: number, containerId: number, page: number, slot: number) =>
    request<void>('/api/binder/card/place-owned', {
      method: 'POST',
      body: JSON.stringify({ lotId, containerId, page, slot }),
    }),
  /** Create a new loose lot from a catalog card and drop it straight into a binder pocket. */
  binderAddMissing: (body: {
    containerId: number;
    page: number;
    slot: number;
    game: string;
    gameSpecificId: string;
    name: string;
    setCode: string;
    setName: string;
    collectorNumber: string;
    rarity: string;
    imageUri?: string | null;
    condition: string;
    isFoil: boolean;
    foilType?: string | null;
    purchasePrice?: number | null;
  }) => request<void>('/api/binder/card/add-missing', { method: 'POST', body: JSON.stringify(body) }),

  // Collection
  collection: (opts: {
    game?: string;
    q?: string;
    containerId?: number;
    /** Narrow to one site (the server already limits results to sites the user can read). */
    siteId?: number;
    skip?: number;
    take?: number;
    stacked?: boolean;
    sort?: string;
    dir?: 'asc' | 'desc';
  }) => request<PagedResult<CardDto>>(`/api/collection${qs(opts)}`),
  /** The collection grouped by `groupBy` (outermost first); `toggled` groups differ from the default state. */
  collectionGrouped: (body: {
    game?: string;
    q?: string;
    containerId?: number;
    siteId?: number;
    skip: number;
    take: number;
    stacked: boolean;
    sort?: string;
    dir?: 'asc' | 'desc';
    groupBy: string[];
    collapsedByDefault: boolean;
    toggled: string[];
  }) => request<PagedResult<GroupedCardRowDto>>('/api/collection/grouped', { method: 'POST', body: JSON.stringify(body) }),

  // Location writes
  locationNameAvailable: (name: string, excludeId?: number) =>
    request<{ available: boolean }>(`/api/locations/name-available${qs({ name, excludeId })}`),
  locationCreate: (body: {
    name: string;
    type: string;
    slotsPerPage?: number;
    game?: string | null;
    deckTypeId?: number | null;
    /** Site to create the location in (omitted = the default site). */
    siteId?: number | null;
  }) => request<LocationSummaryDto>('/api/locations', { method: 'POST', body: JSON.stringify(body) }),
  locationRename: (id: number, name: string) =>
    request<void>(`/api/locations/${id}`, { method: 'PUT', body: JSON.stringify({ name }) }),
  locationDelete: (id: number, moveToBulk: boolean) =>
    request<void>(`/api/locations/${id}${qs({ moveToBulk })}`, { method: 'DELETE' }),
  /** Ignore (or stop ignoring) locations when lists and decklist checks look for cards. */
  locationsSetIgnoredForLists: (ids: number[], value: boolean) =>
    request<void>('/api/locations/ignore-for-lists', { method: 'PUT', body: JSON.stringify({ ids, value }) }),
  locationSetAlwaysAvailable: (id: number, value: boolean) =>
    request<void>(`/api/locations/${id}/always-available`, {
      method: 'PUT',
      body: JSON.stringify({ value }),
    }),
  // Deck box: assign/reassign game + deck type; legacy needs-game list; advisory legality check.
  locationSetDeckBox: (id: number, game: string, deckTypeId?: number | null) =>
    request<void>(`/api/locations/${id}/deck-box`, {
      method: 'PUT',
      body: JSON.stringify({ game, deckTypeId: deckTypeId ?? null }),
    }),
  deckBoxesNeedingGame: () =>
    request<DeckBoxNeedsGameDto[]>('/api/locations/deck-boxes/needs-game'),
  locationDeckLegality: (id: number) =>
    request<DeckLegalityDto>(`/api/locations/${id}/deck-legality`),

  // Deck types (per-game formats): built-ins + custom
  deckTypes: (game: string) => request<DeckTypeDto[]>(`/api/deck-types${qs({ game })}`),
  deckTypeCreate: (body: DeckTypeUpsertRequest) =>
    request<DeckTypeDto>('/api/deck-types', { method: 'POST', body: JSON.stringify(body) }),
  deckTypeUpdate: (id: number, body: DeckTypeUpsertRequest) =>
    request<void>(`/api/deck-types/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  deckTypeDelete: (id: number) =>
    request<void>(`/api/deck-types/${id}`, { method: 'DELETE' }),

  // Card writes
  card: (id: number) => request<CardDto>(`/api/collection/${id}`),
  cardUpdate: (
    id: number,
    body: {
      condition: string;
      language?: string;
      isFoil: boolean;
      foilType?: string | null;
      quantity: number;
      purchasePrice?: number | null;
      note?: string | null;
    },
  ) => request<void>(`/api/collection/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  cardDelete: (id: number) => request<void>(`/api/collection/${id}`, { method: 'DELETE' }),
  /** Split `quantity` copies off a stacked lot into a new loose lot (returns the new lot id). */
  cardSplit: (id: number, quantity: number) =>
    request<{ lotId: number }>(`/api/collection/${id}/split`, {
      method: 'POST',
      body: JSON.stringify({ quantity }),
    }),
  /** Split a stacked lot into single copies — the original keeps one, each other copy gets its own
   *  loose lot (returns the new lot ids). */
  cardSplitSingles: (id: number) =>
    request<{ lotIds: number[] }>(`/api/collection/${id}/split-singles`, { method: 'POST' }),
  cardMove: (cardIds: number[], containerId: number, section?: string) =>
    request<void>('/api/collection/move', {
      method: 'POST',
      body: JSON.stringify({ cardIds, containerId, section }),
    }),
  cardSetTags: (id: number, tags: string[]) =>
    request<void>(`/api/collection/${id}/tags`, { method: 'PUT', body: JSON.stringify({ tags }) }),
  /** Bulk-edit selected lots. Only fields with their `setX` flag are applied. */
  cardBulkUpdate: (body: {
    cardIds: number[];
    setCondition?: boolean;
    condition?: string;
    setLanguage?: boolean;
    language?: string;
    setFoil?: boolean;
    isFoil?: boolean;
    setQuantity?: boolean;
    quantity?: number;
    setPurchasePrice?: boolean;
    purchasePrice?: number | null;
    setNote?: boolean;
    note?: string | null;
    setTags?: boolean;
    tagsMode?: 'add' | 'replace';
    tags?: string[];
  }) => request<void>('/api/collection/bulk-update', { method: 'POST', body: JSON.stringify(body) }),

  // Tags
  tags: () => request<{ id: number; name: string; usageCount: number }[]>('/api/tags'),

  // Sales
  orders: () => request<OrderDto[]>('/api/orders'),
  orderLanes: () => request<WorkflowLaneDto[]>('/api/orders/lanes'),
  orderSetStatus: (id: number, status: string, stageKey?: string) =>
    request<void>(`/api/orders/${id}/status`, {
      method: 'PUT',
      body: JSON.stringify({ status, stageKey }),
    }),
  order: (id: number) => request<OrderDetailDto>(`/api/orders/${id}`),
  orderCreate: (body: { customerId: number; channel: string; orderNumber?: string }) =>
    request<OrderDto>('/api/orders', { method: 'POST', body: JSON.stringify(body) }),
  orderUpdate: (
    id: number,
    body: {
      channel: string;
      orderNumber?: string | null;
      trackingNumber?: string | null;
      carrier?: string | null;
      shippingChargedToBuyer: number;
      shippingCost: number;
      marketplaceFees: number;
      notes?: string | null;
    },
  ) => request<void>(`/api/orders/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  orderDelete: (id: number) => request<void>(`/api/orders/${id}`, { method: 'DELETE' }),
  /** Look up a scanned shipping-label barcode; with `ship`, a single open match is shipped at once. */
  orderShipScan: (code: string, ship: boolean) =>
    request<ShipScanResultDto>('/api/orders/ship-scan', { method: 'POST', body: JSON.stringify({ code, ship }) }),
  orderShip: (id: number) => request<OrderDto>(`/api/orders/${id}/ship`, { method: 'POST' }),
  orderAddLine: (id: number, lotId: number, unitSalePrice: number) =>
    request<OrderLineDto>(`/api/orders/${id}/lines`, {
      method: 'POST',
      body: JSON.stringify({ lotId, unitSalePrice }),
    }),
  orderRemoveLine: (lineId: number) =>
    request<void>(`/api/orders/lines/${lineId}`, { method: 'DELETE' }),

  // Order CSV import
  orderImportFields: () => request<string[]>('/api/orders/import/fields'),
  orderImportTemplates: () => request<OrderImportTemplateDto[]>('/api/orders/import/templates'),
  orderImportTemplateSave: (body: {
    id?: string;
    name: string;
    channel: string;
    columnMappings: Record<string, string>;
  }) =>
    request<OrderImportTemplateDto>('/api/orders/import/templates', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  orderImportTemplateDelete: (id: string) =>
    request<void>(`/api/orders/import/templates/${encodeURIComponent(id)}`, { method: 'DELETE' }),
  orderImportHeaders: (file: File) => {
    const form = new FormData();
    form.append('file', file);
    return postForm<{ headers: string[] }>('/api/orders/import/headers', form);
  },
  orderImportPreview: (file: File, mappings: Record<string, string>, channel: string) => {
    const form = new FormData();
    form.append('file', file);
    form.append('mappingsJson', JSON.stringify(mappings));
    form.append('channel', channel);
    return postForm<OrderImportPreviewDto>('/api/orders/import/preview', form);
  },
  orderImportCommit: (channel: string, rows: OrderImportRowDto[]) =>
    request<{ created: number }>('/api/orders/import/commit', {
      method: 'POST',
      body: JSON.stringify({ channel, rows }),
    }),
  listings: (game?: string) => request<ActiveListingDto[]>(`/api/listings${qs({ game })}`),
  listingDetails: (game?: string) =>
    request<ListingDetailDto[]>(`/api/listings/details${qs({ game })}`),
  listingCreate: (body: CreateListingBody) =>
    request<{ lotId: number }>('/api/listings', { method: 'POST', body: JSON.stringify(body) }),
  listingBulkCreate: (body: BulkListingBody) =>
    request<{ listed: number }>('/api/listings/bulk', { method: 'POST', body: JSON.stringify(body) }),
  listingPick: (lotIds: number[]) =>
    request<{ picked: number }>('/api/listings/pick', { method: 'POST', body: JSON.stringify({ lotIds }) }),
  listingUpdate: (id: number, body: ListingFields) =>
    request<void>(`/api/listings/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  listingUnlist: (lotId: number) =>
    request<void>(`/api/listings/lot/${lotId}`, { method: 'DELETE' }),
  /** A draft eBay listing (suggested title/description + category candidates) to prefill the dialog. */
  ebayListingPrepare: (lotId: number) =>
    request<EbayListingDraftDto>(`/api/listings/ebay/prepare${qs({ lotId })}`),
  /** List a lot for sale and push it to eBay. The lot is listed locally even if the eBay push fails. */
  ebayListingCreate: (body: CreateEbayListingBody) =>
    request<EbayListingResultDto>('/api/listings/ebay', { method: 'POST', body: JSON.stringify(body) }),
  /** Update (revise) an already-published eBay listing with edited price/details. */
  ebayListingRevise: (body: ReviseEbayListingBody) =>
    request<EbayListingResultDto>('/api/listings/ebay/revise', { method: 'POST', body: JSON.stringify(body) }),
  pickListPdfUrl: (game?: string) => `/api/listings/picklist.pdf${qs({ game })}`,
  settings: () => request<SalesSettingsDto>('/api/settings'),
  settingsUpdate: (body: { forSaleLocationId: number | null; movePickedToForSaleLocation: boolean }) =>
    request<void>('/api/settings', { method: 'PUT', body: JSON.stringify(body) }),
  scanBadgeSettings: () => request<ScanBadgeSettingsDto>('/api/settings/scan-badges'),
  scanBadgeSettingsUpdate: (body: ScanBadgeSettingsDto) =>
    request<void>('/api/settings/scan-badges', { method: 'PUT', body: JSON.stringify(body) }),
  /** A printable, thermal-width receipt PDF for an order (open in a new tab, then print). */
  receiptPdfUrl: (orderId: number) => `/api/orders/${orderId}/receipt.pdf`,
  /** Print-ready HTML receipt sized to the configured roll width (auto-opens the print dialog). Preferred
   * for thermal printers — avoids the PDF padding the job out to a full sheet. */
  receiptHtmlUrl: (orderId: number) => `/api/orders/${orderId}/receipt.html`,
  receiptConfig: () => request<ReceiptConfigDto>('/api/settings/receipt'),
  receiptConfigUpdate: (body: {
    company: CompanyProfileDto;
    receipt: ReceiptLayoutDto;
  }) => request<void>('/api/settings/receipt', { method: 'PUT', body: JSON.stringify(body) }),
  receiptLogoUpload: (file: File) => {
    const form = new FormData();
    form.append('file', file);
    return postForm<LogoUploadResultDto>('/api/settings/receipt/logo', form);
  },
  receiptLogoDelete: () => request<void>('/api/settings/receipt/logo', { method: 'DELETE' }),
  customers: () => request<CustomerDto[]>('/api/customers'),
  customerCreate: (body: CustomerFields) =>
    request<CustomerDto>('/api/customers', { method: 'POST', body: JSON.stringify(body) }),
  customerUpdate: (id: number, body: CustomerFields) =>
    request<void>(`/api/customers/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  customerDelete: (id: number) =>
    request<void>(`/api/customers/${id}`, { method: 'DELETE' }),

  // Inventory (sealed)
  inventoryProducts: (game?: string, category?: string) =>
    request<ProductDto[]>(`/api/inventory/products${qs({ game, category })}`),
  inventoryValuation: () => request<InventoryValuationDto>('/api/inventory/valuation'),
  inventoryLots: (productId: number) =>
    request<InventoryLotDto[]>(`/api/inventory/products/${productId}/lots`),
  inventoryProductCreate: (body: ProductFields) =>
    request<ProductDto>('/api/inventory/products', { method: 'POST', body: JSON.stringify(body) }),
  inventoryProductUpdate: (id: number, body: ProductFields) =>
    request<void>(`/api/inventory/products/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  inventoryProductDelete: (id: number) =>
    request<void>(`/api/inventory/products/${id}`, { method: 'DELETE' }),
  inventoryAddLot: (productId: number, body: LotFields) =>
    request<InventoryLotDto>(`/api/inventory/products/${productId}/lots`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  inventoryUpdateLot: (lotId: number, body: LotFields) =>
    request<void>(`/api/inventory/lots/${lotId}`, { method: 'PUT', body: JSON.stringify(body) }),
  inventoryDeleteLot: (lotId: number) =>
    request<void>(`/api/inventory/lots/${lotId}`, { method: 'DELETE' }),

  // Import / Export
  exportUrl: (format: string, game?: string, q?: string) =>
    `/api/export/collection${qs({ format, game, q })}`,
  exportSelection: async (ids: number[], format: string) => {
    const res = await fetch('/api/export/selection', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ ids, format }),
      credentials: 'same-origin',
    });
    if (!res.ok) {
      let message = res.statusText;
      try {
        const b = await res.json();
        if (b?.error) message = b.error;
      } catch {
        /* non-JSON error body */
      }
      throw new ApiError(res.status, message);
    }
    const blob = await res.blob();
    const disposition = res.headers.get('Content-Disposition') ?? '';
    const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
    const filename = match ? decodeURIComponent(match[1]) : `selection-${format}.csv`;
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
  },
  importCsv: async (file: File, skipDuplicates: boolean, targetContainerId?: number) => {
    const form = new FormData();
    form.append('file', file);
    const res = await fetch(`/api/import/csv${qs({ skipDuplicates, targetContainerId })}`, {
      method: 'POST',
      body: form,
      credentials: 'same-origin',
    });
    if (!res.ok) {
      let message = res.statusText;
      try {
        const b = await res.json();
        if (b?.error) message = b.error;
      } catch {
        /* ignore */
      }
      throw new ApiError(res.status, message);
    }
    return (await res.json()) as CsvImportResultDto;
  },
  /** Import a Moxfield/Archidekt deck URL straight into a location as owned lots. */
  importUrl: (body: { url: string; game: string; containerId: number; condition: string; skipDuplicates: boolean }) =>
    request<ImportUrlResultDto>('/api/import/url', { method: 'POST', body: JSON.stringify(body) }),
  /** All-or-nothing CSV import into one location (the Location view's Import). A rejected import
   *  throws an ApiError (422) whose body is a LocationImportFailureDto listing every problem. */
  importCsvToLocation: (locationId: number, file: File) => {
    const form = new FormData();
    form.append('file', file);
    return postForm<LocationImportResultDto>(`/api/import/location/${locationId}/csv`, form);
  },
  /** All-or-nothing Moxfield/Archidekt deck import into one location (422 + LocationImportFailureDto on rejection). */
  importUrlToLocation: (locationId: number, body: { url: string; condition: string }) =>
    request<LocationImportResultDto>(`/api/import/location/${locationId}/url`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  decklistCheck: (body: DecklistCheckRequest) =>
    request<DecklistCheckDto>('/api/decklist/check', { method: 'POST', body: JSON.stringify(body) }),
  /** Download the printable pull list (owned copies to pull, with tick-boxes) for a decklist. */
  decklistPullListPdf: (body: DecklistCheckRequest) =>
    postDownload('/api/decklist/pull-list.pdf', body, 'pull-list.pdf'),
  /** Download the printable missing-cards list (with prices and tick-boxes) for a decklist. */
  decklistMissingListPdf: (body: DecklistCheckRequest) =>
    postDownload('/api/decklist/missing-list.pdf', body, 'missing-cards.pdf'),
  /** Move a decklist check's picks into a deck box (stacks are split so only the needed copies move). */
  decklistMoveToDeckBox: (containerId: number, picks: { lotId: number; quantity: number }[]) =>
    request<{ moved: number }>('/api/decklist/move-to-deck-box', {
      method: 'POST',
      body: JSON.stringify({ containerId, picks }),
    }),

  // Trades (read-only history)
  trades: () => request<TradeSummaryDto[]>('/api/trades'),

  // Trade builder (in-progress draft session → applied to the collection on finalize)
  tradeSession: () =>
    request<{ session: TradeSessionState | null }>('/api/trade-session').then((r) => r.session),
  tradeStart: () =>
    request<{ session: TradeSessionState }>('/api/trade-session/start', { method: 'POST' }).then((r) => r.session),
  tradeAddOwned: (lotId: number) =>
    request<{ session: TradeSessionState }>('/api/trade-session/add-owned', {
      method: 'POST',
      body: JSON.stringify({ lotId }),
    }).then((r) => r.session),
  tradeRemoveItem: (index: number) =>
    request<{ session: TradeSessionState }>('/api/trade-session/remove-item', {
      method: 'POST',
      body: JSON.stringify({ index }),
    }).then((r) => r.session),
  tradeAddOffDb: (name: string, value: number | null, photo: File | null) => {
    const form = new FormData();
    if (name) form.append('name', name);
    if (value != null) form.append('value', String(value));
    if (photo) form.append('photo', photo);
    return postForm<{ session: TradeSessionState }>('/api/trade-session/add-offdb', form).then((r) => r.session);
  },
  tradeFinalize: (note: string, receivedValue: number | null, receivedPhoto: File | null) => {
    const form = new FormData();
    if (note) form.append('note', note);
    if (receivedValue != null) form.append('receivedValue', String(receivedValue));
    if (receivedPhoto) form.append('receivedPhoto', receivedPhoto);
    return postForm<{ applied: number }>('/api/trade-session/finalize', form);
  },
  tradeCancel: () => request<void>('/api/trade-session/cancel', { method: 'POST' }),
  tradeSearch: (q: string) =>
    request<{ results: TradeSearchResult[] }>(`/api/trade-session/search${qs({ q })}`).then((r) => r.results),

  // Card lists
  lists: (game: string) => request<CardListDto[]>(`/api/lists${qs({ game })}`),
  listCreate: (name: string, game: string) =>
    request<CardListDto>('/api/lists', { method: 'POST', body: JSON.stringify({ name, game }) }),
  listRename: (id: number, name: string) =>
    request<void>(`/api/lists/${id}`, { method: 'PUT', body: JSON.stringify({ name }) }),
  listDelete: (id: number) => request<void>(`/api/lists/${id}`, { method: 'DELETE' }),
  listItems: (id: number) => request<CardListItemDto[]>(`/api/lists/${id}/items`),
  /** Add a single "wholly new" card (chosen from the catalog) to a list. Records a printing reference
   * only — no lot is created and the collection is untouched. */
  listAddItem: (id: number, body: AddListItemRequest) =>
    request<CardListItemDto>(`/api/lists/${id}/items`, { method: 'POST', body: JSON.stringify(body) }),
  /** Add a card the user already owns to a list by referencing an existing lot. The lot is not moved or
   * mutated; on commit the referenced copies are relocated to the target location instead of duplicated. */
  listAddFromCollection: (id: number, lotId: number, quantity: number) =>
    request<CardListItemDto>(`/api/lists/${id}/items/from-collection`, {
      method: 'POST',
      body: JSON.stringify({ lotId, quantity }),
    }),
  listRemoveItem: (itemId: number) =>
    request<void>(`/api/lists/items/${itemId}`, { method: 'DELETE' }),
  listSetItemQuantity: (itemId: number, quantity: number) =>
    request<void>(`/api/lists/items/${itemId}`, { method: 'PUT', body: JSON.stringify({ quantity }) }),
  listRefreshPrices: (id: number) =>
    request<void>(`/api/lists/${id}/refresh-prices`, { method: 'POST' }),
  /** Fulfil a list: owned copies (exact printing) move to `moveToContainerId`, copies not in the collection
   * are created as new lots at `addToContainerId`. Omit either to do only the other half. Done copies come
   * off the list; an emptied list is deleted. */
  listFulfill: (id: number, body: FulfillListRequest) =>
    request<FulfillListResultDto>(`/api/lists/${id}/fulfill`, { method: 'POST', body: JSON.stringify(body) }),
  /** Download the whole list as a printable PDF (quantities, owned counts, prices). */
  listPrintPdf: (id: number) => download(`/api/lists/${id}/print.pdf`, 'list.pdf'),
  /** Download the pick list: owned copies to pull, grouped by location, with tick-boxes. */
  listPickListPdf: (id: number) => download(`/api/lists/${id}/pick-list.pdf`, 'pick-list.pdf'),
  /** Download the buy list: copies not in the collection, with prices and tick-boxes. */
  listBuyListPdf: (id: number) => download(`/api/lists/${id}/buy-list.pdf`, 'buy-list.pdf'),
  /** The list (or its to-buy / owned part) as decklist text ("1x Aragorn, the Uniter (LTR) 192") or CSV. */
  listExportText: (id: number, scope: ListExportScope, format: ListExportFormat) =>
    requestText(`/api/lists/${id}/export${qs({ scope, format })}`),
  /** Download the same export as a .txt / .csv file. */
  listExportDownload: (id: number, scope: ListExportScope, format: ListExportFormat) =>
    download(`/api/lists/${id}/export${qs({ scope, format })}`, format === 'csv' ? 'list.csv' : 'list.txt'),
  /** Import a Moxfield/Archidekt decklist URL. Omit `listId` to create a new list named after the deck. */
  listImportUrl: (url: string, game: string, listId?: number, language?: string | null) =>
    request<ImportListResultDto>('/api/lists/import-url', {
      method: 'POST',
      body: JSON.stringify({ url, game, listId, language: language || null }),
    }),
  /** Force a list's card language (`null` = any language). */
  listSetLanguage: (id: number, language: string | null) =>
    request<void>(`/api/lists/${id}/language`, { method: 'PUT', body: JSON.stringify({ language }) }),
  /** Re-fetch the list's deck (its stored URL, or `url`) and return the changes, without applying them. */
  listUpdatePreview: (id: number, url?: string) =>
    request<ListUpdatePreviewDto>(`/api/lists/${id}/update-preview`, {
      method: 'POST',
      body: JSON.stringify({ url: url || null }),
    }),
  /** Apply the approved update rows and remember `url` as the list's source. */
  listUpdateApply: (id: number, url: string, rows: ListUpdateRowDto[]) =>
    request<void>(`/api/lists/${id}/update-apply`, { method: 'POST', body: JSON.stringify({ url, rows }) }),
  /** Owned copies of other printings that could stand in for cards the collection is missing. */
  listSubstitutes: (id: number) =>
    request<ListItemSubstitutesDto[]>(`/api/lists/${id}/substitutes`, { method: 'POST' }),
  /** Apply approved stand-ins. */
  listApplySubstitutes: (id: number, substitutions: ListSubstitutionDto[]) =>
    request<void>(`/api/lists/${id}/substitutes/apply`, {
      method: 'POST',
      body: JSON.stringify({ substitutions }),
    }),

  // Catalog refresh
  catalogStatus: () => request<CatalogStatusDto>('/api/catalog/status'),
  catalogLanguages: () => request<CatalogLanguagesDto[]>('/api/catalog/languages'),
  catalogSetLanguages: (game: string, languages: string[]) =>
    request<CatalogLanguagesDto>('/api/catalog/languages', {
      method: 'PUT',
      body: JSON.stringify({ game, languages }),
    }),
  catalogRefresh: (game: string, operation: 'prices' | 'bulk' | 'hashes' | 'images') =>
    request<void>('/api/catalog/refresh', {
      method: 'POST',
      body: JSON.stringify({ game, operation }),
    }),

  // eBay
  ebayStatus: () => request<EbayStatusDto>('/api/ebay/status'),
  /** Top-level navigation URL to start the eBay OAuth consent flow. */
  ebayConnectUrl: '/api/ebay/connect',
  ebayDisconnect: () => request<void>('/api/ebay/disconnect', { method: 'POST' }),
  ebaySetup: () => request<EbaySetupResultDto>('/api/ebay/setup', { method: 'POST' }),
  ebaySelling: () => request<EbaySellingSettingsDto>('/api/ebay/selling'),
  ebaySellingSave: (body: EbaySellingSettingsDto) =>
    request<EbaySellingSettingsDto>('/api/ebay/selling', { method: 'PUT', body: JSON.stringify(body) }),

  // Scan (server-side image matching)
  /** `language` is the scan session's card language (blank = auto: read off the card / taken from
   * the matched printing). */
  scanMatch: async (image: File, game: string, isFoil: boolean, sets?: string[], language?: string) => {
    const form = new FormData();
    form.append('image', image);
    form.append('game', game);
    form.append('isFoil', String(isFoil));
    if (language) form.append('language', language);
    // One `set` entry per chosen art-fallback set; the server unions them.
    for (const s of sets ?? []) if (s) form.append('set', s);
    const res = await fetch('/api/scan/match', {
      method: 'POST',
      body: form,
      credentials: 'same-origin',
    });
    if (!res.ok) {
      let message = res.statusText;
      try {
        const b = await res.json();
        if (b?.error) message = b.error;
      } catch {
        /* ignore */
      }
      throw new ApiError(res.status, message);
    }
    return (await res.json()) as ScanMatchDto;
  },
  // `sets` scopes the correction search to the chosen "Sets (art fallback)" (empty ⇒ all sets); the
  // server unions them, mirroring the set constraint used for auto-matching.
  scanSearch: (game: string, q: string, sets?: string[], cn?: string) => {
    const sp = new URLSearchParams({ game });
    if (q) sp.set('q', q);
    if (cn) sp.set('cn', cn);
    for (const s of sets ?? []) if (s) sp.append('set', s);
    return request<ScanSearchResultDto[]>(`/api/scan/search?${sp.toString()}`);
  },
  scanFoilTypes: (game: string) => request<string[]>(`/api/scan/foil-types${qs({ game })}`),
  // `applyTagRules` has the server apply the tag rules to the new cards — for one-step adds with no
  // review. The Scan page leaves it off: its review already shows (and lets the user remove) rule tags.
  scanCommit: (containerId: number, items: ScanCommitItem[], applyTagRules = false) =>
    request<ScanCommitResultDto>('/api/scan/commit', {
      method: 'POST',
      body: JSON.stringify({ containerId, items, applyTagRules }),
    }),
  /** The enabled tag rules' tags for scanned, not-yet-saved cards of one game. */
  scanTagRules: (game: string, items: ScanTagRuleItem[]) =>
    request<ScanTagRuleResultDto[]>('/api/scan/tag-rules', {
      method: 'POST',
      body: JSON.stringify({ game, items }),
    }),
  // Export staged scans WITHOUT adding them to the collection. One format ⇒ that file; several ⇒ a
  // zip with one file per format. `fileName` is the base name (no extension).
  scanExport: (formats: string[], items: ScanCommitItem[], fileName: string) =>
    postDownload(
      '/api/scan/export',
      { formats, items, fileName },
      formats.length === 1 ? `${fileName}-${formats[0]}.csv` : `${fileName}.zip`,
    ),
  // Location audit: the confirmed scans become the source of truth for the location (matched cards
  // kept, absent cards deleted, new cards added). Returns the per-bucket summary.
  auditCommit: (containerId: number, items: ScanCommitItem[]) =>
    request<AuditCommitResultDto>('/api/scan/audit-commit', {
      method: 'POST',
      body: JSON.stringify({ containerId, items }),
    }),

  // Scan batches: images dropped in a watched folder, matched in the background. Changing a batch's
  // items requires holding its claim (a 409 means someone else has it, or it closed).
  scanBatches: () => request<ScanBatchSummaryDto[]>('/api/scan/batches'),
  scanBatchCount: () => request<{ unclaimed: number }>('/api/scan/batches/count'),
  scanBatch: (id: number) => request<ScanBatchDto>(`/api/scan/batches/${id}`),
  scanBatchClaim: (id: number, force = false) =>
    request<ScanBatchSummaryDto>(`/api/scan/batches/${id}/claim${qs({ force: force || undefined })}`, {
      method: 'POST',
    }),
  scanBatchRelease: (id: number) => request<void>(`/api/scan/batches/${id}/release`, { method: 'POST' }),
  scanBatchSaveItems: (id: number, edits: ScanBatchItemEdit[]) =>
    request<void>(`/api/scan/batches/${id}/items`, { method: 'PUT', body: JSON.stringify(edits) }),
  scanBatchRemove: (id: number, itemIds: number[]) =>
    request<{ batchClosed: boolean }>(`/api/scan/batches/${id}/items/remove`, {
      method: 'POST',
      body: JSON.stringify({ itemIds }),
    }),
  scanBatchRematch: (id: number, itemIds: number[]) =>
    request<void>(`/api/scan/batches/${id}/items/rematch`, { method: 'POST', body: JSON.stringify({ itemIds }) }),
  scanBatchCommit: (id: number, containerId: number, itemIds: number[]) =>
    request<ScanBatchCommitResultDto>(`/api/scan/batches/${id}/commit`, {
      method: 'POST',
      body: JSON.stringify({ containerId, itemIds }),
    }),
  scanBatchDiscard: (id: number) => request<void>(`/api/scan/batches/${id}/discard`, { method: 'POST' }),
  // Tag rules (admin only).
  tagRules: (game?: string) => request<TagRuleDto[]>(`/api/tag-rules${qs({ game })}`),
  tagRuleCreate: (body: TagRuleInput) =>
    request<TagRuleDto>('/api/tag-rules', { method: 'POST', body: JSON.stringify(body) }),
  tagRuleUpdate: (id: number, body: TagRuleInput) =>
    request<TagRuleDto>(`/api/tag-rules/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  tagRuleDelete: (id: number) => request<void>(`/api/tag-rules/${id}`, { method: 'DELETE' }),
  tagRuleValidate: (game: string, query: string) =>
    request<TagRuleValidationDto>('/api/tag-rules/validate', { method: 'POST', body: JSON.stringify({ game, query }) }),
  tagRulePreview: (game: string, query: string, tags: string[]) =>
    request<TagRulePreviewDto>('/api/tag-rules/preview', {
      method: 'POST',
      body: JSON.stringify({ game, query, tags }),
    }),
  tagRuleRun: (id: number) => request<TagRuleRunResultDto>(`/api/tag-rules/${id}/run`, { method: 'POST' }),
  scanFolderSettings: () => request<ScanFolderSettingsResponse>('/api/settings/scan-folders'),
  scanFolderSettingsUpdate: (body: ScanFolderSettingsDto) =>
    request<void>('/api/settings/scan-folders', { method: 'PUT', body: JSON.stringify(body) }),
};
