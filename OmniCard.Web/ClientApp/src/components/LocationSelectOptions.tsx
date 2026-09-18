import { ListSubheader, MenuItem } from '@mui/material';
import type { ReactNode } from 'react';
import i18n from '../i18n';
import type { LocationSummaryDto } from '../api/types';
import { groupLocations } from '../lib/locationGroups';

// Localized group heading. `groupLocations` returns a stable `key` per group (`__always__`, or the
// type display string), which we resolve to a translated heading here. This module is a plain
// function (no React hooks), so it reads the shared i18n singleton rather than `useTranslation`.
function headingFor(key: string, fallback: string): string {
  if (key === '__always__') return i18n.t('locations.groups.alwaysAvailable');
  return i18n.t(`locations.groups.headings.${key}`, { defaultValue: fallback });
}

/**
 * Options for a MUI `<TextField select>` / `<Select>` location picker: the locations grouped by type
 * under non-selectable `<ListSubheader>`s (Always-Available first, then type groups A→Z), matching the
 * shared move-to-location dialog. Optionally prefixed with an empty-value placeholder item.
 *
 * Returns a flat array of elements (not a component) on purpose — `Select` reads each child's `value`
 * to render the current selection, so the `<MenuItem>`s must be direct children, not nested in a wrapper.
 */
export function locationSelectOptions(
  locations: LocationSummaryDto[] | undefined,
  placeholder?: { label: string; value?: '' | number },
): ReactNode[] {
  const nodes: ReactNode[] = [];
  if (placeholder)
    nodes.push(
      <MenuItem key="__placeholder__" value={placeholder.value ?? ''}>
        {placeholder.label}
      </MenuItem>,
    );
  for (const g of groupLocations(locations ?? [])) {
    nodes.push(
      <ListSubheader key={g.key} disableSticky>
        {headingFor(g.key, g.heading)}
      </ListSubheader>,
    );
    for (const l of g.items)
      nodes.push(
        <MenuItem key={l.id} value={l.id}>
          {l.name}
        </MenuItem>,
      );
  }
  return nodes;
}
