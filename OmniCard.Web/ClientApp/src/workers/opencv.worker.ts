// opencv.js runs entirely in this worker so the ~10 MB wasm runtime never parses/executes on the main
// thread (doing so freezes the tab). The main thread (utils/cvWorker.ts) sends raw frame pixels and gets
// back the detected card quad, or a cropped JPEG. This is a CLASSIC worker (no `type: 'module'`) so
// opencv.js correctly detects a Web Worker environment (`typeof importScripts === 'function'`).

import cvImport from '@techstark/opencv-js';
import { orderCorners, quadArea, type Point } from '../utils/cardDetect';

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
const MIN_AREA_FRACTION = 0.08; // card must fill ≥ this fraction of the frame.
const RECTANGULARITY_MIN = 0.7; // contour area / its min-area-rect area — filters non-rectangular blobs.
const BORDER_EXPAND = 0.015; // grow the quad ~1.5% so the crop keeps a thin (~1mm) open border.
const OUTPUT_LONG_SIDE = 880; // normalized output height (portrait).

function dist(a: Point, b: Point): number {
  return Math.hypot(a.x - b.x, a.y - b.y);
}

interface Candidate {
  pts: Point[];
  area: number;
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
 *  ragged or rounded (dark/borderless cards). Filters by aspect ratio; the caller picks the largest. */
function collectCandidates(binary: Cv, minArea: number): Candidate[] {
  const contours = new cv.MatVector();
  const hierarchy = new cv.Mat();
  const out: Candidate[] = [];
  try {
    cv.findContours(binary, contours, hierarchy, cv.RETR_EXTERNAL, cv.CHAIN_APPROX_SIMPLE);
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
        const [tl, tr, br, bl] = orderCorners(pts);
        const w = (dist(tl, tr) + dist(bl, br)) / 2;
        const h = (dist(tl, bl) + dist(tr, br)) / 2;
        const ratio = Math.min(w, h) / Math.max(w, h);
        if (ratio >= CARD_RATIO_MIN && ratio <= CARD_RATIO_MAX) out.push({ pts, area: quadArea(pts) });
      }
      c.delete();
    }
  } finally {
    contours.delete();
    hierarchy.delete();
  }
  return out;
}

function detect(imageData: ImageData): Point[] | null {
  const minArea = imageData.width * imageData.height * MIN_AREA_FRACTION;

  const src = cv.matFromImageData(imageData);
  const gray = new cv.Mat();
  const blurred = new cv.Mat();
  const edges = new cv.Mat();
  const mask = new cv.Mat();
  const kernel = cv.getStructuringElement(cv.MORPH_RECT, new cv.Size(7, 7));
  let best: Candidate | null = null;

  try {
    cv.cvtColor(src, gray, cv.COLOR_RGBA2GRAY);
    cv.GaussianBlur(gray, blurred, new cv.Size(5, 5), 0);

    // Primary: edge detection with low thresholds + generous dilation so a faint dark-on-neutral card
    // boundary still closes into a loop.
    cv.Canny(blurred, edges, 40, 120);
    cv.dilate(edges, edges, kernel);
    let candidates = collectCandidates(edges, minArea);

    // Fallback: when edges are too faint to yield anything (dark/foil card on a low-contrast surface),
    // segment the card as a blob via adaptive threshold.
    if (candidates.length === 0) {
      cv.adaptiveThreshold(blurred, mask, 255, cv.ADAPTIVE_THRESH_MEAN_C, cv.THRESH_BINARY_INV, 51, 10);
      cv.morphologyEx(mask, mask, cv.MORPH_CLOSE, kernel);
      candidates = collectCandidates(mask, minArea);
    }

    for (const cand of candidates) if (!best || cand.area > best.area) best = cand;
  } finally {
    src.delete();
    gray.delete();
    blurred.delete();
    edges.delete();
    mask.delete();
    kernel.delete();
  }

  return best?.pts ?? null;
}

