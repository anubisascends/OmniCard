// Pure geometry helpers shared by the webcam capture UI. The actual opencv.js work (contour detection
// and perspective-warp crop) runs off the main thread in src/workers/opencv.worker.ts — importing
// opencv on the main thread parses/instantiates ~10 MB and freezes the tab. These helpers are cv-free
// so the dialog can track stability between frames cheaply.

export interface Point {
  x: number;
  y: number;
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
