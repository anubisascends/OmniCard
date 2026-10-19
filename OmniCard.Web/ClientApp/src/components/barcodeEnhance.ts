import {
  BarcodeFormat,
  BinaryBitmap,
  DecodeHintType,
  HybridBinarizer,
  MultiFormatReader,
  RGBLuminanceSource,
} from '@zxing/library';

/** Where the aiming guide sits over the displayed video, as fractions of its box. */
export const GUIDE = { left: 0.08, right: 0.08, top: 0.3, bottom: 0.3 };

const ONE_D = [
  BarcodeFormat.CODE_128,
  BarcodeFormat.CODE_39,
  BarcodeFormat.CODE_93,
  BarcodeFormat.ITF,
  BarcodeFormat.CODABAR,
];

/** Source rows averaged into one scanline: bars are vertical, so this cancels sensor noise without
 *  smearing them (and tolerates a few degrees of tilt). */
const BAND = 8;

/** Unsharp-mask (radius, amount) in upscaled pixels — one tried per frame, in turn. Chosen from an
 *  offline sweep of blurred/noisy Code 39 renders, where they roughly double what zxing reads raw. */
const PASSES: ReadonlyArray<readonly [number, number]> = [
  [3, 5],
  [8, 1.5],
  [5, 5],
];

/** A run of bars ends where the gap to the next edge exceeds this many times the typical gap beside
 *  it: a barcode's widest element is ~4 modules (~2× its median gap), its quiet zone 10+ modules. */
const GAP_BREAK = 4;
/** Fewest edges a run needs to be worth decoding (a 3-character Code 128 has ~30). */
const MIN_EDGES = 20;
/** Cap on runs of bars cut out per frame. */
const MAX_RUNS = 64;
/** Isolated scanlines that must read the same text before it's trusted: tried one by one, hundreds of
 *  rows a second let the odd garbage read slip through Code 128's checksum. Two agreeing rows never
 *  misread offline, and a misread repeated across frames does — so agreement is per frame. */
const AGREE = 2;

/**
 * A second, 1D-only decode pass for camera frames. zxing thresholds each pixel row as-is, so a
 * slightly out-of-focus label (typical of a desk webcam) loses its narrow bars and spaces. This crops
 * the aiming band, averages it into clean scanlines, upscales 2× horizontally for sub-pixel edges and
 * sharpens them before handing the result to zxing's 1D readers.
 *
 * zxing also takes the first start-pattern look-alike in each row and thresholds the row from a histogram
 * of all of it, so clutter beside the label (a bubble mailer, fingers, the pavement) can fail every row
 * even when the barcode is sharp. When the whole band doesn't decode, each scanline's dense runs of bars
 * are cut out on their own between clean white quiet zones — once as sharpened grey, once redrawn black
 * and white from their edges — and decoded row by row until `AGREE` rows read the same text.
 */
export class EnhancedBarcodeDecoder {
  private readonly reader = new MultiFormatReader();
  private canvas: HTMLCanvasElement | null = null;
  private pass = 0;

  constructor() {
    this.reader.setHints(
      new Map<DecodeHintType, unknown>([
        [DecodeHintType.POSSIBLE_FORMATS, ONE_D],
        [DecodeHintType.TRY_HARDER, true],
      ]),
    );
  }

  /** Decode the current frame of `video` (shown with object-fit: cover), or null. */
  decode(video: HTMLVideoElement): string | null {
    const vw = video.videoWidth;
    const vh = video.videoHeight;
    const bw = video.clientWidth;
    const bh = video.clientHeight;
    if (!vw || !vh || !bw || !bh || video.readyState < 2) return null;

    // Map the guide band (full visible width, so the quiet zones survive) back into frame pixels.
    const scale = Math.max(bw / vw, bh / vh);
    const sw = Math.round(Math.min(vw, bw / scale));
    const sx = Math.round((vw - sw) / 2);
    const sy = Math.round((vh - bh / scale) / 2 + (GUIDE.top * bh) / scale);
    const sh = Math.round(((1 - GUIDE.top - GUIDE.bottom) * bh) / scale);
    if (sw < 32 || sh < BAND) return null;

    this.canvas ??= document.createElement('canvas');
    this.canvas.width = sw;
    this.canvas.height = sh;
    const ctx = this.canvas.getContext('2d', { willReadFrequently: true });
    if (!ctx) return null;
    ctx.drawImage(video, sx, sy, sw, sh, 0, 0, sw, sh);
    return this.decodePixels(ctx.getImageData(0, 0, sw, sh).data, sw, sh);
  }

