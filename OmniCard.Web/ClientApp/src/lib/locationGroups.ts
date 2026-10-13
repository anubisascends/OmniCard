import i18n from '../i18n';
import type { LocationSummaryDto } from '../api/types';

// (Plural) section headings per location type. Keys match the display type string the API returns
// (LocationSummaryDto.Type), e.g. "Deck Box" / "Display Case".
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

/** Always-available locations first, then the rest grouped by type with the type groups ordered
 * alphabetically by heading; every group's own items sorted A→Z. Shared by the Locations page, the
 * move-to-location picker, and the inline location dropdowns so grouping stays aligned everywhere. */
export function groupLocations(locations: LocationSummaryDto[]): LocationGroup[] {
  const groups: LocationGroup[] = [];

  const alwaysAvailable = locations.filter((l) => l.isAlwaysAvailable).sort(byName);
  if (alwaysAvailable.length > 0)
    groups.push({ key: '__always__', heading: 'Always Available', items: alwaysAvailable });

  const byType = new Map<string, LocationSummaryDto[]>();
  for (const loc of locations.filter((l) => !l.isAlwaysAvailable)) {
    (byType.get(loc.type) ?? byType.set(loc.type, []).get(loc.type)!).push(loc);
  }

  const typeGroups = [...byType.keys()]
    .map((type) => ({ key: type, heading: TYPE_HEADINGS[type] ?? type, items: byType.get(type)!.sort(byName) }))
    .sort((a, b) => a.heading.localeCompare(b.heading, undefined, { sensitivity: 'base' }));

  groups.push(...typeGroups);
  return groups;
}

// Localized group heading. `groupLocations` returns a stable `key` per group (`__always__`, or the
// type display string), which we resolve to a translated heading here. This is a plain function (no
// React hooks), so it reads the shared i18n singleton rather than `useTranslation`.
export function headingFor(key: string, fallback: string): string {
  if (key === '__always__') return i18n.t('locations.groups.alwaysAvailable');
  return i18n.t(`locations.groups.headings.${key}`, { defaultValue: fallback });
}
