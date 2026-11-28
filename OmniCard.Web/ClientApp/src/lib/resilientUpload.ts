// Helpers for long runs of uploads from a phone (Scan ▸ Save as batch): one stalled or dropped request
// among dozens must not sink the whole run. A phone that dims its screen suspends the page's requests,
// a Wi-Fi ↔ cellular hand-over drops them, and a tunnel/proxy can answer a 502/524 — each is retried.

import { ApiError } from '../api/client';

/** How long one upload may take before it's abandoned and retried. */
const ATTEMPT_TIMEOUT_MS = 90_000;
/** Pauses before the 2nd, 3rd, … attempt. */
const BACKOFF_MS = [1_000, 3_000, 8_000, 15_000];

/** Thrown when an attempt ran out of time (as opposed to the caller aborting the run). */
export class UploadTimeoutError extends Error {
  constructor() {
    super('The upload timed out.');
    this.name = 'UploadTimeoutError';
  }
}

/** Network failures, timeouts, and server answers that a later attempt may get past. A plain 4xx
 *  (bad request, forbidden, conflict…) won't change on a retry. */
export function isRetryable(e: unknown): boolean {
  if (e instanceof UploadTimeoutError) return true;
  if (e instanceof ApiError) return e.status === 0 || e.status === 408 || e.status === 429 || e.status >= 500;
  // fetch rejects with a TypeError ("Failed to fetch" / "Load failed") when the connection drops.
  return e instanceof TypeError;
}

function delay(ms: number, signal?: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    if (signal?.aborted) return reject(signal.reason);
    const timer = setTimeout(done, ms);
    function done() {
      signal?.removeEventListener('abort', onAbort);
      resolve();
    }
    function onAbort() {
      clearTimeout(timer);
      reject(signal!.reason);
    }
    signal?.addEventListener('abort', onAbort, { once: true });
  });
}

/** Resolves once the page is visible again: a hidden page's requests are suspended or fail at once. */
function whenVisible(signal?: AbortSignal): Promise<void> {
  if (typeof document === 'undefined' || !document.hidden) return Promise.resolve();
  return new Promise((resolve, reject) => {
    const onChange = () => {
      if (document.hidden) return;
      cleanup();
      resolve();
    };
    const onAbort = () => {
      cleanup();
      reject(signal!.reason);
    };
    const cleanup = () => {
      document.removeEventListener('visibilitychange', onChange);
      signal?.removeEventListener('abort', onAbort);
    };
    document.addEventListener('visibilitychange', onChange);
    signal?.addEventListener('abort', onAbort, { once: true });
  });
}

/**
 * Run `attempt` until it succeeds: each try gets its own time limit (its signal aborts when it runs
 * out), and a retryable failure is tried again after a pause — once the page is visible again.
 * `signal` stops the whole thing (no further attempts, the current one aborted); `onRetry` hears of
 * each failed attempt that will be tried again.
 */
export async function withRetry<T>(
  attempt: (signal: AbortSignal) => Promise<T>,
  options: { signal?: AbortSignal; onRetry?: (error: unknown) => void } = {},
): Promise<T> {
  const { signal, onRetry } = options;
  for (let tryNo = 0; ; tryNo++) {
    await whenVisible(signal);
    const ctrl = new AbortController();
    let timedOut = false;
    const timer = setTimeout(() => {
      timedOut = true;
      ctrl.abort();
    }, ATTEMPT_TIMEOUT_MS);
    const onAbort = () => ctrl.abort(signal!.reason);
    signal?.addEventListener('abort', onAbort, { once: true });
    try {
      return await attempt(ctrl.signal);
    } catch (e) {
      if (signal?.aborted) throw signal.reason ?? e;
      const err = timedOut ? new UploadTimeoutError() : e;
      if (tryNo >= BACKOFF_MS.length || !isRetryable(err)) throw err;
      onRetry?.(err);
      await delay(BACKOFF_MS[tryNo], signal);
    } finally {
      clearTimeout(timer);
      signal?.removeEventListener('abort', onAbort);
    }
  }
}

/** A copy of `file` held in memory. Scans restored from IndexedDB are disk-backed blobs that mobile
 *  Safari sometimes can't send ("Load failed"); reading it first also turns an unreadable file into a
 *  clear error instead of a stalled request. Scan crops are small, so the copy is cheap. */
export async function inMemoryCopy(file: File): Promise<File> {
  const bytes = await file.arrayBuffer();
  return new File([bytes], file.name || 'scan.jpg', { type: file.type || 'image/jpeg' });
}

/**
 * Keep the screen on while a long run is going (a phone that locks suspends the page). Re-acquired when
 * the page comes back, since the browser drops the lock whenever the page is hidden. Best-effort: where
 * the Wake Lock API is missing or refused it does nothing. Returns the release function.
 */
export function holdScreenWakeLock(): () => void {
  type Sentinel = { release: () => Promise<void> };
  const wakeLock = (navigator as Navigator & { wakeLock?: { request: (type: 'screen') => Promise<Sentinel> } })
    .wakeLock;
  if (!wakeLock || typeof document === 'undefined') return () => undefined;
  let sentinel: Sentinel | null = null;
  let released = false;
  const acquire = () => {
    if (released || document.hidden) return;
    wakeLock
      .request('screen')
      .then((s) => {
        if (released) void s.release().catch(() => undefined);
        else sentinel = s;
      })
      .catch(() => undefined);
  };
  const onVisibility = () => {
    if (!document.hidden) acquire();
  };
  document.addEventListener('visibilitychange', onVisibility);
  acquire();
  return () => {
    released = true;
    document.removeEventListener('visibilitychange', onVisibility);
    void sentinel?.release().catch(() => undefined);
    sentinel = null;
  };
}
