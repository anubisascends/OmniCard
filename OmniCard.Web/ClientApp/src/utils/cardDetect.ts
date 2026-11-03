// Pure geometry helpers shared by the webcam capture UI. The actual opencv.js work (contour detection
// and perspective-warp crop) runs off the main thread in src/workers/opencv.worker.ts — importing
// opencv on the main thread parses/instantiates ~10 MB and freezes the tab. These helpers are cv-free
// so the dialog can track stability between frames cheaply.

export interface Point {
  x: number;
  y: number;
}

/** A card found in a frame: its corners, a 64-bit content signature (8 bytes of a difference hash,
 *  used to tell "the same card still sitting there" from "a new card in the same spot") and a
 *  focus/sharpness score (variance of the Laplacian — only comparable between frames of one card). */
export interface DetectedCard {
  pts: Point[];
  sig: number[];
  sharpness: number;
}

/** Output geometry of a capture: the card body plus the scanner-bed padding around it. */
export interface ScanLayout {
  cardW: number;
  cardH: number;
  padL: number;
  padT: number;
  padR: number;
  padB: number;
  /** Grey level of the padding and the rounded corners (the ADF's white bed reads ≈ 240). */
  bed: number;
  cornerRadius: number;
}

// Captures mimic the dedicated ADF scanners' output, which the server's OCR regions (fractions of the
// whole image) are calibrated on. Medians over 150 of the 300 DPI MTG scans: a 63 × 88 mm card body
// comes out 742 × 1044 px (the feed stretches it ~0.5% lengthwise), deskewed, with 15 px of bed on
// the left, 12 px on the right and below, and the leading (top) edge flush with the image.
const PX_PER_MM_X = 742 / 63;
const PX_PER_MM_Y = 1044 / 88;

/** The ADF-equivalent layout for a game's cards (Yu-Gi-Oh! is 59 × 86 mm; the rest are 63 × 88 mm). */
export function scanLayoutFor(game: string): ScanLayout {
  const [wMm, hMm] = game === 'YuGiOh' ? [59, 86] : [63, 88];
  return {
    cardW: Math.round(wMm * PX_PER_MM_X),
    cardH: Math.round(hMm * PX_PER_MM_Y),
    padL: 15,
    padT: 0,
    padR: 12,
    padB: 12,
    bed: 240,
    cornerRadius: Math.round(3 * PX_PER_MM_X),
  };
}

/** Order four corners as [topLeft, topRight, bottomRight, bottomLeft] using the sum/diff heuristic. */
export function orderCorners(pts: Point[]): [Point, Point, Point, Point] {
  const bySum = [...pts].sort((a, b) => a.x + a.y - (b.x + b.y));
  const byDiff = [...pts].sort((a, b) => a.y - a.x - (b.y - b.x));
  return [bySum[0], byDiff[0], bySum[3], byDiff[3]];
}

/** Centroid of a quad — used to measure positional stability between frames. */
export function centroid(pts: Point[]): Point {
  const n = pts.length;
  return {
    x: pts.reduce((s, p) => s + p.x, 0) / n,
    y: pts.reduce((s, p) => s + p.y, 0) / n,
  };
}

/** Quad area via the shoelace formula (corners may be unordered → order them first). */
export function quadArea(pts: Point[]): number {
  const ring = orderCorners(pts);
  let a = 0;
  for (let i = 0; i < 4; i++) {
    const p = ring[i];
    const q = ring[(i + 1) % 4];
    a += p.x * q.y - q.x * p.y;
  }
  return Math.abs(a) / 2;
}

