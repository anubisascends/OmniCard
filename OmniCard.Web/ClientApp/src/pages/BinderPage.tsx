import { useState } from 'react';
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
import { api } from '../api/client';
import type { BinderCardDto, BinderSlotDto } from '../api/types';

const CARD_BACK_SLUGS = ['mtg', 'optcg', 'riftbound', 'pokemon', 'yugioh', 'fftcg'];
const cardBackSrc = (game: number): string | undefined => {
  const slug = CARD_BACK_SLUGS[game];
  return slug ? `/img/card-back-${slug}.png` : undefined;
};

// Layout presets (slots-per-page → columns) offered in edit mode.
const LAYOUTS = [
  { label: '2 × 2 (4)', slots: 4, cols: 2 },
  { label: '3 × 3 (9)', slots: 9, cols: 3 },
  { label: '3 × 4 (12)', slots: 12, cols: 3 },
  { label: '4 × 4 (16)', slots: 16, cols: 4 },
];

function ReverseCardBack({ game }: { game: number }) {
  const src = cardBackSrc(game);
  return (
    <Tooltip title="Card on the reverse side of this sheet">
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
}: {
  slots: BinderSlotDto[];
  columns: number;
  pageNumber: number;
  editMode: boolean;
  onDragCard: (lotId: number) => void;
  onDropSlot: (page: number, slot: number) => void;
}) {
  return (
    <Paper variant="outlined" sx={{ p: 1, flex: 1 }}>
      <Typography variant="caption" color="text.secondary">
        Page {pageNumber}
      </Typography>
      <Box sx={{ display: 'grid', gap: 0.5, gridTemplateColumns: `repeat(${columns}, 1fr)`, mt: 0.5 }}>
        {slots.map((s) => (
          <Box
            key={s.slotIndex}
            onDragOver={editMode ? (e) => e.preventDefault() : undefined}
            onDrop={editMode ? () => onDropSlot(pageNumber, s.slotIndex) : undefined}
            sx={{
              aspectRatio: '0.72',
              border: '1px dashed',
              borderColor: editMode ? 'primary.light' : 'divider',
              borderRadius: 1,
              overflow: 'hidden',
              bgcolor: 'action.hover',
              display: 'grid',
              placeItems: 'center',
            }}
          >
            {s.card?.imageUrl ? (
              <Tooltip title={`${s.card.name} · ${s.card.condition}${s.card.foil ? ' · Foil' : ''}`}>
                <img
                  src={s.card.imageUrl}
                  alt={s.card.name}
                  draggable={editMode}
                  onDragStart={editMode ? () => onDragCard(s.card!.id) : undefined}
                  style={{
                    width: '100%',
                    height: '100%',
                    objectFit: 'contain',
                    cursor: editMode ? 'grab' : 'default',
                  }}
                />
              </Tooltip>
            ) : s.reverseGame != null ? (
              <ReverseCardBack game={s.reverseGame} />
            ) : null}
          </Box>
        ))}
      </Box>
    </Paper>
  );
}

/** The pool of cards in this binder that aren't placed in a slot — drag them onto slots. Drop a
 * placed card back here to unplace it. */
function UnplacedTray({
  cards,
  onDragCard,
  onDropUnassign,
}: {
  cards: BinderCardDto[];
  onDragCard: (lotId: number) => void;
  onDropUnassign: () => void;
}) {
  return (
    <Paper
      variant="outlined"
      onDragOver={(e) => e.preventDefault()}
      onDrop={onDropUnassign}
      sx={{ p: 1, bgcolor: 'background.default' }}
    >
      <Typography variant="subtitle2" gutterBottom>
        Unplaced cards ({cards.length}) — drag onto a slot; drop a placed card here to remove it
      </Typography>
      {cards.length === 0 ? (
        <Typography variant="body2" color="text.secondary">
          Everything in this binder is placed.
        </Typography>
      ) : (
        <Box sx={{ display: 'flex', gap: 0.5, flexWrap: 'wrap', maxHeight: 220, overflowY: 'auto' }}>
          {cards.map((c) => (
            <Tooltip key={c.id} title={`${c.name} · ${c.condition}${c.foil ? ' · Foil' : ''}`}>
              <Box
                component="img"
                src={c.imageUrl ?? undefined}
                alt={c.name}
                draggable
                onDragStart={() => onDragCard(c.id)}
                sx={{ width: 60, aspectRatio: '0.72', objectFit: 'contain', cursor: 'grab', borderRadius: 0.5 }}
              />
            </Tooltip>
          ))}
        </Box>
      )}
    </Paper>
  );
}

