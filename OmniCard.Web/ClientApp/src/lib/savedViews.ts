import type { SavedViewStateDto } from '../api/types';

/** The current layout of a Collection / Location card list — what a saved view stores and restores. */
export type ViewState = SavedViewStateDto;

/** The game key a saved view uses for the "All Games" selection (no game filter). */
export const ALL_GAMES_KEY = 'all';

/** Grid page sizes offered by the card table (and accepted by the server). */
export const PAGE_SIZES = [25, 50, 100] as const;

// Pre-saved-views preferences, still the starting point of the built-in layout.
const STACK_KEY = 'omnicard.stackDuplicates';
const DISPLAY_KEY = 'omnicard.location.view';
const GROUP_KEY = 'omnicard.deckstack.groupmode';

function read(key: string): string | null {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

function write(key: string, value: string) {
  try {
    localStorage.setItem(key, value);
  } catch {
    /* storage unavailable — the preference just isn't remembered */
  }
}

/**
 * The layout a page opens with when no saved view applies: name A→Z, 100 per page, every column in
 * its default order — plus the stacking / table-vs-stacks / group-by the browser last used.
 */
export function builtInViewState(onLocation: boolean): ViewState {
  return {
    q: '',
    sort: 'name',
    dir: 'asc',
    pageSize: 100,
    stacked: read(STACK_KEY) !== 'false',
    hiddenColumns: [],
    columnOrder: [],
    display: onLocation ? (read(DISPLAY_KEY) === 'stacks' ? 'stacks' : 'table') : null,
    groupBy: onLocation ? (read(GROUP_KEY) === 'tag' ? 'tag' : 'type') : null,
    groupColumns: [],
  };
}

/** Remember the browser-level preferences the built-in layout starts from. */
export function rememberBuiltInPrefs(state: ViewState) {
  write(STACK_KEY, String(state.stacked));
  if (state.display) write(DISPLAY_KEY, state.display);
  if (state.groupBy) write(GROUP_KEY, state.groupBy);
}

/** Fill in anything a stored view lacks (older views, fields that don't apply on this page). */
export function normalizeViewState(state: Partial<ViewState> | undefined, onLocation: boolean): ViewState {
  const base = builtInViewState(onLocation);
  return {
    q: state?.q ?? base.q,
    sort: state?.sort || base.sort,
    dir: state?.dir === 'desc' ? 'desc' : 'asc',
    pageSize: PAGE_SIZES.includes(state?.pageSize as (typeof PAGE_SIZES)[number]) ? state!.pageSize! : base.pageSize,
    stacked: state?.stacked ?? base.stacked,
    hiddenColumns: state?.hiddenColumns ?? [],
    columnOrder: state?.columnOrder ?? [],
    display: onLocation ? state?.display ?? base.display : null,
    groupBy: onLocation ? state?.groupBy ?? base.groupBy : null,
    groupColumns: state?.groupColumns ?? [],
  };
}

const sameList = (a: string[], b: string[]) => a.length === b.length && a.every((v, i) => v === b[i]);

/** True when two layouts would show the same thing (hidden columns compared as a set). */
export function sameViewState(a: ViewState, b: ViewState): boolean {
  return (
    a.q.trim() === b.q.trim() &&
    a.sort === b.sort &&
    a.dir === b.dir &&
    a.pageSize === b.pageSize &&
    a.stacked === b.stacked &&
    sameList([...a.hiddenColumns].sort(), [...b.hiddenColumns].sort()) &&
    sameList(a.columnOrder, b.columnOrder) &&
    (a.display ?? null) === (b.display ?? null) &&
    (a.groupBy ?? null) === (b.groupBy ?? null) &&
    sameList(a.groupColumns ?? [], b.groupColumns ?? [])
  );
}

/** `fields` in display order: the saved order first, then any columns it doesn't mention, as defined. */
export function orderColumns(fields: string[], order: string[]): string[] {
  const known = new Set(fields);
  const ordered = order.filter((f) => known.has(f));
  const placed = new Set(ordered);
  return [...ordered, ...fields.filter((f) => !placed.has(f))];
}