  /** Decode an RGBA region (the guide band's pixels), or null. */
  decodePixels(rgba: Uint8ClampedArray, sw: number, sh: number): string | null {
    const bands = Math.floor(sh / BAND);
    if (sw < 32 || bands < 1) return null;

    const [radius, amount] = PASSES[this.pass];
    this.pass = (this.pass + 1) % PASSES.length;

    const w2 = sw * 2;
    const profile = new Float32Array(sw);
    const smooth = new Float32Array(w2 * bands); // upscaled scanlines, before sharpening
    const out = new Uint8ClampedArray(w2 * bands);
    const span = 2 * radius + 1;
    for (let b = 0; b < bands; b++) {
      profile.fill(0);
      for (let y = b * BAND; y < (b + 1) * BAND; y++) {
        for (let x = 0, i = y * sw * 4; x < sw; x++, i += 4) {
          profile[x] += (rgba[i] + 2 * rgba[i + 1] + rgba[i + 2]) / 4;
        }
      }
      const up = smooth.subarray(b * w2, (b + 1) * w2);
      for (let x = 0; x < w2; x++) {
        const fx = Math.max(0, (x + 0.5) / 2 - 0.5);
        const x0 = Math.floor(fx);
        const x1 = Math.min(sw - 1, x0 + 1);
        const t = fx - x0;
        up[x] = (profile[x0] * (1 - t) + profile[x1] * t) / BAND;
      }
      // Unsharp mask against a sliding box mean.
      let sum = 0;
      for (let k = -radius; k <= radius; k++) sum += up[Math.min(w2 - 1, Math.max(0, k))];
      for (let x = 0, o = b * w2; x < w2; x++) {
        const v = up[x];
        out[o + x] = v + amount * (v - sum / span);
        sum += up[Math.min(w2 - 1, x + radius + 1)] - up[Math.max(0, x - radius)];
      }
    }

    const text = this.read(out, w2, bands);
    if (text) return text;
    const reads = new Map<string, number>();
    for (const row of isolateBarcodes(smooth, out, w2, bands)) {
      const text = this.read(row, row.length, 1);
      if (!text) continue;
      const count = (reads.get(text) ?? 0) + 1;
      if (count >= AGREE) return text;
      reads.set(text, count);
    }
    return null;
  }

  private read(luma: Uint8ClampedArray, width: number, height: number): string | null {
    try {
      const bitmap = new BinaryBitmap(new HybridBinarizer(new RGBLuminanceSource(luma, width, height)));
      return this.reader.decodeWithState(bitmap).getText();
    } catch {
      return null;
    }
  }
}

/**
 * Cuts every dense run of bars out of each scanline as two single-row images between synthetic white
 * quiet zones — so zxing sees only barcode-like content: the sharpened pixels (`sharp`), and the run
 * redrawn from the edges of the unsharpened scanline (`smooth`), which keeps blurred narrow elements
 * that a single threshold loses.
 */
function* isolateBarcodes(smooth: Float32Array, sharp: Uint8ClampedArray, width: number, bands: number) {
  let runs = 0;
  // Middle bands first: that's where the user aims, so they survive the run cap.
  const mid = bands >> 1;
  for (let i = 0; i < bands; i++) {
    const band = i % 2 === 0 ? mid + (i >> 1) : mid - ((i + 1) >> 1);
    if (band < 0 || band >= bands) continue;
    const row = smooth.subarray(band * width, (band + 1) * width);
    for (const [start, end] of findBarRuns(row)) {
      if (runs++ >= MAX_RUNS) return;
      const quiet = Math.max(24, Math.round((end - start) * 0.1));
      let white = 0;
      for (let x = start; x < end; x++) white = Math.max(white, row[x]);
      const grey = new Uint8ClampedArray(end - start + 2 * quiet).fill(white);
      grey.set(sharp.subarray(band * width + start, band * width + end), quiet);
      yield grey;
      const redrawn = new Uint8ClampedArray(end - start + 2 * quiet).fill(255);
      redrawFromEdges(row.subarray(start, end), redrawn, quiet);
      yield redrawn;
    }
  }
}

