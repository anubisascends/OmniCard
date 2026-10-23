import type {
  KioskCardDto,
  KioskGameDto,
  KioskSessionDto,
  KioskStatusDto,
  KioskSubmitResultDto,
  PagedResult,
} from '../api/types';

/** A failed kiosk call. `code` is the server's machine-readable reason (e.g. `name-taken`,
 *  `unavailable`, `no-session`, `not-enrolled`). */
export class KioskApiError extends Error {
  constructor(
    public status: number,
    message: string,
    public code?: string,
  ) {
    super(message);
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(path, {
    ...init,
    headers: { 'Content-Type': 'application/json', ...(init?.headers ?? {}) },
    credentials: 'same-origin',
  });
  if (!res.ok) {
    let message = res.statusText;
    let code: string | undefined;
    try {
      const body = (await res.json()) as { error?: string; code?: string };
      if (body?.error) message = body.error;
      code = body?.code;
    } catch {
      /* non-JSON error body */
    }
    throw new KioskApiError(res.status, message, code);
  }
  if (res.status === 204) return undefined as T;
  return (await res.json()) as T;
}

const post = <T>(path: string, body?: unknown) =>
  request<T>(path, { method: 'POST', body: body === undefined ? undefined : JSON.stringify(body) });

/** The customer kiosk's API (`/api/kiosk`), authenticated by the kiosk device cookie — never the staff sign-in. */
export const kioskApi = {
  status: () => request<KioskStatusDto>('/api/kiosk/status'),
  enroll: (login: string, password: string, deviceName: string) =>
    post<KioskStatusDto>('/api/kiosk/enroll', { login, password, deviceName }),
  unenroll: (login: string, password: string) => post<void>('/api/kiosk/unenroll', { login, password }),
  startSession: (name: string) => post<KioskSessionDto>('/api/kiosk/session', { name }),
  endSession: () => request<void>('/api/kiosk/session', { method: 'DELETE' }),
  /** Null when the session already ended. */
  ping: () => post<KioskSessionDto | undefined>('/api/kiosk/session/ping'),
  addHold: (key: string, quantity = 1) => post<KioskSessionDto>('/api/kiosk/session/holds', { key, quantity }),
  removeHold: (key: string, quantity = 1) => post<KioskSessionDto>('/api/kiosk/session/holds/remove', { key, quantity }),
  submit: () => post<KioskSubmitResultDto>('/api/kiosk/session/submit'),
  games: () => request<KioskGameDto[]>('/api/kiosk/games'),
  cards: (opts: { game: string; q?: string; sort?: string; skip?: number; take?: number }) => {
    const sp = new URLSearchParams();
    for (const [k, v] of Object.entries(opts)) if (v !== undefined && v !== '') sp.set(k, String(v));
    return request<PagedResult<KioskCardDto>>(`/api/kiosk/cards?${sp}`);
  },
};
