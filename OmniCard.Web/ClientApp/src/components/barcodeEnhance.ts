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

/**
 * A second, 1D-only decode pass for soft webcam frames. zxing thresholds each pixel row as-is, so a
 * slightly out-of-focus label (typical of a desk webcam) loses its narrow bars and spaces. This crops
 * the aiming band, averages it into clean scanlines, upscales 2× horizontally for sub-pixel edges and
 * sharpens them before handing the result to zxing's 1D readers.
 */
export class EnhancedBarcodeDecoder {
  private readonly reader = new MultiFormatReader();
  private readonly canvas = document.createElement('canvas');
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
    const bands = Math.floor(sh / BAND);
    if (sw < 32 || bands < 1) return null;

    this.canvas.width = sw;
    this.canvas.height = sh;
    const ctx = this.canvas.getContext('2d', { willReadFrequently: true });
    if (!ctx) return null;
    ctx.drawImage(video, sx, sy, sw, sh, 0, 0, sw, sh);
    const rgba = ctx.getImageData(0, 0, sw, sh).data;

    const [radius, amount] = PASSES[this.pass];
    this.pass = (this.pass + 1) % PASSES.length;

    const w2 = sw * 2;
    const profile = new Float32Array(sw);
    const up = new Float32Array(w2);
    const out = new Uint8ClampedArray(w2 * bands);
    const span = 2 * radius + 1;
    for (let b = 0; b < bands; b++) {
      profile.fill(0);
      for (let y = b * BAND; y < (b + 1) * BAND; y++) {
        for (let x = 0, i = y * sw * 4; x < sw; x++, i += 4) {
          profile[x] += (rgba[i] + 2 * rgba[i + 1] + rgba[i + 2]) / 4;
        }
      }
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

    try {
      const bitmap = new BinaryBitmap(new HybridBinarizer(new RGBLuminanceSource(out, w2, bands)));
      return this.reader.decodeWithState(bitmap).getText();
    } catch {
      return null;
    }
  }
}
