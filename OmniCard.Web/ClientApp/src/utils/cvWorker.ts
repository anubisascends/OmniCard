// Main-thread client for the opencv Web Worker (workers/opencv.worker.ts). All heavy CV runs in the
// worker; here we just create it lazily, await its "ready" signal, and round-trip frames by id. The
// worker chunk (which bundles the ~10 MB opencv runtime) only loads when the webcam dialog first calls
// ensureCvReady(), so the rest of the app never pays for it.

import type { DetectedCard, Point, ScanLayout } from './cardDetect';

// eslint-disable-next-line @typescript-eslint/no-explicit-any
type Pending = { resolve: (m: any) => void; reject: (e: Error) => void };

let worker: Worker | null = null;
let readyPromise: Promise<void> | null = null;
let seq = 0;
const pending = new Map<number, Pending>();
// A detection normally takes tens of ms; one that hasn't come back in this long never will.
const DETECT_TIMEOUT_MS = 5000;

function ensureWorker(): Worker {
  if (worker) return worker;
  // Classic worker (no { type: 'module' }) so opencv.js detects a Web Worker environment.
  worker = new Worker(new URL('../workers/opencv.worker.ts', import.meta.url));
  readyPromise = new Promise((resolve, reject) => {
    worker!.onmessage = (e: MessageEvent) => {
      const m = e.data;
      if (m.type === 'ready') {
        resolve();
        return;
      }
      if (m.type === 'init-error') {
        reject(new Error(m.message));
        return;
      }
      const p = pending.get(m.id);
      if (!p) return;
      pending.delete(m.id);
      if (m.type === 'error') p.reject(new Error(m.message));
      else p.resolve(m);
    };
    worker!.onerror = (e) => {
      const err = new Error(e.message || 'opencv worker failed');
      reject(err);
      // A worker that died after loading (e.g. out of memory on a 4K crop) answers nothing more: fail
      // what it still owed and drop it, so the next call starts a fresh one instead of waiting forever.
      for (const p of pending.values()) p.reject(err);
      pending.clear();
      worker?.terminate();
      worker = null;
    };
  });
  return worker;
}

/** Give up on a request the worker never answers, so a caller's loop can carry on. */
function expireAfter(id: number, ms: number) {
  setTimeout(() => {
    const p = pending.get(id);
    if (!p) return;
    pending.delete(id);
    p.reject(new Error('opencv worker timed out'));
  }, ms);
}

/** Create the worker (if needed) and resolve once opencv is initialized, or reject on load failure. */
export function ensureCvReady(): Promise<void> {
  ensureWorker();
  return readyPromise!;
}

/** Detect cards in a frame — the largest one, or with `multi` every card-sized quad in reading order.
 *  Transfers the pixel buffer (caller must not reuse it). */
export async function detectCards(imageData: ImageData, multi: boolean): Promise<DetectedCard[]> {
  const w = ensureWorker();
  await readyPromise;
  const id = ++seq;
  const buffer = imageData.data.buffer;
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve: (m) => resolve(m.cards ?? []), reject });
    w.postMessage({ type: 'detect', id, width: imageData.width, height: imageData.height, buffer, multi }, [buffer]);
    expireAfter(id, DETECT_TIMEOUT_MS);
  });
}

/** Deskew + crop the given quad from a (full-resolution) frame into the ADF layout, as a JPEG blob. */
export async function cropCard(imageData: ImageData, quad: Point[], layout: ScanLayout): Promise<Blob> {
  const w = ensureWorker();
  await readyPromise;
  const id = ++seq;
  const buffer = imageData.data.buffer;
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve: (m) => resolve(new Blob([m.buffer], { type: 'image/jpeg' })), reject });
    w.postMessage(
      { type: 'crop', id, width: imageData.width, height: imageData.height, buffer, quad, layout },
      [buffer],
    );
  });
}
