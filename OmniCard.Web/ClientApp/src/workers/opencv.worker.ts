// opencv.js runs entirely in this worker so the ~10 MB wasm runtime never parses/executes on the main
// thread (doing so freezes the tab). The main thread (utils/cvWorker.ts) sends raw frame pixels and gets
// back the detected card(s), or a cropped JPEG. This is a CLASSIC worker (no `type: 'module'`) so
// opencv.js correctly detects a Web Worker environment (`typeof importScripts === 'function'`).

import cvImport from '@techstark/opencv-js';
import {
  centroid,
  insetQuad,
  orderCorners,
  pointInQuad,
  quadArea,
  readingOrder,
  refineQuad,
  type DetectedCard,
  type Point,
  type ScanLayout,
} from '../utils/cardDetect';

// opencv typings are loose (a big Emscripten module); treat it as `any` at the boundary.
// eslint-disable-next-line @typescript-eslint/no-explicit-any
type Cv = any;
const cv: Cv = cvImport;

// The tsconfig ships the DOM lib, so the global `postMessage` is typed as Window's (needs a
// targetOrigin). In a worker it's DedicatedWorkerGlobalScope.postMessage(message, transfer?) — wrap it
// with the correct signature so the transfer list type-checks.
const post = (message: unknown, transfer?: Transferable[]) =>
  (postMessage as (m: unknown, t?: Transferable[]) => void)(message, transfer);

// --- Detection/crop tuning. ---
const CARD_RATIO_MIN = 0.55; // trading card short/long ≈ 0.716; accept a generous band (perspective/skew).
const CARD_RATIO_MAX = 0.9;
const MIN_AREA_FRACTION = 0.08; // single-card mode: the card must fill ≥ this fraction of the frame.
const MULTI_MIN_AREA_FRACTION = 0.012; // multi-card mode: a 4×4 layout still clears this.
const MULTI_MIN_RELATIVE_AREA = 0.35; // multi-card mode: cards share one physical size, so drop blobs < 35% of the biggest.
const MAX_CARDS = 16;
const RECTANGULARITY_MIN = 0.7; // contour area / its min-area-rect area — filters non-rectangular blobs.
const CANNY_INSET = 3; // the 7×7 dilation grows an edge contour ~3 px outward; undo it so no background leaks in.
const SIG_W = 9; // difference hash: 9×8 samples → 8×8 = 64 bits.
const SIG_H = 8;
const JPEG_QUALITY = 0.95;

function dist(a: Point, b: Point): number {
  return Math.hypot(a.x - b.x, a.y - b.y);
}

interface Candidate {
  pts: Point[];
  area: number;
  inset: number;
}

/** Four corners of an opencv RotatedRect ({center, size, angle°}), independent of angle convention. */
function rectCorners(rect: { center: { x: number; y: number }; size: { width: number; height: number }; angle: number }): Point[] {
  const a = (rect.angle * Math.PI) / 180;
  const cos = Math.cos(a);
  const sin = Math.sin(a);
  const w = rect.size.width / 2;
  const h = rect.size.height / 2;
  const { x: cx, y: cy } = rect.center;
  return ([[-w, -h], [w, -h], [w, h], [-w, h]] as const).map(([dx, dy]) => ({
    x: cx + dx * cos - dy * sin,
    y: cy + dx * sin + dy * cos,
  }));
}

/** Find card-shaped quads in a binary/edge mat. For each large external contour, prefer a clean
 *  convex-hull quad (preserves perspective); fall back to its min-area rectangle when the outline is
 *  ragged or rounded (dark/borderless cards). Either is then snapped onto the straight sides
 *  (refineQuad) and filtered by aspect ratio; the caller picks among them. */
