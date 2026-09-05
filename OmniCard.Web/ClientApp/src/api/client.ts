import type {
  ActiveListingDto,
  AuthStatusDto,
  BinderStateDto,
  CardDto,
  CsvImportResultDto,
  CardListDto,
  CardListItemDto,
  CatalogStatusDto,
  CommitListResultDto,
  CustomerDto,
  DashboardDto,
  EbaySetupResultDto,
  EbayStatusDto,
  InventoryLotDto,
  DecklistCheckDto,
  GameDto,
  InventoryValuationDto,
  LocationSummaryDto,
  OrderDto,
  PagedResult,
  ProductDto,
  ScanCommitItem,
  ScanCommitResultDto,
  ScanMatchDto,
  ScanSearchResultDto,
  SetChecklistDto,
  SetInfoDto,
  TradeSummaryDto,
  WorkflowLaneDto,
} from './types';

/** Write-request bodies (mirror the Contracts upsert records). */
export interface CustomerFields {
  name: string;
  email?: string | null;
  phone?: string | null;
  city?: string | null;
  state?: string | null;
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

/** Thrown for non-2xx responses; carries the HTTP status so callers can special-case 401. */
export class ApiError extends Error {
  constructor(
    public status: number,
    message: string,
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
    try {
      const body = await res.json();
      if (body?.error) message = body.error;
    } catch {
      /* non-JSON error body */
    }
    throw new ApiError(res.status, message);
  }
  if (res.status === 204) return undefined as T;
  return (await res.json()) as T;
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
  login: (passphrase: string) =>
    request<AuthStatusDto>('/api/auth/login', {
      method: 'POST',
      body: JSON.stringify({ passphrase }),
    }),
  logout: () => request<AuthStatusDto>('/api/auth/logout', { method: 'POST' }),

  // Meta
  games: () => request<GameDto[]>('/api/meta/games'),

  // Dashboard
  dashboard: () => request<DashboardDto>('/api/dashboard'),

  // Locations
  locations: (game?: string) => request<LocationSummaryDto[]>(`/api/locations${qs({ game })}`),
  location: (id: number) => request<LocationSummaryDto>(`/api/locations/${id}`),

  // Sets
  sets: (game: string) => request<SetInfoDto[]>(`/api/sets${qs({ game })}`),
  setChecklist: (game: string, setCode: string) =>
    request<SetChecklistDto>(`/api/sets/${encodeURIComponent(game)}/${encodeURIComponent(setCode)}`),

  // Binder
  binder: (id: number, spread: number) =>
    request<BinderStateDto>(`/api/binder/${id}${qs({ spread })}`),

  // Collection
  collection: (opts: {
    game?: string;
    q?: string;
    containerId?: number;
    skip?: number;
    take?: number;
  }) => request<PagedResult<CardDto>>(`/api/collection${qs(opts)}`),

  // Location writes
  locationNameAvailable: (name: string, excludeId?: number) =>
    request<{ available: boolean }>(`/api/locations/name-available${qs({ name, excludeId })}`),
  locationCreate: (body: { name: string; type: string; slotsPerPage?: number }) =>
    request<LocationSummaryDto>('/api/locations', { method: 'POST', body: JSON.stringify(body) }),
  locationRename: (id: number, name: string) =>
    request<void>(`/api/locations/${id}`, { method: 'PUT', body: JSON.stringify({ name }) }),
  locationDelete: (id: number, moveToBulk: boolean) =>
    request<void>(`/api/locations/${id}${qs({ moveToBulk })}`, { method: 'DELETE' }),
  locationSetAlwaysAvailable: (id: number, value: boolean) =>
    request<void>(`/api/locations/${id}/always-available`, {
      method: 'PUT',
      body: JSON.stringify({ value }),
    }),

  // Card writes
  card: (id: number) => request<CardDto>(`/api/collection/${id}`),
  cardUpdate: (
    id: number,
    body: {
      condition: string;
      isFoil: boolean;
      foilType?: string | null;
      quantity: number;
      purchasePrice?: number | null;
    },
  ) => request<void>(`/api/collection/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  cardDelete: (id: number) => request<void>(`/api/collection/${id}`, { method: 'DELETE' }),
  cardMove: (cardIds: number[], containerId: number, section?: string) =>
    request<void>('/api/collection/move', {
      method: 'POST',
      body: JSON.stringify({ cardIds, containerId, section }),
    }),
  cardSetTags: (id: number, tags: string[]) =>
    request<void>(`/api/collection/${id}/tags`, { method: 'PUT', body: JSON.stringify({ tags }) }),

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
  listings: (game?: string) => request<ActiveListingDto[]>(`/api/listings${qs({ game })}`),
  listingUnlist: (lotId: number) =>
    request<void>(`/api/listings/lot/${lotId}`, { method: 'DELETE' }),
  pickListPdfUrl: (game?: string) => `/api/listings/picklist.pdf${qs({ game })}`,
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
  decklistCheck: (body: { url?: string; text?: string; game: string }) =>
    request<DecklistCheckDto>('/api/decklist/check', { method: 'POST', body: JSON.stringify(body) }),

  // Trades (read-only history)
  trades: () => request<TradeSummaryDto[]>('/api/trades'),

  // Card lists
  lists: (game: string) => request<CardListDto[]>(`/api/lists${qs({ game })}`),
  listCreate: (name: string, game: string) =>
    request<CardListDto>('/api/lists', { method: 'POST', body: JSON.stringify({ name, game }) }),
  listRename: (id: number, name: string) =>
    request<void>(`/api/lists/${id}`, { method: 'PUT', body: JSON.stringify({ name }) }),
  listDelete: (id: number) => request<void>(`/api/lists/${id}`, { method: 'DELETE' }),
  listItems: (id: number) => request<CardListItemDto[]>(`/api/lists/${id}/items`),
  listRemoveItem: (itemId: number) =>
    request<void>(`/api/lists/items/${itemId}`, { method: 'DELETE' }),
  listSetItemQuantity: (itemId: number, quantity: number) =>
    request<void>(`/api/lists/items/${itemId}`, { method: 'PUT', body: JSON.stringify({ quantity }) }),
  listRefreshPrices: (id: number) =>
    request<void>(`/api/lists/${id}/refresh-prices`, { method: 'POST' }),
  listCommit: (id: number, containerId: number, condition: string) =>
    request<CommitListResultDto>(`/api/lists/${id}/commit`, {
      method: 'POST',
      body: JSON.stringify({ containerId, condition }),
    }),

  // Catalog refresh
  catalogStatus: () => request<CatalogStatusDto>('/api/catalog/status'),
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

  // Scan (server-side image matching)
  scanMatch: async (image: File, game: string, isFoil: boolean) => {
    const form = new FormData();
    form.append('image', image);
    form.append('game', game);
    form.append('isFoil', String(isFoil));
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
  scanSearch: (game: string, q: string) =>
    request<ScanSearchResultDto[]>(`/api/scan/search${qs({ game, q })}`),
  scanCommit: (containerId: number, items: ScanCommitItem[]) =>
    request<ScanCommitResultDto>('/api/scan/commit', {
      method: 'POST',
      body: JSON.stringify({ containerId, items }),
    }),
};
