import type { LocationSummaryDto } from '../api/types';

// Order the per-type groups follow, and their (plural) section headings. Keys match the display
// type string the API returns (LocationSummaryDto.Type), e.g. "Deck Box" / "Display Case".
export const TYPE_ORDER = ['Binder', 'Box', 'Deck Box', 'Display Case', 'Bulk'];
export const TYPE_HEADINGS: Record<string, string> = {
  Binder: 'Binders',
  Box: 'Boxes',
  'Deck Box': 'Deck Boxes',
  'Display Case': 'Display Cases',
  Bulk: 'Bulk',
};

export const byName = (a: LocationSummaryDto, b: LocationSummaryDto) =>
  a.name.localeCompare(b.name, undefined, { sensitivity: 'base' });

export interface LocationGroup {
  key: string;
  heading: string;
  items: LocationSummaryDto[];
}

/** Always-available locations first, then the rest grouped by type; every group sorted A→Z.
 * Shared by the Locations page and the move-to-location picker so grouping stays aligned. */
export function groupLocations(locations: LocationSummaryDto[]): LocationGroup[] {
  const groups: LocationGroup[] = [];

  const alwaysAvailable = locations.filter((l) => l.isAlwaysAvailable).sort(byName);
  if (alwaysAvailable.length > 0)
    groups.push({ key: '__always__', heading: 'Always Available', items: alwaysAvailable });

  const byType = new Map<string, LocationSummaryDto[]>();
  for (const loc of locations.filter((l) => !l.isAlwaysAvailable)) {
    (byType.get(loc.type) ?? byType.set(loc.type, []).get(loc.type)!).push(loc);
  }

  const orderedTypes = [...byType.keys()].sort((a, b) => {
    const ia = TYPE_ORDER.indexOf(a);
    const ib = TYPE_ORDER.indexOf(b);
    if (ia !== -1 && ib !== -1) return ia - ib;
    if (ia !== -1) return -1;
    if (ib !== -1) return 1;
    return a.localeCompare(b);
  });

  for (const type of orderedTypes) {
    groups.push({ key: type, heading: TYPE_HEADINGS[type] ?? type, items: byType.get(type)!.sort(byName) });
  }
  return groups;
}
