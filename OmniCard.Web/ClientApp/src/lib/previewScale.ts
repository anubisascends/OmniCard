import { useSyncExternalStore } from 'react';

// Client-side UI preference: how large the hover card-artwork preview renders, as a percentage of its
// base size. 100% is the original 240×340 popup; the Settings slider scales it up to 300%.
const KEY = 'omnicard.previewScale';
export const PREVIEW_SCALE_MIN = 100;
export const PREVIEW_SCALE_MAX = 300;
const DEFAULT = 100;

// Base dimensions the scale multiplies (the original fixed popup size).
export const PREVIEW_BASE_WIDTH = 240;
export const PREVIEW_BASE_MAX_HEIGHT = 340;

const EVENT = 'omnicard.previewScale.changed';

const clamp = (n: number) =>
  Math.min(PREVIEW_SCALE_MAX, Math.max(PREVIEW_SCALE_MIN, Math.round(n)));

export function getPreviewScale(): number {
  const raw = Number(localStorage.getItem(KEY));
  return Number.isFinite(raw) && raw > 0 ? clamp(raw) : DEFAULT;
}

export function setPreviewScale(percent: number): void {
  localStorage.setItem(KEY, String(clamp(percent)));
  // Notify listeners in this tab (the storage event only fires in *other* tabs).
  window.dispatchEvent(new Event(EVENT));
}

/** Reactive read of the preview scale — re-renders when it changes in this tab or another. */
export function usePreviewScale(): number {
  return useSyncExternalStore(
    (cb) => {
      window.addEventListener(EVENT, cb);
      window.addEventListener('storage', cb);
      return () => {
        window.removeEventListener(EVENT, cb);
        window.removeEventListener('storage', cb);
      };
    },
    getPreviewScale,
  );
}
