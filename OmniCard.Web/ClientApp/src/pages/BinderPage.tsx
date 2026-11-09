import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { TFunction } from 'i18next';
import { useParams, Link as RouterLink } from 'react-router-dom';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Breadcrumbs,
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  FormControlLabel,
  InputAdornment,
  Link,
  Menu,
  MenuItem,
  Paper,
  Popover,
  Snackbar,
  Stack,
  Switch,
  Tab,
  Tabs,
  TextField,
  Tooltip,
  Typography,
  alpha,
  type SxProps,
  type Theme,
  IconButton,
} from '@mui/material';
import EditIcon from '@mui/icons-material/Edit';
import AddIcon from '@mui/icons-material/Add';
import SellIcon from '@mui/icons-material/Sell';
import InfoOutlinedIcon from '@mui/icons-material/InfoOutlined';
import FilterListIcon from '@mui/icons-material/FilterList';
import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutline';
import BookmarkAddIcon from '@mui/icons-material/BookmarkAdd';
import EditOutlinedIcon from '@mui/icons-material/EditOutlined';
import LowPriorityIcon from '@mui/icons-material/LowPriority';
import KeyboardIcon from '@mui/icons-material/Keyboard';
import { api } from '../api/client';
import { usePermissions } from '../context/usePermissions';
import type { BinderCardDto, BinderDividerDto, BinderSlotDto, SetInfoDto } from '../api/types';
import { CardImage } from '../components/CardImage';
import { ListForSaleDialog } from '../components/dialogs/ListForSaleDialog';
import { CardEditDrawer } from '../components/dialogs/CardEditDrawer';
import { AddCardDialog, type AddCardFoilRule } from '../components/dialogs/AddCardDialog';
import { SearchBox } from '../components/SearchBox';
import { SetMultiSelect } from '../components/SetMultiSelect';
import { useDefaultSets } from '../components/locationCardFilter';
import { Kbd } from '../components/Kbd';
import { BinderDividerDialog, BinderDividerMarker, BinderDividerTabs, sectionOf } from '../components/BinderDividers';
import { useFormatters } from '../i18n/format';

const CARD_BACK_SLUGS = ['mtg', 'optcg', 'riftbound', 'pokemon', 'yugioh', 'fftcg'];
const cardBackSrc = (game: number): string | undefined => {
  const slug = CARD_BACK_SLUGS[game];
  return slug ? `/img/card-back-${slug}.png` : undefined;
};

// CardGame enum names by ordinal (matches OmniCard.Shared.Cards.CardGame) — used to seed the
// Add-card dialog's game selector from a card already in the binder.
const GAME_NAMES = ['Mtg', 'OnePiece', 'Riftbound', 'Pokemon', 'YuGiOh', 'FinalFantasy'];
const gameName = (game?: number | null): string =>
  (game != null && GAME_NAMES[game]) || 'Mtg';

// Friendly labels for the SalesChannel enum names sent by the API, via the shared common.channels.* keys.
const channelLabel = (c: string | null | undefined, t: TFunction): string =>
  c ? t(`common.channels.${c}`, { defaultValue: c }) : '';
// Picked cards are further along the sale pipeline than merely Listed — warn vs. info.
const statusColor = (status?: string | null): string => (status === 'Picked' ? 'warning.main' : 'info.main');

// Shared look for the overlay price/status pills: a solid, high-contrast chip that stays legible on
// top of any card art (light or dark), rather than plain text over a gradient.
const PILL_SX = {
  px: 0.6,
  py: '2px',
  borderRadius: 0.75,
  fontSize: 11,
  fontWeight: 700,
  lineHeight: 1.35,
  letterSpacing: 0.2,
  color: '#fff',
  boxShadow: 2,
  pointerEvents: 'none',
  whiteSpace: 'nowrap',
  display: 'flex',
  alignItems: 'center',
  gap: 0.25,
} as const;

/** Overlay badges drawn on top of a placed card: a sale-status pill (top-left), the market price
 * (bottom-left) and, when listed, the channel + listed price (bottom-right). Each is a solid pill so
 * it reads clearly over any art. `pointer-events: none` so drag / click still reach the card. */
function CardOverlayBadges({ card }: { card: BinderCardDto }) {
  const { t } = useTranslation();
  const { money } = useFormatters();
  const listed = card.listingStatus;
  return (
    <>
      {listed && (
        <Box sx={{ ...PILL_SX, position: 'absolute', top: 3, left: 3, bgcolor: statusColor(listed) }}>
          <SellIcon sx={{ fontSize: 12 }} />
          {listed === 'Picked' ? t('binder.statusPicked') : t('binder.statusListed')}
        </Box>
      )}
      {card.marketPriceRaw > 0 && (
        <Box
          title={t('binder.marketPriceTitle')}
          sx={{ ...PILL_SX, position: 'absolute', bottom: 3, left: 3, bgcolor: 'rgba(17,17,17,0.82)' }}
        >
          {money(card.marketPriceRaw)}
        </Box>
      )}
      {listed && card.listedPriceRaw != null && card.listedPriceRaw > 0 && (
        <Box
          title={t('binder.listedPriceTitle')}
          sx={{ ...PILL_SX, position: 'absolute', bottom: 3, right: 3, bgcolor: statusColor(listed) }}
        >
          {channelLabel(card.listingChannel, t)} {money(card.listedPriceRaw)}
        </Box>
      )}
    </>
  );
}

// Layout presets (slots-per-page → columns) offered in edit mode.
const LAYOUTS = [
  { slots: 4, cols: 2 },
  { slots: 9, cols: 3 },
  { slots: 12, cols: 3 },
  { slots: 16, cols: 4 },
];

function ReverseCardBack({ game }: { game: number }) {
  const { t } = useTranslation();
  const src = cardBackSrc(game);
  return (
    <Tooltip title={t('binder.reverseSideTooltip')}>
      <Box
        sx={{
          width: '100%',
          height: '100%',
          display: 'grid',
          placeItems: 'center',
          background: (t) =>
            `repeating-linear-gradient(135deg, ${t.palette.action.hover}, ${t.palette.action.hover} 6px, ${t.palette.action.selected} 6px, ${t.palette.action.selected} 12px)`,
        }}
      >
        {src && (
          <img
            src={src}
            alt=""
            style={{ width: '100%', height: '100%', objectFit: 'contain', opacity: 0.85 }}
            onError={(e) => e.currentTarget.remove()}
          />
        )}
      </Box>
    </Tooltip>
  );
}

function SlotGrid({
  slots,
  columns,
  pageNumber,
  editMode,
  onDragCard,
  onDropSlot,
  onSlotContextMenu,
  onCardClick,
  cursorSlot,
  onSlotSelect,
  onSlotOpen,
  heldSlot,
  onSlotPointer,
}: {
  slots: BinderSlotDto[];
  columns: number;
  pageNumber: number;
  editMode: boolean;
  onDragCard: (lotId: number) => void;
  onDropSlot: (page: number, slot: number) => void;
  onSlotContextMenu: (e: React.MouseEvent, page: number, slot: number, card: BinderCardDto | null) => void;
  onCardClick: (card: BinderCardDto) => void;
  /** Simplified editing: the pocket under the keyboard cursor. */
  cursorSlot?: number;
  /** Simplified editing: a click moves the cursor (the caller also opens a card's details)… */
  onSlotSelect?: (slot: number) => void;
  /** …and a double-click on an empty pocket searches for its card. */
  onSlotOpen?: (slot: number) => void;
  /** Spread view: the pocket whose card is being moved from the keyboard (drawn faded). */
  heldSlot?: number;
  /** Spread view: a press on a pocket puts the keyboard cursor there. */
  onSlotPointer?: (slot: number) => void;
}) {
  const { t } = useTranslation();
  return (
    <Paper variant="outlined" sx={{ p: 1, flex: 1 }}>
      <Typography variant="caption" color="text.secondary">
        {t('binder.page', { page: pageNumber })}
      </Typography>
      <Box sx={{ display: 'grid', gap: onSlotSelect ? 1 : 0.5, gridTemplateColumns: `repeat(${columns}, 1fr)`, mt: 0.5 }}>
        {slots.map((s) => (
          <Box
            key={s.slotIndex}
            data-pocket={`${pageNumber}-${s.slotIndex}`}
            onMouseDown={onSlotPointer ? () => onSlotPointer(s.slotIndex) : undefined}
            onDragOver={editMode ? (e) => e.preventDefault() : undefined}
            onDrop={editMode ? () => onDropSlot(pageNumber, s.slotIndex) : undefined}
            onContextMenu={(e) => onSlotContextMenu(e, pageNumber, s.slotIndex, s.card ?? null)}
            onClick={onSlotSelect ? () => onSlotSelect(s.slotIndex) : undefined}
            onDoubleClick={onSlotOpen && !s.card ? () => onSlotOpen(s.slotIndex) : undefined}
            sx={{
              position: 'relative',
              // Scrolled to by the keyboard cursor: stay clear of the fixed app bar (and a phone's bottom bar).
              scrollMarginTop: 72,
              scrollMarginBottom: { xs: 80, sm: 16 },
              aspectRatio: '0.72',
              border: '1px dashed',
              borderColor: editMode ? 'primary.light' : 'divider',
              borderRadius: 1,
              overflow: 'hidden',
              bgcolor: 'action.hover',
              display: 'grid',
              placeItems: 'center',
              cursor: onSlotSelect ? 'pointer' : s.card ? 'default' : 'context-menu',
              ...(cursorSlot === s.slotIndex && {
                outline: '3px solid',
                outlineColor: 'primary.main',
                outlineOffset: 2,
                boxShadow: (th: Theme) => `0 0 0 7px ${alpha(th.palette.primary.main, 0.25)}`,
              }),
              ...(heldSlot === s.slotIndex && {
                opacity: 0.45,
                borderStyle: 'solid',
                borderWidth: 2,
                borderColor: 'warning.main',
              }),
            }}
          >
            {s.card?.imageUrl ? (
              <>
                <Tooltip
                  title={t(onSlotSelect ? 'binder.slotTooltipSimplified' : 'binder.slotTooltip', {
                    name: s.card.name,
                    condition: s.card.condition,
                    foilSuffix: s.card.foil ? t('binder.foilSuffix') : '',
                  })}
                >
                  <CardImage
                    src={s.card.imageUrl}
                    alt={s.card.name}
                    foil={s.card.foil}
                    draggable={editMode}
                    onDragStart={editMode ? () => onDragCard(s.card!.id) : undefined}
                    onClick={onSlotSelect ? undefined : () => onCardClick(s.card!)}
                    wrapperSx={{ width: '100%', height: '100%' }}
                    sx={{
                      width: '100%',
                      height: '100%',
                      objectFit: 'contain',
                      cursor: editMode ? 'grab' : 'pointer',
                    }}
                  />
                </Tooltip>
                <CardOverlayBadges card={s.card} />
              </>
            ) : s.reverseGame != null ? (
              <ReverseCardBack game={s.reverseGame} />
            ) : null}
          </Box>
        ))}
      </Box>
    </Paper>
  );
}

