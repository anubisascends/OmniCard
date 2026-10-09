import { useEffect, useMemo, useRef, useState } from 'react';
import type { TFunction } from 'i18next';
import { useTranslation } from 'react-i18next';
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
  ListItemIcon,
  ListItemText,
  Menu,
  MenuItem,
  Paper,
  Stack,
  Switch,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material';
import AddPhotoAlternateIcon from '@mui/icons-material/AddPhotoAlternate';
import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';
import ArrowDownwardIcon from '@mui/icons-material/ArrowDownward';
import ArrowUpwardIcon from '@mui/icons-material/ArrowUpward';
import CameraAltIcon from '@mui/icons-material/CameraAlt';
import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import CloseIcon from '@mui/icons-material/Close';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import FileDownloadIcon from '@mui/icons-material/FileDownload';
import FolderZipIcon from '@mui/icons-material/FolderZip';
import EditIcon from '@mui/icons-material/Edit';
import LockIcon from '@mui/icons-material/Lock';
import PlaceIcon from '@mui/icons-material/Place';
import ReplayIcon from '@mui/icons-material/Replay';
import SearchIcon from '@mui/icons-material/Search';
import VideocamIcon from '@mui/icons-material/Videocam';
import ZoomInIcon from '@mui/icons-material/ZoomIn';
import { api, ApiError } from '../api/client';
import { useFormatters } from '../i18n/format';
import { useGame } from '../context/GameContext';
import { LocationPickerDialog } from '../components/dialogs/LocationPickerDialog';
import { WebcamScanDialog } from '../components/dialogs/WebcamScanDialog';
import { ScanValueBadges, ListReprintChip } from '../lib/scanBadges';
import { ENGLISH, LanguageChip, LanguageSelect, languageName } from '../lib/cardLanguages';
import {
  EXPORT_FORMATS,
  copyToClipboard,
  fileStamp,
  formatAvailableFor,
  textListLine,
} from '../lib/exportFormats';
import { ScanSessionStore } from '../lib/scanSessionStore';
import { BatchItemStore } from '../lib/batchItemStore';
import { usePermissions } from '../context/usePermissions';
import { ScanBatchesPanel } from '../components/ScanBatchesPanel';
import type {
  AuditCommitResultDto,
  ScanBadgeSettingsDto,
  ScanBatchCommitResultDto,
  ScanBatchDto,
  ScanBatchItemDto,
  ScanBatchItemEdit,
  ScanCommitItem,
  ScanCommitResultDto,
  ScanMatchDto,
  ScanSearchResultDto,
} from '../api/types';
import { CardHoverPreview, type CardHover } from '../components/CardHoverPreview';

const CONDITIONS = ['NM', 'LP', 'MP', 'HP', 'DMG'];

type ItemStatus = 'matching' | 'done' | 'error';

/** Per-copy properties the user can set on a scan (defaults seeded from the batch controls, then
 * overridable per item or in bulk before commit). */