/** Whether `p` lies inside the convex quad `pts`. */
export function pointInQuad(p: Point, pts: Point[]): boolean {
  const ring = orderCorners(pts);
  let sign = 0;
  for (let i = 0; i < 4; i++) {
    const a = ring[i];
    const b = ring[(i + 1) % 4];
    const cross = (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
    if (cross === 0) continue;
    if (sign === 0) sign = Math.sign(cross);
    else if (Math.sign(cross) !== sign) return false;
  }
  return true;
}

/** Move every edge of a convex quad `d` px inward (negative = outward) and return the new corners in
 *  [tl, tr, br, bl] order. Used to undo the outward growth of a dilated edge contour. */
export function insetQuad(pts: Point[], d: number): [Point, Point, Point, Point] {
  const q = orderCorners(pts);
  if (d === 0) return q;
  const c = centroid(q);
  // Each edge i runs q[i] → q[i+1]; shift it along its inward normal.
  const lines = q.map((a, i) => {
    const b = q[(i + 1) % 4];
    const len = Math.hypot(b.x - a.x, b.y - a.y) || 1;
    const u = { x: (b.x - a.x) / len, y: (b.y - a.y) / len };
    let n = { x: -u.y, y: u.x };
    if (n.x * (c.x - a.x) + n.y * (c.y - a.y) < 0) n = { x: -n.x, y: -n.y };
    return { p: { x: a.x + n.x * d, y: a.y + n.y * d }, u };
  });
  // Corner i is where edge i-1 meets edge i.
  return q.map((orig, i) => intersect(lines[(i + 3) % 4], lines[i]) ?? orig) as [Point, Point, Point, Point];
}

interface Line {
  p: Point;
  u: Point;
}

function intersect(l1: Line, l2: Line): Point | null {
  const denom = l1.u.x * l2.u.y - l1.u.y * l2.u.x;
  if (Math.abs(denom) < 1e-6) return null;
  const t = ((l2.p.x - l1.p.x) * l2.u.y - (l2.p.y - l1.p.y) * l2.u.x) / denom;
  return { x: l1.p.x + l1.u.x * t, y: l1.p.y + l1.u.y * t };
}

/** Total-least-squares line through `pts` (centroid + principal direction). */
function fitLine(pts: Point[]): Line {
  const c = centroid(pts);
  let sxx = 0;
  let sxy = 0;
  let syy = 0;
  for (const p of pts) {
    const dx = p.x - c.x;
    const dy = p.y - c.y;
    sxx += dx * dx;
    sxy += dx * dy;
    syy += dy * dy;
  }
  const angle = 0.5 * Math.atan2(2 * sxy, sxx - syy);
  return { p: c, u: { x: Math.cos(angle), y: Math.sin(angle) } };
}

/**
 * Snap a rough quad onto the card's straight sides. The hull/polygon corners of a rounded-corner card
 * sit on the corner arcs, so the rough quad's edges are chords that cut ~1 mm into the card. Each side
 * is re-fit to the contour points along its middle (skipping the arcs) and the corners become where
 * neighbouring sides meet — the card's virtual square corners, as an ADF crop has them.
 */
export function refineQuad(contour: Point[], quad: Point[]): [Point, Point, Point, Point] {
  const q = orderCorners(quad);
  const lines: Line[] = q.map((a, i) => {
    const b = q[(i + 1) % 4];
    const len = Math.hypot(b.x - a.x, b.y - a.y) || 1;
    const u = { x: (b.x - a.x) / len, y: (b.y - a.y) / len };
    const tol = Math.max(4, len * 0.04);
    const near = contour.filter((p) => {
      const t = ((p.x - a.x) * u.x + (p.y - a.y) * u.y) / len;
      const off = Math.abs((p.x - a.x) * u.y - (p.y - a.y) * u.x);
      return t > 0.15 && t < 0.85 && off < tol;
    });
    return near.length >= 8 ? fitLine(near) : { p: a, u };
  });
  return q.map((orig, i) => intersect(lines[(i + 3) % 4], lines[i]) ?? orig) as [Point, Point, Point, Point];
}

/** Sort quads into reading order: rows top to bottom (a row = centroids within half a card's short
 *  side), then left to right within a row. Multi-card captures arrive in the review list in this order. */
export function readingOrder<T extends { pts: Point[] }>(cards: T[]): T[] {
  if (cards.length < 2) return cards;
  const withC = cards.map((card) => {
    const [tl, tr, , bl] = orderCorners(card.pts);
    const shortSide = Math.min(Math.hypot(tl.x - tr.x, tl.y - tr.y), Math.hypot(tl.x - bl.x, tl.y - bl.y));
    return { card, c: centroid(card.pts), shortSide };
  });
  withC.sort((a, b) => a.c.y - b.c.y);
  const rows: (typeof withC)[] = [];
  for (const item of withC) {
    const row = rows[rows.length - 1];
    if (row && Math.abs(item.c.y - row[0].c.y) < row[0].shortSide / 2) row.push(item);
    else rows.push([item]);
  }
  return rows.flatMap((row) => row.sort((a, b) => a.c.x - b.c.x).map((i) => i.card));
}

/** Hamming distance between two content signatures (8-byte difference hashes). */
export function sigDistance(a: number[], b: number[]): number {
  let d = 0;
  for (let i = 0; i < Math.min(a.length, b.length); i++) {
    let x = a[i] ^ b[i];
    while (x) {
      d += x & 1;
      x >>= 1;
    }
  }
  return d;
}