/** Left-hand pool of cards in this binder that aren't placed in a slot. Search it with Scryfall
 * syntax (set:, cn:, t:, c:, tag:, or plain name) — the filter is applied server-side. Drag a card
 * onto a slot to place it; drop a placed card back onto this sidebar to unplace it. */
function UnplacedSidebar({
  cards,
  loading,
  filter,
  onFilter,
  onDragCard,
  onDropUnassign,
  onCardContextMenu,
  onCardClick,
  onCardPickUp,
  onLeave,
  autoFocus = true,
  sx,
}: {
  cards: BinderCardDto[];
  loading: boolean;
  filter: string;
  onFilter: (v: string) => void;
  onDragCard: (lotId: number) => void;
  onDropUnassign: () => void;
  onCardContextMenu: (e: React.MouseEvent, card: BinderCardDto) => void;
  onCardClick: (card: BinderCardDto) => void;
  /** Keyboard placing (spread view): Space on a card picks it up to drop into a pocket. */
  onCardPickUp?: (card: BinderCardDto) => void;
  /** Esc in the list: back to the pages. */
  onLeave?: () => void;
  /** Focus the search box on mount (not in simplified editing, where the keyboard drives the pockets). */
  autoFocus?: boolean;
  /** Placement overrides (simplified editing docks the list in the page's left margin). */
  sx?: SxProps<Theme>;
}) {
  const { t } = useTranslation();
  const { money } = useFormatters();
  // The list's keyboard: ↑ ↓ between cards (↑ from the first one back to the search box), Enter for a
  // card's details, Space to pick it up for a pocket, Shift+F10 for its menu, Esc back to the pages.
  const onRowKey = (e: React.KeyboardEvent<HTMLElement>, card: BinderCardDto) => {
    const row = e.currentTarget;
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault();
      const sibling = (e.key === 'ArrowDown' ? row.nextElementSibling : row.previousElementSibling) as HTMLElement | null;
      if (sibling) sibling.focus();
      else if (e.key === 'ArrowUp') row.closest('[data-unplaced]')?.querySelector<HTMLInputElement>('input')?.focus();
    } else if (e.key === 'Enter') {
      e.preventDefault();
      onCardClick(card);
    } else if (e.key === ' ' && onCardPickUp) {
      e.preventDefault();
      onCardPickUp(card);
    } else if (e.key === 'ContextMenu' || (e.key === 'F10' && e.shiftKey)) {
      e.preventDefault();
      const r = row.getBoundingClientRect();
      onCardContextMenu({ preventDefault: () => {}, clientX: r.left + 24, clientY: r.top + r.height / 2 } as React.MouseEvent, card);
    }
  };
  return (
    <Paper
      variant="outlined"
      data-unplaced
      onDragOver={(e) => e.preventDefault()}
      onDrop={onDropUnassign}
      onKeyDown={(e) => {
        if (e.key === 'Escape' && onLeave && !e.defaultPrevented) {
          e.preventDefault();
          onLeave();
        } else if (e.key === 'ArrowDown' && e.target instanceof HTMLInputElement) {
          // From the search box down into the cards.
          const first = e.currentTarget.querySelector<HTMLElement>('[data-unplaced-card]');
          if (first) {
            e.preventDefault();
            first.focus();
          }
        }
      }}
      sx={[
        {
          width: { xs: '100%', sm: 300 },
          flexShrink: 0,
          alignSelf: 'flex-start',
          position: { xs: 'static', sm: 'sticky' },
          top: 8,
          maxHeight: { xs: 360, sm: 'calc(100vh - 32px)' },
          boxSizing: 'border-box',
          display: 'flex',
          flexDirection: 'column',
          p: 1,
          bgcolor: 'background.default',
        },
        ...(Array.isArray(sx) ? sx : [sx]),
      ]}
    >
      <Stack direction="row" spacing={1} alignItems="center" sx={{ mb: 1 }}>
        <Typography variant="subtitle2" sx={{ flexGrow: 1 }}>
          {t('binder.unplacedCards', { count: cards.length })}
        </Typography>
        {loading && <CircularProgress size={14} />}
      </Stack>
      <Box sx={{ mb: 0.5 }}>
        <SearchBox value={filter} onChange={onFilter} autoFocus={autoFocus} />
      </Box>
      <Typography variant="caption" color="text.secondary" sx={{ mb: 1 }}>
        {t('binder.dragHint')}
      </Typography>
      <Box sx={{ overflowY: 'auto', flexGrow: 1 }}>
        {cards.length === 0 ? (
          <Typography variant="body2" color="text.secondary" sx={{ px: 0.5, py: 1 }}>
            {filter.trim() ? t('binder.noCardsMatch') : t('binder.allPlaced')}
          </Typography>
        ) : (
          <Stack spacing={0.5}>
            {cards.map((c) => (
              <Stack
                key={c.id}
                direction="row"
                spacing={1}
                alignItems="center"
                draggable
                tabIndex={0}
                data-unplaced-card
                onDragStart={() => onDragCard(c.id)}
                onClick={() => onCardClick(c)}
                onContextMenu={(e) => onCardContextMenu(e, c)}
                onKeyDown={(e) => onRowKey(e, c)}
                sx={{
                  p: 0.5,
                  borderRadius: 1,
                  cursor: 'grab',
                  '&:hover': { bgcolor: 'action.hover' },
                  '&:focus-visible': { outline: '2px solid', outlineColor: 'primary.main', bgcolor: 'action.selected' },
                }}
              >
                <CardImage
                  src={c.imageUrl ?? undefined}
                  alt={c.name}
                  foil={c.foil}
                  wrapperSx={{ flexShrink: 0, borderRadius: 0.5, overflow: 'hidden' }}
                  sx={{ width: 40, aspectRatio: '0.72', objectFit: 'contain', display: 'block' }}
                />
                <Box sx={{ minWidth: 0 }}>
                  <Typography variant="body2" noWrap>
                    {c.name}
                  </Typography>
                  <Typography variant="caption" color="text.secondary" noWrap sx={{ display: 'block' }}>
                    {t('binder.sidebarCardMeta', {
                      setCode: c.setCode,
                      number: c.number,
                      condition: c.condition,
                      foilSuffix: c.foil ? t('binder.foilSuffix') : '',
                    })}
                  </Typography>
                  <Stack direction="row" spacing={0.5} alignItems="center" sx={{ mt: 0.25 }}>
                    {c.marketPriceRaw > 0 && (
                      <Typography variant="caption" color="text.secondary" noWrap>
                        {money(c.marketPriceRaw)}
                      </Typography>
                    )}
                    {c.listingStatus && (
                      <Typography
                        variant="caption"
                        noWrap
                        sx={{ fontWeight: 700, color: statusColor(c.listingStatus) }}
                      >
                        {c.listingStatus === 'Picked' ? t('binder.statusPicked') : t('binder.statusListed')}
                        {c.listedPriceRaw != null && c.listedPriceRaw > 0
                          ? ` · ${channelLabel(c.listingChannel, t)} ${money(c.listedPriceRaw)}`
                          : ''}
                      </Typography>
                    )}
                  </Stack>
                </Box>
              </Stack>
            ))}
          </Stack>
        )}
      </Box>
    </Paper>
  );
}

// Simplified editing: one page at a time, driven by the keyboard. The switch is remembered per browser.
const SIMPLIFIED_KEY = 'omnicard.binder.simplified';
const readSimplified = () => {
  try {
    return localStorage.getItem(SIMPLIFIED_KEY) === '1';
  } catch {
    return false;
  }
};
const writeSimplified = (on: boolean) => {
  try {
    localStorage.setItem(SIMPLIFIED_KEY, on ? '1' : '0');
  } catch {
    // Storage blocked — the switch just isn't remembered.
  }
};

// The foil rule ("cards matching this search are foil") belongs to the binder, so it's remembered per
// binder in this browser.
const foilRuleKey = (binderId: number) => `omnicard.binder.foilRule.${binderId}`;
const readFoilRule = (binderId: number): AddCardFoilRule => {
  try {
    const saved = JSON.parse(localStorage.getItem(foilRuleKey(binderId)) ?? 'null');
    if (saved && typeof saved.query === 'string')
      return { query: saved.query, foilType: typeof saved.foilType === 'string' ? saved.foilType : null };
  } catch {
    // Unreadable or blocked storage — start without a rule.
  }
  return { query: '', foilType: null };
};
const writeFoilRule = (binderId: number, rule: AddCardFoilRule) => {
  try {
    if (rule.query.trim() || rule.foilType) localStorage.setItem(foilRuleKey(binderId), JSON.stringify(rule));
    else localStorage.removeItem(foilRuleKey(binderId));
  } catch {
    // Storage blocked — the rule just isn't remembered.
  }
};

/** What Ctrl+C on a pocket puts on the clipboard: the lot to copy, plus enough to say what it is. */
type CopiedCard = {
  kind: 'omnicard/binder-card';
  lotId: number;
  name: string;
  setCode: string;
  number: string;
};
const COPIED_KIND = 'omnicard/binder-card';
const parseCopied = (text: string): CopiedCard | null => {
  try {
    const v = JSON.parse(text);
    return v && v.kind === COPIED_KIND && typeof v.lotId === 'number' ? (v as CopiedCard) : null;
  } catch {
    return null;
  }
};

/** Page 1 stands alone on spread 0; after that pages pair up two per spread (2–3, 4–5, …). */
const spreadOfPage = (page: number) => Math.floor(page / 2);
const firstPageOfSpread = (spread: number) => (spread === 0 ? 1 : spread * 2);

/** Where a page sits physically: its sheet (0-based), the sheet's first page and side count, and which
 *  side the page is — `only` for a single-sided sheet. Null when the page is past the end. */
const sheetOfPage = (sheetSides: number[], page: number) => {
  let first = 1;
  for (let index = 0; index < sheetSides.length; index++) {
    const sides = sheetSides[index];
    if (page < first + sides) {
      const side: 'front' | 'back' | 'only' = sides === 1 ? 'only' : page === first ? 'front' : 'back';
      return { index, first, sides, side };
    }
    first += sides;
  }
  return null;
};

type Cursor = { page: number; slot: number };