interface ItemProps {
  condition: string;
  /** Printed language code of the copy ("en", "ja", …). Seeded from the session's language, then
   * from the server's match (which reads the printed language where the card shows one). */
  language: string;
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
  /** The local image (uploads only; a background-batch item's image lives on the server). */
  file?: File;
  /** The server-side item id when this scan belongs to a background batch. */
  batchItemId?: number;
  status: ItemStatus;
  match?: ScanMatchDto;
  /** A manual correction chosen from the catalog search; overrides `match` when committing. */
  override?: ScanSearchResultDto;
  error?: string;
  /** Whether this card is included in the commit. */
  include: boolean;
  /** The user eyeballed the scan vs. art and confirmed the match (or corrected it). */
  verified?: boolean;
  /** The tags in `tags` that a tag rule put there (shown with a marker; removable like any tag). */
  ruleTags?: string[];
  /** Rule tags the user removed from this item; the rules won't add them back. */
  dismissedRuleTags?: string[];
  /** The {@link ruleSignature} the item's rule tags were last worked out for. */
  ruleSig?: string;
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

/** The server payload for one scan (commit, audit-commit and export all take this shape). Only
 * call for an item with a resolved identity. */
function toCommitItem(it: ScanItem, game: string): ScanCommitItem {
  const id = identityOf(it)!;
  return {
    ...id,
    game,
    condition: it.condition,
    language: it.language,
    isFoil: it.isFoil,
    foilType: it.isFoil ? it.foilType : null,
    quantity: it.quantity,
    purchasePrice: it.purchasePrice.trim() === '' ? null : Number(it.purchasePrice),
    note: it.note.trim() === '' ? null : it.note.trim(),
    tags: it.tags,
    // Carry the scan's hash so the server records this confirmed identity for future auto-matching.
    // Present on both auto-matches and manual corrections (it.match holds the original scan result
    // even when the identity was overridden).
    scanHash: it.match?.scanHash ?? null,
  };
}

// --- Tag rules: admin-defined searches whose tags are pre-filled on matching scans. ---

const hasTag = (list: readonly string[] | undefined, tag: string) =>
  !!list?.some((x) => x.toLowerCase() === tag.toLowerCase());

/** Everything the tag rules look at for an item; when it changes, the item's rule tags are worked
 * out again. Null while the item has no identity. */
function ruleSignature(it: ScanItem, game: string): string | null {
  const id = identityOf(it);
  if (!id) return null;
  return [game, id.gameCardId, it.isFoil ? (it.foilType ?? 'foil') : '', it.condition, it.language].join('|');
}

/** Fold freshly computed rule tags into an item: rule tags that no longer apply come off, new ones go
 * on (except ones the user removed). Tags the user added themselves are left alone. */
function withRuleTags(it: ScanItem, computed: string[], sig: string): ScanItem {
  const old = it.ruleTags ?? [];
  const wanted = computed.filter((tag) => !hasTag(it.dismissedRuleTags, tag));
  // First look at this item (e.g. a background batch reopened): a matching tag already on it came
  // from the rules last time, so it's marked as a rule tag again.
  const firstLook = it.ruleSig === undefined;
  const tags = it.tags.filter((tag) => !(hasTag(old, tag) && !hasTag(wanted, tag)));
  const ruleTags: string[] = [];
  for (const tag of wanted) {
    if (!hasTag(tags, tag)) {
      tags.push(tag);
      ruleTags.push(tag);
    } else if (hasTag(old, tag) || firstLook) {
      ruleTags.push(tag);
    }
  }
  return { ...it, tags, ruleTags, ruleSig: sig };
}

/** A user edit of an item's tags. A rule tag they remove is remembered so the rules don't re-add it;
 * typing a dismissed tag back in makes it theirs. */
function withUserTags(it: ScanItem, tags: string[]): Partial<ScanItem> {
  const ruleTags = it.ruleTags ?? [];
  return {
    tags,
    ruleTags: ruleTags.filter((tag) => hasTag(tags, tag)),
    dismissedRuleTags: [
      ...(it.dismissedRuleTags ?? []).filter((tag) => !hasTag(tags, tag)),
      ...ruleTags.filter((tag) => !hasTag(tags, tag)),
    ],
  };
}

/** The item without its rule tags (tag rules switched off). */
function withoutRuleTags(it: ScanItem): ScanItem {
  if (!it.ruleTags?.length && it.ruleSig === undefined) return it;
  return { ...it, tags: it.tags.filter((tag) => !hasTag(it.ruleTags, tag)), ruleTags: [], ruleSig: undefined };
}

const APPLY_TAG_RULES_KEY = 'omnicard.scan.applyTagRules';

/** Whether this browser last had "Apply tag rules" on (default on). */
function loadApplyTagRules(): boolean {
  try {
    return localStorage.getItem(APPLY_TAG_RULES_KEY) !== 'false';
  } catch {
    return true;
  }
}

/** A scan's tags as chips; tags added by a tag rule carry a marker. */
function TagChips({ tags, ruleTags }: { tags: string[]; ruleTags?: string[] }) {
  const { t } = useTranslation();
  return (
    <>
      {tags.map((tag) =>
        hasTag(ruleTags, tag) ? (
          <Tooltip key={tag} title={t('scan.tagRules.chipHint')}>
            <Chip size="small" variant="outlined" color="secondary" icon={<AutoAwesomeIcon />} label={tag} />
          </Tooltip>
        ) : (
          <Chip key={tag} size="small" variant="outlined" label={tag} />
        ),
      )}
    </>
  );
}

// --- Background batches: the server's batch items ↔ the page's scan items. ---

const batchItemKey = (id: number) => `b${id}`;

function fromBatchItem(dto: ScanBatchItemDto): ScanItem {
  return {
    key: batchItemKey(dto.id),
    batchItemId: dto.id,
    fileName: dto.fileName,
    previewUrl: dto.imageUrl,
    status: dto.status === 'Pending' ? 'matching' : dto.status === 'Error' ? 'error' : 'done',
    match: dto.match ?? undefined,
    override: dto.override ?? undefined,
    error: dto.error ?? undefined,
    include: dto.include,
    verified: dto.verified,
    condition: dto.condition,
    language: dto.language || ENGLISH,
    isFoil: dto.isFoil,
    foilType: dto.foilType ?? null,
    quantity: dto.quantity,
    purchasePrice: dto.purchasePrice == null ? '' : String(dto.purchasePrice),
    tags: dto.tags,
    note: dto.note ?? '',
  };
}

/** The reviewer-editable fields of a batch item, as saved to the server (match results never are). */
function toBatchEdit(it: ScanItem): ScanBatchItemEdit | null {
  if (it.batchItemId == null) return null;
  const price = it.purchasePrice.trim() === '' ? null : Number(it.purchasePrice);
  return {
    id: it.batchItemId,
    include: it.include,
    verified: !!it.verified,
    override: it.override ?? null,
    condition: it.condition,
    language: it.language,
    isFoil: it.isFoil,
    foilType: it.isFoil ? it.foilType : null,
    quantity: it.quantity,
    purchasePrice: price != null && Number.isFinite(price) ? price : null,
    tags: it.tags,
    note: it.note.trim() === '' ? null : it.note.trim(),
  };
}

/**
 * Fold the latest server copy of a batch into the local list. When reviewing, local edits win: only
 * items still matching locally take the server's match result, new items (late files) are added, and
 * items gone from the server are dropped. Read-only viewers just mirror the server. Unchanged items
 * keep their object identity. `fresh` lists the items taken wholesale from the server.
 */
function mergeBatchItems(
  local: ScanItem[],
  server: ScanBatchItemDto[],
  readOnly: boolean,
): { items: ScanItem[]; fresh: ScanItem[] } {
  const byKey = new Map(local.map((it) => [it.key, it]));
  const fresh: ScanItem[] = [];
  const items = server.map((dto) => {
    const mine = byKey.get(batchItemKey(dto.id));
    if (!mine || readOnly) {
      const next = fromBatchItem(dto);
      fresh.push(next);
      return next;
    }
    if (mine.status === 'matching' && dto.status !== 'Pending') {
      const matched = fromBatchItem(dto);
      return {
        ...mine,
        status: matched.status,
        match: matched.match,
        error: matched.error,
        include: matched.include,
        language: matched.language,
      };
    }
    if (mine.status !== 'matching' && dto.status === 'Pending') return { ...mine, status: 'matching' as const };
    return mine;
  });
  return { items, fresh };
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
  const { t } = useTranslation();
  const fmt = useFormatters();
  if (item.override) return <Chip size="small" color="info" label={t('scan.status.corrected')} />;
  if (item.status === 'matching') return <CircularProgress size={18} />;
  if (item.status === 'error') return <Chip size="small" color="error" label={t('scan.status.error')} />;
  const m = item.match;
  if (!m?.matched) return <Chip size="small" color="error" label={t('scan.status.noMatch')} />;
  const c = m.confidence ?? 0;
  const color = c >= 50 ? 'success' : c >= 15 ? 'warning' : 'error';
  return <Chip size="small" color={color} label={`${fmt.number(Math.round(c))}%`} />;
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
  const { t } = useTranslation();
  const [q, setQ] = useState('');
  const [cn, setCn] = useState('');
  // Hovering a result's thumbnail pops the enlarged art (same preview as the location grids).
  const [hover, setHover] = useState<CardHover | null>(null);
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
          placeholder={t('scan.correction.searchByNamePlaceholder', { game })}
          value={q}
          onChange={(e) => setQ(e.target.value)}
          sx={{ flex: '1 1 200px' }}
        />
        <TextField
          size="small"
          label={t('common.labels.collectorNumber')}
          value={cn}
          onChange={(e) => setCn(e.target.value)}
          sx={{ width: 120 }}
        />
      </Stack>
      <Typography variant="caption" color="text.secondary" sx={{ mt: 0.5, display: 'block' }}>
        {setCodes.length
          ? t('scan.correction.searchingWithin', { sets: setNames })
          : t('scan.correction.searchingAllSets')}
      </Typography>
      {search.isFetching && (
        <Typography variant="caption" color="text.secondary" sx={{ mt: 1, display: 'block' }}>
          {t('common.states.searching')}
        </Typography>
      )}
      {search.data && (
        <>
          <Typography variant="caption" color="text.secondary" sx={{ mt: 1, display: 'block' }}>
            {search.data.length === 0
              ? t('scan.correction.noMatches')
              : t('scan.correction.matchCount', { count: search.data.length })}
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
                        onMouseEnter={(e) => setHover({ el: e.currentTarget, url: r.imageUri!, foil: false })}
                        onMouseLeave={() => setHover(null)}
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
      <CardHoverPreview hover={hover} onClose={() => setHover(null)} />
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
function propsSummary(item: ScanItem, t: TFunction): string {
  const parts = [t(`common.conditions.${item.condition}`)];
  if (item.language !== ENGLISH) parts.push(languageName(t, item.language));
  if (item.isFoil)
    parts.push(item.foilType ? t('scan.props.foilWithType', { type: item.foilType }) : t('common.labels.foil'));
  if (item.quantity > 1) parts.push(`×${item.quantity}`);
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
  readOnly = false,
}: {
  item: ScanItem;
  selected: boolean;
  badgeSettings?: ScanBadgeSettingsDto;
  onSelect: () => void;
  onToggle: (v: boolean, shiftKey: boolean) => void;
  readOnly?: boolean;
}) {
  const { t } = useTranslation();
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
        disabled={!id || readOnly}
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
        <Thumb src={item.previewUrl} alt={t('scan.alt.uploadedScan')} />
        <Thumb src={id?.imageUri ?? null} alt={t('scan.alt.matchedArt')} />
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
            : (item.error ?? t('scan.status.noMatch'))}
        </Typography>
        <Typography variant="caption" color="text.secondary" noWrap display="block">
          {propsSummary(item, t)}
        </Typography>
        <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap sx={{ mt: 0.5 }}>
          <ConfidenceChip item={item} />
          <LanguageChip language={item.language} detected={badgeMatch?.languageDetected} />
          <TagChips tags={item.tags} ruleTags={item.ruleTags} />
          <ScanValueBadges
            isNew={badgeMatch?.isNew}
            price={badgeMatch?.marketPrice}
            settings={badgeSettings}
          />
          <ListReprintChip
            isListReprint={badgeMatch?.isListReprint}
            unresolved={badgeMatch?.listReprintUnresolved}
          />
        </Stack>
      </Box>
    </Box>
  );
}

/** The per-copy property editors, reused by the detail panel and (a subset) the bulk dialog. */
function PropertyFields({
  game,
  props,
  onChange,
  foilTypeOptions,
  tagOptions,
  ruleTags,
  disabled = false,
}: {
  game: string;
  props: ItemProps;
  onChange: (patch: Partial<ItemProps>) => void;
  foilTypeOptions: string[];
  tagOptions: string[];
  /** Tags a tag rule added (marked in the tags box). */
  ruleTags?: string[];
  disabled?: boolean;
}) {
  const { t } = useTranslation();
  return (
    <Stack spacing={2}>
      <Stack direction="row" spacing={2} flexWrap="wrap" useFlexGap>
        <TextField
          select
          size="small"
          label={t('common.labels.condition')}
          value={props.condition}
          disabled={disabled}
          onChange={(e) => onChange({ condition: e.target.value })}
          sx={{ minWidth: 120 }}
        >
          {CONDITIONS.map((c) => (
            <MenuItem key={c} value={c}>
              {c}
            </MenuItem>
          ))}
        </TextField>
        <LanguageSelect
          game={game}
          value={props.language}
          disabled={disabled}
          onChange={(language) => onChange({ language })}
        />
        <TextField
          size="small"
          type="number"
          label={t('common.labels.quantity')}
          value={props.quantity}
          disabled={disabled}
          onChange={(e) => onChange({ quantity: Math.max(1, Number(e.target.value) || 1) })}
          inputProps={{ min: 1 }}
          sx={{ width: 110 }}
        />
        <TextField
          size="small"
          type="number"
          label={t('common.labels.purchasePrice')}
          value={props.purchasePrice}
          disabled={disabled}
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
              disabled={disabled}
              onChange={(e) => onChange({ isFoil: e.target.checked })}
            />
          }
          label={t('common.labels.foil')}
        />
        {props.isFoil && (
          <Autocomplete
            freeSolo
            size="small"
            options={foilTypeOptions}
            value={props.foilType ?? ''}
            disabled={disabled}
            onChange={(_, v) => onChange({ foilType: v || null })}
            onInputChange={(_, v) => onChange({ foilType: v || null })}
            sx={{ minWidth: 200 }}
            renderInput={(p) => <TextField {...p} label={t('scan.props.foilType')} />}
          />
        )}
      </Stack>
      <Autocomplete
        multiple
        freeSolo
        size="small"
        options={tagOptions}
        value={props.tags}
        disabled={disabled}
        onChange={(_, v) => onChange({ tags: v })}
        renderTags={(value, getTagProps) =>
          value.map((tag, index) => {
            const { key, ...tagProps } = getTagProps({ index });
            const fromRule = hasTag(ruleTags, tag);
            return (
              <Chip
                key={key}
                {...tagProps}
                size="small"
                label={tag}
                icon={fromRule ? <AutoAwesomeIcon /> : undefined}
                color={fromRule ? 'secondary' : 'default'}
                variant={fromRule ? 'outlined' : 'filled'}
                title={fromRule ? t('scan.tagRules.chipHint') : undefined}
              />
            );
          })
        }
        renderInput={(p) => <TextField {...p} label={t('common.labels.tags')} />}
      />
      <TextField
        size="small"
        label={t('common.labels.note')}
        multiline
        minRows={2}
        value={props.note}
        disabled={disabled}
        onChange={(e) => onChange({ note: e.target.value })}
        placeholder={t('scan.props.notePlaceholder')}
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
  readOnly = false,
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
  /** Viewing someone else's background batch: hide the actions and lock the fields. */
  readOnly?: boolean;
}) {
  const { t } = useTranslation();
  const [correcting, setCorrecting] = useState(false);
  const [scanOpen, setScanOpen] = useState(false);
  const id = identityOf(item);

  return (
    <Paper variant="outlined" sx={{ p: 3 }}>
      <Stack spacing={2}>
        {/* Side-by-side compare at the top: uploaded scan vs. matched art, each half width. */}
        <Stack direction="row" spacing={2}>
          <ImagePane label={t('scan.detail.uploadedScan')} src={item.previewUrl} alt={item.fileName} />
          <ImagePane
            label={t('scan.detail.matchedArt')}
            src={id?.imageUri ?? null}
            alt={id?.name ?? t('scan.status.noMatch')}
            placeholder={item.status === 'matching' ? t('scan.status.matching') : t('scan.status.noMatch')}
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
          {item.verified && <Chip size="small" color="success" label={t('scan.status.verified')} />}
          {item.match?.edition && (
            <Chip size="small" variant="outlined" color="info" label={item.match.edition} />
          )}
          <ListReprintChip
            isListReprint={item.match?.isListReprint}
            unresolved={item.match?.listReprintUnresolved}
          />
          <Box sx={{ flexGrow: 1 }} />
          <Button variant="outlined" startIcon={<ZoomInIcon />} onClick={() => setScanOpen(true)}>
            {t('scan.detail.viewScan')}
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
          <Alert severity="warning">{t('scan.detail.noConfidentMatch')}</Alert>
        )}

        {readOnly ? null : correcting ? (
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
              {t('common.actions.cancel')}
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
              {item.verified ? t('scan.detail.looksCorrect') : t('scan.detail.confirmMatch')}
            </Button>
            <Button variant="outlined" startIcon={<SearchIcon />} onClick={() => setCorrecting(true)}>
              {t('scan.detail.searchCatalog')}
            </Button>
            {item.include ? (
              <Button variant="text" onClick={() => onToggle(false)}>
                {t('scan.detail.excludeFromCommit')}
              </Button>
            ) : (
              <Button variant="text" disabled={!id} onClick={() => onToggle(true)}>
                {t('scan.detail.includeInCommit')}
              </Button>
            )}
            <Box sx={{ flexGrow: 1 }} />
            <Button variant="text" color="error" startIcon={<CloseIcon />} onClick={onRemove}>
              {t('common.actions.remove')}
            </Button>
          </Stack>
        )}

        <Divider textAlign="left">
          <Typography variant="caption" color="text.secondary">
            {t('scan.detail.cardProperties')}
          </Typography>
        </Divider>
        <PropertyFields
          game={game}
          props={item}
          onChange={onProps}
          foilTypeOptions={foilTypeOptions}
          tagOptions={tagOptions}
          ruleTags={item.ruleTags}
          disabled={readOnly}
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
  setLanguage: boolean;
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
    setLanguage: false,
    setFoil: false,
    setTags: false,
    tagsMode: 'add',
    setQuantity: false,
    setPrice: false,
    setNote: false,
    props: { condition: 'NM', language: ENGLISH, isFoil: false, foilType: null, quantity: 1, purchasePrice: '', tags: [], note: '' },
  };
}

