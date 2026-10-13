import type { TFunction } from 'i18next';
import type { DeckSyncSourceDto } from '../api/types';

/** Where an owned copy sits: "Trade Binder · page 3 · slot 4". */
export function sourcePosition(t: TFunction, s: DeckSyncSourceDto): string {
  return [
    s.locationName,
    s.section,
    s.page != null ? t('lists.substitutes.page', { page: s.page }) : null,
    s.slot != null ? t('lists.substitutes.slot', { slot: s.slot }) : null,
  ]
    .filter(Boolean)
    .join(' · ');
}