/** The simplified editor's keys, as [keys, i18n key of what they do]. */
const SIMPLIFIED_KEYS: [string[], string][] = [
  [['←', '↑', '↓', '→'], 'binder.keys.move'],
  [['PgUp', 'PgDn'], 'binder.keys.page'],
  [['Home', 'End'], 'binder.keys.firstLast'],
  [['Ctrl+Home', 'Ctrl+End'], 'binder.keys.firstLastPage'],
  [['Alt+PgUp', 'Alt+PgDn'], 'binder.keys.dividers'],
  [['Enter'], 'binder.keys.search'],
  [['A–Z'], 'binder.keys.type'],
  [['F2'], 'binder.keys.sets'],
  [['Del'], 'binder.keys.remove'],
  [['Ctrl+C'], 'binder.keys.copy'],
  [['Ctrl+V'], 'binder.keys.paste'],
  [['+'], 'binder.keys.addPage'],
  [['−'], 'binder.keys.removePage'],
];

/** The spread view's keys, as [keys, i18n key, only while editing]. */
const SPREAD_KEYS: [string[], string, boolean][] = [
  [['←', '↑', '↓', '→'], 'binder.navKeys.move', false],
  [['PgUp', 'PgDn'], 'binder.navKeys.spread', false],
  [['Home', 'End'], 'binder.keys.firstLast', false],
  [['Ctrl+Home', 'Ctrl+End'], 'binder.keys.firstLastPage', false],
  [['Alt+PgUp', 'Alt+PgDn'], 'binder.keys.dividers', false],
  [['Enter'], 'binder.navKeys.details', false],
  [['Shift+F10'], 'binder.navKeys.menu', false],
  [['Tab', 'Shift+Tab'], 'binder.navKeys.tab', false],
  [['Space'], 'binder.navKeys.pickUp', true],
  [['Esc'], 'binder.navKeys.cancel', true],
  [['Del'], 'binder.keys.remove', true],
  [['F2'], 'binder.keys.sets', true],
  [['?'], 'binder.navKeys.help', false],
];

/** The pages a spread shows: page 1 alone on spread 0, then pairs (the last may be alone). */
const pagesOfSpread = (spread: number, totalPages: number) =>
  (spread === 0 ? [1] : [spread * 2, spread * 2 + 1]).filter((p) => p <= totalPages);

/** Keys typed into these belong to them, not to the binder cursor. */
const EDITABLE = 'input, textarea, select, [contenteditable="true"], [role="combobox"], [role="menu"], [role="listbox"], [role="dialog"]';
/** Enter/Space on a focused control presses it. */
const PRESSABLE = 'button, a, [role="button"], [role="tab"]';

