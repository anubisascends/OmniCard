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
  Link,
  Menu,
  MenuItem,
  Paper,
  Stack,
  Switch,
  Tab,
  Tabs,
  TextField,
  Tooltip,
  Typography,
  alpha,
  type Theme,
} from '@mui/material';
import EditIcon from '@mui/icons-material/Edit';
import AddIcon from '@mui/icons-material/Add';
import SellIcon from '@mui/icons-material/Sell';
import InfoOutlinedIcon from '@mui/icons-material/InfoOutlined';
import { api } from '../api/client';
import type { BinderCardDto, BinderSlotDto } from '../api/types';
import { CardImage } from '../components/CardImage';
import { ListForSaleDialog } from '../components/dialogs/ListForSaleDialog';
import { CardEditDrawer } from '../components/dialogs/CardEditDrawer';
import { AddCardDialog } from '../components/dialogs/AddCardDialog';
import { SearchBox } from '../components/SearchBox';
import { Kbd } from '../components/Kbd';
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
  /** Simplified editing: a click moves the cursor (instead of opening the card's details)… */
  onSlotSelect?: (slot: number) => void;
  /** …and a double-click searches for the pocket's card. */
  onSlotOpen?: (slot: number) => void;
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
            onDragOver={editMode ? (e) => e.preventDefault() : undefined}
            onDrop={editMode ? () => onDropSlot(pageNumber, s.slotIndex) : undefined}
            onContextMenu={(e) => onSlotContextMenu(e, pageNumber, s.slotIndex, s.card ?? null)}
            onClick={onSlotSelect ? () => onSlotSelect(s.slotIndex) : undefined}
            onDoubleClick={onSlotOpen ? () => onSlotOpen(s.slotIndex) : undefined}
            sx={{
              position: 'relative',
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
}: {
  cards: BinderCardDto[];
  loading: boolean;
  filter: string;
  onFilter: (v: string) => void;
  onDragCard: (lotId: number) => void;
  onDropUnassign: () => void;
  onCardContextMenu: (e: React.MouseEvent, card: BinderCardDto) => void;
  onCardClick: (card: BinderCardDto) => void;
}) {
  const { t } = useTranslation();
  const { money } = useFormatters();
  return (
    <Paper
      variant="outlined"
      onDragOver={(e) => e.preventDefault()}
      onDrop={onDropUnassign}
      sx={{
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
      }}
    >
      <Stack direction="row" spacing={1} alignItems="center" sx={{ mb: 1 }}>
        <Typography variant="subtitle2" sx={{ flexGrow: 1 }}>
          {t('binder.unplacedCards', { count: cards.length })}
        </Typography>
        {loading && <CircularProgress size={14} />}
      </Stack>
      <Box sx={{ mb: 0.5 }}>
        <SearchBox value={filter} onChange={onFilter} autoFocus />
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
                onDragStart={() => onDragCard(c.id)}
                onClick={() => onCardClick(c)}
                onContextMenu={(e) => onCardContextMenu(e, c)}
                sx={{
                  p: 0.5,
                  borderRadius: 1,
                  cursor: 'grab',
                  '&:hover': { bgcolor: 'action.hover' },
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
  [['Enter'], 'binder.keys.search'],
  [['A–Z'], 'binder.keys.type'],
  [['Del'], 'binder.keys.remove'],
  [['+'], 'binder.keys.addPage'],
  [['−'], 'binder.keys.removePage'],
];

/** Keys typed into these belong to them, not to the binder cursor. */
const EDITABLE = 'input, textarea, select, [contenteditable="true"], [role="combobox"], [role="menu"], [role="listbox"], [role="dialog"]';
/** Enter/Space on a focused control presses it. */
const PRESSABLE = 'button, a, [role="button"], [role="tab"]';

export function BinderPage() {
  const { t } = useTranslation();
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
  // `quick`: opened from the simplified editor (catalog-only search, cursor advances after the add);
  // `initialName`: the keystroke(s) that opened it.
  const [addSlot, setAddSlot] = useState<{
    page: number;
    slot: number;
    card: BinderCardDto | null;
    quick?: boolean;
    initialName?: string;
  } | null>(null);

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
    if (!quick) return;
    for (const s of [viewSpread - 1, viewSpread + 1])
      if (s >= 0 && s < totalSpreads)
        qc.prefetchQuery({ queryKey: ['binder', binderId, s], queryFn: () => api.binder(binderId, s), staleTime: 30_000 });
  }, [quick, viewSpread, totalSpreads, binderId, qc]);

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

  /** Next pocket in reading order, on to the next page. */
  const advance = () =>
    setCursor((c) =>
      c.slot + 1 < slotsPerPage ? { ...c, slot: c.slot + 1 } : c.page < totalPages ? { page: c.page + 1, slot: 0 } : c,
    );

  // Simplified editing's keyboard. Re-bound each render so it always sees the current cursor and page.
  useEffect(() => {
    if (!quick || !data) return;
    const onKey = (e: globalThis.KeyboardEvent) => {
      if (e.defaultPrevented || e.ctrlKey || e.altKey || e.metaKey) return;
      if (addSlot || detailCardId != null || listCard || slotMenu || cardMenu || addAnchor || confirmRemove != null) return;
      const el = e.target instanceof HTMLElement ? e.target : null;
      if (el?.closest(EDITABLE)) return;
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
          openSearch(cursor.slot);
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

  if (isLoading || !data) return <CircularProgress />;

  const dropOnSlot = (page: number, slot: number) => {
    if (dragLotId != null) run(assign.mutateAsync({ page, slot }));
  };
  const dropUnassign = () => {
    if (dragLotId != null) run(unassign.mutateAsync());
  };

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

      {quick ? (
        <>
          {/* Simplified editing: page navigation (the keyboard does the same with PgUp/PgDn). */}
          <Stack direction="row" spacing={1} alignItems="center" justifyContent="center">
            <Button
              size="small"
              disabled={cursor.page <= 1}
              onClick={() => {
                setCursor((c) => ({ ...c, page: c.page - 1 }));
                gridRef.current?.focus({ preventScroll: true });
              }}
            >
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
            <Button
              size="small"
              disabled={cursor.page >= data.totalPages}
              onClick={() => {
                setCursor((c) => ({ ...c, page: c.page + 1 }));
                gridRef.current?.focus({ preventScroll: true });
              }}
            >
              {t('binder.next')}
            </Button>
          </Stack>

          {/* The cursor's page alone, centred and sized to fit the window. */}
          <Box
            ref={gridRef}
            tabIndex={0}
            sx={{
              alignSelf: 'center',
              width: {
                xs: '100%',
                // Pocket aspect 0.72, so a page of c × r pockets is about c·0.72 / r as wide as it is tall.
                sm: `min(100%, max(320px, calc((100vh - 470px) * ${(data.columns * 0.72) / (data.slotsPerPage / data.columns)})))`,
              },
              borderRadius: 1,
              outline: 'none',
              '&:focus-visible': { boxShadow: (th) => `0 0 0 2px ${th.palette.primary.main}` },
            }}
          >
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
              onSlotSelect={(slot) => setCursor((c) => ({ ...c, slot }))}
              onSlotOpen={(slot) => openSearch(slot)}
            />
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
            <Tabs value={data.spreadIndex} onChange={(_, v) => setSpread(v)} variant="scrollable" scrollButtons="auto" sx={{ flexGrow: 1, minHeight: 36 }}>
              {data.spreadTabs.map((t) => (
                <Tab key={t.index} value={t.index} label={t.label} sx={{ minHeight: 36 }} />
              ))}
            </Tabs>
            <Button size="small" disabled={spread >= data.totalSpreads - 1} onClick={() => setSpread((s) => Math.min(data.totalSpreads - 1, s + 1))}>
              {t('binder.next')}
            </Button>
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
              />
            )}
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ flexGrow: 1, minWidth: 0 }}>
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
                />
              ) : (
                <Box sx={{ flex: 1, display: { xs: 'none', sm: 'block' } }} />
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
                />
              ) : (
                // Keep the empty half reserved so a lone left page (e.g. an odd last page / a single
                // backside) fills only its half of the spread, mirroring page 1 alone on the right.
                <Box sx={{ flex: 1, display: { xs: 'none', sm: 'block' } }} />
              )}
            </Stack>
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

      {addSlot && (
        <AddCardDialog
          open
          target={{
            kind: 'pocket',
            containerId: binderId,
            page: addSlot.page,
            slot: addSlot.slot,
            occupantName: addSlot.card?.name,
          }}
          defaultGame={gameName(
            addSlot.card?.game ??
              data.leftSlots.concat(data.rightSlots).find((s) => s.card)?.card?.game,
          )}
          simplified={addSlot.quick}
          initialName={addSlot.initialName}
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