/** Draws `seg` into `dst` (from `offset`) as pure black and white, switching at each gradient peak
 *  that is strong relative to its neighbourhood — so a blurred narrow bar or space still flips it. */
function redrawFromEdges(seg: Float32Array, dst: Uint8ClampedArray, offset: number) {
  const n = seg.length;
  const grad = new Float32Array(n);
  for (let x = 1; x < n - 1; x++) grad[x] = seg[x + 1] - seg[x - 1];
  const edges: number[] = [];
  const signs: number[] = [];
  for (let x = 1; x < n - 1; x++) {
    const g = Math.abs(grad[x]);
    if (g < Math.abs(grad[x - 1]) || g <= Math.abs(grad[x + 1])) continue;
    let local = 0;
    for (let k = Math.max(1, x - 24); k < Math.min(n - 1, x + 24); k++) local = Math.max(local, Math.abs(grad[k]));
    if (g < Math.max(3, local * 0.25)) continue;
    const sign = Math.sign(grad[x]);
    if (signs.length > 0 && signs[signs.length - 1] === sign) {
      if (g > Math.abs(grad[edges[edges.length - 1]])) edges[edges.length - 1] = x;
      continue;
    }
    edges.push(x);
    signs.push(sign);
  }
  let black = false;
  for (let x = 0, e = 0; x < n; x++) {
    while (e < edges.length && edges[e] <= x) black = signs[e++] < 0;
    dst[offset + x] = black ? 0 : 255;
  }
}

/** [start, end) spans of `row` holding a dense run of alternating edges — candidate barcodes. */
function findBarRuns(row: Float32Array): [number, number][] {
  const n = row.length;
  const grad = new Float32Array(n);
  let peak = 0;
  for (let x = 1; x < n - 1; x++) {
    grad[x] = row[x + 1] - row[x - 1];
    peak = Math.max(peak, Math.abs(grad[x]));
  }
  const threshold = Math.max(6, peak * 0.15);

  // Edges = gradient peaks; consecutive same-direction peaks collapse into the stronger one.
  const edges: number[] = [];
  let lastSign = 0;
  for (let x = 1; x < n - 1; x++) {
    const g = Math.abs(grad[x]);
    if (g < threshold || g < Math.abs(grad[x - 1]) || g <= Math.abs(grad[x + 1])) continue;
    const sign = Math.sign(grad[x]);
    if (sign === lastSign) {
      if (g > Math.abs(grad[edges[edges.length - 1]])) edges[edges.length - 1] = x;
    } else {
      edges.push(x);
      lastSign = sign;
    }
  }
  if (edges.length < MIN_EDGES) return [];

  const gaps = edges.slice(1).map((e, i) => e - edges[i]);
  const runs: [number, number][] = [];
  let first = 0; // index into edges of the current run's first edge
  const close = (last: number, nextGap: number) => {
    if (last - first + 1 >= MIN_EDGES) {
      const prevGap = first > 0 ? gaps[first - 1] : Infinity;
      const typical = median(gaps.slice(first, last));
      // Keep a little of the white either side, but never reach into whatever lies past the gap.
      const start = Math.max(0, edges[first] - Math.min(2 * typical, prevGap / 2));
      const end = Math.min(n, edges[last] + Math.min(2 * typical, nextGap / 2) + 1);
      runs.push([Math.floor(start), Math.ceil(end)]);
    }
  };
  for (let i = 0; i < gaps.length; i++) {
    const before = gaps.slice(Math.max(0, i - 8), i);
    const after = gaps.slice(i + 1, i + 9);
    const typical = Math.min(
      before.length >= 3 ? median(before) : Infinity,
      after.length >= 3 ? median(after) : Infinity,
    );
    if (gaps[i] > GAP_BREAK * typical) {
      close(i, gaps[i]);
      first = i + 1;
    }
  }
  close(edges.length - 1, Infinity);
  return runs;
}

function median(values: number[]): number {
  if (values.length === 0) return 0;
  const sorted = [...values].sort((a, b) => a - b);
  return sorted[sorted.length >> 1];
}
