import { useEffect, useMemo, useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Autocomplete,
  Box,
  Button,
  Checkbox,
  Chip,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Divider,
  FormControlLabel,
  IconButton,
  MenuItem,
  Paper,
  Stack,
  Switch,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material';
import AddPhotoAlternateIcon from '@mui/icons-material/AddPhotoAlternate';
import ArrowDownwardIcon from '@mui/icons-material/ArrowDownward';
import ArrowUpwardIcon from '@mui/icons-material/ArrowUpward';
import CameraAltIcon from '@mui/icons-material/CameraAlt';
import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import CloseIcon from '@mui/icons-material/Close';
import EditIcon from '@mui/icons-material/Edit';
import PlaceIcon from '@mui/icons-material/Place';
import SearchIcon from '@mui/icons-material/Search';
import VideocamIcon from '@mui/icons-material/Videocam';
import ZoomInIcon from '@mui/icons-material/ZoomIn';
import { api } from '../api/client';
import { useGame } from '../context/GameContext';
import { LocationPickerDialog } from '../components/dialogs/LocationPickerDialog';
import { WebcamScanDialog } from '../components/dialogs/WebcamScanDialog';
import { ScanValueBadges } from '../lib/scanBadges';
import type { ScanBadgeSettingsDto, ScanMatchDto, ScanSearchResultDto } from '../api/types';

const CONDITIONS = ['NM', 'LP', 'MP', 'HP', 'DMG'];

type ItemStatus = 'matching' | 'done' | 'error';

/** Per-copy properties the user can set on a scan (defaults seeded from the batch controls, then
 * overridable per item or in bulk before commit). */
interface ItemProps {
  condition: string;
  isFoil: boolean;
  foilType: string | null;
  quantity: number;
  /** Kept as a string for the text field; '' means "no purchase price". */
  purchasePrice: string;
  tags: string[];
  note: string;
}

interface ScanItem extends ItemProps {
  key: string;
  fileName: string;
  previewUrl: string;
  file: File;
  status: ItemStatus;
  match?: ScanMatchDto;
  /** A manual correction chosen from the catalog search; overrides `match` when committing. */
  override?: ScanSearchResultDto;
  error?: string;
  /** Whether this card is included in the commit. */
  include: boolean;
  /** The user eyeballed the scan vs. art and confirmed the match (or corrected it). */
  verified?: boolean;
}

let seq = 0;

/** A browser can't render TIFF in an <img>, so we skip the local blob preview and use the
 * server-rendered JPEG preview (ScanMatchDto.scanPreviewDataUri) that the match returns instead. */
function isTiff(file: File): boolean {
  return (
    file.type === 'image/tiff' ||
    file.type === 'image/tif' ||
    file.type === 'image/x-tiff' ||
    /\.tiff?$/i.test(file.name)
  );
}

/** The identity fields for an item, preferring a manual correction over the auto-match. */
function identityOf(item: ScanItem) {
  if (item.override) {
    const o = item.override;
    return {
      gameCardId: o.gameCardId,
      name: o.name,
      setCode: o.setCode,
      setName: o.setName,
      collectorNumber: o.collectorNumber,
      rarity: o.rarity,
      imageUri: o.imageUri ?? null,
    };
  }
  const m = item.match;
  if (m?.matched) {
    return {
      gameCardId: m.gameCardId ?? '',
      name: m.name ?? '',
      setCode: m.setCode ?? '',
      setName: m.setName ?? '',
      collectorNumber: m.collectorNumber ?? '',
      rarity: m.rarity ?? '',
      imageUri: m.imageUri ?? null,
    };
  }
  return null;
}

/** Sortable/filterable name for an item, falling back to the file name for unmatched scans. */
function itemName(item: ScanItem): string {
  return identityOf(item)?.name ?? item.fileName;
}

/** Match confidence as a number, or null when there's no auto-match confidence (unmatched, still
 * matching, errored, or hand-corrected — corrections carry no confidence score). */
function itemConfidence(item: ScanItem): number | null {
  if (item.override) return null;
  const m = item.match;
  if (!m?.matched) return null;
  return m.confidence ?? 0;
}

/** Matched-card market value, or null when unknown (unmatched, corrected, or no price). */
function itemPrice(item: ScanItem): number | null {
  if (item.override) return null;
  return item.match?.marketPrice ?? null;
}

type SortKey = 'none' | 'name' | 'confidence' | 'value';
type SortDir = 'asc' | 'desc';
type CheckedFilter = 'all' | 'checked' | 'unchecked';

interface ScanListControls {
  checked: CheckedFilter;
  name: string;
  /** Minimum confidence % to show; '' means no confidence floor. */
  minConfidence: string;
  /** Minimum market value to show; '' means no price floor. */
  minPrice: string;
  sortKey: SortKey;
  sortDir: SortDir;
}

const DEFAULT_CONTROLS: ScanListControls = {
  checked: 'all',
  name: '',
  minConfidence: '',
  minPrice: '',
  sortKey: 'none',
  sortDir: 'asc',
};

/** Apply the filter half of the list controls to a single item. */
function passesFilter(item: ScanItem, c: ScanListControls): boolean {
  if (c.checked === 'checked' && !item.include) return false;
  if (c.checked === 'unchecked' && item.include) return false;
  if (c.name.trim()) {
    if (!itemName(item).toLowerCase().includes(c.name.trim().toLowerCase())) return false;
  }
  if (c.minConfidence.trim() !== '') {
    const floor = Number(c.minConfidence);
    const conf = itemConfidence(item);
    if (conf === null || conf < floor) return false;
  }
  if (c.minPrice.trim() !== '') {
    const floor = Number(c.minPrice);
    const price = itemPrice(item);
    if (price === null || price < floor) return false;
  }
  return true;
}

/** Filter then sort `items` per the list controls. Sort is stable and leaves the original scan order
 * (newest-first) intact when the sort key is 'none'; items missing a sort value sort last. */
function applyControls(items: ScanItem[], c: ScanListControls): ScanItem[] {
  const filtered = items.filter((it) => passesFilter(it, c));
  if (c.sortKey === 'none') return filtered;
  const dir = c.sortDir === 'asc' ? 1 : -1;
  const valueOf = (it: ScanItem): number | string | null => {
    switch (c.sortKey) {
      case 'name':
        return itemName(it).toLowerCase();
      case 'confidence':
        return itemConfidence(it);
      case 'value':
        return itemPrice(it);
      default:
        return null;
    }
  };
  return filtered
    .map((it, i) => ({ it, i }))
    .sort((a, b) => {
      const av = valueOf(a.it);
      const bv = valueOf(b.it);
      // Missing values always sink to the bottom regardless of sort direction.
      if (av === null && bv === null) return a.i - b.i;
      if (av === null) return 1;
      if (bv === null) return -1;
      const cmp = typeof av === 'string' ? av.localeCompare(bv as string) : av - (bv as number);
      return cmp !== 0 ? cmp * dir : a.i - b.i; // Stable tiebreak by original index.
    })
    .map((x) => x.it);
}

function ConfidenceChip({ item }: { item: ScanItem }) {
  if (item.override) return <Chip size="small" color="info" label="Corrected" />;
  if (item.status === 'matching') return <CircularProgress size={18} />;
  if (item.status === 'error') return <Chip size="small" color="error" label="Error" />;
  const m = item.match;
  if (!m?.matched) return <Chip size="small" color="error" label="No match" />;
  const c = m.confidence ?? 0;
  const color = c >= 50 ? 'success' : c >= 15 ? 'warning' : 'error';
  return <Chip size="small" color={color} label={`${Math.round(c)}%`} />;
}

/** Inline catalog search used to correct a bad/absent match. Searches by name and/or collector
 * number; results are scoped to the chosen "Sets (art fallback)" (`setCodes`, empty ⇒ all sets) —
 * that selection is the single source of truth for which sets to look through, so there's no
 * per-search set picker. Results are uncapped server-side, so every printing shows. */
function CorrectionSearch({
  game,
  setCodes,
  setNames,
  onPick,
}: {
  game: string;
  setCodes: string[];
  setNames: string;
  onPick: (r: ScanSearchResultDto) => void;
}) {
  const [q, setQ] = useState('');
  const [cn, setCn] = useState('');
  // Debounce so fast typing doesn't fan out overlapping requests (which previously raced on the
  // server's shared DbContext and returned spurious "No matches").
  const [dq, setDq] = useState('');
  const [dcn, setDcn] = useState('');
  useEffect(() => {
    const t = setTimeout(() => {
      setDq(q);
      setDcn(cn);
    }, 250);
    return () => clearTimeout(t);
  }, [q, cn]);
  const active = dq.trim().length >= 2 || dcn.trim() !== '';
  const search = useQuery({
    queryKey: ['scan-search', game, dq, dcn, setCodes],
    queryFn: () => api.scanSearch(game, dq, setCodes, dcn.trim() || undefined),
    enabled: active,
  });
  return (
    <Box sx={{ mt: 1 }}>
      <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
        <TextField
          size="small"
          autoFocus
          placeholder={`Search ${game} by name…`}
          value={q}
          onChange={(e) => setQ(e.target.value)}
          sx={{ flex: '1 1 200px' }}
        />
        <TextField
          size="small"
          label="Collector #"
          value={cn}
          onChange={(e) => setCn(e.target.value)}
          sx={{ width: 120 }}
        />
      </Stack>
      <Typography variant="caption" color="text.secondary" sx={{ mt: 0.5, display: 'block' }}>
        {setCodes.length
          ? `Searching within: ${setNames}`
          : 'Searching all sets — pick “Sets (art fallback)” above to narrow.'}
      </Typography>
      {search.isFetching && (
        <Typography variant="caption" color="text.secondary" sx={{ mt: 1, display: 'block' }}>
          Searching…
        </Typography>
      )}
      {search.data && (
        <>
          <Typography variant="caption" color="text.secondary" sx={{ mt: 1, display: 'block' }}>
            {search.data.length === 0
              ? 'No matches.'
              : `${search.data.length} match${search.data.length === 1 ? '' : 'es'}`}
          </Typography>
          {search.data.length > 0 && (
            <Stack sx={{ mt: 0.5, maxHeight: 320, overflowY: 'auto' }} spacing={0.5}>
              {search.data.map((r) => (
                <Button
                  key={`${r.gameCardId}-${r.setCode}-${r.collectorNumber}`}
                  size="small"
                  variant="text"
                  startIcon={
                    r.imageUri ? (
                      <Box
                        component="img"
                        src={r.imageUri}
                        alt=""
                        sx={{ width: 24, height: 34, objectFit: 'contain', borderRadius: 0.5 }}
                      />
                    ) : undefined
                  }
                  sx={{ justifyContent: 'flex-start', textTransform: 'none' }}
                  onClick={() => onPick(r)}
                >
                  {r.name} · {r.setCode.toUpperCase()} #{r.collectorNumber}
                  {r.rarity ? ` · ${r.rarity}` : ''}
                </Button>
              ))}
            </Stack>
          )}
        </>
      )}
    </Box>
  );
}

/** One labelled image pane (half-width) in the detail-panel compare row. */
function ImagePane({
  label,
  src,
  alt,
  placeholder,
}: {
  label: string;
  src?: string | null;
  alt: string;
  placeholder?: string;
}) {
  return (
    <Stack spacing={0.5} sx={{ flex: 1, minWidth: 0, alignItems: 'center' }}>
      <Typography variant="caption" color="text.secondary" sx={{ fontWeight: 600 }}>
        {label}
      </Typography>
      <Box
        sx={{
          width: '100%',
          height: { xs: 260, sm: 340, md: 420 },
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          bgcolor: 'action.hover',
          borderRadius: 1,
          overflow: 'hidden',
        }}
      >
        {src ? (
          <Box
            component="img"
            src={src}
            alt={alt}
            sx={{ maxWidth: '100%', maxHeight: '100%', objectFit: 'contain' }}
          />
        ) : (
          <Typography variant="body2" color="text.secondary">
            {placeholder ?? '—'}
          </Typography>
        )}
      </Box>
    </Stack>
  );
}

/** A letterboxed thumbnail used in the master list. */
function Thumb({ src, alt }: { src?: string | null; alt: string }) {
  return (
    <Box
      sx={{
        width: 84,
        height: 116,
        flexShrink: 0,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        bgcolor: 'action.hover',
        borderRadius: 1,
        overflow: 'hidden',
      }}
    >
      {src ? (
        <Box
          component="img"
          src={src}
          alt={alt}
          sx={{ maxWidth: '100%', maxHeight: '100%', objectFit: 'contain' }}
        />
      ) : (
        <Typography variant="caption" color="text.secondary">
          —
        </Typography>
      )}
    </Box>
  );
}

/** A one-line summary of an item's per-copy properties, shown on the master row. */
function propsSummary(item: ScanItem): string {
  const parts = [item.condition];
  if (item.isFoil) parts.push(item.foilType ? `Foil (${item.foilType})` : 'Foil');
  if (item.quantity > 1) parts.push(`×${item.quantity}`);
  if (item.tags.length) parts.push(item.tags.join(', '));
  if (item.note.trim()) parts.push('📝');
  return parts.join(' · ');
}

/** Master-list row: scan thumbnail beside matched-art thumbnail + identity + status. */
function MasterRow({
  item,
  selected,
  badgeSettings,
  onSelect,
  onToggle,
}: {
  item: ScanItem;
  selected: boolean;
  badgeSettings?: ScanBadgeSettingsDto;
  onSelect: () => void;
  onToggle: (v: boolean, shiftKey: boolean) => void;
}) {
  const id = identityOf(item);
  // Badges reflect the auto-match; once the user hand-corrects a card we no longer have its price /
  // ownership status, so they're suppressed for overrides.
  const badgeMatch = item.override ? undefined : item.match;
  // Whether Shift was held for the interaction that is about to fire onChange. Set from the mouse
  // (onClick) and keyboard (onKeyDown, for Space-toggle) so range-select works either way, and never
  // goes stale between a mouse click and a later keyboard toggle.
  const shiftHeld = useRef(false);
  return (
    <Box
      onClick={onSelect}
      sx={{
        p: 1.5,
        display: 'flex',
        gap: 1.5,
        alignItems: 'center',
        cursor: 'pointer',
        borderLeft: 4,
        borderColor: selected ? 'primary.main' : 'transparent',
        bgcolor: selected ? 'action.selected' : 'transparent',
        '&:hover': { bgcolor: selected ? 'action.selected' : 'action.hover' },
      }}
    >
      <Checkbox
        checked={item.include}
        disabled={!id}
        // Suppress the browser's shift-click text selection across rows without blocking the toggle.
        onMouseDown={(e) => {
          if (e.shiftKey) e.preventDefault();
        }}
        onClick={(e) => {
          e.stopPropagation();
          shiftHeld.current = e.shiftKey;
        }}
        onKeyDown={(e) => {
          shiftHeld.current = e.shiftKey;
        }}
        onChange={(e) => onToggle(e.target.checked, shiftHeld.current)}
        sx={{ p: 0 }}
      />
      <Stack direction="row" spacing={0.75}>
        <Thumb src={item.previewUrl} alt="uploaded scan" />
        <Thumb src={id?.imageUri ?? null} alt="matched art" />
      </Stack>
      <Box sx={{ minWidth: 0, flex: 1 }}>
        <Stack direction="row" spacing={0.5} alignItems="center">
          {item.verified && <CheckCircleIcon color="success" sx={{ fontSize: 20 }} />}
          <Typography variant="subtitle1" noWrap sx={{ fontWeight: 600 }}>
            {id?.name ?? item.fileName}
          </Typography>
        </Stack>
        <Typography variant="body2" color="text.secondary" noWrap display="block">
          {id
            ? `${id.setName} · ${id.setCode.toUpperCase()} #${id.collectorNumber}`
            : (item.error ?? 'No match')}
        </Typography>
        <Typography variant="caption" color="text.secondary" noWrap display="block">
          {propsSummary(item)}
        </Typography>
        <Stack direction="row" spacing={1} alignItems="center" sx={{ mt: 0.5 }}>
          <ConfidenceChip item={item} />
          <ScanValueBadges
            isNew={badgeMatch?.isNew}
            price={badgeMatch?.marketPrice}
            settings={badgeSettings}
          />
        </Stack>
      </Box>
    </Box>
  );
}

/** The per-copy property editors, reused by the detail panel and (a subset) the bulk dialog. */
function PropertyFields({
  props,
  onChange,
  foilTypeOptions,
  tagOptions,
}: {
  props: ItemProps;
  onChange: (patch: Partial<ItemProps>) => void;
  foilTypeOptions: string[];
  tagOptions: string[];
}) {
  return (
    <Stack spacing={2}>
      <Stack direction="row" spacing={2} flexWrap="wrap" useFlexGap>
        <TextField
          select
          size="small"
          label="Condition"
          value={props.condition}
          onChange={(e) => onChange({ condition: e.target.value })}
          sx={{ minWidth: 120 }}
        >
          {CONDITIONS.map((c) => (
            <MenuItem key={c} value={c}>
              {c}
            </MenuItem>
          ))}
        </TextField>
        <TextField
          size="small"
          type="number"
          label="Quantity"
          value={props.quantity}
          onChange={(e) => onChange({ quantity: Math.max(1, Number(e.target.value) || 1) })}
          inputProps={{ min: 1 }}
          sx={{ width: 110 }}
        />
        <TextField
          size="small"
          type="number"
          label="Purchase price"
          value={props.purchasePrice}
          onChange={(e) => onChange({ purchasePrice: e.target.value })}
          inputProps={{ step: '0.01', min: 0 }}
          sx={{ width: 140 }}
        />
      </Stack>
      <Stack direction="row" spacing={2} alignItems="center" flexWrap="wrap" useFlexGap>
        <FormControlLabel
          control={
            <Switch
              checked={props.isFoil}
              onChange={(e) => onChange({ isFoil: e.target.checked })}
            />
          }
          label="Foil"
        />
        {props.isFoil && (
          <Autocomplete
            freeSolo
            size="small"
            options={foilTypeOptions}
            value={props.foilType ?? ''}
            onChange={(_, v) => onChange({ foilType: v || null })}
            onInputChange={(_, v) => onChange({ foilType: v || null })}
            sx={{ minWidth: 200 }}
            renderInput={(p) => <TextField {...p} label="Foil type" />}
          />
        )}
      </Stack>
      <Autocomplete
        multiple
        freeSolo
        size="small"
        options={tagOptions}
        value={props.tags}
        onChange={(_, v) => onChange({ tags: v })}
        renderInput={(p) => <TextField {...p} label="Tags" />}
      />
      <TextField
        size="small"
        label="Note"
        multiline
        minRows={2}
        value={props.note}
        onChange={(e) => onChange({ note: e.target.value })}
        placeholder="e.g. signed, played, misprint…"
      />
    </Stack>
  );
}

/** Detail panel for the selected scan: compare + verify/correct/remove + per-copy properties. */
function DetailPanel({
  item,
  game,
  artSets,
  foilTypeOptions,
  tagOptions,
  badgeSettings,
  onToggle,
  onVerify,
  onCorrect,
  onRemove,
  onProps,
}: {
  item: ScanItem;
  game: string;
  artSets: { setCode: string; setName: string }[];
  foilTypeOptions: string[];
  tagOptions: string[];
  badgeSettings?: ScanBadgeSettingsDto;
  onToggle: (v: boolean) => void;
  onVerify: () => void;
  onCorrect: (r: ScanSearchResultDto) => void;
  onRemove: () => void;
  onProps: (patch: Partial<ItemProps>) => void;
}) {
  const [correcting, setCorrecting] = useState(false);
  const [scanOpen, setScanOpen] = useState(false);
  const id = identityOf(item);

  return (
    <Paper variant="outlined" sx={{ p: 3 }}>
      <Stack spacing={2}>
        {/* Side-by-side compare at the top: uploaded scan vs. matched art, each half width. */}
        <Stack direction="row" spacing={2}>
          <ImagePane label="Uploaded scan" src={item.previewUrl} alt={item.fileName} />
          <ImagePane
            label="Matched art"
            src={id?.imageUri ?? null}
            alt={id?.name ?? 'No match'}
            placeholder={item.status === 'matching' ? 'Matching…' : 'No match'}
          />
        </Stack>

        <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
          <ConfidenceChip item={item} />
          {!item.override && (
            <ScanValueBadges
              isNew={item.match?.isNew}
              price={item.match?.marketPrice}
              settings={badgeSettings}
              size="medium"
            />
          )}
          {item.verified && <Chip size="small" color="success" label="Verified" />}
          <Box sx={{ flexGrow: 1 }} />
          <Button variant="outlined" startIcon={<ZoomInIcon />} onClick={() => setScanOpen(true)}>
            View scan
          </Button>
        </Stack>

        {id ? (
          <Box>
            <Typography variant="h5">{id.name}</Typography>
            <Typography variant="subtitle1" color="text.secondary">
              {id.setName} · {id.setCode.toUpperCase()} #{id.collectorNumber}
              {id.rarity ? ` · ${id.rarity}` : ''}
            </Typography>
          </Box>
        ) : (
          <Alert severity="warning">
            No confident match. Use “Search catalog” to pick the correct card, or “View scan” to read
            it.
          </Alert>
        )}

        {correcting ? (
          <Box>
            <CorrectionSearch
              game={game}
              setCodes={artSets.map((s) => s.setCode)}
              setNames={artSets.map((s) => s.setName).join(', ')}
              onPick={(r) => {
                onCorrect(r);
                setCorrecting(false);
              }}
            />
            <Button size="small" onClick={() => setCorrecting(false)} sx={{ mt: 1 }}>
              Cancel
            </Button>
          </Box>
        ) : (
          <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
            <Button
              variant={item.verified ? 'outlined' : 'contained'}
              color="success"
              startIcon={<CheckCircleIcon />}
              disabled={!id}
              onClick={onVerify}
            >
              {item.verified ? 'Looks correct' : 'Confirm match'}
            </Button>
            <Button variant="outlined" startIcon={<SearchIcon />} onClick={() => setCorrecting(true)}>
              Search catalog
            </Button>
            {item.include ? (
              <Button variant="text" onClick={() => onToggle(false)}>
                Exclude from commit
              </Button>
            ) : (
              <Button variant="text" disabled={!id} onClick={() => onToggle(true)}>
                Include in commit
              </Button>
            )}
            <Box sx={{ flexGrow: 1 }} />
            <Button variant="text" color="error" startIcon={<CloseIcon />} onClick={onRemove}>
              Remove
            </Button>
          </Stack>
        )}

        <Divider textAlign="left">
          <Typography variant="caption" color="text.secondary">
            Card properties
          </Typography>
        </Divider>
        <PropertyFields
          props={item}
          onChange={onProps}
          foilTypeOptions={foilTypeOptions}
          tagOptions={tagOptions}
        />

        <Typography variant="caption" color="text.secondary" sx={{ wordBreak: 'break-all' }}>
          {item.fileName}
        </Typography>
      </Stack>

      {/* Zoom-to-read popup of the uploaded scan. */}
      <Dialog open={scanOpen} onClose={() => setScanOpen(false)} maxWidth="lg">
        <DialogContent sx={{ p: 1, position: 'relative', bgcolor: 'action.hover' }}>
          <IconButton
            onClick={() => setScanOpen(false)}
            sx={{ position: 'absolute', top: 8, right: 8, bgcolor: 'background.paper', boxShadow: 1 }}
          >
            <CloseIcon />
          </IconButton>
          <Box
            component="img"
            src={item.previewUrl}
            alt={item.fileName}
            sx={{ display: 'block', maxWidth: '88vw', maxHeight: '85vh', objectFit: 'contain' }}
          />
        </DialogContent>
      </Dialog>
    </Paper>
  );
}

/** Which fields the bulk-edit dialog will write, plus their values. Only enabled fields apply. */
interface BulkEditState {
  setCondition: boolean;
  setFoil: boolean;
  setTags: boolean;
  tagsMode: 'add' | 'replace';
  setQuantity: boolean;
  setPrice: boolean;
  setNote: boolean;
  props: ItemProps;
}

function newBulkState(): BulkEditState {
  return {
    setCondition: false,
    setFoil: false,
    setTags: false,
    tagsMode: 'add',
    setQuantity: false,
    setPrice: false,
    setNote: false,
    props: { condition: 'NM', isFoil: false, foilType: null, quantity: 1, purchasePrice: '', tags: [], note: '' },
  };
}

/** Apply the enabled bulk fields onto an existing item's properties. */
function applyBulk(state: BulkEditState, item: ScanItem): Partial<ItemProps> {
  const patch: Partial<ItemProps> = {};
  if (state.setCondition) patch.condition = state.props.condition;
  if (state.setFoil) {
    patch.isFoil = state.props.isFoil;
    patch.foilType = state.props.isFoil ? state.props.foilType : null;
  }
  if (state.setQuantity) patch.quantity = state.props.quantity;
  if (state.setPrice) patch.purchasePrice = state.props.purchasePrice;
  if (state.setNote) patch.note = state.props.note;
  if (state.setTags) {
    patch.tags =
      state.tagsMode === 'replace'
        ? [...state.props.tags]
        : [...new Set([...item.tags, ...state.props.tags])];
  }
  return patch;
}

function BulkEditDialog({
  open,
  count,
  foilTypeOptions,
  tagOptions,
  onApply,
  onClose,
}: {
  open: boolean;
  count: number;
  foilTypeOptions: string[];
  tagOptions: string[];
  onApply: (state: BulkEditState) => void;
  onClose: () => void;
}) {
  const [state, setState] = useState<BulkEditState>(newBulkState);
  // Reset the form each time the dialog opens so it never carries a stale selection.
  useEffect(() => {
    if (open) setState(newBulkState());
  }, [open]);

  const patchProps = (patch: Partial<ItemProps>) =>
    setState((s) => ({ ...s, props: { ...s.props, ...patch } }));
  const anyEnabled =
    state.setCondition || state.setFoil || state.setTags || state.setQuantity || state.setPrice || state.setNote;

  const row = (enabled: boolean, toggle: (v: boolean) => void, control: React.ReactNode) => (
    <Stack direction="row" spacing={2} alignItems="center">
      <Checkbox checked={enabled} onChange={(e) => toggle(e.target.checked)} sx={{ p: 0 }} />
      <Box sx={{ flex: 1, opacity: enabled ? 1 : 0.5, pointerEvents: enabled ? 'auto' : 'none' }}>
        {control}
      </Box>
    </Stack>
  );

  return (
    <Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth>
      <DialogTitle>Edit {count} selected card{count === 1 ? '' : 's'}</DialogTitle>
      <DialogContent dividers>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          Tick a property to apply it to every checked card. Unticked properties are left unchanged.
        </Typography>
        <Stack spacing={2}>
          {row(
            state.setCondition,
            (v) => setState((s) => ({ ...s, setCondition: v })),
            <TextField
              select
              size="small"
              fullWidth
              label="Condition"
              value={state.props.condition}
              onChange={(e) => patchProps({ condition: e.target.value })}
            >
              {CONDITIONS.map((c) => (
                <MenuItem key={c} value={c}>
                  {c}
                </MenuItem>
              ))}
            </TextField>,
          )}
          {row(
            state.setFoil,
            (v) => setState((s) => ({ ...s, setFoil: v })),
            <Stack direction="row" spacing={2} alignItems="center">
              <FormControlLabel
                control={
                  <Switch
                    checked={state.props.isFoil}
                    onChange={(e) => patchProps({ isFoil: e.target.checked })}
                  />
                }
                label="Foil"
              />
              {state.props.isFoil && (
                <Autocomplete
                  freeSolo
                  size="small"
                  options={foilTypeOptions}
                  value={state.props.foilType ?? ''}
                  onChange={(_, v) => patchProps({ foilType: v || null })}
                  onInputChange={(_, v) => patchProps({ foilType: v || null })}
                  sx={{ minWidth: 200 }}
                  renderInput={(p) => <TextField {...p} label="Foil type" />}
                />
              )}
            </Stack>,
          )}
          {row(
            state.setQuantity,
            (v) => setState((s) => ({ ...s, setQuantity: v })),
            <TextField
              size="small"
              type="number"
              label="Quantity"
              value={state.props.quantity}
              onChange={(e) => patchProps({ quantity: Math.max(1, Number(e.target.value) || 1) })}
              inputProps={{ min: 1 }}
            />,
          )}
          {row(
            state.setPrice,
            (v) => setState((s) => ({ ...s, setPrice: v })),
            <TextField
              size="small"
              type="number"
              fullWidth
              label="Purchase price"
              value={state.props.purchasePrice}
              onChange={(e) => patchProps({ purchasePrice: e.target.value })}
              inputProps={{ step: '0.01', min: 0 }}
            />,
          )}
          {row(
            state.setTags,
            (v) => setState((s) => ({ ...s, setTags: v })),
            <Stack direction="row" spacing={1} alignItems="flex-start">
              <TextField
                select
                size="small"
                label="Mode"
                value={state.tagsMode}
                onChange={(e) => setState((s) => ({ ...s, tagsMode: e.target.value as 'add' | 'replace' }))}
                sx={{ width: 120 }}
              >
                <MenuItem value="add">Add</MenuItem>
                <MenuItem value="replace">Replace</MenuItem>
              </TextField>
              <Autocomplete
                multiple
                freeSolo
                size="small"
                sx={{ flex: 1 }}
                options={tagOptions}
                value={state.props.tags}
                onChange={(_, v) => patchProps({ tags: v })}
                renderInput={(p) => <TextField {...p} label="Tags" />}
              />
            </Stack>,
          )}
          {row(
            state.setNote,
            (v) => setState((s) => ({ ...s, setNote: v })),
            <TextField
              size="small"
              fullWidth
              label="Note"
              multiline
              minRows={2}
              value={state.props.note}
              onChange={(e) => patchProps({ note: e.target.value })}
            />,
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" disabled={!anyEnabled} onClick={() => onApply(state)}>
          Apply to {count}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

export function ScanPage() {
  const qc = useQueryClient();
  const { game: contextGame } = useGame();
  const [game, setGame] = useState(contextGame ?? 'Mtg');
  // Multiple art-fallback sets: matching is constrained to the union of these (empty = all sets).
  const [artSets, setArtSets] = useState<{ setCode: string; setName: string }[]>([]);
  const [isFoil, setIsFoil] = useState(false);
  const [condition, setCondition] = useState('NM');
  const [containerId, setContainerId] = useState<number | ''>('');
  const [pickerOpen, setPickerOpen] = useState(false);
  const [webcamOpen, setWebcamOpen] = useState(false);
  const [bulkOpen, setBulkOpen] = useState(false);
  const [items, setItems] = useState<ScanItem[]>([]);
  const [selectedKey, setSelectedKey] = useState<string | null>(null);
  const [controls, setControls] = useState<ScanListControls>(DEFAULT_CONTROLS);
  const fileInput = useRef<HTMLInputElement>(null);
  const cameraInput = useRef<HTMLInputElement>(null);

  const selectedItem = items.find((it) => it.key === selectedKey) ?? null;

  // The filtered + sorted view of the scans. Everything the toolbar acts on (Confirm / Add, the
  // select-all header, bulk edit, and the batch counts) is scoped to these visible items, so a
  // filtered list only ever confirms/commits what the user can actually see.
  const visibleItems = useMemo(() => applyControls(items, controls), [items, controls]);
  const visibleKeys = useMemo(() => new Set(visibleItems.map((it) => it.key)), [visibleItems]);
  const controlsActive =
    controls.checked !== 'all' ||
    controls.name.trim() !== '' ||
    controls.minConfidence.trim() !== '' ||
    controls.minPrice.trim() !== '';

  // Keep a valid selection: default to the first item, and re-point if the selected one is removed.
  useEffect(() => {
    if (items.length === 0) {
      if (selectedKey !== null) setSelectedKey(null);
    } else if (!items.some((it) => it.key === selectedKey)) {
      setSelectedKey(items[0].key);
    }
  }, [items, selectedKey]);

  const updateItem = (key: string, patch: Partial<ScanItem>) =>
    setItems((prev) => prev.map((it) => (it.key === key ? { ...it, ...patch } : it)));

  // Anchor for Shift-click range selection: the key of the last checkbox toggled by a plain click.
  // A Shift-click sets every selectable row between the anchor and the clicked row to the clicked
  // row's new state; the anchor only moves on a plain click, so consecutive Shift-clicks re-range
  // from the same origin (standard file-explorer / Gmail behaviour).
  const anchorKeyRef = useRef<string | null>(null);
  const toggleInclude = (key: string, checked: boolean, shiftKey: boolean) => {
    // Range selection walks the VISIBLE order so a Shift-click never toggles rows hidden by a filter.
    const idx = visibleItems.findIndex((it) => it.key === key);
    const anchorIdx = anchorKeyRef.current
      ? visibleItems.findIndex((it) => it.key === anchorKeyRef.current)
      : -1;
    if (shiftKey && anchorIdx !== -1 && idx !== -1) {
      const [lo, hi] = anchorIdx <= idx ? [anchorIdx, idx] : [idx, anchorIdx];
      const rangeKeys = new Set(visibleItems.slice(lo, hi + 1).map((it) => it.key));
      // Only rows with a resolved identity are checkable, mirroring the single-toggle guard.
      setItems((prev) =>
        prev.map((it) =>
          rangeKeys.has(it.key) && identityOf(it) ? { ...it, include: checked } : it,
        ),
      );
      return; // Keep the anchor where it is so the range can be adjusted with another Shift-click.
    }
    setItems((prev) => prev.map((it) => (it.key === key ? { ...it, include: checked } : it)));
    anchorKeyRef.current = key;
  };

  // Remove staged scans AND free the blob URL backing each one's preview. Each scan's thumbnail is
  // an `URL.createObjectURL(file)` that pins the image in the tab's memory until explicitly revoked;
  // without this a long scanning session (removes + commits of hundreds of cards) slowly leaks that
  // memory. Revoke is idempotent, so a dev-mode double-invoked updater is harmless.
  const dropItems = (shouldDrop: (it: ScanItem) => boolean | undefined) =>
    setItems((prev) => {
      prev.forEach((it) => {
        if (shouldDrop(it)) URL.revokeObjectURL(it.previewUrl);
      });
      return prev.filter((it) => !shouldDrop(it));
    });

  // Free every remaining preview URL when the page unmounts (navigating away mid-session).
  const itemsRef = useRef(items);
  itemsRef.current = items;
  useEffect(() => () => itemsRef.current.forEach((it) => URL.revokeObjectURL(it.previewUrl)), []);

  const gamesQuery = useQuery({ queryKey: ['games'], queryFn: api.games });
  const setsQuery = useQuery({ queryKey: ['sets', game], queryFn: () => api.sets(game), enabled: !!game });
  const locations = useQuery({ queryKey: ['locations', undefined], queryFn: () => api.locations() });
  const foilTypesQuery = useQuery({
    queryKey: ['scan-foil-types', game],
    queryFn: () => api.scanFoilTypes(game),
    enabled: !!game,
  });
  const tagsQuery = useQuery({ queryKey: ['tags'], queryFn: api.tags });
  const badgeSettingsQuery = useQuery({
    queryKey: ['scan-badge-settings'],
    queryFn: api.scanBadgeSettings,
    staleTime: 5 * 60 * 1000,
  });
  const badgeSettings = badgeSettingsQuery.data;
  const foilTypeOptions = foilTypesQuery.data ?? [];
  const tagOptions = useMemo(() => (tagsQuery.data ?? []).map((t) => t.name), [tagsQuery.data]);
  const sets = setsQuery.data ?? [];

  // Reset the set filter whenever the game changes (a set only belongs to one game).
  useEffect(() => {
    setArtSets([]);
  }, [game]);

  // A scan is committable only when it is BOTH confirmed (verified) AND checked (include).
  const isCommittable = (it: ScanItem) => it.include && it.verified && !!identityOf(it);

  // Only ever act on visible items — a filtered list confirms/commits exactly what's on screen.
  const isVisibleCommittable = (it: ScanItem) => visibleKeys.has(it.key) && isCommittable(it);

  const commit = useMutation({
    mutationFn: () => {
      const payload = items.filter(isVisibleCommittable).map((it) => {
        const id = identityOf(it)!;
        return {
          ...id,
          game,
          condition: it.condition,
          isFoil: it.isFoil,
          foilType: it.isFoil ? it.foilType : null,
          quantity: it.quantity,
          purchasePrice: it.purchasePrice.trim() === '' ? null : Number(it.purchasePrice),
          note: it.note.trim() === '' ? null : it.note.trim(),
          tags: it.tags,
        };
      });
      return api.scanCommit(containerId as number, payload);
    },
    onSuccess: (res) => {
      // Drop the cards that were just committed; keep everything else (unchecked or unconfirmed).
      dropItems(isVisibleCommittable);
      qc.invalidateQueries({ queryKey: ['collection'] });
      qc.invalidateQueries({ queryKey: ['locations'] });
      qc.invalidateQueries({ queryKey: ['dashboard'] });
      return res;
    },
  });

  /** Confirm every visible checked-and-matched item at once (batch verify). */
  const confirmChecked = () =>
    setItems((prev) =>
      prev.map((it) =>
        visibleKeys.has(it.key) && it.include && identityOf(it) ? { ...it, verified: true } : it,
      ),
    );

  // --- Master-list selection helpers (visible cards only; only those with a resolved identity are
  // checkable). ---
  const selectAll = () =>
    setItems((prev) =>
      prev.map((it) => (visibleKeys.has(it.key) && identityOf(it) ? { ...it, include: true } : it)),
    );
  const selectNone = () =>
    setItems((prev) => prev.map((it) => (visibleKeys.has(it.key) ? { ...it, include: false } : it)));
  const invertSelection = () =>
    setItems((prev) =>
      prev.map((it) =>
        visibleKeys.has(it.key) && identityOf(it) ? { ...it, include: !it.include } : it,
      ),
    );

  const applyBulkEdit = (state: BulkEditState) => {
    setItems((prev) =>
      prev.map((it) =>
        visibleKeys.has(it.key) && it.include ? { ...it, ...applyBulk(state, it) } : it,
      ),
    );
    setBulkOpen(false);
  };

  function handleFiles(files: FileList | null) {
    return stageFiles(Array.from(files ?? []));
  }

  // Stage a set of images and match them with bounded concurrency. Shared by the file-input paths and
  // the webcam capture dialog (each captured card arrives here as a one-element File[]).
  async function stageFiles(chosen: File[]) {
    if (chosen.length === 0) return;
    // Seed each new scan's per-copy properties from the current batch defaults. A TIFF gets no local
    // blob preview (browsers can't render it) — the server preview arrives with the match.
    const staged: ScanItem[] = chosen.map((file) => ({
      key: `s${seq++}`,
      fileName: file.name,
      previewUrl: isTiff(file) ? '' : URL.createObjectURL(file),
      file,
      status: 'matching',
      include: false,
      condition,
      isFoil,
      foilType: null,
      quantity: 1,
      purchasePrice: '',
      tags: [],
      note: '',
    }));
    setItems((prev) => [...staged, ...prev]);
    // Auto-select the first newly-added card so the detail panel has something to show.
    setSelectedKey((cur) => cur ?? staged[0]?.key ?? null);

    // Match with BOUNDED concurrency. Firing an entire batch at once overwhelmed the server
    // (each match is heavy CPU: hashing + OCR + rotation retries) and raced the game services'
    // shared read context — the source of the batch "internal server error"s. A small worker
    // pool keeps a few matches in flight without stampeding it.
    const MAX_IN_FLIGHT = 4;
    const queue = [...staged];
    const setCodes = artSets.map((s) => s.setCode);
    async function worker() {
      for (;;) {
        const staging = queue.shift();
        if (!staging) return;
        try {
          const match = await api.scanMatch(staging.file, game, isFoil, setCodes);
          // TIFF uploads carry no local preview; adopt the server-rendered one when present.
          const patch: Partial<ScanItem> = { status: 'done', match, include: match.matched };
          if (match.scanPreviewDataUri) patch.previewUrl = match.scanPreviewDataUri;
          updateItem(staging.key, patch);
        } catch (e) {
          updateItem(staging.key, { status: 'error', error: (e as Error).message });
        }
      }
    }
    await Promise.all(Array.from({ length: Math.min(MAX_IN_FLIGHT, staged.length) }, worker));
  }

  // All batch counts are scoped to the visible (filtered) list so the buttons' numbers match what
  // they'll act on.
  const committableCount = useMemo(() => visibleItems.filter(isCommittable).length, [visibleItems]);
  // Checked + matched but not yet confirmed — the batch "Confirm checked" button targets these.
  const confirmableCount = useMemo(
    () => visibleItems.filter((it) => it.include && !it.verified && identityOf(it)).length,
    [visibleItems],
  );
  const checkedCount = useMemo(() => visibleItems.filter((it) => it.include).length, [visibleItems]);
  const selectableCount = useMemo(
    () => visibleItems.filter((it) => identityOf(it)).length,
    [visibleItems],
  );
  const stillMatching = items.some((it) => it.status === 'matching');
  const selectedLocationName = useMemo(
    () => (containerId === '' ? null : locations.data?.find((l) => l.id === containerId)?.name ?? null),
    [containerId, locations.data],
  );

  return (
    <Stack spacing={3}>
      <Typography variant="h4">Scan cards</Typography>
      <Typography variant="body2" color="text.secondary" sx={{ mt: -1 }}>
        Upload photos or scans of cards. Each image is matched against the selected game's catalog on
        the server; review the matches, correct any that are wrong, then add them to a location.
      </Typography>

      <Paper variant="outlined" sx={{ p: 2 }}>
        <Stack direction="row" spacing={2} flexWrap="wrap" useFlexGap alignItems="center">
          <TextField
            select
            size="small"
            label="Game"
            value={game}
            onChange={(e) => setGame(e.target.value)}
            sx={{ minWidth: 200 }}
          >
            {gamesQuery.data?.map((g) => (
              <MenuItem key={g.id} value={g.id}>
                {g.displayName}
              </MenuItem>
            ))}
          </TextField>
          <Autocomplete
            multiple
            size="small"
            options={sets}
            getOptionLabel={(s) => s.setName}
            isOptionEqualToValue={(a, b) => a.setCode === b.setCode}
            value={artSets}
            onChange={(_, v) => setArtSets(v)}
            sx={{ minWidth: 260, flex: '1 1 260px' }}
            renderInput={(p) => (
              <TextField {...p} label="Sets (art fallback)" placeholder={artSets.length ? '' : 'All sets'} />
            )}
          />
          <TextField
            select
            size="small"
            label="Condition"
            value={condition}
            onChange={(e) => setCondition(e.target.value)}
            sx={{ minWidth: 120 }}
          >
            {CONDITIONS.map((c) => (
              <MenuItem key={c} value={c}>
                {c}
              </MenuItem>
            ))}
          </TextField>
          <FormControlLabel
            control={<Checkbox checked={isFoil} onChange={(e) => setIsFoil(e.target.checked)} />}
            label="Foil"
          />
          <Button
            variant="contained"
            startIcon={<CameraAltIcon />}
            onClick={() => cameraInput.current?.click()}
          >
            Take photo
          </Button>
          <Button
            variant="outlined"
            startIcon={<VideocamIcon />}
            onClick={() => setWebcamOpen(true)}
          >
            Use webcam
          </Button>
          <Button
            variant="outlined"
            startIcon={<AddPhotoAlternateIcon />}
            onClick={() => fileInput.current?.click()}
          >
            Add images
          </Button>
          {/* Camera capture: on a phone this opens the rear camera directly; on desktop the
              `capture` hint is ignored and it falls back to a normal file picker. */}
          <input
            ref={cameraInput}
            type="file"
            accept="image/*"
            capture="environment"
            hidden
            onChange={(e) => {
              void handleFiles(e.target.files);
              e.target.value = '';
            }}
          />
          <input
            ref={fileInput}
            type="file"
            accept="image/jpeg,image/png,image/tiff,.tif,.tiff"
            multiple
            hidden
            onChange={(e) => {
              void handleFiles(e.target.files);
              e.target.value = '';
            }}
          />
        </Stack>
        <Typography variant="caption" color="text.secondary" sx={{ mt: 1, display: 'block' }}>
          Game / sets / condition / foil above seed each new scan; edit any card (or several at once)
          below before committing.
        </Typography>
      </Paper>

      {items.length > 0 && (
        <Paper variant="outlined" sx={{ p: 2, position: 'sticky', top: 56, zIndex: 1 }}>
          <Stack direction="row" spacing={2} alignItems="center" flexWrap="wrap" useFlexGap>
            <Button
              variant="outlined"
              startIcon={<PlaceIcon />}
              onClick={() => setPickerOpen(true)}
              sx={{ minWidth: 220, justifyContent: 'flex-start', textTransform: 'none' }}
            >
              {selectedLocationName ?? 'Add to location…'}
            </Button>
            <Button
              variant="outlined"
              color="success"
              startIcon={<CheckCircleIcon />}
              disabled={confirmableCount === 0}
              onClick={confirmChecked}
            >
              Confirm {confirmableCount} checked
            </Button>
            <Button
              variant="contained"
              disabled={
                committableCount === 0 || containerId === '' || commit.isPending || stillMatching
              }
              onClick={() => commit.mutate()}
            >
              {commit.isPending
                ? 'Adding…'
                : `Add ${committableCount} confirmed card${committableCount === 1 ? '' : 's'}`}
            </Button>
            <Typography variant="caption" color="text.secondary">
              Only confirmed &amp; checked scans are added
              {controlsActive ? ', and only those shown by the current filter' : ''}.
            </Typography>
            {commit.error && <Alert severity="error">{(commit.error as Error).message}</Alert>}
            {commit.data && (
              <Alert severity="success">Added {commit.data.imported} card(s) to your collection.</Alert>
            )}
          </Stack>
        </Paper>
      )}

      {items.length > 0 && (
        <Paper variant="outlined" sx={{ p: 2 }}>
          <Stack direction="row" spacing={2} alignItems="center" flexWrap="wrap" useFlexGap>
            <TextField
              size="small"
              label="Filter by name"
              value={controls.name}
              onChange={(e) => setControls((c) => ({ ...c, name: e.target.value }))}
              InputProps={{ startAdornment: <SearchIcon fontSize="small" sx={{ mr: 0.5, color: 'text.secondary' }} /> }}
              sx={{ flex: '1 1 200px', minWidth: 180 }}
            />
            <TextField
              select
              size="small"
              label="Show"
              value={controls.checked}
              onChange={(e) => setControls((c) => ({ ...c, checked: e.target.value as CheckedFilter }))}
              sx={{ minWidth: 150 }}
            >
              <MenuItem value="all">All</MenuItem>
              <MenuItem value="checked">Checked only</MenuItem>
              <MenuItem value="unchecked">Unchecked only</MenuItem>
            </TextField>
            <TextField
              size="small"
              type="number"
              label="Min confidence %"
              value={controls.minConfidence}
              onChange={(e) => setControls((c) => ({ ...c, minConfidence: e.target.value }))}
              inputProps={{ min: 0, max: 100 }}
              sx={{ width: 150 }}
            />
            <TextField
              size="small"
              type="number"
              label="Min value"
              value={controls.minPrice}
              onChange={(e) => setControls((c) => ({ ...c, minPrice: e.target.value }))}
              inputProps={{ step: '0.01', min: 0 }}
              sx={{ width: 130 }}
            />
            <Divider orientation="vertical" flexItem sx={{ display: { xs: 'none', sm: 'block' } }} />
            <TextField
              select
              size="small"
              label="Sort by"
              value={controls.sortKey}
              onChange={(e) => setControls((c) => ({ ...c, sortKey: e.target.value as SortKey }))}
              sx={{ minWidth: 160 }}
            >
              <MenuItem value="none">None</MenuItem>
              <MenuItem value="name">Name</MenuItem>
              <MenuItem value="confidence">Confidence</MenuItem>
              <MenuItem value="value">Market value</MenuItem>
            </TextField>
            <Tooltip title={controls.sortDir === 'asc' ? 'Ascending' : 'Descending'}>
              <span>
                <IconButton
                  size="small"
                  disabled={controls.sortKey === 'none'}
                  onClick={() =>
                    setControls((c) => ({ ...c, sortDir: c.sortDir === 'asc' ? 'desc' : 'asc' }))
                  }
                >
                  {controls.sortDir === 'asc' ? <ArrowUpwardIcon /> : <ArrowDownwardIcon />}
                </IconButton>
              </span>
            </Tooltip>
            <Box sx={{ flexGrow: 1 }} />
            {(controlsActive || controls.sortKey !== 'none') && (
              <>
                <Typography variant="caption" color="text.secondary">
                  Showing {visibleItems.length} of {items.length}
                </Typography>
                <Button size="small" onClick={() => setControls(DEFAULT_CONTROLS)}>
                  Clear
                </Button>
              </>
            )}
          </Stack>
        </Paper>
      )}

      {items.length > 0 && (
        <Box
          sx={{
            display: 'grid',
            gap: 2,
            gridTemplateColumns: { xs: '1fr', md: '440px 1fr' },
            alignItems: 'start',
          }}
        >
          {/* Master: the list of scanned cards (scan thumb + matched-art thumb per row). */}
          <Paper variant="outlined" sx={{ overflow: 'hidden', maxHeight: { md: '75vh' }, overflowY: { md: 'auto' } }}>
            {/* Selection header: check/uncheck/invert + bulk edit. */}
            <Box
              sx={{
                p: 1,
                display: 'flex',
                alignItems: 'center',
                gap: 0.5,
                flexWrap: 'wrap',
                borderBottom: 1,
                borderColor: 'divider',
                position: 'sticky',
                top: 0,
                bgcolor: 'background.paper',
                zIndex: 1,
              }}
            >
              <Tooltip title={checkedCount === selectableCount ? 'Uncheck all' : 'Check all'}>
                <span>
                  <Checkbox
                    size="small"
                    disabled={selectableCount === 0}
                    checked={selectableCount > 0 && checkedCount === selectableCount}
                    indeterminate={checkedCount > 0 && checkedCount < selectableCount}
                    onChange={(e) => (e.target.checked ? selectAll() : selectNone())}
                  />
                </span>
              </Tooltip>
              <Button size="small" onClick={selectAll} disabled={selectableCount === 0}>
                All
              </Button>
              <Button size="small" onClick={selectNone} disabled={checkedCount === 0}>
                None
              </Button>
              <Button size="small" onClick={invertSelection} disabled={selectableCount === 0}>
                Invert
              </Button>
              <Box sx={{ flexGrow: 1 }} />
              <Typography variant="caption" color="text.secondary">
                {checkedCount} checked
              </Typography>
              <Button
                size="small"
                variant="outlined"
                startIcon={<EditIcon />}
                disabled={checkedCount === 0}
                onClick={() => setBulkOpen(true)}
              >
                Edit
              </Button>
            </Box>
            <Stack divider={<Divider />}>
              {visibleItems.map((item) => (
                <MasterRow
                  key={item.key}
                  item={item}
                  selected={item.key === selectedKey}
                  badgeSettings={badgeSettings}
                  onSelect={() => setSelectedKey(item.key)}
                  onToggle={(v, shiftKey) => toggleInclude(item.key, v, shiftKey)}
                />
              ))}
              {visibleItems.length === 0 && (
                <Box sx={{ p: 3, textAlign: 'center' }}>
                  <Typography variant="body2" color="text.secondary">
                    No scans match the current filter.
                  </Typography>
                  <Button size="small" sx={{ mt: 1 }} onClick={() => setControls(DEFAULT_CONTROLS)}>
                    Clear filter
                  </Button>
                </Box>
              )}
            </Stack>
          </Paper>

          {/* Detail: the selected card's full-size compare + verify/correct/remove + properties. */}
          {selectedItem ? (
            <DetailPanel
              key={selectedItem.key}
              item={selectedItem}
              game={game}
              artSets={artSets}
              foilTypeOptions={foilTypeOptions}
              tagOptions={tagOptions}
              badgeSettings={badgeSettings}
              onToggle={(v) => updateItem(selectedItem.key, { include: v })}
              onVerify={() => updateItem(selectedItem.key, { verified: true, include: true })}
              onCorrect={(r) =>
                updateItem(selectedItem.key, { override: r, include: true, verified: true })
              }
              onRemove={() => dropItems((it) => it.key === selectedItem.key)}
              onProps={(patch) => updateItem(selectedItem.key, patch)}
            />
          ) : (
            <Paper
              variant="outlined"
              sx={{ p: 3, display: 'flex', alignItems: 'center', justifyContent: 'center' }}
            >
              <Typography color="text.secondary">Select a scanned card to review it.</Typography>
            </Paper>
          )}
        </Box>
      )}

      <BulkEditDialog
        open={bulkOpen}
        count={checkedCount}
        foilTypeOptions={foilTypeOptions}
        tagOptions={tagOptions}
        onApply={applyBulkEdit}
        onClose={() => setBulkOpen(false)}
      />

      <LocationPickerDialog
        open={pickerOpen}
        title="Add scanned cards to location"
        onPick={(id) => {
          setContainerId(id);
          setPickerOpen(false);
        }}
        onClose={() => setPickerOpen(false)}
      />
      <WebcamScanDialog
        open={webcamOpen}
        onCapture={(file) => void stageFiles([file])}
        onClose={() => setWebcamOpen(false)}
      />
    </Stack>
  );
}
