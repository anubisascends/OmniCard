// Main-thread glue between a camera frame or phone photo and the opencv worker (utils/cvWorker.ts):
// reads pixels off a canvas, runs detection on a downscaled copy, and crops each card at full
// resolution into the ADF scanner layout (cardDetect.scanLayoutFor) so the server sees what it would
// get from a dedicated scanner.

import { scanLayoutFor, type Point } from './cardDetect';
import { cropCard, detectCards, ensureCvReady } from './cvWorker';

const PHOTO_DETECT_WIDTH = 1024; // photos get more detection resolution than the live loop: a spread of cards is small.
const MAX_CANVAS_PIXELS = 16_000_000; // iOS Safari refuses bigger canvases, so a 24/48 MP photo is drawn down to fit.
const REGION_MARGIN = 8; // px of context kept around a card's bounding box for the warp's interpolation.

/** One card's pixels: its bounding box (plus a margin) out of the full-resolution frame, and its quad
 *  in that box's coordinates. Reading only the box keeps a 4K frame or 12 MP photo cheap. */
export interface CardRegion {
  image: ImageData;
  quad: Point[];
}

/** Copy each quad's region out of `canvas` now (synchronously), so the crops can run afterwards
 *  while the canvas is redrawn with later frames. */
export function readRegions(canvas: HTMLCanvasElement, quads: Point[][]): CardRegion[] {
  const ctx = canvas.getContext('2d', { willReadFrequently: true });
  if (!ctx) return [];
  return quads.map((quad) => {
    const xs = quad.map((p) => p.x);
    const ys = quad.map((p) => p.y);
    const x0 = Math.max(0, Math.floor(Math.min(...xs)) - REGION_MARGIN);
    const y0 = Math.max(0, Math.floor(Math.min(...ys)) - REGION_MARGIN);
    const x1 = Math.min(canvas.width, Math.ceil(Math.max(...xs)) + REGION_MARGIN);
    const y1 = Math.min(canvas.height, Math.ceil(Math.max(...ys)) + REGION_MARGIN);
    return {
      image: ctx.getImageData(x0, y0, Math.max(1, x1 - x0), Math.max(1, y1 - y0)),
      quad: quad.map((p) => ({ x: p.x - x0, y: p.y - y0 })),
    };
  });
}

/** Deskew one region into the game's ADF layout (JPEG). */
export function cropRegion(region: CardRegion, game: string): Promise<Blob> {
  return cropCard(region.image, region.quad, scanLayoutFor(game));
}

/** Width in px of a quad's shorter side — how much real detail a capture of it will carry. */
export function cardWidthPx(quad: Point[]): number {
  const d = (a: Point, b: Point) => Math.hypot(a.x - b.x, a.y - b.y);
  const sides = [d(quad[0], quad[1]), d(quad[1], quad[2]), d(quad[2], quad[3]), d(quad[3], quad[0])];
  return Math.min((sides[0] + sides[2]) / 2, (sides[1] + sides[3]) / 2);
}

function drawTo(source: CanvasImageSource, width: number, height: number): HTMLCanvasElement {
  const canvas = document.createElement('canvas');
  canvas.width = width;
  canvas.height = height;
  canvas.getContext('2d', { willReadFrequently: true })?.drawImage(source, 0, 0, width, height);
  return canvas;
}

/**
 * Turn a phone photo into one ADF-style scan per card in it — a single card held up to the camera, or
 * several laid out with gaps between them (reading order). Falls back to the untouched photo when no
 * card is found or the image can't be decoded (e.g. a TIFF), so nothing the user picked is lost.
 */
export async function splitPhotoIntoCards(file: File, game: string): Promise<File[]> {
  try {
    await ensureCvReady();
    const bitmap = await createImageBitmap(file); // applies the photo's EXIF orientation
    try {
      const fit = Math.min(1, Math.sqrt(MAX_CANVAS_PIXELS / (bitmap.width * bitmap.height)));
      const full = drawTo(bitmap, Math.round(bitmap.width * fit), Math.round(bitmap.height * fit));
      const dw = Math.min(PHOTO_DETECT_WIDTH, full.width);
      const dh = Math.round((full.height * dw) / full.width);
      const small = drawTo(full, dw, dh);
      const pixels = small.getContext('2d', { willReadFrequently: true })?.getImageData(0, 0, dw, dh);
      if (!pixels) return [file];

      const cards = await detectCards(pixels, true);
      if (cards.length === 0) return [file];
      const scale = full.width / dw;
      const regions = readRegions(full, cards.map((c) => c.pts.map((p) => ({ x: p.x * scale, y: p.y * scale }))));
      const base = file.name.replace(/\.[^.]+$/, '') || 'photo';
      const files: File[] = [];
      for (const [i, region] of regions.entries()) {
        const blob = await cropRegion(region, game);
        files.push(new File([blob], regions.length === 1 ? `${base}.jpg` : `${base}-${i + 1}.jpg`, { type: 'image/jpeg' }));
      }
      return files;
    } finally {
      bitmap.close();
    }
  } catch {
    return [file];
  }
}