export function BinderPage() {
  const { id } = useParams();
  const binderId = Number(id);
  const qc = useQueryClient();
  const [spread, setSpread] = useState(0);
  const [editMode, setEditMode] = useState(false);
  const [dragLotId, setDragLotId] = useState<number | null>(null);
  const [addAnchor, setAddAnchor] = useState<null | HTMLElement>(null);
  const [error, setError] = useState<string | null>(null);

  const { data, isLoading, isFetching } = useQuery({
    queryKey: ['binder', binderId, spread],
    queryFn: () => api.binder(binderId, spread),
    placeholderData: keepPreviousData,
  });
  const unplaced = useQuery({
    queryKey: ['binder-unplaced', binderId],
    queryFn: () => api.binderUnplaced(binderId),
    enabled: editMode,
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
          Locations
        </Link>
        <Link component={RouterLink} to={`/location/${binderId}`}>
          {data.containerName}
        </Link>
        <Typography color="text.primary">Binder</Typography>
      </Breadcrumbs>

      <Stack direction="row" spacing={2} alignItems="center" flexWrap="wrap" useFlexGap>
        <Typography variant="h4">{data.containerName}</Typography>
        <Typography variant="body2" color="text.secondary">
          {data.pageRangeLabel} · {data.totalPages} pages
        </Typography>
        {isFetching && <CircularProgress size={16} />}
        <Box sx={{ flexGrow: 1 }} />
        <Button
          size="small"
          variant={editMode ? 'contained' : 'outlined'}
          startIcon={<EditIcon />}
          onClick={() => setEditMode((v) => !v)}
        >
          {editMode ? 'Done editing' : 'Edit'}
        </Button>
      </Stack>

      {/* Edit toolbar */}
      {editMode && (
        <Paper variant="outlined" sx={{ p: 1 }}>
          <Stack direction="row" spacing={2} alignItems="center" flexWrap="wrap" useFlexGap>
            <Button size="small" startIcon={<AddIcon />} onClick={(e) => setAddAnchor(e.currentTarget)}>
              Add page
            </Button>
            <Menu anchorEl={addAnchor} open={!!addAnchor} onClose={() => setAddAnchor(null)}>
              <MenuItem onClick={() => { setAddAnchor(null); run(addPage.mutateAsync('double')); }}>
                Double-sided sheet
              </MenuItem>
              <MenuItem onClick={() => { setAddAnchor(null); run(addPage.mutateAsync('single')); }}>
                Single-sided page
              </MenuItem>
            </Menu>
            <TextField
              select
              size="small"
              label="Layout"
              value={data.slotsPerPage}
              onChange={(e) => {
                const l = LAYOUTS.find((x) => x.slots === Number(e.target.value));
                if (l) run(layout.mutateAsync({ slots: l.slots, cols: l.cols }));
              }}
              sx={{ minWidth: 140 }}
            >
              {LAYOUTS.map((l) => (
                <MenuItem key={l.slots} value={l.slots}>{l.label}</MenuItem>
              ))}
            </TextField>
            {data.leftPageNumber != null && (
              <Button size="small" color="error" onClick={() => run(removePage.mutateAsync(data.leftPageNumber!))}>
                Remove page {data.leftPageNumber}
              </Button>
            )}
            {data.rightPageNumber != null && (
              <Button size="small" color="error" onClick={() => run(removePage.mutateAsync(data.rightPageNumber!))}>
                Remove page {data.rightPageNumber}
              </Button>
            )}
          </Stack>
        </Paper>
      )}
      {error && <Alert severity="error" onClose={() => setError(null)}>{error}</Alert>}

      {/* Spread navigation */}
      <Stack direction="row" spacing={1} alignItems="center">
        <Button size="small" disabled={spread <= 0} onClick={() => setSpread((s) => Math.max(0, s - 1))}>
          ‹ Prev
        </Button>
        <Tabs value={data.spreadIndex} onChange={(_, v) => setSpread(v)} variant="scrollable" scrollButtons="auto" sx={{ flexGrow: 1, minHeight: 36 }}>
          {data.spreadTabs.map((t) => (
            <Tab key={t.index} value={t.index} label={t.label} sx={{ minHeight: 36 }} />
          ))}
        </Tabs>
        <Button size="small" disabled={spread >= data.totalSpreads - 1} onClick={() => setSpread((s) => Math.min(data.totalSpreads - 1, s + 1))}>
          Next ›
        </Button>
      </Stack>

      {/* Spread */}
      <Stack direction="row" spacing={2}>
        {data.leftPageNumber != null ? (
          <SlotGrid
            slots={data.leftSlots}
            columns={data.columns}
            pageNumber={data.leftPageNumber}
            editMode={editMode}
            onDragCard={setDragLotId}
            onDropSlot={dropOnSlot}
          />
        ) : (
          <Box sx={{ flex: 1 }} />
        )}
        {data.rightPageNumber != null && (
          <SlotGrid
            slots={data.rightSlots}
            columns={data.columns}
            pageNumber={data.rightPageNumber}
            editMode={editMode}
            onDragCard={setDragLotId}
            onDropSlot={dropOnSlot}
          />
        )}
      </Stack>

      {editMode && (
        <UnplacedTray
          cards={unplaced.data ?? []}
          onDragCard={setDragLotId}
          onDropUnassign={dropUnassign}
        />
      )}
    </Stack>
  );
}
