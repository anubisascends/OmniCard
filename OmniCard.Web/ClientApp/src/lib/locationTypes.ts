// The user-creatable container types (ContainerType minus the system-only Bulk). Shared by the
// Locations page's add bar and the move picker's inline create section so the list stays in one place.
// `value` is the ContainerType enum name the API expects on create; `label` is the display text.
export const LOCATION_TYPES = [
  { value: 'Binder', label: 'Binder' },
  { value: 'Box', label: 'Box' },
  { value: 'DeckBox', label: 'Deck Box' },
  { value: 'DisplayCase', label: 'Display Case' },
] as const;

export type LocationTypeValue = (typeof LOCATION_TYPES)[number]['value'];

/** True for the deck-box type, which alone carries a game system + deck type. */
export const isDeckBoxType = (type: string) => type === 'DeckBox';
