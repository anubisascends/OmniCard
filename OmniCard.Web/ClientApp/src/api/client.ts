import type {
  AuthStatusDto,
  BinderStateDto,
  CardDto,
  DashboardDto,
  GameDto,
  LocationSummaryDto,
  PagedResult,
  SetChecklistDto,
  SetInfoDto,
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
};