function collectCandidates(binary: Cv, minArea: number, inset: number): Candidate[] {
  const contours = new cv.MatVector();
  const hierarchy = new cv.Mat();
  const out: Candidate[] = [];
  try {
    // Every contour point (not just segment ends) so each side can be line-fit in refineQuad.
    cv.findContours(binary, contours, hierarchy, cv.RETR_EXTERNAL, cv.CHAIN_APPROX_NONE);
    for (let i = 0; i < contours.size(); i++) {
      const c = contours.get(i);
      const cArea = cv.contourArea(c);
      if (cArea < minArea) {
        c.delete();
        continue;
      }
      let pts: Point[] | null = null;
      const hull = new cv.Mat();
      const approx = new cv.Mat();
      try {
        cv.convexHull(c, hull);
        const peri = cv.arcLength(hull, true);
        cv.approxPolyDP(hull, approx, 0.02 * peri, true);
        if (approx.rows === 4) {
          pts = [];
          for (let r = 0; r < 4; r++) pts.push({ x: approx.intPtr(r, 0)[0], y: approx.intPtr(r, 0)[1] });
        } else {
          const rect = cv.minAreaRect(c);
          const rectArea = rect.size.width * rect.size.height;
          if (rectArea > 0 && cArea / rectArea >= RECTANGULARITY_MIN) pts = rectCorners(rect);
        }
      } finally {
        hull.delete();
        approx.delete();
      }
      if (pts) {
        const outline: Point[] = [];
        for (let k = 0; k < c.data32S.length; k += 2) outline.push({ x: c.data32S[k], y: c.data32S[k + 1] });
        pts = refineQuad(outline, pts);
        const [tl, tr, br, bl] = orderCorners(pts);
        const w = (dist(tl, tr) + dist(bl, br)) / 2;
        const h = (dist(tl, bl) + dist(tr, br)) / 2;
        const ratio = Math.min(w, h) / Math.max(w, h);
        if (ratio >= CARD_RATIO_MIN && ratio <= CARD_RATIO_MAX) out.push({ pts, area: quadArea(pts), inset });
      }
      c.delete();
    }
  } finally {
    contours.delete();
    hierarchy.delete();
  }
  return out;
}

/** 64-bit difference hash of the quad's content (warped flat, so it holds while the card sits still
 *  and changes completely when a different card takes its place). */
function signature(gray: Cv, q: [Point, Point, Point, Point]): number[] {
  const srcTri = cv.matFromArray(4, 1, cv.CV_32FC2, [q[0].x, q[0].y, q[1].x, q[1].y, q[2].x, q[2].y, q[3].x, q[3].y]);
  const dstTri = cv.matFromArray(4, 1, cv.CV_32FC2, [0, 0, SIG_W * 4, 0, SIG_W * 4, SIG_H * 4, 0, SIG_H * 4]);
  const M = cv.getPerspectiveTransform(srcTri, dstTri);
  const warped = new cv.Mat();
  const small = new cv.Mat();
  try {
    cv.warpPerspective(gray, warped, M, new cv.Size(SIG_W * 4, SIG_H * 4), cv.INTER_LINEAR, cv.BORDER_REPLICATE);
    cv.resize(warped, small, new cv.Size(SIG_W, SIG_H), 0, 0, cv.INTER_AREA);
    const bytes: number[] = [];
    for (let y = 0; y < SIG_H; y++) {
      let b = 0;
      for (let x = 0; x < SIG_W - 1; x++) {
        if (small.ucharAt(y, x) > small.ucharAt(y, x + 1)) b |= 1 << x;
      }
      bytes.push(b);
    }
    return bytes;
  } finally {
    srcTri.delete();
    dstTri.delete();
    M.delete();
    warped.delete();
    small.delete();
  }
}

/** Focus score: variance of the Laplacian over the quad's bounding box. Drops sharply with motion blur
 *  or a lens still hunting for focus. */
function sharpness(gray: Cv, q: Point[]): number {
  const xs = q.map((p) => p.x);
  const ys = q.map((p) => p.y);
  const x0 = Math.max(0, Math.floor(Math.min(...xs)));
  const y0 = Math.max(0, Math.floor(Math.min(...ys)));
  const x1 = Math.min(gray.cols, Math.ceil(Math.max(...xs)));
  const y1 = Math.min(gray.rows, Math.ceil(Math.max(...ys)));
  if (x1 - x0 < 8 || y1 - y0 < 8) return 0;
  const roi = gray.roi(new cv.Rect(x0, y0, x1 - x0, y1 - y0));
  const lap = new cv.Mat();
  const mean = new cv.Mat();
  const std = new cv.Mat();
  try {
    cv.Laplacian(roi, lap, cv.CV_64F);
    cv.meanStdDev(lap, mean, std);
    const s = std.doubleAt(0, 0);
    return s * s;
  } finally {
    roi.delete();
    lap.delete();
    mean.delete();
    std.delete();
  }
}

