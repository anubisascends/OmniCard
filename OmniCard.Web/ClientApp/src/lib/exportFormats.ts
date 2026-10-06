/**
 * The collection/scan export formats the server understands (`CsvExportFormats` on the backend).
 * Labels live under `collection.exportFormats.<key>`.
 */
export const EXPORT_FORMATS = [
  'appnative',
  'tcgplayer',
  'moxfield',
  'manabox',
  'archidekt',
  'deckbox',
  'dragonshield',
  'ticker',
  'text',
] as const;

export type ExportFormat = (typeof EXPORT_FORMATS)[number];

/** Formats whose target app only handles Magic: The Gathering. */
export const MTG_ONLY_FORMATS: ReadonlySet<string> = new Set(['moxfield', 'manabox', 'archidekt', 'deckbox']);

export function formatAvailableFor(format: string, game: string): boolean {
  return game === 'Mtg' || !MTG_ONLY_FORMATS.has(format);
}

/** One plain-text list line — "4 Lightning Bolt (2X2) 117 *F*" — the paste format Moxfield,
 * Archidekt, ManaBox and most deck builders accept. Mirrors the server's `TextListLine`. */
export function textListLine(card: {
  quantity: number;
  name: string;
  setCode?: string | null;
  collectorNumber?: string | null;
  isFoil: boolean;
  foilType?: string | null;
}): string {
  let line = `${Math.max(1, card.quantity)} ${card.name}`;
  if (card.setCode?.trim()) {
    line += ` (${card.setCode.toUpperCase()})`;
    if (card.collectorNumber?.trim()) line += ` ${card.collectorNumber}`;
  }
  if (card.isFoil) line += card.foilType?.toLowerCase().includes('etched') ? ' *E*' : ' *F*';
  return line;
}

/** Copy text to the clipboard. `navigator.clipboard` only exists in a secure context (https or
 * localhost), and the app is often reached over plain-http LAN, so fall back to a hidden textarea. */
export async function copyToClipboard(text: string): Promise<boolean> {
  try {
    if (navigator.clipboard && window.isSecureContext) {
      await navigator.clipboard.writeText(text);
      return true;
    }
  } catch {
    /* fall through to the legacy path */
  }
  const ta = document.createElement('textarea');
  ta.value = text;
  ta.setAttribute('readonly', '');
  ta.style.position = 'fixed';
  ta.style.opacity = '0';
  document.body.appendChild(ta);
  ta.select();
  try {
    return document.execCommand('copy');
  } catch {
    return false;
  } finally {
    ta.remove();
  }
}

/** Local timestamp for export file names: 2026-10-06-1432. */
export function fileStamp(d = new Date()): string {
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}-${p(d.getHours())}${p(d.getMinutes())}`;
}
