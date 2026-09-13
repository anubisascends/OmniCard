// Main-thread client for the opencv Web Worker (workers/opencv.worker.ts). All heavy CV runs in the
// worker; here we just create it lazily, await its "ready" signal, and round-trip frames by id. The
// worker chunk (which bundles the ~10 MB opencv runtime) only loads when the webcam dialog first calls
// ensureCvReady(), so the rest of the app never pays for it.

import type { Point } from './cardDetect';

// eslint-disable-next-line @typescript-eslint/no-explicit-any
type Pending = { resolve: (m: any) => void; reject: (e: Error) => void };

let worker: Worker | null = null;
let readyPromise: Promise<void> | null = null;
let seq = 0;
const pending = new Map<number, Pending>();

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
    worker!.onerror = (e) => reject(new Error(e.message || 'opencv worker failed to load'));
  });
  return worker;
}

/** Create the worker (if needed) and resolve once opencv is initialized, or reject on load failure. */
export function ensureCvReady(): Promise<void> {
  ensureWorker();
  return readyPromise!;
}

/** Detect the most card-like quad in a frame. Transfers the pixel buffer (caller must not reuse it). */
export async function detectCardQuad(imageData: ImageData): Promise<Point[] | null> {
  const w = ensureWorker();
  await readyPromise;
  const id = ++seq;
  const buffer = imageData.data.buffer;
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve: (m) => resolve(m.quad ?? null), reject });
    w.postMessage({ type: 'detect', id, width: imageData.width, height: imageData.height, buffer }, [buffer]);
  });
}

/** Deskew + crop the given quad from a (full-resolution) frame to a portrait JPEG blob. */
export async function cropCard(imageData: ImageData, quad: Point[]): Promise<Blob> {
  const w = ensureWorker();
  await readyPromise;
  const id = ++seq;
  const buffer = imageData.data.buffer;
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve: (m) => resolve(new Blob([m.buffer], { type: 'image/jpeg' })), reject });
    w.postMessage(
      { type: 'crop', id, width: imageData.width, height: imageData.height, buffer, quad },
      [buffer],
    );
  });
}