/** Largest card in single mode; in multi mode every card-sized quad (overlaps merged, reading order). */
function pick(candidates: Candidate[], multi: boolean): Candidate[] {
  const bySize = [...candidates].sort((a, b) => b.area - a.area);
  if (!multi) return bySize.slice(0, 1);
  const kept: Candidate[] = [];
  for (const cand of bySize) {
    if (cand.area < bySize[0].area * MULTI_MIN_RELATIVE_AREA) break;
    const c = centroid(cand.pts);
    if (kept.some((k) => pointInQuad(c, k.pts) || pointInQuad(centroid(k.pts), cand.pts))) continue;
    kept.push(cand);
    if (kept.length === MAX_CARDS) break;
  }
  return readingOrder(kept);
}

function detect(imageData: ImageData, multi: boolean): DetectedCard[] {
  const minArea = imageData.width * imageData.height * (multi ? MULTI_MIN_AREA_FRACTION : MIN_AREA_FRACTION);

  const src = cv.matFromImageData(imageData);
  const gray = new cv.Mat();
  const blurred = new cv.Mat();
  const edges = new cv.Mat();
  const mask = new cv.Mat();
  const kernel = cv.getStructuringElement(cv.MORPH_RECT, new cv.Size(7, 7));

  try {
    cv.cvtColor(src, gray, cv.COLOR_RGBA2GRAY);
    cv.GaussianBlur(gray, blurred, new cv.Size(5, 5), 0);

    // Primary: edge detection with low thresholds + generous dilation so a faint dark-on-neutral card
    // boundary still closes into a loop.
    cv.Canny(blurred, edges, 40, 120);
    cv.dilate(edges, edges, kernel);
    let candidates = collectCandidates(edges, minArea, CANNY_INSET);

    // Fallback: when edges are too faint to yield anything (dark/foil card on a low-contrast surface),
    // segment the card as a blob via adaptive threshold.
    if (candidates.length === 0) {
      cv.adaptiveThreshold(blurred, mask, 255, cv.ADAPTIVE_THRESH_MEAN_C, cv.THRESH_BINARY_INV, 51, 10);
      cv.morphologyEx(mask, mask, cv.MORPH_CLOSE, kernel);
      candidates = collectCandidates(mask, minArea, 0);
    }

    return pick(candidates, multi).map((cand) => {
      const q = insetQuad(cand.pts, cand.inset);
      return { pts: q, sig: signature(blurred, q), sharpness: sharpness(gray, q) };
    });
  } finally {
    src.delete();
    gray.delete();
    blurred.delete();
    edges.delete();
    mask.delete();
    kernel.delete();
  }
}

/** Deskew the quad into the ADF scanner's geometry (see ScanLayout): the card body at 300 DPI on a flat
 *  bed-colored margin, rounded corners filled with bed, portrait, as a JPEG. */
