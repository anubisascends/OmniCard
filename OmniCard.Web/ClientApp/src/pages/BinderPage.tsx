import { useEffect, useState } from 'react';
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
  Link,
  Menu,
  MenuItem,
  Paper,
  Stack,
  Tab,
  Tabs,
  TextField,
  Tooltip,
  Typography,
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
import { AddCardToSlotDialog } from '../components/dialogs/AddCardToSlotDialog';
import { SearchBox } from '../components/SearchBox';
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
}: {
  slots: BinderSlotDto[];
  columns: number;
  pageNumber: number;
  editMode: boolean;
  onDragCard: (lotId: number) => void;
  onDropSlot: (page: number, slot: number) => void;
  onSlotContextMenu: (e: React.MouseEvent, page: number, slot: number, card: BinderCardDto | null) => void;
  onCardClick: (card: BinderCardDto) => void;
}) {
  const { t } = useTranslation();
  return (
    <Paper variant="outlined" sx={{ p: 1, flex: 1 }}>
      <Typography variant="caption" color="text.secondary">
        {t('binder.page', { page: pageNumber })}
      </Typography>
      <Box sx={{ display: 'grid', gap: 0.5, gridTemplateColumns: `repeat(${columns}, 1fr)`, mt: 0.5 }}>
        {slots.map((s) => (
          <Box
            key={s.slotIndex}
            onDragOver={editMode ? (e) => e.preventDefault() : undefined}
            onDrop={editMode ? () => onDropSlot(pageNumber, s.slotIndex) : undefined}
            onContextMenu={(e) => onSlotContextMenu(e, pageNumber, s.slotIndex, s.card ?? null)}
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
              cursor: s.card ? 'default' : 'context-menu',
            }}
          >
            {s.card?.imageUrl ? (
              <>
                <Tooltip title={t('binder.slotTooltip', { name: s.card.name, condition: s.card.condition, foilSuffix: s.card.foil ? t('binder.foilSuffix') : '' })}>
                  <CardImage
                    src={s.card.imageUrl}
                    alt={s.card.name}
                    foil={s.card.foil}
                    draggable={editMode}
                    onDragStart={editMode ? () => onDragCard(s.card!.id) : undefined}
                    onClick={() => onCardClick(s.card!)}
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
        width: 300,
        flexShrink: 0,
        alignSelf: 'flex-start',
        position: 'sticky',
        top: 8,
        maxHeight: 'calc(100vh - 32px)',
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

export function BinderPage() {
  const { t } = useTranslation();
  const { id } = useParams();
  const binderId = Number(id);
  const qc = useQueryClient();
  const [spread, setSpread] = useState(0);
  const [editMode, setEditMode] = useState(false);
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
  const [addSlot, setAddSlot] = useState<{ page: number; slot: number; card: BinderCardDto | null } | null>(null);

  const openCardMenu = (e: React.MouseEvent, card: BinderCardDto) => {
    e.preventDefault();
    setCardMenu({ x: e.clientX, y: e.clientY, card });
  };
  const openSlotMenu = (e: React.MouseEvent, page: number, slot: number, card: BinderCardDto | null) => {
    e.preventDefault();
    setSlotMenu({ x: e.clientX, y: e.clientY, page, slot, card });
  };

  // Debounce the search box so each keystroke doesn't hit the server; keeps type-ahead snappy.
  useEffect(() => {
    const t = setTimeout(() => setDebouncedFilter(filter.trim()), 200);
    return () => clearTimeout(t);
  }, [filter]);

  const { data, isLoading, isFetching } = useQuery({
    queryKey: ['binder', binderId, spread],
    queryFn: () => api.binder(binderId, spread),
    placeholderData: keepPreviousData,
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
          {data.pageRangeLabel} · {t('binder.pages', { count: data.totalPages })}
        </Typography>
        {isFetching && <CircularProgress size={16} />}
        <Box sx={{ flexGrow: 1 }} />
        <Button
          size="small"
          variant={editMode ? 'contained' : 'outlined'}
          startIcon={<EditIcon />}
          onClick={() => setEditMode((v) => !v)}
        >
          {editMode ? t('binder.doneEditing') : t('common.actions.edit')}
        </Button>
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
            {data.leftPageNumber != null && (
              <Button size="small" color="error" onClick={() => run(removePage.mutateAsync(data.leftPageNumber!))}>
                {t('binder.removePage', { page: data.leftPageNumber })}
              </Button>
            )}
            {data.rightPageNumber != null && (
              <Button size="small" color="error" onClick={() => run(removePage.mutateAsync(data.rightPageNumber!))}>
                {t('binder.removePage', { page: data.rightPageNumber })}
              </Button>
            )}
          </Stack>
        </Paper>
      )}
      {error && <Alert severity="error" onClose={() => setError(null)}>{error}</Alert>}

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

      {/* Spread (+ unplaced sidebar in edit mode) */}
      <Stack direction="row" spacing={2} alignItems="flex-start">
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
        <Stack direction="row" spacing={2} sx={{ flexGrow: 1, minWidth: 0 }}>
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
            <Box sx={{ flex: 1 }} />
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
            <Box sx={{ flex: 1 }} />
          )}
        </Stack>
      </Stack>

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
            setAddSlot({ page: slotMenu!.page, slot: slotMenu!.slot, card: slotMenu!.card });
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
        <AddCardToSlotDialog
          open
          containerId={binderId}
          page={addSlot.page}
          slot={addSlot.slot}
          occupantName={addSlot.card?.name}
          defaultGame={gameName(
            addSlot.card?.game ??
              data.leftSlots.concat(data.rightSlots).find((s) => s.card)?.card?.game,
          )}
          onClose={() => setAddSlot(null)}
          onDone={refresh}
        />
      )}

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