export function BinderPage() {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const { can } = usePermissions();
  const { id } = useParams();
  const binderId = Number(id);
  const qc = useQueryClient();
  const [spread, setSpread] = useState(0);
  const [editMode, setEditMode] = useState(false);
  const [simplified, setSimplified] = useState(readSimplified);
  const [cursor, setCursor] = useState<Cursor>({ page: 1, slot: 0 });
  // Simplified editing: the page − asked to remove, awaiting confirmation.
  const [confirmRemove, setConfirmRemove] = useState<number | null>(null);
  // Simplified editing: why a key did nothing (+ on a sheet's front); cleared when the cursor moves.
  const [notice, setNotice] = useState<string | null>(null);
  const gridRef = useRef<HTMLDivElement>(null);
  // Simplified editing shows the cursor's page alone; the spread follows it.
  const quick = editMode && simplified;
  const viewSpread = quick ? spreadOfPage(cursor.page) : spread;
  const [dragLotId, setDragLotId] = useState<number | null>(null);
  const [addAnchor, setAddAnchor] = useState<null | HTMLElement>(null);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState('');
  const [debouncedFilter, setDebouncedFilter] = useState('');
  // Context menu for a card in the Unplaced sidebar (details / list for sale).
  const [cardMenu, setCardMenu] = useState<{ x: number; y: number; card: BinderCardDto } | null>(null);
  // Context menu for a binder pocket (add card + occupant actions).
  const [slotMenu, setSlotMenu] = useState<{ x: number; y: number; page: number; slot: number; card: BinderCardDto | null } | null>(null);
  const [listCard, setListCard] = useState<BinderCardDto | null>(null);
  const [detailCardId, setDetailCardId] = useState<number | null>(null);
  // An unplaced card asked to be deleted from the collection, awaiting confirmation.
  const [deleteCard, setDeleteCard] = useState<BinderCardDto | null>(null);
  // `quick`: opened from the simplified editor (catalog-only search, cursor advances after the add);
  // `initialName`: the keystroke(s) that opened it.
  const [addSlot, setAddSlot] = useState<{
    page: number;
    slot: number;
    card: BinderCardDto | null;
    quick?: boolean;
    initialName?: string;
  } | null>(null);
  // The sets card searches start limited to. They start on the binder's default sets (seeded below) and a
  // change lasts for this sitting only. `filterGame` is null until chosen (the filter then follows the game
  // of the cards on screen), unless the binder is fixed to a game.
  const [filterGame, setFilterGame] = useState<string | null>(null);
  const [filterSets, setFilterSets] = useState<SetInfoDto[]>([]);
  const [setsAnchor, setSetsAnchor] = useState<HTMLElement | null>(null);
  const setsButtonRef = useRef<HTMLButtonElement>(null);
  const games = useQuery({ queryKey: ['games'], queryFn: api.games, enabled: editMode });
  // The binder's foil rule, handed to the Add card dialog.
  const [foilRule, setFoilRule] = useState<AddCardFoilRule>(() => readFoilRule(binderId));
  useEffect(() => setFoilRule(readFoilRule(binderId)), [binderId]);
  const updateFoilRule = (patch: Partial<AddCardFoilRule>) => {
    const next = { ...foilRule, ...patch };
    setFoilRule(next);
    writeFoilRule(binderId, next);
  };
  // Simplified editing: the card Ctrl+C last copied (it's on the clipboard too; Ctrl+V reads it from there).
  const [copied, setCopied] = useState<CopiedCard | null>(null);
  // Dividers: the add / edit dialog, a tab's right-click menu, the one waiting on a remove confirmation,
  // the one just removed (for Undo), and the tab last clicked — when another divider shares its spread /
  // page, the clicked one is the one highlighted while that view is on screen.
  const [dividerDialog, setDividerDialog] = useState<
    { mode: 'add'; page: number } | { mode: 'edit'; divider: BinderDividerDto } | null
  >(null);
  const [dividerMenu, setDividerMenu] = useState<{ x: number; y: number; divider: BinderDividerDto } | null>(null);
  const [confirmRemoveDivider, setConfirmRemoveDivider] = useState<BinderDividerDto | null>(null);
  const [removedDivider, setRemovedDivider] = useState<BinderDividerDto | null>(null);
  const [pinnedDivider, setPinnedDivider] = useState<{ id: number; view: string } | null>(null);
  // The spread view's keyboard: a pocket cursor across both pages (`navCursor`, drawn only while the keyboard
  // is in use and on the pages), a card picked up with Space to drop into another pocket (`held`; `from` is
  // null for one picked from Unplaced), and the key list (?).
  const spreadRef = useRef<HTMLDivElement>(null);
  const [navCursor, setNavCursor] = useState<Cursor>({ page: 1, slot: 0 });
  const [navKeys, setNavKeys] = useState(false);
  const [navFocus, setNavFocus] = useState(false);
  const [held, setHeld] = useState<{ lotId: number; name: string; from: Cursor | null } | null>(null);
  const [showKeys, setShowKeys] = useState(false);

  const openCardMenu = (e: React.MouseEvent, card: BinderCardDto) => {
    e.preventDefault();
    setCardMenu({ x: e.clientX, y: e.clientY, card });
  };
  const openSlotMenu = (e: React.MouseEvent, page: number, slot: number, card: BinderCardDto | null) => {
    e.preventDefault();
    if (quick) setCursor({ page, slot });
    setSlotMenu({ x: e.clientX, y: e.clientY, page, slot, card });
  };

  // Debounce the search box so each keystroke doesn't hit the server; keeps type-ahead snappy.
  useEffect(() => {
    const t = setTimeout(() => setDebouncedFilter(filter.trim()), 200);
    return () => clearTimeout(t);
  }, [filter]);

  const { data, isLoading, isFetching } = useQuery({
    queryKey: ['binder', binderId, viewSpread],
    queryFn: () => api.binder(binderId, viewSpread),
    placeholderData: keepPreviousData,
  });
  // Site access: a binder in a site the user can only view can be flipped through but not edited.
  const locQuery = useQuery({ queryKey: ['location', binderId], queryFn: () => api.location(binderId) });
  const canWrite = locQuery.data?.canWrite !== false;
  // A binder fixed to a game keeps the set filter (and the add dialog) on that game, and the filter starts
  // on the binder's default sets. Otherwise, without a chosen game, the set filter (and the foil rule's
  // finishes) are for the game of the cards on screen.
  const locGame = locQuery.data?.game ?? null;
  const locSets = useDefaultSets(locGame, locQuery.data?.defaultSets);
  const seededFilterFor = useRef<number | null>(null);
  useEffect(() => {
    if (!locQuery.data || !locSets.ready || seededFilterFor.current === binderId) return;
    seededFilterFor.current = binderId;
    setFilterGame(locGame);
    setFilterSets(locSets.sets);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [binderId, locQuery.data, locSets.ready]);
  const spreadGame = gameName(data?.leftSlots.concat(data.rightSlots).find((s) => s.card)?.card?.game);
  const setsGame = locGame ?? filterGame ?? spreadGame;
  const foilTypes = useQuery({
    queryKey: ['scan-foil-types', setsGame],
    queryFn: () => api.scanFoilTypes(setsGame),
    enabled: editMode,
  });
  const unplaced = useQuery({
    queryKey: ['binder-unplaced', binderId, debouncedFilter],
    queryFn: () => api.binderUnplaced(binderId, debouncedFilter || undefined),
    enabled: editMode,
    placeholderData: keepPreviousData,
  });

  const refresh = () => {
    qc.invalidateQueries({ queryKey: ['binder', binderId] });
    qc.invalidateQueries({ queryKey: ['binder-unplaced', binderId] });
    setDragLotId(null);
  };
  const run = (p: Promise<unknown>) => {
    setError(null);
    p.then(refresh).catch((e) => setError((e as Error).message));
  };

  const assign = useMutation({ mutationFn: (v: { page: number; slot: number }) => api.binderAssign(dragLotId!, binderId, v.page, v.slot) });
  const unassign = useMutation({ mutationFn: () => api.binderUnassign(dragLotId!) });
  const addPage = useMutation({ mutationFn: (mode: 'single' | 'double') => api.binderAddPage(binderId, mode) });
  const removePage = useMutation({ mutationFn: (page: number) => api.binderRemovePage(binderId, page) });
  const layout = useMutation({ mutationFn: (l: { slots: number; cols: number }) => api.binderLayout(binderId, l.slots, l.cols) });

  // Keep the cursor inside the binder when pages are removed or the layout shrinks.
  const totalPages = data?.totalPages ?? 1;
  const slotsPerPage = data?.slotsPerPage ?? 9;
  useEffect(() => {
    if (cursor.page > totalPages || cursor.slot >= slotsPerPage)
      setCursor((c) => ({ page: Math.min(c.page, totalPages), slot: Math.min(c.slot, slotsPerPage - 1) }));
  }, [cursor, totalPages, slotsPerPage]);

  // Turning pages should feel instant: fetch the spreads either side of the cursor's.
  const totalSpreads = data?.totalSpreads ?? 1;
  useEffect(() => {
    for (const s of [viewSpread - 1, viewSpread + 1])
      if (s >= 0 && s < totalSpreads)
        qc.prefetchQuery({ queryKey: ['binder', binderId, s], queryFn: () => api.binder(binderId, s), staleTime: 30_000 });
  }, [viewSpread, totalSpreads, binderId, qc]);

  // Entering simplified editing puts the keyboard on the page (off the switch / Edit button).
  useEffect(() => {
    if (quick) gridRef.current?.focus({ preventScroll: true });
  }, [quick]);

  /** Switch simplified editing on/off, carrying the position across (spread ⇄ its first page). */
  const toggleSimplified = (on: boolean) => {
    setSimplified(on);
    writeSimplified(on);
    if (on) setCursor({ page: firstPageOfSpread(spread), slot: 0 });
    else setSpread(spreadOfPage(cursor.page));
  };
  const toggleEdit = () => {
    if (!editMode && simplified) setCursor({ page: firstPageOfSpread(spread), slot: 0 });
    if (editMode && simplified) setSpread(spreadOfPage(cursor.page));
    setEditMode((v) => !v);
  };

  // The cursor's page, when the loaded spread has it (a page turn shows empty pockets until it arrives).
  const cursorSlots =
    data?.leftPageNumber === cursor.page ? data.leftSlots : data?.rightPageNumber === cursor.page ? data.rightSlots : null;
  const cursorCard = cursorSlots?.[cursor.slot]?.card ?? null;

  const openSearch = (slot: number, initialName?: string) => {
    const card = cursorSlots?.[slot]?.card ?? null;
    setCursor((c) => ({ ...c, slot }));
    setAddSlot({ page: cursor.page, slot, card, quick: true, initialName });
  };
  // The sheet a new one would go in after: the cursor's page, or in the spread view the left page (the
  // gap between facing pages). Only after a sheet's back (or a single-sided sheet) — like slipping a new
  // sheet into a real binder; a front's other side is still the same sheet.
  const sheetSides = data?.sheetSides ?? [];
  const cursorSheet = sheetOfPage(sheetSides, cursor.page);
  const insertAfterPage = quick ? cursor.page : data?.leftPageNumber ?? null;
  const insertAfterSheet = insertAfterPage != null ? sheetOfPage(sheetSides, insertAfterPage) : null;
  const canInsertAfter = insertAfterSheet != null && insertAfterSheet.side !== 'front';

  useEffect(() => setNotice(null), [cursor, quick]);

  /** Insert an empty double-sided sheet after the one `page` is on, and turn to its front. */
  const insertSheetAfter = (page: number) => {
    const sheet = sheetOfPage(sheetSides, page);
    if (!sheet) return;
    const newPage = sheet.first + sheet.sides;
    run(
      (async () => {
        await api.binderInsertPage(binderId, sheet.index + 1, true);
        // Load the new page before moving there, so the cursor isn't clamped back to the old last page.
        await qc.invalidateQueries({ queryKey: ['binder', binderId] });
        await qc.fetchQuery({ queryKey: ['binder', binderId, spreadOfPage(newPage)], queryFn: () => api.binder(binderId, spreadOfPage(newPage)) });
        if (quick) setCursor({ page: newPage, slot: 0 });
        else setSpread(spreadOfPage(newPage));
      })(),
    );
  };

  /** Simplified editing's Last: the last page holding a card, on the pocket after its last card (the next
   *  one to fill) — or the card itself when it's in the page's final pocket. An empty binder: page 1. */
  const lastCursor = (): Cursor => {
    const p = data?.lastPlacedPocket;
    return p ? { page: p.page, slot: Math.min(p.slot + 1, slotsPerPage - 1) } : { page: 1, slot: 0 };
  };
  /** Turn to a pocket from a button, keeping the keyboard on the page. */
  const turnTo = (c: Cursor) => {
    setCursor(c);
    gridRef.current?.focus({ preventScroll: true });
  };

  // Dividers. "Here" is the page a new divider goes in front of: the page on screen in simplified editing,
  // otherwise the right-hand page of the spread (the gap between the facing pages), or the one page
  // showing when there's only one.
  const dividers = data?.dividers ?? [];
  const herePage = quick ? cursor.page : data?.rightPageNumber ?? data?.leftPageNumber ?? 1;
  const viewKey = quick ? `p${cursor.page}` : `s${spread}`;
  // A tab is highlighted only while its divider is on screen: on the page in Simplified editing, otherwise
  // on either page of the spread. Between dividers no tab is highlighted; the header still names the section.
  const visiblePages = quick
    ? [cursor.page]
    : [data?.leftPageNumber, data?.rightPageNumber].filter((p): p is number => p != null);
  const dividersInView = dividers.filter((d) => visiblePages.includes(d.page));
  const activeDivider =
    (pinnedDivider?.view === viewKey ? dividersInView.find((d) => d.id === pinnedDivider.id) : undefined) ??
    dividersInView[dividersInView.length - 1] ??
    null;
  const sectionDivider = sectionOf(dividers, herePage);
  /** Turn to a divider: its page in simplified editing, otherwise the spread it sits in. */
  const goToDivider = (d: BinderDividerDto) => {
    if (quick) turnTo({ page: d.page, slot: 0 });
    else setSpread(spreadOfPage(d.page));
    setPinnedDivider({ id: d.id, view: quick ? `p${d.page}` : `s${spreadOfPage(d.page)}` });
  };
  const closeDividerUi = () => {
    setDividerDialog(null);
    setDividerMenu(null);
    if (quick) requestAnimationFrame(() => gridRef.current?.focus({ preventScroll: true }));
  };
  const cancelRemoveDivider = () => {
    setConfirmRemoveDivider(null);
    closeDividerUi();
  };
  /** Remove a divider once the user has confirmed it (see confirmRemoveDivider). */
  const removeDivider = (d: BinderDividerDto) => {
    run(api.binderDividerRemove(binderId, d.id));
    setRemovedDivider(d);
  };

  /** Next pocket in reading order, on to the next page. */
  const advance = () =>
    setCursor((c) =>
      c.slot + 1 < slotsPerPage ? { ...c, slot: c.slot + 1 } : c.page < totalPages ? { page: c.page + 1, slot: 0 } : c,
    );

  // Simplified editing's keyboard. Re-bound each render so it always sees the current cursor and page.
  useEffect(() => {
    if (!quick || !data) return;
    const onKey = (e: globalThis.KeyboardEvent) => {
      if (e.defaultPrevented) return;
      if (addSlot || detailCardId != null || listCard || slotMenu || cardMenu || addAnchor || setsAnchor || confirmRemove != null || deleteCard || dividerDialog || dividerMenu || confirmRemoveDivider) return;
      const el = e.target instanceof HTMLElement ? e.target : null;
      if (el?.closest(EDITABLE)) return;
      // Ctrl+Home / Ctrl+End: the front of page 1 / the last page with a card.
      if (e.ctrlKey && !e.altKey && !e.metaKey && (e.key === 'Home' || e.key === 'End')) {
        e.preventDefault();
        setCursor(e.key === 'Home' ? { page: 1, slot: 0 } : lastCursor());
        return;
      }
      // Alt+PgUp / Alt+PgDn: the previous / next divider.
      if (e.altKey && !e.ctrlKey && !e.metaKey && (e.key === 'PageUp' || e.key === 'PageDown')) {
        e.preventDefault();
        const target =
          e.key === 'PageDown'
            ? dividers.find((d) => d.page > cursor.page)
            : [...dividers].reverse().find((d) => d.page < cursor.page);
        if (target) goToDivider(target);
        return;
      }
      if (e.ctrlKey || e.altKey || e.metaKey) return;
      const cols = data.columns;
      const col = cursor.slot % cols;
      let next: Cursor | null = null;
      switch (e.key) {
        case 'ArrowRight':
          // Off the right edge onto the next page, same row — like reading across a spread.
          next = col < cols - 1 ? { ...cursor, slot: cursor.slot + 1 } : cursor.page < totalPages ? { page: cursor.page + 1, slot: cursor.slot - col } : cursor;
          break;
        case 'ArrowLeft':
          next = col > 0 ? { ...cursor, slot: cursor.slot - 1 } : cursor.page > 1 ? { page: cursor.page - 1, slot: cursor.slot + cols - 1 } : cursor;
          break;
        case 'ArrowDown':
          next = cursor.slot + cols < slotsPerPage ? { ...cursor, slot: cursor.slot + cols } : cursor;
          break;
        case 'ArrowUp':
          next = cursor.slot - cols >= 0 ? { ...cursor, slot: cursor.slot - cols } : cursor;
          break;
        case 'Home':
          next = { ...cursor, slot: 0 };
          break;
        case 'End':
          next = { ...cursor, slot: slotsPerPage - 1 };
          break;
        case 'PageDown':
          next = { ...cursor, page: Math.min(totalPages, cursor.page + 1) };
          break;
        case 'PageUp':
          next = { ...cursor, page: Math.max(1, cursor.page - 1) };
          break;
        case 'Enter':
          if (el?.closest(PRESSABLE)) return;
          e.preventDefault();
          // A card's details (to correct it); an empty pocket searches. Typing replaces a card.
          if (cursorCard) setDetailCardId(cursorCard.id);
          else openSearch(cursor.slot);
          return;
        case '+':
        case '=':
          e.preventDefault();
          if (cursorSheet?.side === 'front') setNotice(t('binder.insertFromBack', { page: cursor.page, back: cursor.page + 1 }));
          else insertSheetAfter(cursor.page);
          return;
        case '-':
          // A whole sheet goes at once, so ask first (Enter confirms, Esc cancels).
          e.preventDefault();
          setConfirmRemove(cursor.page);
          return;
        case 'Delete':
        case 'Backspace':
          if (cursorCard) {
            e.preventDefault();
            run(api.binderUnassign(cursorCard.id));
          }
          return;
        default:
          // Any printable character starts the search with it — no Enter needed.
          if (e.key.length === 1 && e.key.trim() !== '') {
            e.preventDefault();
            openSearch(cursor.slot, e.key);
          }
          return;
      }
      e.preventDefault();
      setCursor(next);
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  });

  // Simplified editing's copy/paste: Ctrl+C on a pocket copies its card, Ctrl+V pastes a new copy of it into
  // the cursor's pocket (a card already there goes back to Unplaced) and moves on. These are clipboard
  // events rather than keys, so the browser asks no clipboard permission, and text boxes keep their own.
  useEffect(() => {
    if (!quick || !data) return;
    const ignore = (e: ClipboardEvent) =>
      !!(addSlot || detailCardId != null || listCard || slotMenu || cardMenu || addAnchor || setsAnchor || confirmRemove != null || deleteCard || dividerDialog || dividerMenu || confirmRemoveDivider) ||
      (e.target instanceof HTMLElement && !!e.target.closest(EDITABLE));
    const onCopy = (e: ClipboardEvent) => {
      // Text selected on the page copies as usual.
      if (ignore(e) || !cursorCard || window.getSelection()?.toString()) return;
      const card: CopiedCard = {
        kind: COPIED_KIND,
        lotId: cursorCard.id,
        name: cursorCard.name,
        setCode: cursorCard.setCode,
        number: cursorCard.number,
      };
      e.preventDefault();
      e.clipboardData?.setData('text/plain', JSON.stringify(card));
      setCopied(card);
    };
    const onPaste = (e: ClipboardEvent) => {
      if (ignore(e)) return;
      e.preventDefault();
      // Whatever is on the clipboard wins: text copied since the card means there's no card to paste.
      // (An empty text clipboard falls back to the card copied here.)
      const text = e.clipboardData?.getData('text/plain') ?? '';
      const card = text ? parseCopied(text) : copied;
      if (!card) {
        setNotice(t('binder.clipboardNotCard'));
        return;
      }
      setCopied(card);
      // Its own pocket already holds it.
      if (cursorCard?.id === card.lotId) return;
      run(api.binderPaste(card.lotId, binderId, cursor.page, cursor.slot));
      // On at once, so a quick second Ctrl+V fills the next pocket rather than replacing this one.
      advance();
    };
    window.addEventListener('copy', onCopy);
    window.addEventListener('paste', onPaste);
    return () => {
      window.removeEventListener('copy', onCopy);
      window.removeEventListener('paste', onPaste);
    };
  });

  // --- The spread view's keyboard (everything but Simplified editing). ---
  // The cursor lives on a page of the spread on screen; after a turn with the mouse it starts again on the
  // spread's first page. The spread's pages come from its number, not the loaded data, so a key pressed
  // while the next spread is still loading already counts from the right place.
  const spreadPages = pagesOfSpread(spread, totalPages);
  const nav: Cursor = spreadPages.includes(navCursor.page)
    ? { page: navCursor.page, slot: Math.min(navCursor.slot, slotsPerPage - 1) }
    : { page: spreadPages[0] ?? 1, slot: 0 };
  const navSlots =
    data?.leftPageNumber === nav.page ? data.leftSlots : data?.rightPageNumber === nav.page ? data.rightSlots : null;
  const navCard = navSlots?.[nav.slot]?.card ?? null;
  const showNav = !quick && navKeys && navFocus;
  const canArrange = editMode && canWrite;
  useEffect(() => {
    if (!editMode) setHeld(null);
  }, [editMode]);

  const pocketEl = (c: Cursor) => spreadRef.current?.querySelector<HTMLElement>(`[data-pocket="${c.page}-${c.slot}"]`) ?? null;
  /** Put the cursor on a pocket (turning to its spread) and the keyboard on the pages. */
  const moveNav = (c: Cursor) => {
    setNavCursor(c);
    setSpread(spreadOfPage(c.page));
    setNavKeys(true);
    spreadRef.current?.focus({ preventScroll: true });
  };
  /** After a divider tab or a spread tab is chosen, the keyboard goes back to the pages (from that page's
   *  first pocket), so the arrows and PgUp/PgDn carry on there instead of in the tab strip. */
  const focusPages = (page?: number) => {
    if (page != null) setNavCursor({ page, slot: 0 });
    requestAnimationFrame(() => (quick ? gridRef : spreadRef).current?.focus({ preventScroll: true }));
  };
  /** The pocket's actions, opened from the keyboard (Shift+F10 / the menu key) beside the pocket. */
  const openNavMenu = () => {
    const r = pocketEl(nav)?.getBoundingClientRect();
    if (!r) return;
    setSlotMenu({ x: r.left + r.width / 2, y: r.top + r.height / 2, page: nav.page, slot: nav.slot, card: navCard });
  };
  /** Space while editing: pick up the pocket's card, or drop the one held here (a card already here swaps
   *  places with it, or goes back to Unplaced when the held card came from there). */
  const pickUpOrDrop = () => {
    if (held) {
      const fromHere = held.from?.page === nav.page && held.from.slot === nav.slot;
      if (!fromHere) run(api.binderAssign(held.lotId, binderId, nav.page, nav.slot));
      setHeld(null);
    } else if (navCard) {
      setHeld({ lotId: navCard.id, name: navCard.name, from: nav });
    }
  };
  const overlayOpen = !!(
    addSlot || detailCardId != null || listCard || slotMenu || cardMenu || addAnchor || setsAnchor ||
    confirmRemove != null || deleteCard || dividerDialog || dividerMenu || confirmRemoveDivider
  );

  // Keep the cursor's pocket on screen, in both views. It's scrolled to once per move, as soon as it is
  // drawn — a turned-to spread may still be loading when the key is pressed, so this runs after every
  // render until the pocket is there. (Pockets keep a scroll margin clear of the app bar.)
  const scrollTarget = quick ? `${cursor.page}-${cursor.slot}` : showNav ? `${nav.page}-${nav.slot}` : null;
  const scrolledTo = useRef<string | null>(null);
  useEffect(() => {
    if (!scrollTarget) {
      scrolledTo.current = null;
      return;
    }
    if (scrolledTo.current === scrollTarget) return;
    const el = document.querySelector<HTMLElement>(`[data-pocket="${scrollTarget}"]`);
    if (!el) return;
    scrolledTo.current = scrollTarget;
    el.scrollIntoView({ block: 'nearest', inline: 'nearest' });
  });

  // Re-bound each render so it always sees the current cursor and spread.
  useEffect(() => {
    if (quick || !data) return;
    const onKey = (e: globalThis.KeyboardEvent) => {
      if (e.defaultPrevented || overlayOpen) return;
      const el = e.target instanceof HTMLElement ? e.target : null;
      if (el?.closest(EDITABLE)) return;
      const onPages = !!el && !!spreadRef.current?.contains(el);
      const mods = e.ctrlKey || e.altKey || e.metaKey;

      if (e.key === '?' && !e.ctrlKey && !e.altKey && !e.metaKey) {
        e.preventDefault();
        setShowKeys((v) => !v);
        return;
      }
      // Alt+PgUp / Alt+PgDn: the previous / next divider.
      if (e.altKey && !e.ctrlKey && !e.metaKey && (e.key === 'PageUp' || e.key === 'PageDown')) {
        e.preventDefault();
        const target =
          e.key === 'PageDown'
            ? dividers.find((d) => d.page > Math.max(...spreadPages))
            : [...dividers].reverse().find((d) => d.page < Math.min(...spreadPages));
        if (target) {
          goToDivider(target);
          moveNav({ page: target.page, slot: 0 });
        }
        return;
      }
      // The tab strips (spreads, dividers) and the Unplaced list have their own arrows / Home / End.
      if (el?.closest('[role="tablist"], [data-unplaced]') && !onPages) return;
      // PgUp / PgDn: turn the spread, keeping the cursor's side and pocket where the next spread has it.
      if (!mods && (e.key === 'PageUp' || e.key === 'PageDown')) {
        e.preventDefault();
        const to = Math.min(totalSpreads - 1, Math.max(0, spread + (e.key === 'PageDown' ? 1 : -1)));
        if (to === spread) return;
        const pages = pagesOfSpread(to, totalPages);
        const right = nav.page === spreadPages[spreadPages.length - 1] && spreadPages.length > 1;
        moveNav({ page: (right ? pages[pages.length - 1] : pages[0]) ?? 1, slot: nav.slot });
        return;
      }
      // Ctrl+Home / Ctrl+End: the front of page 1 / the pocket after the last card.
      if (e.ctrlKey && !e.altKey && !e.metaKey && (e.key === 'Home' || e.key === 'End')) {
        e.preventDefault();
        moveNav(e.key === 'Home' ? { page: 1, slot: 0 } : lastCursor());
        return;
      }
      // Anywhere else (the page itself, a button such as Prev or Next) the arrows drive the pages; Enter and
      // Space still press a focused button (see below).
      if (mods) return;
      // While the cursor is hidden (after a click, or a tab), the first arrow only shows it where it is.
      if (!showNav && ['ArrowRight', 'ArrowLeft', 'ArrowUp', 'ArrowDown'].includes(e.key)) {
        e.preventDefault();
        moveNav(nav);
        return;
      }
      const cols = data.columns;
      const col = nav.slot % cols;
      let next: Cursor;
      switch (e.key) {
        case 'ArrowRight':
          // Off a page's right edge onto the next page, same row — across the spread, then on to the next.
          next = col < cols - 1 ? { ...nav, slot: nav.slot + 1 } : nav.page < totalPages ? { page: nav.page + 1, slot: nav.slot - col } : nav;
          break;
        case 'ArrowLeft':
          next = col > 0 ? { ...nav, slot: nav.slot - 1 } : nav.page > 1 ? { page: nav.page - 1, slot: nav.slot + cols - 1 } : nav;
          break;
        case 'ArrowDown':
          next = nav.slot + cols < slotsPerPage ? { ...nav, slot: nav.slot + cols } : nav;
          break;
        case 'ArrowUp':
          next = nav.slot - cols >= 0 ? { ...nav, slot: nav.slot - cols } : nav;
          break;
        case 'Home':
          next = { ...nav, slot: 0 };
          break;
        case 'End':
          next = { ...nav, slot: slotsPerPage - 1 };
          break;
        case 'Enter':
          if (!onPages && el?.closest(PRESSABLE)) return;
          e.preventDefault();
          setNavKeys(true);
          // A card's details; an empty pocket (while editing) adds a card to it.
          if (navCard) setDetailCardId(navCard.id);
          else if (canArrange) setAddSlot({ page: nav.page, slot: nav.slot, card: null });
          return;
        case ' ':
          if (!canArrange || (!onPages && el?.closest(PRESSABLE))) return;
          e.preventDefault();
          setNavKeys(true);
          pickUpOrDrop();
          return;
        case 'Escape':
          if (!held) return;
          e.preventDefault();
          setHeld(null);
          return;
        case 'Delete':
        case 'Backspace':
          if (!canArrange || !navCard) return;
          e.preventDefault();
          run(api.binderUnassign(navCard.id));
          return;
        case 'F10':
          if (!e.shiftKey) return;
          e.preventDefault();
          openNavMenu();
          return;
        case 'ContextMenu':
          e.preventDefault();
          openNavMenu();
          return;
        default:
          return;
      }
      e.preventDefault();
      moveNav(next);
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  });

  const openSets = () => setSetsAnchor(setsButtonRef.current);
  const closeSets = () => {
    setSetsAnchor(null);
    // Back to the pocket cursor, not the button (where Enter would open the filter again).
    if (quick) requestAnimationFrame(() => gridRef.current?.focus({ preventScroll: true }));
  };
  // F2 opens (and closes) the set filter in either edit mode; the Add card dialog has its own F2.
  useEffect(() => {
    if (!editMode) return;
    const onKey = (e: globalThis.KeyboardEvent) => {
      if (e.key !== 'F2' || e.defaultPrevented) return;
      if (addSlot || detailCardId != null || listCard || confirmRemove != null || deleteCard || dividerDialog || confirmRemoveDivider) return;
      e.preventDefault();
      if (setsAnchor) closeSets();
      else openSets();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  });

  if (isLoading || !data) return <CircularProgress />;

  const setsLabel =
    filterSets.length === 0
      ? t('common.labels.allSets')
      : filterSets
          .slice(0, 3)
          .map((s) => s.setCode.toUpperCase())
          .join(', ') + (filterSets.length > 3 ? ` ${t('binder.setFilter.more', { n: fmt.number(filterSets.length - 3) })}` : '');

  // The rule's finish, when the game on screen offers it (otherwise the game's basic foil).
  const finishes = foilTypes.data ?? [];
  const ruleFinish = foilRule.foilType && finishes.includes(foilRule.foilType) ? foilRule.foilType : finishes[0] ?? '';
  const dialogFoilRule: AddCardFoilRule | undefined = foilRule.query.trim()
    ? { query: foilRule.query, foilType: ruleFinish || null }
    : undefined;
  // Simplified editing lists the binder's unplaced cards beside the page only while there are some.
  const showUnplaced = (unplaced.data?.length ?? 0) > 0 || filter.trim() !== '';
  // The page's width: the window's height decides (pocket aspect 0.72, so a page of c × r pockets is about
  // c·0.72 / r as wide as it is tall), never wider than the room there is.
  const pageWidth = `min(100%, max(320px, calc((100vh - 470px) * ${(data.columns * 0.72) / (data.slotsPerPage / data.columns)})))`;

  const dropOnSlot = (page: number, slot: number) => {
    if (dragLotId != null) run(assign.mutateAsync({ page, slot }));
  };
  const dropUnassign = () => {
    if (dragLotId != null) run(unassign.mutateAsync());
  };
  /** A spread page's keyboard cursor + held card, and a press that moves the cursor there. */
  const navGridProps = (page: number) => ({
    cursorSlot: showNav && nav.page === page ? nav.slot : undefined,
    heldSlot: held?.from?.page === page ? held.from.slot : undefined,
    onSlotPointer: (slot: number) => {
      setNavCursor({ page, slot });
      setNavKeys(false);
    },
  });

  return (
    <Stack spacing={2}>
      <Breadcrumbs>
        <Link component={RouterLink} to="/locations">
          {t('binder.locations')}
        </Link>
        <Link component={RouterLink} to={`/location/${binderId}`}>
          {data.containerName}
        </Link>
        <Typography color="text.primary">{t('binder.breadcrumb')}</Typography>
      </Breadcrumbs>

      <Stack direction="row" spacing={2} alignItems="center" flexWrap="wrap" useFlexGap>
        <Typography variant="h4">{data.containerName}</Typography>
        <Typography variant="body2" color="text.secondary">
          {quick ? t('binder.page', { page: cursor.page }) : data.pageRangeLabel} · {t('binder.pages', { count: data.totalPages })}
          {sectionDivider && ` · ${sectionDivider.name}`}
        </Typography>
        {isFetching && <CircularProgress size={16} />}
        <Box sx={{ flexGrow: 1 }} />
        {canWrite && (
          <Button
            size="small"
            variant={editMode ? 'contained' : 'outlined'}
            startIcon={<EditIcon />}
            onClick={toggleEdit}
          >
            {editMode ? t('binder.doneEditing') : t('common.actions.edit')}
          </Button>
        )}
      </Stack>

      {/* Edit toolbar */}
      {editMode && (
        <Paper variant="outlined" sx={{ p: 1 }}>
          <Stack direction="row" spacing={2} alignItems="center" flexWrap="wrap" useFlexGap>
            <Button size="small" startIcon={<AddIcon />} onClick={(e) => setAddAnchor(e.currentTarget)}>
              {t('binder.addPage')}
            </Button>
            <Menu anchorEl={addAnchor} open={!!addAnchor} onClose={() => setAddAnchor(null)}>
              <MenuItem onClick={() => { setAddAnchor(null); run(addPage.mutateAsync('double')); }}>
                {t('binder.doubleSidedSheet')}
              </MenuItem>
              <MenuItem onClick={() => { setAddAnchor(null); run(addPage.mutateAsync('single')); }}>
                {t('binder.singleSidedPage')}
              </MenuItem>
              {canInsertAfter && insertAfterPage! < data.totalPages && (
                <MenuItem onClick={() => { setAddAnchor(null); insertSheetAfter(insertAfterPage!); }}>
                  {t('binder.sheetAfterPage', { page: insertAfterPage })}
                </MenuItem>
              )}
            </Menu>
            <Tooltip describeChild title={t('binder.dividers.addTooltip')}>
              <Button size="small" startIcon={<BookmarkAddIcon />} onClick={() => setDividerDialog({ mode: 'add', page: herePage })}>
                {t('binder.dividers.add')}
              </Button>
            </Tooltip>
            <TextField
              select
              size="small"
              label={t('binder.layout')}
              value={data.slotsPerPage}
              onChange={(e) => {
                const l = LAYOUTS.find((x) => x.slots === Number(e.target.value));
                if (l) run(layout.mutateAsync({ slots: l.slots, cols: l.cols }));
              }}
              sx={{ minWidth: 140 }}
            >
              {LAYOUTS.map((l) => (
                <MenuItem key={l.slots} value={l.slots}>
                  {t('binder.layoutGrid', { cols: l.cols, rows: l.slots / l.cols, count: l.slots })}
                </MenuItem>
              ))}
            </TextField>
            {(quick ? [cursor.page] : [data.leftPageNumber, data.rightPageNumber]).map(
              (p) =>
                p != null && (
                  <Button key={p} size="small" color="error" onClick={() => run(removePage.mutateAsync(p))}>
                    {t('binder.removePage', { page: p })}
                  </Button>
                ),
            )}
            <Tooltip title={t('binder.setFilter.tooltip')}>
              <Button
                ref={setsButtonRef}
                size="small"
                variant={filterSets.length ? 'contained' : 'text'}
                startIcon={<FilterListIcon />}
                onClick={openSets}
              >
                {t('binder.setFilter.button', { sets: setsLabel })}
              </Button>
            </Tooltip>
            <Popover
              open={!!setsAnchor}
              anchorEl={setsAnchor}
              onClose={closeSets}
              disableRestoreFocus
              anchorOrigin={{ vertical: 'bottom', horizontal: 'left' }}
            >
              <Stack spacing={1.5} sx={{ p: 2, width: { xs: 'calc(100vw - 32px)', sm: 380 } }}>
                <Typography variant="subtitle2">{t('binder.setFilter.title')}</Typography>
                <TextField
                  select
                  size="small"
                  label={t('common.labels.game')}
                  value={setsGame}
                  disabled={!!locGame}
                  helperText={locGame ? t('binder.setFilter.gameFixed') : undefined}
                  onChange={(e) => {
                    setFilterGame(e.target.value);
                    setFilterSets([]);
                  }}
                >
                  {(games.data ?? [{ id: setsGame, displayName: setsGame }]).map((g) => (
                    <MenuItem key={g.id} value={g.id}>
                      {g.displayName}
                    </MenuItem>
                  ))}
                </TextField>
                <SetMultiSelect
                  game={setsGame}
                  value={filterSets}
                  onChange={(v) => {
                    // Picking sets pins the filter to this game, wherever the pages turn.
                    setFilterGame(setsGame);
                    setFilterSets(v);
                  }}
                  autoFocus
                />
                <Typography variant="caption" color="text.secondary">
                  {t('binder.setFilter.help')}
                </Typography>
                <Stack direction="row" spacing={1} justifyContent="flex-end">
                  {locSets.sets.length > 0 && (
                    <Button
                      size="small"
                      disabled={
                        filterSets.map((s) => s.setCode).join(',') === locSets.sets.map((s) => s.setCode).join(',')
                      }
                      onClick={() => setFilterSets(locSets.sets)}
                    >
                      {t('binder.setFilter.useDefaults')}
                    </Button>
                  )}
                  <Button size="small" disabled={filterSets.length === 0} onClick={() => setFilterSets([])}>
                    {t('common.actions.clear')}
                  </Button>
                  <Button size="small" variant="contained" onClick={closeSets}>
                    {t('common.actions.done')}
                  </Button>
                </Stack>
              </Stack>
            </Popover>
            <Tooltip title={t('binder.foilRule.tooltip')}>
              <TextField
                size="small"
                label={t('binder.foilRule.label')}
                placeholder={t('binder.foilRule.placeholder')}
                value={foilRule.query}
                onChange={(e) => updateFoilRule({ query: e.target.value })}
                onKeyDown={(e) => {
                  // Done with the rule: back to the pockets.
                  if (quick && (e.key === 'Enter' || e.key === 'Escape')) {
                    e.preventDefault();
                    gridRef.current?.focus({ preventScroll: true });
                  }
                }}
                sx={{ width: 220 }}
                slotProps={{
                  input: {
                    startAdornment: (
                      <InputAdornment position="start">
                        <AutoAwesomeIcon fontSize="small" color={foilRule.query.trim() ? 'primary' : 'disabled'} />
                      </InputAdornment>
                    ),
                  },
                }}
              />
            </Tooltip>
            {foilRule.query.trim() && finishes.length > 0 && (
              <TextField
                select
                size="small"
                label={t('binder.foilRule.finish')}
                value={ruleFinish}
                onChange={(e) => updateFoilRule({ foilType: e.target.value })}
                sx={{ minWidth: 150 }}
              >
                {finishes.map((f) => (
                  <MenuItem key={f} value={f}>
                    {f}
                  </MenuItem>
                ))}
              </TextField>
            )}
            <Box sx={{ flexGrow: 1 }} />
            <Tooltip title={t('binder.simplifiedTooltip')}>
              <FormControlLabel
                control={<Switch size="small" checked={simplified} onChange={(e) => toggleSimplified(e.target.checked)} />}
                label={t('binder.simplified')}
                sx={{ mr: 0 }}
              />
            </Tooltip>
          </Stack>
        </Paper>
      )}
      {error && <Alert severity="error" onClose={() => setError(null)}>{error}</Alert>}
      {quick && notice && <Alert severity="info" onClose={() => setNotice(null)}>{notice}</Alert>}

      {/* Dividers: always-there tabs that jump to their section; the one you're in stands out. */}
      {dividers.length > 0 && (
        <BinderDividerTabs
          dividers={dividers}
          activeId={activeDivider?.id ?? null}
          onSelect={(d, how) => {
            goToDivider(d);
            if (how === 'activate') focusPages(d.page);
          }}
          editable={editMode && canWrite}
          onRemove={setConfirmRemoveDivider}
          onTabContextMenu={(e, d) => {
            e.preventDefault();
            setDividerMenu({ x: e.clientX, y: e.clientY, divider: d });
          }}
        />
      )}

      {quick ? (
        <>
          {/* Simplified editing: page navigation (the keyboard does the same with PgUp/PgDn). */}
          <Stack direction="row" spacing={1} alignItems="center" justifyContent="center">
            <Tooltip title={t('binder.firstTooltip')}>
              <span>
                <Button size="small" disabled={cursor.page <= 1 && cursor.slot === 0} onClick={() => turnTo({ page: 1, slot: 0 })}>
                  {t('binder.first')}
                </Button>
              </span>
            </Tooltip>
            <Button size="small" disabled={cursor.page <= 1} onClick={() => turnTo({ ...cursor, page: cursor.page - 1 })}>
              {t('binder.prev')}
            </Button>
            <Typography variant="subtitle2" sx={{ minWidth: 120, textAlign: 'center' }}>
              {t('binder.pageOf', { page: cursor.page, total: data.totalPages })}
              {cursorSheet && (
                <Typography component="span" variant="body2" color="text.secondary">
                  {' · '}
                  {t(`binder.side.${cursorSheet.side}`, { sheet: cursorSheet.index + 1 })}
                </Typography>
              )}
            </Typography>
            <Button size="small" disabled={cursor.page >= data.totalPages} onClick={() => turnTo({ ...cursor, page: cursor.page + 1 })}>
              {t('binder.next')}
            </Button>
            <Tooltip title={t('binder.lastTooltip')}>
              <Button size="small" onClick={() => turnTo(lastCursor())}>
                {t('binder.last')}
              </Button>
            </Tooltip>
          </Stack>

          {/* The cursor's page alone, centred and sized to fit the window. The unplaced cards sit in the margin
              to its left on a wide screen (below it otherwise) and take no room of their own, so the page
              doesn't move when they come and go. */}
          <Box sx={{ position: 'relative', display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 2 }}>
            <Box
              ref={gridRef}
              tabIndex={0}
              sx={{
                width: { xs: '100%', sm: pageWidth },
                borderRadius: 1,
                outline: 'none',
                '&:focus-visible': { boxShadow: (th) => `0 0 0 2px ${th.palette.primary.main}` },
              }}
            >
              {dividers.some((d) => d.page === cursor.page) && (
                <BinderDividerMarker horizontal dividers={dividers.filter((d) => d.page === cursor.page)} sx={{ mb: 1 }} />
              )}
              <SlotGrid
                slots={cursorSlots ?? Array.from({ length: data.slotsPerPage }, (_, i) => ({ slotIndex: i, card: null }))}
                columns={data.columns}
                pageNumber={cursor.page}
                editMode={editMode}
                onDragCard={setDragLotId}
                onDropSlot={dropOnSlot}
                onSlotContextMenu={openSlotMenu}
                onCardClick={(c) => setDetailCardId(c.id)}
                cursorSlot={cursor.slot}
                onSlotSelect={(slot) => {
                  setCursor((c) => ({ ...c, slot }));
                  // Clicking a card opens its details, ready to correct.
                  const card = cursorSlots?.[slot]?.card;
                  if (card) setDetailCardId(card.id);
                }}
                onSlotOpen={(slot) => openSearch(slot)}
              />
            </Box>
            {showUnplaced && (
              <UnplacedSidebar
                cards={unplaced.data ?? []}
                loading={unplaced.isFetching}
                filter={filter}
                onFilter={setFilter}
                onDragCard={setDragLotId}
                onDropUnassign={dropUnassign}
                onCardContextMenu={openCardMenu}
                onCardClick={(c) => setDetailCardId(c.id)}
                autoFocus={false}
                sx={{
                  position: { xs: 'static', lg: 'absolute' },
                  left: 0,
                  top: 0,
                  bottom: 0,
                  alignSelf: { xs: 'stretch', lg: 'auto' },
                  width: { xs: '100%', lg: `min(300px, calc((100% - ${pageWidth}) / 2 - 16px))` },
                  maxHeight: { xs: 360, lg: 'none' },
                }}
              />
            )}
          </Box>
          <Stack spacing={1} alignItems="center">
            <Typography variant="body2" color="text.secondary">
              {cursorCard
                ? t('binder.cursorCard', {
                    page: cursor.page,
                    slot: cursor.slot + 1,
                    name: cursorCard.name,
                    set: cursorCard.setCode.toUpperCase(),
                    number: cursorCard.number,
                  })
                : t('binder.cursorEmpty', { page: cursor.page, slot: cursor.slot + 1 })}
            </Typography>
            {copied && (
              <Typography variant="caption" color="text.secondary">
                {t('binder.copiedCard', { name: copied.name, set: copied.setCode.toUpperCase(), number: copied.number })}
              </Typography>
            )}
            <Stack direction="row" spacing={1.5} justifyContent="center" flexWrap="wrap" useFlexGap>
              {SIMPLIFIED_KEYS.map(([keys, label]) => (
                <Stack key={label} direction="row" spacing={0.5} alignItems="center">
                  {keys.map((k) => (
                    <Kbd key={k}>{k}</Kbd>
                  ))}
                  <Typography variant="caption" color="text.secondary">
                    {t(label)}
                  </Typography>
                </Stack>
              ))}
            </Stack>
          </Stack>
        </>
      ) : (
        <>
          {/* Spread navigation */}
          <Stack direction="row" spacing={1} alignItems="center">
            <Button size="small" disabled={spread <= 0} onClick={() => setSpread((s) => Math.max(0, s - 1))}>
              {t('binder.prev')}
            </Button>
            <Tabs value={data.spreadIndex} onChange={(_, v) => { setSpread(v); focusPages(); }} variant="scrollable" scrollButtons="auto" sx={{ flexGrow: 1, minHeight: 36 }}>
              {data.spreadTabs.map((t) => (
                <Tab key={t.index} value={t.index} label={t.label} sx={{ minHeight: 36 }} />
              ))}
            </Tabs>
            <Button size="small" disabled={spread >= data.totalSpreads - 1} onClick={() => setSpread((s) => Math.min(data.totalSpreads - 1, s + 1))}>
              {t('binder.next')}
            </Button>
            <Tooltip title={t('binder.navKeys.toggle')}>
              <IconButton
                size="small"
                aria-label={t('binder.navKeys.toggle')}
                aria-pressed={showKeys}
                color={showKeys ? 'primary' : 'default'}
                onClick={() => setShowKeys((v) => !v)}
              >
                <KeyboardIcon fontSize="small" />
              </IconButton>
            </Tooltip>
          </Stack>

          {/* Spread (+ unplaced sidebar in edit mode). A phone shows the spread's pages one above the other. */}
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} alignItems={{ xs: 'stretch', sm: 'flex-start' }}>
            {editMode && (
              <UnplacedSidebar
                cards={unplaced.data ?? []}
                loading={unplaced.isFetching}
                filter={filter}
                onFilter={setFilter}
                onDragCard={setDragLotId}
                onDropUnassign={dropUnassign}
                onCardContextMenu={openCardMenu}
                onCardClick={(c) => setDetailCardId(c.id)}
                onCardPickUp={
                  canWrite
                    ? (c) => {
                        setHeld({ lotId: c.id, name: c.name, from: null });
                        moveNav(nav);
                      }
                    : undefined
                }
                onLeave={() => moveNav(nav)}
              />
            )}
            {/* The pages take the keyboard (Tab stops here); the cursor shows once keys are used. */}
            <Stack
              ref={spreadRef}
              tabIndex={0}
              role="group"
              aria-label={data.pageRangeLabel}
              aria-describedby="binder-nav-status"
              direction={{ xs: 'column', sm: 'row' }}
              spacing={2}
              onFocus={(e) => {
                setNavFocus(true);
                if (e.target === e.currentTarget && e.currentTarget.matches(':focus-visible')) setNavKeys(true);
              }}
              onBlur={(e) => {
                if (!e.currentTarget.contains(e.relatedTarget as Node | null)) setNavFocus(false);
              }}
              onMouseDown={() => setNavKeys(false)}
              onContextMenu={(e) => {
                // The menu key / Shift+F10 on the pages themselves: the cursor's pocket menu.
                if (e.target !== e.currentTarget) return;
                e.preventDefault();
                openNavMenu();
              }}
              sx={{
                flexGrow: 1,
                minWidth: 0,
                borderRadius: 1,
                outline: 'none',
                '&:focus-visible': { boxShadow: (th) => `0 0 0 2px ${th.palette.primary.main}` },
              }}
            >
              {/* A divider sits in front of its page: at the left edge for the left page, between the
                  facing pages for the right one. */}
              {data.leftPageNumber != null && dividers.some((d) => d.page === data.leftPageNumber) && (
                <BinderDividerMarker dividers={dividers.filter((d) => d.page === data.leftPageNumber)} />
              )}
              {data.leftPageNumber != null ? (
                <SlotGrid
                  slots={data.leftSlots}
                  columns={data.columns}
                  pageNumber={data.leftPageNumber}
                  editMode={editMode}
                  onDragCard={setDragLotId}
                  onDropSlot={dropOnSlot}
                  onSlotContextMenu={openSlotMenu}
                  onCardClick={(c) => setDetailCardId(c.id)}
                  {...navGridProps(data.leftPageNumber)}
                />
              ) : (
                <Box sx={{ flex: 1, display: { xs: 'none', sm: 'block' } }} />
              )}
              {data.rightPageNumber != null && dividers.some((d) => d.page === data.rightPageNumber) && (
                <BinderDividerMarker dividers={dividers.filter((d) => d.page === data.rightPageNumber)} />
              )}
              {data.rightPageNumber != null ? (
                <SlotGrid
                  slots={data.rightSlots}
                  columns={data.columns}
                  pageNumber={data.rightPageNumber}
                  editMode={editMode}
                  onDragCard={setDragLotId}
                  onDropSlot={dropOnSlot}
                  onSlotContextMenu={openSlotMenu}
                  onCardClick={(c) => setDetailCardId(c.id)}
                  {...navGridProps(data.rightPageNumber)}
                />
              ) : (
                // Keep the empty half reserved so a lone left page (e.g. an odd last page / a single
                // backside) fills only its half of the spread, mirroring page 1 alone on the right.
                <Box sx={{ flex: 1, display: { xs: 'none', sm: 'block' } }} />
              )}
            </Stack>
          </Stack>

          {/* Where the keyboard is (read out by screen readers too), and the keys. */}
          <Stack spacing={1} alignItems="center">
            <Typography
              id="binder-nav-status"
              role="status"
              variant="body2"
              color={held ? 'warning.main' : 'text.secondary'}
              sx={{ minHeight: '1.5em', textAlign: 'center' }}
            >
              {held
                ? t('binder.navKeys.holding', { name: held.name })
                : showNav
                  ? navCard
                    ? t('binder.cursorCard', {
                        page: nav.page,
                        slot: nav.slot + 1,
                        name: navCard.name,
                        set: navCard.setCode.toUpperCase(),
                        number: navCard.number,
                      })
                    : t('binder.cursorEmpty', { page: nav.page, slot: nav.slot + 1 })
                  : ''}
            </Typography>
            {showKeys && (
              <Stack direction="row" spacing={1.5} justifyContent="center" flexWrap="wrap" useFlexGap>
                {SPREAD_KEYS.filter(([, , editing]) => !editing || canArrange).map(([keys, label]) => (
                  <Stack key={label} direction="row" spacing={0.5} alignItems="center">
                    {keys.map((k) => (
                      <Kbd key={k}>{k}</Kbd>
                    ))}
                    <Typography variant="caption" color="text.secondary">
                      {t(label)}
                    </Typography>
                  </Stack>
                ))}
              </Stack>
            )}
          </Stack>
        </>
      )}

      {/* Unplaced-sidebar card menu */}
      <Menu
        open={cardMenu != null}
        onClose={() => setCardMenu(null)}
        anchorReference="anchorPosition"
        anchorPosition={cardMenu ? { top: cardMenu.y, left: cardMenu.x } : undefined}
      >
        <MenuItem
          onClick={() => {
            setDetailCardId(cardMenu!.card.id);
            setCardMenu(null);
          }}
        >
          <InfoOutlinedIcon fontSize="small" sx={{ mr: 1 }} />
          {t('binder.cardDetails')}
        </MenuItem>
        <MenuItem
          onClick={() => {
            setListCard(cardMenu!.card);
            setCardMenu(null);
          }}
        >
          <SellIcon fontSize="small" sx={{ mr: 1 }} />
          {t('binder.listForSale')}
        </MenuItem>
        {can('collection.delete') && (
          <MenuItem
            onClick={() => {
              setDeleteCard(cardMenu!.card);
              setCardMenu(null);
            }}
            sx={{ color: 'error.main' }}
          >
            <DeleteOutlineIcon fontSize="small" sx={{ mr: 1 }} />
            {t('binder.deleteCard')}
          </MenuItem>
        )}
      </Menu>

      {/* Binder-pocket menu */}
      <Menu
        open={slotMenu != null}
        onClose={() => setSlotMenu(null)}
        anchorReference="anchorPosition"
        anchorPosition={slotMenu ? { top: slotMenu.y, left: slotMenu.x } : undefined}
      >
        <MenuItem
          onClick={() => {
            setAddSlot({ page: slotMenu!.page, slot: slotMenu!.slot, card: slotMenu!.card, quick });
            setSlotMenu(null);
          }}
        >
          <AddIcon fontSize="small" sx={{ mr: 1 }} />
          {slotMenu?.card ? t('binder.replaceCardInPocket') : t('binder.addCardToPocket')}
        </MenuItem>
        {slotMenu?.card && (
          <MenuItem
            onClick={() => {
              setDetailCardId(slotMenu!.card!.id);
              setSlotMenu(null);
            }}
          >
            <InfoOutlinedIcon fontSize="small" sx={{ mr: 1 }} />
            {t('binder.cardDetails')}
          </MenuItem>
        )}
        {slotMenu?.card && (
          <MenuItem
            onClick={() => {
              setListCard(slotMenu!.card);
              setSlotMenu(null);
            }}
          >
            <SellIcon fontSize="small" sx={{ mr: 1 }} />
            {t('binder.listForSale')}
          </MenuItem>
        )}
      </Menu>

      {/* Divider tab menu (edit mode) */}
      <Menu
        open={dividerMenu != null}
        onClose={closeDividerUi}
        anchorReference="anchorPosition"
        anchorPosition={dividerMenu ? { top: dividerMenu.y, left: dividerMenu.x } : undefined}
      >
        <MenuItem
          onClick={() => {
            setDividerDialog({ mode: 'edit', divider: dividerMenu!.divider });
            setDividerMenu(null);
          }}
        >
          <EditOutlinedIcon fontSize="small" sx={{ mr: 1 }} />
          {t('binder.dividers.edit')}
        </MenuItem>
        <MenuItem
          disabled={dividerMenu?.divider.page === herePage}
          onClick={() => {
            run(api.binderDividerUpdate(binderId, dividerMenu!.divider.id, { page: herePage }));
            closeDividerUi();
          }}
        >
          <LowPriorityIcon fontSize="small" sx={{ mr: 1 }} />
          {t('binder.dividers.moveHere', { page: fmt.number(herePage) })}
        </MenuItem>
        <MenuItem
          onClick={() => {
            setConfirmRemoveDivider(dividerMenu!.divider);
            setDividerMenu(null);
          }}
          sx={{ color: 'error.main' }}
        >
          <DeleteOutlineIcon fontSize="small" sx={{ mr: 1 }} />
          {t('binder.dividers.remove')}
        </MenuItem>
      </Menu>

      <BinderDividerDialog
        open={dividerDialog != null}
        title={
          dividerDialog?.mode === 'edit'
            ? t('binder.dividers.editTitle')
            : t('binder.dividers.addTitle', { page: fmt.number(dividerDialog?.page ?? herePage) })
        }
        initialName={dividerDialog?.mode === 'edit' ? dividerDialog.divider.name : ''}
        initialColor={dividerDialog?.mode === 'edit' ? dividerDialog.divider.color : null}
        onClose={closeDividerUi}
        onSave={(name, color) => {
          const dlg = dividerDialog!;
          run(
            dlg.mode === 'add'
              ? api.binderDividerAdd(binderId, dlg.page, name, color)
              : api.binderDividerUpdate(binderId, dlg.divider.id, { name, color: color ?? '' }),
          );
          closeDividerUi();
        }}
      />

      <Snackbar
        open={removedDivider != null}
        autoHideDuration={6000}
        onClose={(_, reason) => reason !== 'clickaway' && setRemovedDivider(null)}
        message={t('binder.dividers.removed', { name: removedDivider?.name })}
        action={
          <Button
            color="inherit"
            size="small"
            onClick={() => {
              const d = removedDivider!;
              setRemovedDivider(null);
              run(api.binderDividerAdd(binderId, d.page, d.name, d.color ?? null));
            }}
          >
            {t('binder.dividers.undo')}
          </Button>
        }
      />

      {addSlot && (
        <AddCardDialog
          open
          target={{
            kind: 'pocket',
            containerId: binderId,
            page: addSlot.page,
            slot: addSlot.slot,
            occupantName: addSlot.card?.name,
            lockGame: !!locGame,
          }}
          defaultGame={locGame ?? (addSlot.card ? gameName(addSlot.card.game) : setsGame)}
          simplified={addSlot.quick}
          initialName={addSlot.initialName}
          setFilter={{ game: setsGame, sets: filterSets }}
          foilRule={dialogFoilRule}
          onClose={() => setAddSlot(null)}
          onDone={() => {
            refresh();
            // Filling a binder from the keyboard: straight on to the next pocket.
            if (addSlot.quick) advance();
          }}
        />
      )}

      <Dialog open={confirmRemove != null} onClose={() => setConfirmRemove(null)} maxWidth="xs" fullWidth>
        <DialogTitle>{t('binder.removePageTitle', { page: confirmRemove })}</DialogTitle>
        <DialogContent>
          <DialogContentText>{t('binder.removePageConfirm')}</DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setConfirmRemove(null)}>{t('common.actions.cancel')}</Button>
          <Button
            color="error"
            variant="contained"
            autoFocus
            onClick={() => {
              run(removePage.mutateAsync(confirmRemove!));
              setConfirmRemove(null);
            }}
          >
            {t('binder.removePage', { page: confirmRemove })}
          </Button>
        </DialogActions>
      </Dialog>

      <Dialog open={confirmRemoveDivider != null} onClose={cancelRemoveDivider} maxWidth="xs" fullWidth>
        <DialogTitle>{t('binder.dividers.removeTitle', { name: confirmRemoveDivider?.name })}</DialogTitle>
        <DialogContent>
          <DialogContentText>
            {t('binder.dividers.removeConfirm', { page: fmt.number(confirmRemoveDivider?.page ?? 0) })}
          </DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button onClick={cancelRemoveDivider}>{t('common.actions.cancel')}</Button>
          <Button
            color="error"
            variant="contained"
            autoFocus
            onClick={() => {
              const d = confirmRemoveDivider!;
              setConfirmRemoveDivider(null);
              removeDivider(d);
              closeDividerUi();
            }}
          >
            {t('binder.dividers.remove')}
          </Button>
        </DialogActions>
      </Dialog>

      <Dialog open={deleteCard != null} onClose={() => setDeleteCard(null)} maxWidth="xs" fullWidth>
        <DialogTitle>{t('binder.deleteCardTitle', { name: deleteCard?.name })}</DialogTitle>
        <DialogContent>
          <DialogContentText>
            {t('binder.deleteCardConfirm', { set: deleteCard?.setCode.toUpperCase(), number: deleteCard?.number })}
          </DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setDeleteCard(null)}>{t('common.actions.cancel')}</Button>
          <Button
            color="error"
            variant="contained"
            autoFocus
            onClick={() => {
              const card = deleteCard!;
              setDeleteCard(null);
              run(
                api.cardDelete(card.id).then(() => {
                  qc.invalidateQueries({ queryKey: ['collection'] });
                  qc.invalidateQueries({ queryKey: ['location-cards'] });
                  qc.invalidateQueries({ queryKey: ['locations'] });
                }),
              );
            }}
          >
            {t('common.actions.delete')}
          </Button>
        </DialogActions>
      </Dialog>

      <CardEditDrawer
        cardId={detailCardId}
        onClose={() => {
          setDetailCardId(null);
          refresh();
        }}
      />

      <ListForSaleDialog
        target={
          listCard
            ? { lotId: listCard.id, name: listCard.name, quantity: 1, marketPrice: listCard.marketPriceRaw }
            : null
        }
        onClose={() => setListCard(null)}
        onListed={refresh}
      />
    </Stack>
  );
}