/** Apply the enabled bulk fields onto an existing item's properties. */
function applyBulk(state: BulkEditState, item: ScanItem): Partial<ItemProps> {
  const patch: Partial<ItemProps> = {};
  if (state.setCondition) patch.condition = state.props.condition;
  if (state.setLanguage) patch.language = state.props.language;
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
  game,
  count,
  foilTypeOptions,
  tagOptions,
  onApply,
  onClose,
}: {
  open: boolean;
  game: string;
  count: number;
  foilTypeOptions: string[];
  tagOptions: string[];
  onApply: (state: BulkEditState) => void;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const [state, setState] = useState<BulkEditState>(newBulkState);
  // Reset the form each time the dialog opens so it never carries a stale selection.
  useEffect(() => {
    if (open) setState(newBulkState());
  }, [open]);

  const patchProps = (patch: Partial<ItemProps>) =>
    setState((s) => ({ ...s, props: { ...s.props, ...patch } }));
  const anyEnabled =
    state.setCondition ||
    state.setLanguage ||
    state.setFoil ||
    state.setTags ||
    state.setQuantity ||
    state.setPrice ||
    state.setNote;

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
      <DialogTitle>{t('scan.bulk.title', { count })}</DialogTitle>
      <DialogContent dividers>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          {t('scan.bulk.help')}
        </Typography>
        <Stack spacing={2}>
          {row(
            state.setCondition,
            (v) => setState((s) => ({ ...s, setCondition: v })),
            <TextField
              select
              size="small"
              fullWidth
              label={t('common.labels.condition')}
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
            state.setLanguage,
            (v) => setState((s) => ({ ...s, setLanguage: v })),
            <LanguageSelect
              game={game}
              fullWidth
              sx={{}}
              value={state.props.language}
              onChange={(language) => patchProps({ language })}
            />,
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
                label={t('common.labels.foil')}
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
                  renderInput={(p) => <TextField {...p} label={t('scan.props.foilType')} />}
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
              label={t('common.labels.quantity')}
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
              label={t('common.labels.purchasePrice')}
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
                label={t('scan.bulk.mode')}
                value={state.tagsMode}
                onChange={(e) => setState((s) => ({ ...s, tagsMode: e.target.value as 'add' | 'replace' }))}
                sx={{ width: 120 }}
              >
                <MenuItem value="add">{t('scan.bulk.modeAdd')}</MenuItem>
                <MenuItem value="replace">{t('scan.bulk.modeReplace')}</MenuItem>
              </TextField>
              <Autocomplete
                multiple
                freeSolo
                size="small"
                sx={{ flex: 1 }}
                options={tagOptions}
                value={state.props.tags}
                onChange={(_, v) => patchProps({ tags: v })}
                renderInput={(p) => <TextField {...p} label={t('common.labels.tags')} />}
              />
            </Stack>,
          )}
          {row(
            state.setNote,
            (v) => setState((s) => ({ ...s, setNote: v })),
            <TextField
              size="small"
              fullWidth
              label={t('common.labels.note')}
              multiline
              minRows={2}
              value={state.props.note}
              onChange={(e) => patchProps({ note: e.target.value })}
            />,
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('common.actions.cancel')}</Button>
        <Button variant="contained" disabled={!anyEnabled} onClick={() => onApply(state)}>
          {t('scan.bulk.applyTo', { count })}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

const ZIP_FORMATS_KEY = 'omnicard.scanExport.zipFormats';

/** The formats last chosen for a multi-format export (a per-browser convenience). */
function loadZipFormats(): string[] {
  try {
    const raw = localStorage.getItem(ZIP_FORMATS_KEY);
    const parsed: unknown = raw ? JSON.parse(raw) : null;
    if (Array.isArray(parsed)) {
      return parsed.filter((f): f is string => (EXPORT_FORMATS as readonly string[]).includes(f as string));
    }
  } catch {
    /* storage unavailable or corrupt — use the default */
  }
  return ['tcgplayer', 'ticker'];
}

/** Pick several export formats and download them together as one .zip. */
function ExportZipDialog({
  open,
  game,
  count,
  onClose,
  onExport,
}: {
  open: boolean;
  game: string;
  count: number;
  onClose: () => void;
  onExport: (formats: string[]) => void;
}) {
  const { t } = useTranslation();
  const [chosen, setChosen] = useState<string[]>(loadZipFormats);
  const selected = EXPORT_FORMATS.filter((f) => chosen.includes(f) && formatAvailableFor(f, game));
  const toggle = (f: string, on: boolean) =>
    setChosen((prev) => (on ? [...prev.filter((x) => x !== f), f] : prev.filter((x) => x !== f)));

  return (
    <Dialog open={open} onClose={onClose} maxWidth="xs" fullWidth>
      <DialogTitle>{t('scan.export.zipTitle')}</DialogTitle>
      <DialogContent>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
          {t('scan.export.zipIntro', { count })}
        </Typography>
        <Stack>
          {EXPORT_FORMATS.map((f) => {
            const available = formatAvailableFor(f, game);
            return (
              <FormControlLabel
                key={f}
                disabled={!available}
                control={
                  <Checkbox
                    checked={available && chosen.includes(f)}
                    onChange={(e) => toggle(f, e.target.checked)}
                  />
                }
                label={
                  available
                    ? t(`collection.exportFormats.${f}`)
                    : `${t(`collection.exportFormats.${f}`)} (${t('scan.export.mtgOnly')})`
                }
              />
            );
          })}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('common.actions.cancel')}</Button>
        <Button
          variant="contained"
          startIcon={<FolderZipIcon />}
          disabled={selected.length === 0}
          onClick={() => {
            try {
              localStorage.setItem(ZIP_FORMATS_KEY, JSON.stringify(chosen));
            } catch {
              /* storage unavailable — the choice just isn't remembered */
            }
            onExport(selected);
          }}
        >
          {t('scan.export.zipAction', { count: selected.length })}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

/** When run as an audit ({@link ScanPageProps.auditMode}), the Scan view is locked to one location
 * and its commit reconciles that location against the scan instead of appending lots. */
export interface ScanPageProps {
  /** Pre-selected, non-changeable target location. Hides the location picker. */
  lockedContainerId?: number;
  /** Switches the commit to an audit reconcile (delete-not-found / add-new / overwrite-matched). */
  auditMode?: boolean;
  /** Called with the audit summary after a successful audit commit. */
  onAuditCommitted?: (result: AuditCommitResultDto) => void;
  /** Review a background scan batch instead of local uploads (see ScanBatchPage). */
  batch?: ScanBatchMode;
}

/** A watched-folder batch under review: its items come from (and edits save to) the server, the game
 * and match settings are fixed by the batch, and nothing is matched in the browser. */
export interface ScanBatchMode {
  /** The latest server copy of the batch (re-polled while it's still matching). */
  data: ScanBatchDto;
  /** Someone else is reviewing it (or it's closed): nothing can be changed. */
  readOnly: boolean;
  /** A save or commit was refused with 409 — the claim was lost. */
  onClaimLost: () => void;
  /** Items were changed server-side (commit, remove, rematch) — refetch the batch. */
  onChanged: () => void;
  /** Everything in the batch has been committed or removed. */
  onClosed: () => void;
}

export function ScanPage({ lockedContainerId, auditMode = false, onAuditCommitted, batch }: ScanPageProps = {}) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const { game: contextGame } = useGame();
  const batchId = batch?.data.summary.id;
  const readOnly = !!batch?.readOnly;
  const [game, setGame] = useState(batch?.data.summary.game ?? contextGame ?? 'Mtg');
  // Multiple art-fallback sets: matching is constrained to the union of these (empty = all sets).
  const [artSets, setArtSets] = useState<{ setCode: string; setName: string }[]>([]);
  const [isFoil, setIsFoil] = useState(false);
  const [condition, setCondition] = useState('NM');
  // The scan session's card language; '' = auto (read off the card / taken from the matched printing).
  const [language, setLanguage] = useState('');
  // In audit mode the target location is fixed to the audited container and cannot be changed.
  // A batch pre-selects its folder's default location (still changeable).
  const [containerId, setContainerId] = useState<number | ''>(
    lockedContainerId ?? batch?.data.summary.defaultContainerId ?? '',
  );
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
    setLanguage('');
  }, [game]);

  // A batch's "Sets (art fallback)" are fixed; they still scope the correction search.
  const batchSetCodes = batch?.data.setCodes;
  useEffect(() => {
    if (!batchSetCodes?.length || !setsQuery.data) return;
    const wanted = new Set(batchSetCodes.map((c) => c.toLowerCase()));
    setArtSets(setsQuery.data.filter((s) => wanted.has(s.setCode.toLowerCase())));
  }, [batchSetCodes, setsQuery.data]);

  // A scan is committable only when it is BOTH confirmed (verified) AND checked (include).
  const isCommittable = (it: ScanItem) => it.include && it.verified && !!identityOf(it);

  // Only ever act on visible items — a filtered list confirms/commits exactly what's on screen.
  const isVisibleCommittable = (it: ScanItem) => visibleKeys.has(it.key) && isCommittable(it);

  const commit = useMutation<ScanCommitResultDto | AuditCommitResultDto | ScanBatchCommitResultDto>({
    mutationFn: async () => {
      if (batchStore && batchId != null) {
        // The server commits its saved copy, so flush pending edits first.
        await batchStore.sync(itemsRef.current);
        const ids = items.filter(isVisibleCommittable).map((it) => it.batchItemId!);
        return api.scanBatchCommit(batchId, containerId as number, ids);
      }
      const payload = items.filter(isVisibleCommittable).map((it) => toCommitItem(it, game));
      // Audit mode reconciles the location against the scan; normal mode appends lots.
      return auditMode
        ? api.auditCommit(containerId as number, payload)
        : api.scanCommit(containerId as number, payload);
    },
    onSuccess: (res) => {
      // Drop the cards that were just committed; keep everything else (unchecked or unconfirmed).
      dropItems(isVisibleCommittable);
      qc.invalidateQueries({ queryKey: ['collection'] });
      qc.invalidateQueries({ queryKey: ['locations'] });
      qc.invalidateQueries({ queryKey: ['dashboard'] });
      if (auditMode) {
        // The audited location's card lists change wholesale — refresh its detail view too.
        qc.invalidateQueries({ queryKey: ['location'] });
        onAuditCommitted?.(res as AuditCommitResultDto);
      }
      if (batch) {
        if ((res as ScanBatchCommitResultDto).batchClosed) batch.onClosed();
        else batch.onChanged();
      }
      return res;
    },
    onError: (e) => {
      if (batch && e instanceof ApiError && e.status === 409) batch.onClaimLost();
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
      prev.map((it) => {
        if (!visibleKeys.has(it.key) || !it.include) return it;
        const patch = applyBulk(state, it);
        return { ...it, ...patch, ...(patch.tags ? withUserTags(it, patch.tags) : {}) };
      }),
    );
    setBulkOpen(false);
  };

  /** A per-item property edit from the detail panel (tag edits remember removed rule tags). */
  const updateProps = (key: string, patch: Partial<ItemProps>) =>
    setItems((prev) =>
      prev.map((it) =>
        it.key === key ? { ...it, ...patch, ...(patch.tags ? withUserTags(it, patch.tags) : {}) } : it,
      ),
    );

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
      language: language || ENGLISH,
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
    await matchItems(staged);
  }

  async function matchItems(staged: ScanItem[], matchGame = game) {
    // Match with BOUNDED concurrency. Firing an entire batch at once overwhelmed the server
    // (each match is heavy CPU: hashing + OCR + rotation retries) and raced the game services'
    // shared read context — the source of the batch "internal server error"s. A small worker
    // pool keeps a few matches in flight without stampeding it. 8 measured best on a 12-thread
    // server (~42→31 ms/card vs 4 in flight); beyond that GDI+ imaging contention flattens the gain.
    const MAX_IN_FLIGHT = 8;
    const queue = [...staged];
    const setCodes = artSets.map((s) => s.setCode);
    async function worker() {
      for (;;) {
        const staging = queue.shift();
        if (!staging) return;
        if (!staging.file) continue;
        try {
          const match = await api.scanMatch(staging.file, matchGame, staging.isFoil, setCodes, language);
          // TIFF uploads carry no local preview; adopt the server-rendered one when present.
          const patch: Partial<ScanItem> = { status: 'done', match, include: match.matched };
          // The server's language (printed marker > session choice > matched printing) seeds the copy.
          if (match.language) patch.language = match.language;
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
  // Local uploads wait for every match before committing; a background batch can commit whatever is
  // confirmed while the rest is still matching on the server.
  const stillMatching = !batch && items.some((it) => it.status === 'matching');
  const selectedLocationName = useMemo(
    () => (containerId === '' ? null : locations.data?.find((l) => l.id === containerId)?.name ?? null),
    [containerId, locations.data],
  );

  // --- Refresh/crash safety: mirror the scan list into IndexedDB so a reload (e.g. mid-recording)
  // doesn't lose staged scans. Scan and each location's audit keep separate sessions. ---
  // Background batches persist on the server, not in IndexedDB.
  const isBatch = !!batch;
  const sessionStore = useMemo(
    () => (isBatch ? null : new ScanSessionStore<ScanItem>(auditMode ? `audit-${lockedContainerId}` : 'scan')),
    [auditMode, lockedContainerId, isBatch],
  );
  const restoreDone = useRef(false);
  const [restoredKeys, setRestoredKeys] = useState<Set<string>>(new Set());
  useEffect(() => {
    if (!sessionStore) return;
    let cancelled = false;
    restoreDone.current = false;
    void sessionStore.load().then((restored) => {
      if (cancelled) return;
      if (restored && restored.items.length > 0) {
        // Never reuse a key the new list already holds (scans staged before the restore finished).
        seq = Math.max(seq, ...restored.items.map((it) => Number(it.key.slice(1)) + 1).filter(Number.isFinite));
        const taken = new Set(itemsRef.current.map((it) => it.key));
        const revived = restored.items.map((it) => ({
          ...it,
          key: taken.has(it.key) ? `s${seq++}` : it.key,
          // Blob URLs die with the tab; TIFFs never had one (their preview is the server's data URI).
          previewUrl:
            !it.file || isTiff(it.file) ? (it.match?.scanPreviewDataUri ?? '') : URL.createObjectURL(it.file),
        }));
        setGame(restored.game);
        setItems((prev) => [...prev, ...revived]);
        setRestoredKeys(new Set(revived.map((it) => it.key)));
        // Scans that were still matching when the page went away get matched again.
        const pending = revived.filter((it) => it.status === 'matching');
        if (pending.length > 0) void matchItems(pending, restored.game);
      }
      restoreDone.current = true;
    });
    return () => {
      cancelled = true;
    };
    // matchItems is a plain function re-created each render; the restore must run once per session.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sessionStore]);
  useEffect(() => {
    if (!sessionStore || !restoreDone.current) return;
    const handle = window.setTimeout(() => void sessionStore.sync(game, items), 400);
    return () => window.clearTimeout(handle);
  }, [items, game, sessionStore]);
  // Flush on unmount (e.g. the audit navigates away right after committing) — the debounce would drop it.
  const gameRef = useRef(game);
  gameRef.current = game;
  useEffect(
    () => () => {
      if (sessionStore && restoreDone.current) void sessionStore.sync(gameRef.current, itemsRef.current);
    },
    [sessionStore],
  );
  // --- Background batch: items come from the server copy (re-polled while matching) and the
  // reviewer's edits are saved back, debounced like the IndexedDB session. ---
  const claimLostRef = useRef(batch?.onClaimLost);
  claimLostRef.current = batch?.onClaimLost;
  const batchStore = useMemo(
    () =>
      batchId == null
        ? null
        : new BatchItemStore<ScanItem>(batchId, toBatchEdit, (e) => {
            if (e instanceof ApiError && e.status === 409) claimLostRef.current?.();
            else console.warn('Saving batch edits failed', e);
          }),
    [batchId],
  );
  const batchItems = batch?.data.items;
  useEffect(() => {
    if (!batchStore || !batchItems) return;
    const merged = mergeBatchItems(itemsRef.current, batchItems, readOnly);
    batchStore.prime(merged.fresh);
    setItems(merged.items);
  }, [batchItems, batchStore, readOnly]);
  useEffect(() => {
    if (!batchStore || readOnly) return;
    const handle = window.setTimeout(() => void batchStore.sync(items), 400);
    return () => window.clearTimeout(handle);
  }, [items, batchStore, readOnly]);
  // Flush the last edits when leaving the page (the debounce would drop them).
  const readOnlyRef = useRef(readOnly);
  readOnlyRef.current = readOnly;
  useEffect(
    () => () => {
      if (batchStore && !readOnlyRef.current) void batchStore.sync(itemsRef.current);
    },
    [batchStore],
  );

  // --- Tag rules: work out each matched scan's rule tags (re-checked whenever what the rules look at
  // changes — identity, finish, condition, language, game) and pre-fill them, removable, for review.
  // A reviewer can switch this off for the session; the choice is remembered per browser. ---
  const [applyTagRules, setApplyTagRulesState] = useState(loadApplyTagRules);
  const setApplyTagRules = (on: boolean) => {
    setApplyTagRulesState(on);
    try {
      localStorage.setItem(APPLY_TAG_RULES_KEY, String(on));
    } catch {
      /* storage unavailable — the choice just isn't remembered */
    }
    if (!on) setItems((prev) => prev.map(withoutRuleTags));
  };
  const rulesInFlight = useRef(new Set<string>());
  useEffect(() => {
    if (readOnly || !applyTagRules) return;
    const pending = items.flatMap((it) => {
      const sig = ruleSignature(it, game);
      return sig !== null && sig !== it.ruleSig && !rulesInFlight.current.has(`${it.key}|${sig}`) ? [{ it, sig }] : [];
    });
    if (pending.length === 0) return;
    const handle = window.setTimeout(() => {
      for (let i = 0; i < pending.length; i += 200) {
        const chunk = pending.slice(i, i + 200);
        const sigs = new Map(chunk.map(({ it, sig }) => [it.key, sig]));
        chunk.forEach(({ it, sig }) => rulesInFlight.current.add(`${it.key}|${sig}`));
        // Apply only to items still in the state the rules were checked for. A failed check leaves the
        // tags alone but marks the item checked, so it isn't retried in a loop.
        const settle = (results: Map<string, string[]> | null) =>
          setItems((prev) =>
            prev.map((it) => {
              const sig = sigs.get(it.key);
              if (sig === undefined || ruleSignature(it, game) !== sig) return it;
              return results ? withRuleTags(it, results.get(it.key) ?? [], sig) : { ...it, ruleSig: sig };
            }),
          );
        api
          .scanTagRules(
            game,
            chunk.map(({ it }) => {
              const id = identityOf(it)!;
              return {
                key: it.key,
                gameCardId: id.gameCardId,
                name: id.name,
                setCode: id.setCode,
                collectorNumber: id.collectorNumber,
                rarity: id.rarity,
                condition: it.condition,
                language: it.language,
                isFoil: it.isFoil,
                foilType: it.isFoil ? it.foilType : null,
              };
            }),
          )
          .then((res) => settle(new Map(res.map((r) => [r.key, r.tags]))))
          .catch((e) => {
            console.warn('Checking tag rules failed', e);
            settle(null);
          })
          .finally(() => chunk.forEach(({ it, sig }) => rulesInFlight.current.delete(`${it.key}|${sig}`)));
      }
    }, 300);
    return () => window.clearTimeout(handle);
  }, [items, game, applyTagRules, readOnly]);
  // Hold the commit until every card about to be added has its rule tags.
  const rulesPending =
    applyTagRules && !readOnly && items.some((it) => isVisibleCommittable(it) && ruleSignature(it, game) !== it.ruleSig);

  /** Remove scans from the list — and, for a batch, from the server (deleting their stored images). */
  const removeItems = (shouldRemove: (it: ScanItem) => boolean | undefined) => {
    if (batch && batchId != null) {
      const ids = itemsRef.current
        .filter((it) => shouldRemove(it) && it.batchItemId != null)
        .map((it) => it.batchItemId!);
      if (ids.length > 0)
        void api
          .scanBatchRemove(batchId, ids)
          .then((r) => (r.batchClosed ? batch.onClosed() : batch.onChanged()))
          .catch((e) => {
            if (e instanceof ApiError && e.status === 409) batch.onClaimLost();
          });
    }
    dropItems(shouldRemove);
  };

  const errorItemIds = useMemo(
    () => items.filter((it) => it.status === 'error' && it.batchItemId != null).map((it) => it.batchItemId!),
    [items],
  );
  const rematch = useMutation({
    mutationFn: () => api.scanBatchRematch(batchId!, errorItemIds),
    onSuccess: () => {
      const retried = new Set(errorItemIds);
      setItems((prev) =>
        prev.map((it) =>
          it.batchItemId != null && retried.has(it.batchItemId)
            ? { ...it, status: 'matching' as const, error: undefined, match: undefined, include: false, verified: false }
            : it,
        ),
      );
      batch?.onChanged();
    },
    onError: (e) => {
      if (batch && e instanceof ApiError && e.status === 409) batch.onClaimLost();
    },
  });

  const restoredStillPresent = useMemo(
    () => items.filter((it) => restoredKeys.has(it.key)).length,
    [items, restoredKeys],
  );

  // --- Export staged scans (CSV / text / zip) WITHOUT adding them to the collection. Scoped like
  // Confirm/Add to the visible + checked list, but confirmation isn't required. ---
  const { can } = usePermissions();
  const canExport = can('export.run');
  const exportableItems = useMemo(
    () => visibleItems.filter((it) => it.include && identityOf(it)),
    [visibleItems],
  );
  const [exportAnchor, setExportAnchor] = useState<HTMLElement | null>(null);
  const [zipOpen, setZipOpen] = useState(false);
  const [exportNotice, setExportNotice] = useState<{ severity: 'success' | 'error'; text: string } | null>(
    null,
  );
  const exportScans = useMutation({
    mutationFn: (formats: string[]) => {
      const baseName = auditMode
        ? `audit-${selectedLocationName ?? lockedContainerId}-${fileStamp()}`
        : `scan-${fileStamp()}`;
      return api.scanExport(
        formats,
        exportableItems.map((it) => toCommitItem(it, game)),
        baseName,
      );
    },
    onMutate: () => setExportNotice(null),
    onSuccess: () =>
      setExportNotice({ severity: 'success', text: t('scan.export.success', { count: exportableItems.length }) }),
    onError: (e) => setExportNotice({ severity: 'error', text: (e as Error).message }),
  });
  const exportFormats = (formats: string[]) => {
    setExportAnchor(null);
    setZipOpen(false);
    exportScans.mutate(formats);
  };
  const copyTextList = async () => {
    setExportAnchor(null);
    const text = exportableItems
      .map((it) => {
        const id = identityOf(it)!;
        return textListLine({
          quantity: it.quantity,
          name: id.name,
          setCode: id.setCode,
          collectorNumber: id.collectorNumber,
          isFoil: it.isFoil,
          foilType: it.foilType,
        });
      })
      .join('\n');
    const ok = await copyToClipboard(text);
    setExportNotice(
      ok
        ? { severity: 'success', text: t('scan.export.copied', { count: exportableItems.length }) }
        : { severity: 'error', text: t('scan.export.copyFailed') },
    );
  };

  return (
    <Stack spacing={3}>
      {/* In audit and batch mode the wrapping page supplies its own header/banner. */}
      {!auditMode && !batch && (
        <>
          <Typography variant="h4">{t('scan.title')}</Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mt: -1 }}>
            {t('scan.intro')}
          </Typography>
          <ScanBatchesPanel />
        </>
      )}

      {batch ? (
        <Paper variant="outlined" sx={{ p: 2 }}>
          <Stack direction="row" spacing={2} flexWrap="wrap" useFlexGap alignItems="center">
            <Chip variant="outlined" label={gamesQuery.data?.find((g) => g.id === game)?.displayName ?? game} />
            <Typography variant="body2" color="text.secondary">
              {t('scan.batches.settingsSummary', {
                sets: artSets.length
                  ? artSets.map((s) => s.setName).join(', ')
                  : batch.data.setCodes.length
                    ? batch.data.setCodes.join(', ').toUpperCase()
                    : t('scan.batches.allSets'),
                condition: batch.data.condition || 'NM',
                language: batch.data.language ? languageName(t, batch.data.language) : t('scan.batches.autoLanguage'),
                foil: batch.data.isFoil ? t('scan.batches.yes') : t('scan.batches.no'),
              })}
            </Typography>
            <Box sx={{ flexGrow: 1 }} />
            {!readOnly && errorItemIds.length > 0 && (
              <Button
                variant="outlined"
                startIcon={<ReplayIcon />}
                disabled={rematch.isPending}
                onClick={() => rematch.mutate()}
              >
                {t('scan.batches.retryErrors', { count: errorItemIds.length })}
              </Button>
            )}
          </Stack>
        </Paper>
      ) : (
        <Paper variant="outlined" sx={{ p: 2 }}>
          <Stack direction="row" spacing={2} flexWrap="wrap" useFlexGap alignItems="center">
            <TextField
              select
              size="small"
              label={t('common.labels.game')}
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
                <TextField
                  {...p}
                  label={t('scan.controls.artSets')}
                  placeholder={artSets.length ? '' : t('scan.controls.allSets')}
                />
              )}
            />
            <TextField
              select
              size="small"
              label={t('common.labels.condition')}
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
            <Tooltip title={t('scan.controls.languageHelp')}>
              <Box>
                <LanguageSelect
                  game={game}
                  allowAuto
                  label={t('scan.controls.language')}
                  value={language}
                  onChange={setLanguage}
                />
              </Box>
            </Tooltip>
            <FormControlLabel
              control={<Checkbox checked={isFoil} onChange={(e) => setIsFoil(e.target.checked)} />}
              label={t('common.labels.foil')}
            />
            <Button
              variant="contained"
              startIcon={<CameraAltIcon />}
              onClick={() => cameraInput.current?.click()}
            >
              {t('scan.controls.takePhoto')}
            </Button>
            <Button
              variant="outlined"
              startIcon={<VideocamIcon />}
              onClick={() => setWebcamOpen(true)}
            >
              {t('scan.controls.useWebcam')}
            </Button>
            <Button
              variant="outlined"
              startIcon={<AddPhotoAlternateIcon />}
              onClick={() => fileInput.current?.click()}
            >
              {t('scan.controls.addImages')}
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
            {t('scan.controls.seedHint')}
          </Typography>
        </Paper>
      )}

      {restoredStillPresent > 0 && (
        <Alert
          severity="info"
          onClose={() => setRestoredKeys(new Set())}
          action={
            <Button color="inherit" size="small" onClick={() => dropItems((it) => restoredKeys.has(it.key))}>
              {t('scan.session.discard')}
            </Button>
          }
        >
          {t('scan.session.restored', { count: restoredStillPresent })}
        </Alert>
      )}

      {items.length > 0 && (
        <Paper variant="outlined" sx={{ p: 2, position: 'sticky', top: 56, zIndex: 1 }}>
          <Stack direction="row" spacing={2} alignItems="center" flexWrap="wrap" useFlexGap>
            {/* Audit is locked to one location: show it read-only instead of the picker. */}
            {readOnly ? null : lockedContainerId != null ? (
              <Chip
                icon={<LockIcon />}
                variant="outlined"
                label={selectedLocationName ?? t('scan.commit.addToLocation')}
                sx={{ minWidth: 220, justifyContent: 'flex-start' }}
              />
            ) : (
              <Button
                variant="outlined"
                startIcon={<PlaceIcon />}
                onClick={() => setPickerOpen(true)}
                sx={{ minWidth: 220, justifyContent: 'flex-start', textTransform: 'none' }}
              >
                {selectedLocationName ?? t('scan.commit.addToLocation')}
              </Button>
            )}
            {!readOnly && (
              <>
                <Button
                  variant="outlined"
                  color="success"
                  startIcon={<CheckCircleIcon />}
                  disabled={confirmableCount === 0}
                  onClick={confirmChecked}
                >
                  {t('scan.commit.confirmChecked', { count: confirmableCount })}
                </Button>
                <Button
                  variant="contained"
                  color={auditMode ? 'warning' : 'primary'}
                  disabled={
                    committableCount === 0 ||
                    containerId === '' ||
                    commit.isPending ||
                    stillMatching ||
                    rulesPending
                  }
                  onClick={() => commit.mutate()}
                >
                  {commit.isPending
                    ? auditMode
                      ? t('scan.audit.committing')
                      : t('common.states.adding')
                    : auditMode
                      ? t('scan.audit.commit', { count: committableCount })
                      : t('scan.commit.addConfirmed', { count: committableCount })}
                </Button>
                <Tooltip title={t('scan.tagRules.switchHint')}>
                  <FormControlLabel
                    control={
                      <Switch
                        size="small"
                        checked={applyTagRules}
                        onChange={(e) => setApplyTagRules(e.target.checked)}
                      />
                    }
                    label={t('scan.tagRules.switch')}
                  />
                </Tooltip>
              </>
            )}
            {canExport && (
              <>
                <Tooltip title={t('scan.export.hint')}>
                  <span>
                    <Button
                      variant="outlined"
                      startIcon={<FileDownloadIcon />}
                      disabled={exportableItems.length === 0 || exportScans.isPending}
                      onClick={(e) => setExportAnchor(e.currentTarget)}
                    >
                      {exportScans.isPending
                        ? t('scan.export.exporting')
                        : t('scan.export.button', { count: exportableItems.length })}
                    </Button>
                  </span>
                </Tooltip>
                <Menu anchorEl={exportAnchor} open={!!exportAnchor} onClose={() => setExportAnchor(null)}>
                  {EXPORT_FORMATS.map((f) => {
                    const available = formatAvailableFor(f, game);
                    return (
                      <MenuItem key={f} disabled={!available} onClick={() => exportFormats([f])}>
                        <ListItemText
                          primary={t(`collection.exportFormats.${f}`)}
                          secondary={available ? undefined : t('scan.export.mtgOnly')}
                        />
                      </MenuItem>
                    );
                  })}
                  <Divider />
                  <MenuItem onClick={() => void copyTextList()}>
                    <ListItemIcon>
                      <ContentCopyIcon fontSize="small" />
                    </ListItemIcon>
                    <ListItemText primary={t('scan.export.copyText')} />
                  </MenuItem>
                  <MenuItem
                    onClick={() => {
                      setExportAnchor(null);
                      setZipOpen(true);
                    }}
                  >
                    <ListItemIcon>
                      <FolderZipIcon fontSize="small" />
                    </ListItemIcon>
                    <ListItemText primary={t('scan.export.zip')} />
                  </MenuItem>
                </Menu>
                <ExportZipDialog
                  open={zipOpen}
                  game={game}
                  count={exportableItems.length}
                  onClose={() => setZipOpen(false)}
                  onExport={exportFormats}
                />
              </>
            )}
            <Typography variant="caption" color="text.secondary">
              {auditMode
                ? t('scan.audit.commitHint')
                : controlsActive
                  ? t('scan.commit.addedNoteFiltered')
                  : t('scan.commit.addedNote')}
            </Typography>
            {commit.error && <Alert severity="error">{(commit.error as Error).message}</Alert>}
            {exportNotice && (
              <Alert severity={exportNotice.severity} onClose={() => setExportNotice(null)}>
                {exportNotice.text}
              </Alert>
            )}
            {!auditMode && commit.data && (
              <Alert severity="success">
                {t('scan.commit.success', { count: (commit.data as { imported: number }).imported })}
              </Alert>
            )}
          </Stack>
        </Paper>
      )}

      {items.length > 0 && (
        <Paper variant="outlined" sx={{ p: 2 }}>
          <Stack direction="row" spacing={2} alignItems="center" flexWrap="wrap" useFlexGap>
            <TextField
              size="small"
              label={t('scan.filter.filterByName')}
              value={controls.name}
              onChange={(e) => setControls((c) => ({ ...c, name: e.target.value }))}
              InputProps={{ startAdornment: <SearchIcon fontSize="small" sx={{ mr: 0.5, color: 'text.secondary' }} /> }}
              sx={{ flex: '1 1 200px', minWidth: 180 }}
            />
            <TextField
              select
              size="small"
              label={t('scan.filter.show')}
              value={controls.checked}
              onChange={(e) => setControls((c) => ({ ...c, checked: e.target.value as CheckedFilter }))}
              sx={{ minWidth: 150 }}
            >
              <MenuItem value="all">{t('scan.filter.all')}</MenuItem>
              <MenuItem value="checked">{t('scan.filter.checkedOnly')}</MenuItem>
              <MenuItem value="unchecked">{t('scan.filter.uncheckedOnly')}</MenuItem>
            </TextField>
            <TextField
              size="small"
              type="number"
              label={t('scan.filter.minConfidence')}
              value={controls.minConfidence}
              onChange={(e) => setControls((c) => ({ ...c, minConfidence: e.target.value }))}
              inputProps={{ min: 0, max: 100 }}
              sx={{ width: 150 }}
            />
            <TextField
              size="small"
              type="number"
              label={t('scan.filter.minValue')}
              value={controls.minPrice}
              onChange={(e) => setControls((c) => ({ ...c, minPrice: e.target.value }))}
              inputProps={{ step: '0.01', min: 0 }}
              sx={{ width: 130 }}
            />
            <Divider orientation="vertical" flexItem sx={{ display: { xs: 'none', sm: 'block' } }} />
            <TextField
              select
              size="small"
              label={t('scan.filter.sortBy')}
              value={controls.sortKey}
              onChange={(e) => setControls((c) => ({ ...c, sortKey: e.target.value as SortKey }))}
              sx={{ minWidth: 160 }}
            >
              <MenuItem value="none">{t('scan.filter.sortNone')}</MenuItem>
              <MenuItem value="name">{t('common.labels.name')}</MenuItem>
              <MenuItem value="confidence">{t('scan.filter.sortConfidence')}</MenuItem>
              <MenuItem value="value">{t('scan.filter.sortValue')}</MenuItem>
            </TextField>
            <Tooltip title={controls.sortDir === 'asc' ? t('scan.filter.ascending') : t('scan.filter.descending')}>
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
                  {t('scan.filter.showingOf', { visible: visibleItems.length, total: items.length })}
                </Typography>
                <Button size="small" onClick={() => setControls(DEFAULT_CONTROLS)}>
                  {t('common.actions.clear')}
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
              <Tooltip title={checkedCount === selectableCount ? t('scan.master.uncheckAll') : t('scan.master.checkAll')}>
                <span>
                  <Checkbox
                    size="small"
                    disabled={selectableCount === 0 || readOnly}
                    checked={selectableCount > 0 && checkedCount === selectableCount}
                    indeterminate={checkedCount > 0 && checkedCount < selectableCount}
                    onChange={(e) => (e.target.checked ? selectAll() : selectNone())}
                  />
                </span>
              </Tooltip>
              <Button size="small" onClick={selectAll} disabled={selectableCount === 0 || readOnly}>
                {t('scan.master.all')}
              </Button>
              <Button size="small" onClick={selectNone} disabled={checkedCount === 0 || readOnly}>
                {t('scan.master.none')}
              </Button>
              <Button size="small" onClick={invertSelection} disabled={selectableCount === 0 || readOnly}>
                {t('scan.master.invert')}
              </Button>
              <Box sx={{ flexGrow: 1 }} />
              <Typography variant="caption" color="text.secondary">
                {t('scan.master.checkedCount', { count: checkedCount })}
              </Typography>
              <Button
                size="small"
                variant="outlined"
                startIcon={<EditIcon />}
                disabled={checkedCount === 0 || readOnly}
                onClick={() => setBulkOpen(true)}
              >
                {t('common.actions.edit')}
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
                  readOnly={readOnly}
                />
              ))}
              {visibleItems.length === 0 && (
                <Box sx={{ p: 3, textAlign: 'center' }}>
                  <Typography variant="body2" color="text.secondary">
                    {t('scan.master.noMatchFilter')}
                  </Typography>
                  <Button size="small" sx={{ mt: 1 }} onClick={() => setControls(DEFAULT_CONTROLS)}>
                    {t('scan.master.clearFilter')}
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
                updateItem(selectedItem.key, {
                  override: r,
                  include: true,
                  verified: true,
                  ...(r.language && r.language !== ENGLISH ? { language: r.language } : {}),
                })
              }
              onRemove={() => removeItems((it) => it.key === selectedItem.key)}
              onProps={(patch) => updateProps(selectedItem.key, patch)}
              readOnly={readOnly}
            />
          ) : (
            <Paper
              variant="outlined"
              sx={{ p: 3, display: 'flex', alignItems: 'center', justifyContent: 'center' }}
            >
              <Typography color="text.secondary">{t('scan.detail.selectPrompt')}</Typography>
            </Paper>
          )}
        </Box>
      )}

      <BulkEditDialog
        open={bulkOpen}
        game={game}
        count={checkedCount}
        foilTypeOptions={foilTypeOptions}
        tagOptions={tagOptions}
        onApply={applyBulkEdit}
        onClose={() => setBulkOpen(false)}
      />

      {lockedContainerId == null && (
        <LocationPickerDialog
          open={pickerOpen}
          title={t('scan.locationPicker.title')}
          onPick={(id) => {
            setContainerId(id);
            setPickerOpen(false);
          }}
          onClose={() => setPickerOpen(false)}
        />
      )}
      <WebcamScanDialog
        open={webcamOpen}
        onCapture={(file) => void stageFiles([file])}
        onClose={() => setWebcamOpen(false)}
      />
    </Stack>
  );
}