async function crop(imageData: ImageData, quad: Point[], layout: ScanLayout): Promise<ArrayBuffer> {
  let [tl, tr, br, bl] = orderCorners(quad);
  // A card lying landscape is turned clockwise to portrait (its left edge becomes the top).
  if (dist(tl, tr) + dist(bl, br) > dist(tl, bl) + dist(tr, br)) [tl, tr, br, bl] = [bl, tl, tr, br];
  const srcW = (dist(tl, tr) + dist(bl, br)) / 2;
  const { cardW, cardH } = layout;

  // warpPerspective has no area filter: shrinking a 4K card straight to 300 DPI would alias the fine
  // print OCR reads, so a big downscale warps at native size first and then area-resamples.
  const bigDownscale = srcW / cardW > 1.5;
  const warpW = bigDownscale ? Math.round(srcW) : cardW;
  const warpH = bigDownscale ? Math.round((srcW * cardH) / cardW) : cardH;

  const src = cv.matFromImageData(imageData);
  const warped = new cv.Mat();
  const srcTri = cv.matFromArray(4, 1, cv.CV_32FC2, [tl.x, tl.y, tr.x, tr.y, br.x, br.y, bl.x, bl.y]);
  const dstTri = cv.matFromArray(4, 1, cv.CV_32FC2, [0, 0, warpW, 0, warpW, warpH, 0, warpH]);
  const M = cv.getPerspectiveTransform(srcTri, dstTri);
  let resized: Cv | null = null;

  try {
    cv.warpPerspective(src, warped, M, new cv.Size(warpW, warpH), cv.INTER_CUBIC, cv.BORDER_REPLICATE);
    let cardMat: Cv = warped;
    if (bigDownscale) {
      resized = new cv.Mat();
      cv.resize(warped, resized, new cv.Size(cardW, cardH), 0, 0, cv.INTER_AREA);
      cardMat = resized;
    }

    // matFromImageData produced RGBA, and warp/resize preserve channel count → build ImageData
    // directly (copies the pixels) before releasing the mats.
    const card = new OffscreenCanvas(cardW, cardH);
    const cardCtx = card.getContext('2d');
    if (!cardCtx) throw new Error('OffscreenCanvas 2D context unavailable');
    cardCtx.putImageData(new ImageData(new Uint8ClampedArray(cardMat.data), cardW, cardH), 0, 0);

    const out = new OffscreenCanvas(layout.padL + cardW + layout.padR, layout.padT + cardH + layout.padB);
    const ctx = out.getContext('2d');
    if (!ctx) throw new Error('OffscreenCanvas 2D context unavailable');
    ctx.fillStyle = `rgb(${layout.bed}, ${layout.bed}, ${layout.bed})`;
    ctx.fillRect(0, 0, out.width, out.height);
    // The quad's corners are the card's virtual square corners; outside its rounded corners is table,
    // which an ADF shows as bed. (Browsers without roundRect keep square corners.)
    ctx.save();
    if (typeof ctx.roundRect === 'function') {
      ctx.beginPath();
      ctx.roundRect(layout.padL, layout.padT, cardW, cardH, layout.cornerRadius);
      ctx.clip();
    }
    ctx.drawImage(card, layout.padL, layout.padT);
    ctx.restore();
    const blob = await out.convertToBlob({ type: 'image/jpeg', quality: JPEG_QUALITY });
    return await blob.arrayBuffer();
  } finally {
    src.delete();
    warped.delete();
    srcTri.delete();
    dstTri.delete();
    M.delete();
    resized?.delete();
  }
}

// --- Runtime init: opencv's Module initializes asynchronously; poll `cv.Mat` for readiness. The Module
// is also thenable and resolves to itself, so we strip `then` before use to avoid promise-unwrap loops.
function initCv(): Promise<void> {
  return new Promise((resolve, reject) => {
    const ready = () => typeof cv.Mat === 'function';
    const finish = () => {
      cv.then = undefined;
      resolve();
    };
    if (ready()) {
      finish();
      return;
    }
    const start = Date.now();
    const timer = setInterval(() => {
      if (ready()) {
        clearInterval(timer);
        finish();
      } else if (Date.now() - start > 60_000) {
        clearInterval(timer);
        reject(new Error('opencv.js failed to initialize (timed out)'));
      }
    }, 30);
  });
}

interface DetectMsg { type: 'detect'; id: number; width: number; height: number; buffer: ArrayBuffer; multi: boolean }
interface CropMsg {
  type: 'crop';
  id: number;
  width: number;
  height: number;
  buffer: ArrayBuffer;
  quad: Point[];
  layout: ScanLayout;
}
type InMsg = DetectMsg | CropMsg;

initCv().then(
  () => {
    post({ type: 'ready' });
  },
  (e: Error) => {
    post({ type: 'init-error', message: e.message });
  },
);

onmessage = async (e: MessageEvent<InMsg>) => {
  const msg = e.data;
  try {
    const imageData = new ImageData(new Uint8ClampedArray(msg.buffer), msg.width, msg.height);
    if (msg.type === 'detect') {
      post({ type: 'result', id: msg.id, cards: detect(imageData, msg.multi) });
    } else {
      const buffer = await crop(imageData, msg.quad, msg.layout);
      post({ type: 'result', id: msg.id, buffer }, [buffer]);
    }
  } catch (err) {
    post({ type: 'error', id: msg.id, message: (err as Error).message });
  }
};
