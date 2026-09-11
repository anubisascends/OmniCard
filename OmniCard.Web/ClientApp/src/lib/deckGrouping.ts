import type { CardDto } from '../api/types';

// Reserved tag (matches the server's DeckCardClassifier.CommanderTag) marking a card as the deck's
// commander/leader. A deck may have more than one (partners, backgrounds, etc.).
export const COMMANDER_TAG = 'commander';

export interface DeckGroup {
  /** Stable key for React lists. */
  key: string;
  /** Display heading (the primary type, or "Commander"). */
  heading: string;
  /** Total copies in the group (sum of quantities). */
  count: number;
  cards: CardDto[];
}

export function isCommander(card: CardDto): boolean {
  return card.tags.some((t) => t.toLowerCase() === COMMANDER_TAG);
}

/** Sum of copies across the cards (each row's quantity). This is the whole-deck total, so for a
 * commander deck it includes every commander plus the main deck. */
export function totalDeckCards(cards: CardDto[]): number {
  return cards.reduce((sum, c) => sum + Math.max(1, c.quantity), 0);
}

// Magic type-line main types, in the precedence used to bucket a multi-type card (e.g. an
// "Artifact Creature" groups under Creature). Checked first so Magic decks get familiar buckets.
const MTG_TYPES = [
  'creature',
  'planeswalker',
  'battle',
  'instant',
  'sorcery',
  'artifact',
  'enchantment',
  'land',
] as const;

// For non-Magic games the card type is usually a short phrase; collapse the common multi-word forms
// (e.g. Yu-Gi-Oh "Effect Monster" → Monster, Pokémon "Basic Pokémon" → Pokémon) to a single bucket.
const KEYWORD_BUCKETS: [needle: string, label: string][] = [
  ['pokémon', 'Pokémon'],
  ['pokemon', 'Pokémon'],
  ['trainer', 'Trainer'],
  ['energy', 'Energy'],
  ['monster', 'Monster'],
  ['spell', 'Spell'],
  ['trap', 'Trap'],
];

const cap = (s: string) => (s.length === 0 ? s : s[0].toUpperCase() + s.slice(1));
const titleCase = (s: string) =>
  s.replace(/\w\S*/g, (w) => w[0].toUpperCase() + w.slice(1).toLowerCase());

/**
 * Reduces a card's type string to a single game-agnostic grouping heading. Strips the subtype (the
 * part after an em/en dash or bullet), then: Magic main types win first; otherwise common non-Magic
 * multi-word types collapse to a keyword bucket; otherwise the leading main-type phrase is used.
 * Empty/unknown types fall in "Other".
 */
export function primaryType(cardType?: string | null): string {
  const raw = (cardType ?? '').trim();
  if (raw.length === 0) return 'Other';

  // Keep only the main-types portion (before the subtype separator).
  const main = raw.split(/[—–•·]| - /)[0].trim();
  const lower = main.toLowerCase();

  for (const t of MTG_TYPES) if (lower.includes(t)) return cap(t);
  for (const [needle, label] of KEYWORD_BUCKETS) if (lower.includes(needle)) return label;

  return main.length > 0 ? titleCase(main) : 'Other';
}

// Group display order: Commander pinned first, then the Magic types in play order, then any other
// (non-Magic) type alphabetically, with "Other" pinned last.
const TYPE_ORDER: Record<string, number> = {
  Commander: 0,
  Creature: 1,
  Planeswalker: 2,
  Battle: 3,
  Instant: 4,
  Sorcery: 5,
  Artifact: 6,
  Enchantment: 7,
  Land: 8,
  Other: 100,
};
const orderOf = (heading: string) => TYPE_ORDER[heading] ?? 50;

export type DeckGroupMode = 'type' | 'tag';

/** Heading for the group that holds cards with no tags (used in tag mode). */
export const UNTAGGED_HEADING = 'Untagged';

const byName = (a: CardDto, b: CardDto) =>
  a.name.localeCompare(b.name, undefined, { sensitivity: 'base' });

/** Groups a deck's cards for the stacked view by the chosen mode. */
export function groupDeckCards(cards: CardDto[], mode: DeckGroupMode = 'type'): DeckGroup[] {
  return mode === 'tag' ? groupByTag(cards) : groupByType(cards);
}

/**
 * Groups by primary card type: a "Commander" group (any card tagged commander) first, then a group
 * per primary type. Ordered by TYPE_ORDER then alphabetically; cards within a group sorted by name.
 */
function groupByType(cards: CardDto[]): DeckGroup[] {
  const byHeading = new Map<string, CardDto[]>();
  for (const card of cards) {
    const heading = isCommander(card) ? 'Commander' : primaryType(card.cardType);
    (byHeading.get(heading) ?? byHeading.set(heading, []).get(heading)!).push(card);
  }

  return [...byHeading.entries()]
    .map(([heading, groupCards]) => ({
      key: heading,
      heading,
      count: totalDeckCards(groupCards),
      cards: [...groupCards].sort(byName),
    }))
    .sort((a, b) => orderOf(a.heading) - orderOf(b.heading) || a.heading.localeCompare(b.heading));
}

/**
 * Groups by tag: one group per distinct tag (a card with several tags appears under each), plus an
 * "Untagged" group (pinned last) for cards with no tags. Tag groups are alphabetical. Group counts
 * can overlap across tags; the view's overall total is computed separately from the raw card list.
 */
function groupByTag(cards: CardDto[]): DeckGroup[] {
  // key = lowercased tag (for case-insensitive dedup); value keeps the first-seen display casing.
  const byTag = new Map<string, { heading: string; cards: CardDto[] }>();
  const untagged: CardDto[] = [];

  for (const card of cards) {
    if (card.tags.length === 0) {
      untagged.push(card);
      continue;
    }
    for (const tag of card.tags) {
      const key = tag.toLowerCase();
      const entry = byTag.get(key) ?? byTag.set(key, { heading: tag, cards: [] }).get(key)!;
      entry.cards.push(card);
    }
  }

  const groups: DeckGroup[] = [...byTag.entries()]
    .map(([key, v]) => ({
      key: `tag:${key}`,
      heading: v.heading,
      count: totalDeckCards(v.cards),
      cards: [...v.cards].sort(byName),
    }))
    .sort((a, b) => a.heading.localeCompare(b.heading, undefined, { sensitivity: 'base' }));

  if (untagged.length > 0)
    groups.push({
      key: 'tag:__untagged__',
      heading: UNTAGGED_HEADING,
      count: totalDeckCards(untagged),
      cards: [...untagged].sort(byName),
    });

  return groups;
}
