import type { LabelCardDto } from '../../api/types';

// The card values a label can print. A label element is filled from a field when its *name* is the
// field's key (cardName, setCode, …; a trailing number is ignored, so a second copy can be cardName2).
// Static ("not editable") text and barcodes can also combine fields with placeholders, e.g.
// "{setCode} #{collectorNumber}". Any other element prints what the designer shows.

export const LABEL_FIELD_KEYS = [
  'cardName',
  'game',
  'setCode',
  'setName',
  'collectorNumber',
  'rarity',
  'condition',
  'language',
  'finish',
  'marketPrice',
  'location',
  'cardUrl',
  'tcgplayerUrl',
  'cardId',
  'tcgplayerId',
] as const;

export type LabelFieldKey = (typeof LABEL_FIELD_KEYS)[number];

export type LabelValues = Record<LabelFieldKey, string>;

/** The element the designer's "Add field" inserts for a field: links become QR codes, ids barcodes. */
export const FIELD_ELEMENT: Record<LabelFieldKey, 'text' | 'qrcode' | 'code128'> = {
  cardName: 'text',
  game: 'text',
  setCode: 'text',
  setName: 'text',
  collectorNumber: 'text',
  rarity: 'text',
  condition: 'text',
  language: 'text',
  finish: 'text',
  marketPrice: 'text',
  location: 'text',
  cardUrl: 'qrcode',
  tcgplayerUrl: 'qrcode',
  cardId: 'code128',
  tcgplayerId: 'code128',
};

const KEYS = new Set<string>(LABEL_FIELD_KEYS);

/** The field an element named `name` is filled from, or null for a plain element. */
export function fieldOf(name: string | undefined): LabelFieldKey | null {
  if (!name) return null;
  if (KEYS.has(name)) return name as LabelFieldKey;
  const base = name.replace(/[\s_-]*\d+$/, '');
  return KEYS.has(base) ? (base as LabelFieldKey) : null;
}

/** Where a card's own page lives: the SPA root (…/app) the card links are built from. */
export function defaultLinkBase(): string {
  return `${window.location.origin}${import.meta.env.BASE_URL.replace(/\/$/, '')}`;
}

export const cardLink = (base: string, id: number) => `${base.replace(/\/+$/, '')}/card/${id}`;

/** True when card links would point at this machine only (a phone scanning the QR code can't reach it). */
export function isLocalOnlyBase(base: string): boolean {
  try {
    const host = new URL(base).hostname;
    return host === 'localhost' || host === '127.0.0.1' || host === '[::1]' || host.endsWith('.localhost');
  } catch {
    return true;
  }
}

export function labelValues(card: LabelCardDto, opts: { money: (v: number) => string; linkBase: string }): LabelValues {
  return {
    cardName: card.name,
    game: card.gameName,
    setCode: card.setCode.toUpperCase(),
    setName: card.setName,
    collectorNumber: card.collectorNumber,
    rarity: card.rarity,
    condition: card.condition,
    language: (card.language || 'en').toUpperCase(),
    finish: card.finish,
    marketPrice: card.marketPrice > 0 ? opts.money(card.marketPrice) : '',
    location: card.location ?? '',
    cardUrl: cardLink(opts.linkBase, card.id),
    tcgplayerUrl: card.tcgPlayerUrl,
    cardId: String(card.id),
    tcgplayerId: card.tcgPlayerId != null ? String(card.tcgPlayerId) : '',
  };
}

/** The made-up card the designer's samples and test prints use. */
export const SAMPLE_CARD: LabelCardDto = {
  id: 1234,
  game: 'Mtg',
  gameName: 'Magic: The Gathering',
  name: 'Lightning Bolt',
  setCode: 'm11',
  setName: 'Magic 2011',
  collectorNumber: '149',
  rarity: 'Common',
  condition: 'NM',
  language: 'en',
  finish: 'Foil',
  quantity: 1,
  marketPrice: 1.23,
  tcgPlayerUrl: 'https://www.tcgplayer.com/product/12345',
  tcgPlayerId: 12345,
  location: 'Trade Binder',
};

/** One label's pdfme input: every field (for placeholders), each field element's value under its own
 * name, and — for plain editable elements — the content the designer shows. */
export function buildInput(
  schemas: { name: string; type: string; content?: string; readOnly?: boolean }[],
  values: LabelValues,
): Record<string, string> {
  const input: Record<string, string> = { ...values };
  for (const s of schemas) {
    if (s.readOnly) continue;
    const key = fieldOf(s.name);
    if (s.type === 'multiVariableText') input[s.name] = JSON.stringify(values);
    else input[s.name] = key ? values[key] : s.content ?? '';
  }
  return input;
}
