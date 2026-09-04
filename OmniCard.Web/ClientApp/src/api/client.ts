import type {
  ActiveListingDto,
  AuthStatusDto,
  BinderStateDto,
  CardDto,
  CsvImportResultDto,
  CustomerDto,
  DashboardDto,
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
  WorkflowLaneDto,
} from './types';

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
  customers: () => request<CustomerDto[]>('/api/customers'),

  // Inventory (sealed)
  inventoryProducts: (game?: string, category?: string) =>
    request<ProductDto[]>(`/api/inventory/products${qs({ game, category })}`),
  inventoryValuation: () => request<InventoryValuationDto>('/api/inventory/valuation'),

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
