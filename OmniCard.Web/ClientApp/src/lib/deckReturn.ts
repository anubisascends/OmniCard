import type { TFunction } from 'i18next';
import type { DeckReturnLineDto } from '../api/types';

/** "Blue · Page 3 · Slot 4" — skipping absent parts. */
function position(t: TFunction, section: string | null, page: number | null, slot: number | null): string {
  return [
    section,
    page != null ? t('lists.substitutes.page', { page }) : null,
    slot != null ? t('lists.substitutes.slot', { slot }) : null,
  ]
    .filter(Boolean)
    .join(' · ');
}

/** Where at its destination a returned copy goes ("Page 3 · Slot 4", or "Loose"). */
export function returnPosition(t: TFunction, l: DeckReturnLineDto): string {
  return position(t, l.section, l.page, l.slot) || t('deckbox.return.loose');
}

/** Why a copy didn't go back where it came from, or null when it did. */
export function returnIssue(t: TFunction, l: DeckReturnLineDto): string | null {
  if (l.issue === 'None') return null;
  const pos = position(t, l.originSection, l.originPage, l.originSlot);
  const origin = l.originName ? (pos ? `${l.originName} (${pos})` : l.originName) : '';
  return t(`deckbox.return.issues.${l.issue}`, { origin, card: l.slotTakenBy ?? '' });
}
