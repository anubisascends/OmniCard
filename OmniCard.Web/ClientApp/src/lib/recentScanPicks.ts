/**
 * The printings picked from the Scan page's catalog search during this browser session, newest first,
 * per game. The search lists any of them it finds again under "Recently selected", above the rest.
 * Kept in sessionStorage: it survives a refresh and ends with the tab. Best-effort — if storage is
 * unavailable the picks are only remembered while the page stays open.
 */

const MAX_PICKS = 100;
const memory = new Map<string, string[]>();

const storageKey = (game: string) => `omnicard.scan.recentPicks.${game}`;

/** The remembered printing ids for `game`, most recent first. */
export function recentScanPicks(game: string): string[] {
  try {
    const raw = sessionStorage.getItem(storageKey(game));
    if (raw) {
      const parsed: unknown = JSON.parse(raw);
      if (Array.isArray(parsed)) return parsed.filter((x): x is string => typeof x === 'string');
    }
  } catch {
    // Fall through to the in-memory copy.
  }
  return memory.get(game) ?? [];
}

/** Remember a pick (moving it to the front if it was already there). Returns the new list. */
export function rememberScanPick(game: string, gameCardId: string): string[] {
  const next = [gameCardId, ...recentScanPicks(game).filter((id) => id !== gameCardId)].slice(0, MAX_PICKS);
  memory.set(game, next);
  try {
    sessionStorage.setItem(storageKey(game), JSON.stringify(next));
  } catch {
    // Memory copy only.
  }
  return next;
}

/** Forget every pick for `game`. */
export function clearScanPicks(game: string): void {
  memory.delete(game);
  try {
    sessionStorage.removeItem(storageKey(game));
  } catch {
    // Nothing stored.
  }
}