function expandQuad(corners: [Point, Point, Point, Point], w: number, h: number): [Point, Point, Point, Point] {
  const cx = (corners[0].x + corners[1].x + corners[2].x + corners[3].x) / 4;
  const cy = (corners[0].y + corners[1].y + corners[2].y + corners[3].y) / 4;
  return corners.map((p) => ({
    x: Math.max(0, Math.min(w, cx + (p.x - cx) * (1 + BORDER_EXPAND))),
    y: Math.max(0, Math.min(h, cy + (p.y - cy) * (1 + BORDER_EXPAND))),
  })) as [Point, Point, Point, Point];
}

async function crop(imageData: ImageData, quad: Point[]): Promise<ArrayBuffer> {
  const [tl, tr, br, bl] = expandQuad(orderCorners(quad), imageData.width, imageData.height);
  const cardW = Math.round(Math.max(dist(tl, tr), dist(bl, br)));
  const cardH = Math.round(Math.max(dist(tl, bl), dist(tr, br)));

  const src = cv.matFromImageData(imageData);
  const warped = new cv.Mat();
  const srcTri = cv.matFromArray(4, 1, cv.CV_32FC2, [tl.x, tl.y, tr.x, tr.y, br.x, br.y, bl.x, bl.y]);
  const dstTri = cv.matFromArray(4, 1, cv.CV_32FC2, [0, 0, cardW, 0, cardW, cardH, 0, cardH]);
  const M = cv.getPerspectiveTransform(srcTri, dstTri);
  let rotated: Cv | null = null;
  let resized: Cv | null = null;

  try {
    cv.warpPerspective(src, warped, M, new cv.Size(cardW, cardH), cv.INTER_LINEAR, cv.BORDER_REPLICATE);

    // Rotate to portrait if the card came in landscape (wider than tall).
    let outMat: Cv = warped;
    if (cardW > cardH) {
      rotated = new cv.Mat();
      cv.rotate(warped, rotated, cv.ROTATE_90_CLOCKWISE);
      outMat = rotated;
    }

    // Normalize size (keep aspect) so we don't ship an oversized blob.
    const longSide = Math.max(outMat.rows, outMat.cols);
    if (longSide > OUTPUT_LONG_SIDE) {
      const scale = OUTPUT_LONG_SIDE / longSide;
      resized = new cv.Mat();
      cv.resize(
        outMat,
        resized,
        new cv.Size(Math.round(outMat.cols * scale), Math.round(outMat.rows * scale)),
        0,
        0,
        cv.INTER_AREA,
      );
      outMat = resized;
    }

    // matFromImageData produced RGBA, and warp/rotate/resize preserve channel count → build ImageData
    // directly (copies the pixels) before releasing the mats.
    const out = new ImageData(new Uint8ClampedArray(outMat.data), outMat.cols, outMat.rows);
    const canvas = new OffscreenCanvas(out.width, out.height);
    const ctx = canvas.getContext('2d');
    if (!ctx) throw new Error('OffscreenCanvas 2D context unavailable');
    ctx.putImageData(out, 0, 0);
    const blob = await canvas.convertToBlob({ type: 'image/jpeg', quality: 0.92 });
    return await blob.arrayBuffer();
  } finally {
    src.delete();
    warped.delete();
    srcTri.delete();
    dstTri.delete();
    M.delete();
    rotated?.delete();
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

interface DetectMsg { type: 'detect'; id: number; width: number; height: number; buffer: ArrayBuffer }
interface CropMsg { type: 'crop'; id: number; width: number; height: number; buffer: ArrayBuffer; quad: Point[] }
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
      post({ type: 'result', id: msg.id, quad: detect(imageData) });
    } else {
      const buffer = await crop(imageData, msg.quad);
      post({ type: 'result', id: msg.id, buffer }, [buffer]);
    }
  } catch (err) {
    post({ type: 'error', id: msg.id, message: (err as Error).message });
  }
};
