import { memo, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { KeyboardEvent as ReactKeyboardEvent, PointerEvent as ReactPointerEvent } from 'react';
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
  DialogContentText,
  DialogTitle,
  Divider,
  FormControlLabel,
  IconButton,
  LinearProgress,
  ListItemIcon,
  ListItemText,
  Menu,
  MenuItem,
  Paper,
  Stack,
  Switch,
  TextField,
  Toolbar,
  Tooltip,
  Typography,
  Slider,
  useMediaQuery,
  useTheme,
} from '@mui/material';
import AddPhotoAlternateIcon from '@mui/icons-material/AddPhotoAlternate';
import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';
import HistoryIcon from '@mui/icons-material/History';
import ArrowDownwardIcon from '@mui/icons-material/ArrowDownward';
import RequestQuoteIcon from '@mui/icons-material/RequestQuote';
import ArrowUpwardIcon from '@mui/icons-material/ArrowUpward';
import CameraAltIcon from '@mui/icons-material/CameraAlt';
import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import CloseIcon from '@mui/icons-material/Close';
import CloudUploadIcon from '@mui/icons-material/CloudUpload';
import DeleteIcon from '@mui/icons-material/Delete';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import FileDownloadIcon from '@mui/icons-material/FileDownload';
import FolderZipIcon from '@mui/icons-material/FolderZip';
import KeyboardIcon from '@mui/icons-material/Keyboard';
import EditIcon from '@mui/icons-material/Edit';
import ErrorOutlineIcon from '@mui/icons-material/ErrorOutline';
import TrackChangesIcon from '@mui/icons-material/TrackChanges';
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
import { splitPhotoIntoCards } from '../utils/cardCapture';
import { ensureCvReady } from '../utils/cvWorker';
import { ScanValueBadges, ListReprintChip } from '../lib/scanBadges';
import { ENGLISH, LanguageChip, LanguageSelect, languageName } from '../lib/cardLanguages';
import { clearScanPicks, recentScanPicks, rememberScanPick } from '../lib/recentScanPicks';
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
import { ScanBatchesPanel, invalidateScanBatches } from '../components/ScanBatchesPanel';
import { ScanValidationBar, type ScanValidationResult } from '../components/ScanValidationBar';
import { BuyListHitChip, buyListFrameSx } from '../components/BuyListBadge';
import type {
  BuyListHitDto,
  AuditCommitResultDto,
  ScanBadgeSettingsDto,
  ScanBatchCommitResultDto,
  ScanBatchDto,
  ScanBatchItemDto,
  ScanBatchItemEdit,
  ScanBatchUploadFinishedDto,
  ScanBatchUploadItem,
  ScanCommitItem,
  ScanCommitResultDto,
  ScanMatchDto,
  ScanSearchResultDto,
  ScanTagRuleItem,
} from '../api/types';
import { CardHoverPreview, type CardHover } from '../components/CardHoverPreview';
import { Kbd } from '../components/Kbd';
import { SetMultiSelect } from '../components/SetMultiSelect';
import { LONG_PRESS_SX, useIsPhone, useLongPress } from '../lib/responsive';
import { useUserPreference } from '../lib/userPreferences';

import { SelectField } from '../components/SelectField';
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
  /** Set when the card is on the game's Buy List. */
  buyList?: BuyListHitDto;
  /** The {@link buyListSignature} {@link buyList} was last worked out for. */
  buyListSig?: string;
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

/** What the server's search rules (tag rules, validation) look at for a scan. Only call for an item
 * with a resolved identity. */
function toRuleItem(it: ScanItem): ScanTagRuleItem {
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
    tags: it.tags,
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

/** What the Buy List looks at for an item, so a hand-corrected card is checked again. Null while the
 * item has no identity. */
function buyListSignature(it: ScanItem, game: string): string | null {
  const id = identityOf(it);
  return id ? [game, id.gameCardId, id.name, it.isFoil ? 'foil' : ''].join('|') : null;
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
  return batchProps(it, it.batchItemId);
}

/** A local scan as sent when the list is saved as a batch: the browser's match goes with it (a scan
 * still matching, or that failed, goes without one and the server matches it). */
function toUploadItem(it: ScanItem, sequence: number): ScanBatchUploadItem {
  return {
    sequence,
    match: it.status === 'done' && it.match ? { ...it.match, scanPreviewDataUri: null } : null,
    props: batchProps(it, 0),
  };
}

function batchProps(it: ScanItem, id: number): ScanBatchItemEdit {
  const price = it.purchasePrice.trim() === '' ? null : Number(it.purchasePrice);
  return {
    id,
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
        // A scan the reviewer already checked (e.g. to delete it) stays checked once its match lands.
        include: mine.include || matched.include,
        language: matched.language,
        // The server applied a finish read off the card to the item.
        ...(matched.match?.foilDetected != null ? { isFoil: matched.isFoil, foilType: matched.foilType } : {}),
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
  /** Show only the scans that failed the last validation. */
  failedOnly: boolean;
  /** Show only the scans on the Buy List. */
  buyListOnly: boolean;
}

const DEFAULT_CONTROLS: ScanListControls = {
  checked: 'all',
  name: '',
  minConfidence: '',
  minPrice: '',
  sortKey: 'none',
  sortDir: 'asc',
  failedOnly: false,
  buyListOnly: false,
};

/** Apply the filter half of the list controls to a single item. `failed` holds the keys that failed
 * the last validation. */
function passesFilter(item: ScanItem, c: ScanListControls, failed?: ReadonlySet<string>): boolean {
  if (c.failedOnly && !failed?.has(item.key)) return false;
  if (c.buyListOnly && !item.buyList) return false;
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
function applyControls(items: ScanItem[], c: ScanListControls, failed?: ReadonlySet<string>): ScanItem[] {
  const filtered = items.filter((it) => passesFilter(it, c, failed));
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
/** Case/accent-insensitive, digit-aware ("2" before "10", "OP01-009" before "OP01-019"). */
const resultCollator = new Intl.Collator(undefined, { numeric: true, sensitivity: 'base' });

/** Correction search results in the order a person scans them: name, then set code newest-looking
 * first (descending), then collector number ascending, then English ahead of the same printing's other
 * languages. Blank values sink to the end of their group. */
function compareSearchResults(a: ScanSearchResultDto, b: ScanSearchResultDto): number {
  const text = (x?: string | null, y?: string | null) => {
    if (!x || !y) return x ? -1 : y ? 1 : 0;
    return resultCollator.compare(x, y);
  };
  // A row without a language is English.
  const nonEnglish = (r: ScanSearchResultDto) => (r.language && r.language !== 'en' ? 1 : 0);
  return (
    text(a.name, b.name) ||
    // Descending, but still with blanks last.
    (a.setCode && b.setCode ? resultCollator.compare(b.setCode, a.setCode) : text(a.setCode, b.setCode)) ||
    text(a.collectorNumber, b.collectorNumber) ||
    nonEnglish(a) - nonEnglish(b) ||
    text(a.language, b.language)
  );
}

function CorrectionSearch({
  game,
  setCodes,
  setNames,
  onPick,
  onCancel,
}: {
  game: string;
  setCodes: string[];
  setNames: string;
  onPick: (r: ScanSearchResultDto) => void;
  onCancel: () => void;
}) {
  const { t } = useTranslation();
  const rootRef = useRef<HTMLDivElement>(null);
  const [q, setQ] = useState('');
  const [cn, setCn] = useState('');
  // Hovering a result's thumbnail pops the enlarged art (same preview as the location grids).
  const [hover, setHover] = useState<CardHover | null>(null);
  const longPress = useLongPress();
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
  const sorted = useMemo(() => (search.data ? [...search.data].sort(compareSearchResults) : undefined), [search.data]);
  // Printings picked earlier this session that this search found again: listed first (newest pick
  // first) under their own heading, and left out of the rest.
  const [recentIds, setRecentIds] = useState(() => recentScanPicks(game));
  const { recent, others } = useMemo(() => {
    if (!sorted) return { recent: [], others: [] };
    const rank = new Map(recentIds.map((id, i) => [id, i]));
    return {
      recent: sorted.filter((r) => rank.has(r.gameCardId)).sort((a, b) => rank.get(a.gameCardId)! - rank.get(b.gameCardId)!),
      others: sorted.filter((r) => !rank.has(r.gameCardId)),
    };
  }, [sorted, recentIds]);
  const pick = (r: ScanSearchResultDto) => {
    rememberScanPick(game, r.gameCardId);
    onPick(r);
  };
  const resultButton = (r: ScanSearchResultDto) => (
    <Button
      key={`${r.gameCardId}-${r.setCode}-${r.collectorNumber}`}
      data-scan-result
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
            sx={{ width: { xs: 36, sm: 24 }, height: { xs: 50, sm: 34 }, objectFit: 'contain', borderRadius: 0.5 }}
          />
        ) : undefined
      }
      sx={{ ...LONG_PRESS_SX, justifyContent: 'flex-start', textAlign: 'left', textTransform: 'none' }}
      {...longPress((el) => r.imageUri && setHover({ el, url: r.imageUri, foil: false, touch: true }))}
      onClick={() => pick(r)}
    >
      {r.name} · {r.setCode.toUpperCase()} #{r.collectorNumber}
      {r.rarity ? ` · ${r.rarity}` : ''}
      {/* Language-aware catalogs hold one row per language, so name it to tell them apart. */}
      {r.language ? ` · ${languageName(t, r.language)}` : ''}
    </Button>
  );
  // Keyboard review: Esc cancels; ↓/↑ walk from the search boxes through the results, and Enter on
  // a focused result picks it (it's a button).
  const onKeyDown = (e: ReactKeyboardEvent) => {
    if (e.key === 'Escape') {
      e.preventDefault();
      e.stopPropagation();
      onCancel();
      return;
    }
    if (e.key !== 'ArrowDown' && e.key !== 'ArrowUp') return;
    const results = Array.from(rootRef.current?.querySelectorAll<HTMLElement>('[data-scan-result]') ?? []);
    if (results.length === 0) return;
    e.preventDefault();
    const at = results.indexOf(document.activeElement as HTMLElement);
    const next = e.key === 'ArrowDown' ? Math.min(at + 1, results.length - 1) : at - 1;
    if (next < 0) rootRef.current?.querySelector<HTMLInputElement>('input')?.focus();
    else results[next].focus();
  };
  return (
    <Box ref={rootRef} sx={{ mt: 1 }} onKeyDown={onKeyDown}>
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
      {sorted && (
        <>
          <Typography variant="caption" color="text.secondary" sx={{ mt: 1, display: 'block' }}>
            {sorted.length === 0
              ? t('scan.correction.noMatches')
              : t('scan.correction.matchCount', { count: sorted.length })}
          </Typography>
          {sorted.length > 0 && (
            <Stack sx={{ mt: 0.5, maxHeight: 320, overflowY: 'auto' }} spacing={0.5}>
              {recent.length > 0 && (
                <>
                  <Stack direction="row" alignItems="center" spacing={1}>
                    <HistoryIcon fontSize="small" color="action" />
                    <Typography variant="overline" color="text.secondary" sx={{ flexGrow: 1, lineHeight: 2 }}>
                      {t('scan.correction.recentlySelected')}
                    </Typography>
                    <Button
                      size="small"
                      tabIndex={-1}
                      onClick={() => {
                        clearScanPicks(game);
                        setRecentIds([]);
                      }}
                    >
                      {t('scan.correction.clearRecent')}
                    </Button>
                  </Stack>
                  {recent.map(resultButton)}
                  {others.length > 0 && (
                    <>
                      <Divider />
                      <Typography variant="overline" color="text.secondary" sx={{ lineHeight: 2 }}>
                        {t('scan.correction.otherResults')}
                      </Typography>
                    </>
                  )}
                </>
              )}
              {others.map(resultButton)}
            </Stack>
          )}
        </>
      )}
      <CardHoverPreview hover={hover} onClose={() => setHover(null)} />
    </Box>
  );
}

/** Cards sent per request when adding confirmed cards (smaller = smoother progress). */
const COMMIT_CHUNK_SIZE = 25;

/** Add one chunk's commit result to the running total. */
function mergeCommitResults(
  a: ScanCommitResultDto & Partial<ScanBatchCommitResultDto>,
  b: ScanCommitResultDto | ScanBatchCommitResultDto,
): ScanCommitResultDto & Partial<ScanBatchCommitResultDto> {
  const sb = b as ScanCommitResultDto & Partial<ScanBatchCommitResultDto>;
  const buyList =
    a.buyList && sb.buyList
      ? {
          matchedCopies: a.buyList.matchedCopies + sb.buyList.matchedCopies,
          setAsideCopies: a.buyList.setAsideCopies + sb.buyList.setAsideCopies,
          setAsideLocations: [...new Set([...a.buyList.setAsideLocations, ...sb.buyList.setAsideLocations])],
          notSetAside: a.buyList.notSetAside + sb.buyList.notSetAside,
        }
      : (a.buyList ?? sb.buyList ?? null);
  return {
    imported: a.imported + sb.imported,
    ruleTagged: (a.ruleTagged ?? 0) + (sb.ruleTagged ?? 0) || undefined,
    lotIds: a.lotIds || sb.lotIds ? [...(a.lotIds ?? []), ...(sb.lotIds ?? [])] : undefined,
    buyList,
    // Only the last chunk can close the batch.
    batchClosed: sb.batchClosed ?? a.batchClosed,
  };
}

/** The signed-in user's remembered review settings (see `useUserPreference`). */
interface ScanReviewPrefs {
  /** Height of the compare strip in px; null = the default for the screen size. Not used on phones. */
  compareHeight: number | null;
  /** While Enter is held, confirm one card every this many ms; 0 = holding Enter does nothing more. */
  holdEnterMs: number;
}
const SCAN_REVIEW_PREF = 'scan.review';
const SCAN_REVIEW_DEFAULTS: ScanReviewPrefs = { compareHeight: null, holdEnterMs: 500 };
const COMPARE_MIN_HEIGHT = 200;
const COMPARE_MAX_HEIGHT = 1400;
/** The paces offered for holding Enter (ms per card); 0 = off. */
const HOLD_ENTER_STEPS = [0, 150, 250, 400, 500, 750, 1000, 1500, 2000, 3000];

const clampCompareHeight = (h: unknown): number | null =>
  typeof h === 'number' && Number.isFinite(h)
    ? Math.round(Math.min(COMPARE_MAX_HEIGHT, Math.max(COMPARE_MIN_HEIGHT, h)))
    : null;
/** The stored pace snapped to an offered step (a stored value from elsewhere can be anything). */
const holdEnterPace = (ms: unknown): number => {
  if (typeof ms !== 'number' || !Number.isFinite(ms) || ms <= 0) return 0;
  return HOLD_ENTER_STEPS.reduce((best, s) => (s > 0 && Math.abs(s - ms) < Math.abs(best - ms) ? s : best), 500);
};

function useScanReviewPrefs() {
  return useUserPreference<ScanReviewPrefs>(SCAN_REVIEW_PREF, SCAN_REVIEW_DEFAULTS);
}

/** Card proportions, used to reserve an image's width before it has loaded. */
const CARD_ASPECT = '63 / 88';
const COMPARE_PADDING = 8;
const COMPARE_LABEL_HEIGHT = 22;

/**
 * Uploaded scan and matched art next to each other in one strip (not each centred in its own half), so the
 * two are easy to compare. The strip's bottom edge drags to resize it; the height is remembered per user.
 */
function ComparePanel({
  scanSrc,
  scanAlt,
  artSrc,
  artAlt,
  artPlaceholder,
}: {
  scanSrc?: string | null;
  scanAlt: string;
  artSrc?: string | null;
  artAlt: string;
  artPlaceholder: string;
}) {
  const { t } = useTranslation();
  const theme = useTheme();
  const isPhone = useIsPhone();
  const isMdUp = useMediaQuery(theme.breakpoints.up('md'));
  const [prefs, setPrefs] = useScanReviewPrefs();
  const [dragHeight, setDragHeight] = useState<number | null>(null);
  const drag = useRef<{ pointerId: number; startY: number; startHeight: number } | null>(null);

  const defaultHeight = isPhone ? 260 : isMdUp ? 420 : 340;
  const height = dragHeight ?? (isPhone ? null : clampCompareHeight(prefs.compareHeight)) ?? defaultHeight;
  const imageHeight = height - 2 * COMPARE_PADDING - COMPARE_LABEL_HEIGHT;

  const onPointerDown = (e: ReactPointerEvent<HTMLDivElement>) => {
    if (e.button !== 0) return;
    e.preventDefault();
    e.currentTarget.setPointerCapture(e.pointerId);
    drag.current = { pointerId: e.pointerId, startY: e.clientY, startHeight: height };
    setDragHeight(height);
  };
  const onPointerMove = (e: ReactPointerEvent<HTMLDivElement>) => {
    const d = drag.current;
    if (!d || d.pointerId !== e.pointerId) return;
    setDragHeight(clampCompareHeight(d.startHeight + e.clientY - d.startY));
  };
  const endDrag = (e: ReactPointerEvent<HTMLDivElement>) => {
    const d = drag.current;
    if (!d || d.pointerId !== e.pointerId) return;
    drag.current = null;
    const final = clampCompareHeight(d.startHeight + e.clientY - d.startY);
    if (final != null && final !== d.startHeight) setPrefs({ compareHeight: final });
    setDragHeight(null);
  };
  const onKeyDown = (e: ReactKeyboardEvent<HTMLDivElement>) => {
    if (e.key !== 'ArrowUp' && e.key !== 'ArrowDown') return;
    // Handled here: the page's own ArrowUp/Down (move between cards) skips prevented keys.
    e.preventDefault();
    const next = clampCompareHeight(height + (e.key === 'ArrowDown' ? 20 : -20));
    if (next != null) setPrefs({ compareHeight: next });
  };

  const pane = (label: string, src: string | null | undefined, alt: string, placeholder: string) => (
    <Stack spacing={0} sx={{ alignItems: 'center', minWidth: 0, maxWidth: { xs: 'calc(50% - 4px)', sm: 'calc(50% - 8px)' } }}>
      <Typography
        variant="caption"
        color="text.secondary"
        noWrap
        sx={{ fontWeight: 600, height: COMPARE_LABEL_HEIGHT, lineHeight: `${COMPARE_LABEL_HEIGHT}px`, maxWidth: '100%' }}
      >
        {label}
      </Typography>
      {src ? (
        <Box
          // A fresh element per image, so the previous card's picture never lingers while this loads.
          key={src}
          component="img"
          src={src}
          alt={alt}
          draggable={false}
          sx={{
            display: 'block',
            height: imageHeight,
            width: 'auto',
            maxWidth: '100%',
            // Hold a card-shaped space while loading; the image's own shape once it has (e.g. landscape cards).
            aspectRatio: `auto ${CARD_ASPECT}`,
            objectFit: 'contain',
          }}
        />
      ) : (
        <Box
          sx={{
            height: imageHeight,
            aspectRatio: CARD_ASPECT,
            maxWidth: '100%',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            border: 1,
            borderStyle: 'dashed',
            borderColor: 'divider',
            borderRadius: 1,
            p: 1,
          }}
        >
          <Typography variant="body2" color="text.secondary" align="center">
            {placeholder}
          </Typography>
        </Box>
      )}
    </Stack>
  );

  return (
    <Box>
      <Box
        sx={{
          height,
          p: `${COMPARE_PADDING}px`,
          display: 'flex',
          justifyContent: 'center',
          alignItems: 'flex-start',
          gap: { xs: 1, sm: 2 },
          bgcolor: 'action.hover',
          borderRadius: 1,
          overflow: 'hidden',
        }}
      >
        {pane(t('scan.detail.uploadedScan'), scanSrc, scanAlt, '—')}
        {pane(t('scan.detail.matchedArt'), artSrc, artAlt, artPlaceholder)}
      </Box>
      {!isPhone && (
        <Tooltip title={t('scan.detail.resizeHint')} placement="bottom" enterDelay={600}>
          <Box
            role="separator"
            aria-orientation="horizontal"
            aria-label={t('scan.detail.resizeLabel')}
            aria-valuemin={COMPARE_MIN_HEIGHT}
            aria-valuemax={COMPARE_MAX_HEIGHT}
            aria-valuenow={height}
            tabIndex={0}
            onPointerDown={onPointerDown}
            onPointerMove={onPointerMove}
            onPointerUp={endDrag}
            onPointerCancel={endDrag}
            onDoubleClick={() => setPrefs({ compareHeight: null })}
            onKeyDown={onKeyDown}
            sx={{
              height: 12,
              cursor: 'ns-resize',
              touchAction: 'none',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              borderRadius: 1,
              outline: 'none',
              '& > span': {
                width: 48,
                height: 4,
                borderRadius: 2,
                bgcolor: dragHeight != null ? 'primary.main' : 'divider',
                transition: 'background-color 120ms',
              },
              '&:hover > span, &:focus-visible > span': { bgcolor: 'primary.main' },
            }}
          >
            <span />
          </Box>
        </Tooltip>
      )}
    </Box>
  );
}

/** A letterboxed thumbnail used in the master list. */
function Thumb({ src, alt }: { src?: string | null; alt: string }) {
  return (
    <Box
      sx={{
        width: { xs: 56, sm: 84 },
        height: { xs: 78, sm: 116 },
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

/** Master-list row: scan thumbnail beside matched-art thumbnail + identity + status. Memoized (with
 * key-based callbacks) so moving the selection re-renders two rows, not the whole list. */
const MasterRow = memo(function MasterRow({
  item,
  number,
  selected,
  badgeSettings,
  onSelect,
  onToggle,
  readOnly = false,
  failedValidation = false,
}: {
  item: ScanItem;
  /** The scan's place in the whole list (filters and sorts don't change it). */
  number: number;
  selected: boolean;
  badgeSettings?: ScanBadgeSettingsDto;
  onSelect: (key: string) => void;
  onToggle: (key: string, v: boolean, shiftKey: boolean) => void;
  readOnly?: boolean;
  /** Failed the last validation: drawn with a red frame AND a labelled chip (never colour alone). */
  failedValidation?: boolean;
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
  // Keep the selected row on screen as the keyboard review moves through the list. Only the list
  // scrolls (never the page), and the list's sticky header is allowed for at the top.
  const rowRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const row = rowRef.current;
    const list = row?.closest<HTMLElement>('[data-scan-list]');
    if (!selected || !row || !list || list.scrollHeight <= list.clientHeight) return;
    const r = row.getBoundingClientRect();
    const l = list.getBoundingClientRect();
    const top = l.top + ((list.firstElementChild as HTMLElement | null)?.offsetHeight ?? 0);
    if (r.top < top) list.scrollTop -= top - r.top;
    else if (r.bottom > l.bottom) list.scrollTop += r.bottom - l.bottom;
  }, [selected]);
  return (
    <Box
      ref={rowRef}
      data-scan-row={item.key}
      onClick={() => onSelect(item.key)}
      sx={{
        p: { xs: 1, sm: 1.5 },
        display: 'flex',
        gap: { xs: 1, sm: 1.5 },
        alignItems: 'center',
        cursor: 'pointer',
        borderLeft: 4,
        borderColor: selected ? 'primary.main' : 'transparent',
        // Inset so the frame doesn't shift the layout or get clipped by the list.
        ...(failedValidation && { outline: '2px solid', outlineColor: 'error.main', outlineOffset: '-2px' }),
        ...buyListFrameSx(item.buyList?.priority),
        bgcolor: selected ? 'action.selected' : 'transparent',
        '&:hover': { bgcolor: selected ? 'action.selected' : 'action.hover' },
      }}
    >
      <Typography
        variant="caption"
        color={selected ? 'primary' : 'text.secondary'}
        sx={{ minWidth: 28, textAlign: 'right', fontWeight: selected ? 700 : 400, fontVariantNumeric: 'tabular-nums' }}
      >
        {t('scan.master.rowNumber', { number })}
      </Typography>
      <Checkbox
        checked={item.include}
        disabled={readOnly}
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
        onChange={(e) => onToggle(item.key, e.target.checked, shiftHeld.current)}
        sx={{ p: 0 }}
      />
      <Stack direction="row" spacing={{ xs: 0.5, sm: 0.75 }}>
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
          {item.buyList && <BuyListHitChip hit={item.buyList} />}
          {failedValidation && (
            <Chip size="small" color="error" icon={<ErrorOutlineIcon />} label={t('scan.validation.failedChip')} />
          )}
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
});

/** The per-copy property editors, reused by the detail panel and (a subset) the bulk dialog. */
function PropertyFields({
  game,
  props,
  onChange,
  foilTypeOptions,
  tagOptions,
  ruleTags,
  foilDetected,
  disabled = false,
}: {
  game: string;
  props: ItemProps;
  onChange: (patch: Partial<ItemProps>) => void;
  foilTypeOptions: string[];
  tagOptions: string[];
  /** Tags a tag rule added (marked in the tags box). */
  ruleTags?: string[];
  /** The finish the matcher read off the card, if any. */
  foilDetected?: boolean | null;
  disabled?: boolean;
}) {
  const { t } = useTranslation();
  return (
    <Stack spacing={2}>
      <Stack direction="row" spacing={2} flexWrap="wrap" useFlexGap>
        <SelectField
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
        </SelectField>
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
        {foilDetected != null && foilDetected === props.isFoil && (
          <Typography variant="caption" color="text.secondary">
            {t(foilDetected ? 'scan.props.foilDetected' : 'scan.props.nonFoilDetected')}
          </Typography>
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

/** The selected scan's number, and a box to jump to another one: a long review on a phone can pick up
 * where it left off. Enter (or the phone keyboard's Go) jumps; a number past the end does nothing. */
function GoToScan({
  selected,
  total,
  onGo,
}: {
  selected: number | null;
  total: number;
  onGo: (number: number) => void;
}) {
  const { t } = useTranslation();
  const [text, setText] = useState('');
  const number = Math.floor(Number(text));
  const valid = text.trim() !== '' && number >= 1 && number <= total;
  const go = () => {
    if (!valid) return;
    onGo(number);
    setText('');
  };
  return (
    <Stack direction="row" spacing={0.5} alignItems="center">
      {selected != null && (
        <Typography variant="caption" sx={{ fontWeight: 600, whiteSpace: 'nowrap' }}>
          {t('scan.master.selectedNumber', { number: selected, total })}
        </Typography>
      )}
      <TextField
        size="small"
        type="number"
        placeholder={t('scan.master.goToPlaceholder')}
        value={text}
        onChange={(e) => setText(e.target.value)}
        onKeyDown={(e) => {
          // Enter here must not confirm the selected card.
          e.stopPropagation();
          if (e.key === 'Enter') go();
        }}
        inputProps={{ min: 1, max: total, inputMode: 'numeric', enterKeyHint: 'go', 'aria-label': t('scan.master.goTo') }}
        sx={{ width: 92, '& input': { py: 0.5, px: 1 } }}
      />
      <Button size="small" disabled={!valid} onClick={go} sx={{ minWidth: 0 }}>
        {t('scan.master.go')}
      </Button>
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
  correcting,
  onCorrectingChange,
  readOnly = false,
  failedValidationQuery = null,
  intake = false,
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
  /** Whether the correction search is open (owned by the page so the S / Enter shortcuts can open it). */
  correcting: boolean;
  onCorrectingChange: (open: boolean) => void;
  /** Viewing someone else's background batch: hide the actions and lock the fields. */
  readOnly?: boolean;
  /** The validation query this card failed (null = it didn't fail, or wasn't validated). */
  failedValidationQuery?: string | null;
  /** On the intake page, where cards are bought rather than added. */
  intake?: boolean;
}) {
  const { t } = useTranslation();
  const setCorrecting = onCorrectingChange;
  const [scanOpen, setScanOpen] = useState(false);
  const id = identityOf(item);

  return (
    <Paper variant="outlined" sx={{ p: { xs: 1.5, sm: 3 }, border: { xs: 0, sm: 1 }, borderColor: { sm: 'divider' } }}>
      <Stack spacing={2}>
        {/* Compare at the top: uploaded scan and matched art next to each other. */}
        <ComparePanel
          scanSrc={item.previewUrl}
          scanAlt={item.fileName}
          artSrc={id?.imageUri ?? null}
          artAlt={id?.name ?? t('scan.status.noMatch')}
          artPlaceholder={item.status === 'matching' ? t('scan.status.matching') : t('scan.status.noMatch')}
        />

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

        {item.buyList && (
          <Alert severity="warning" icon={<TrackChangesIcon />}>
            <Typography variant="subtitle2">
              {t('buyList.scan.detailTitle', { count: item.buyList.wantedQuantity })} ·{' '}
              {t(`buyList.priority.${item.buyList.priority}`)}
            </Typography>
            {item.buyList.note && (
              <Typography variant="body2" sx={{ whiteSpace: 'pre-line' }}>
                {item.buyList.note}
              </Typography>
            )}
            {item.buyList.setAsideLocationName && (
              <Typography variant="body2">
                {t(intake ? 'buyList.scan.setAsideIntake' : 'buyList.scan.setAside', {
                  location: item.buyList.setAsideLocationName,
                })}
              </Typography>
            )}
          </Alert>
        )}

        {failedValidationQuery !== null && (
          <Alert severity="error" icon={<ErrorOutlineIcon />}>
            {id
              ? t('scan.validation.detailFailed', { query: failedValidationQuery })
              : t('scan.validation.detailFailedNoMatch', { query: failedValidationQuery })}
          </Alert>
        )}

        {readOnly ? null : correcting ? (
          <Box>
            <CorrectionSearch
              game={game}
              setCodes={artSets.map((s) => s.setCode)}
              setNames={artSets.map((s) => s.setName).join(', ')}
              onPick={(r) => {
                setCorrecting(false);
                onCorrect(r);
              }}
              onCancel={() => setCorrecting(false)}
            />
            <Button size="small" onClick={() => setCorrecting(false)} sx={{ mt: 1 }}>
              {t('common.actions.cancel')}
            </Button>
          </Box>
        ) : (
          <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
            <Tooltip title={t('scan.keyboard.confirmTooltip')}>
              <span>
                <Button
                  variant={item.verified ? 'outlined' : 'contained'}
                  color="success"
                  startIcon={<CheckCircleIcon />}
                  disabled={!id}
                  onClick={onVerify}
                >
                  {item.verified ? t('scan.detail.looksCorrect') : t('scan.detail.confirmMatch')}
                </Button>
              </span>
            </Tooltip>
            <Tooltip title={t('scan.keyboard.searchTooltip')}>
              <Button variant="outlined" startIcon={<SearchIcon />} onClick={() => setCorrecting(true)}>
                {t('scan.detail.searchCatalog')}
              </Button>
            </Tooltip>
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
          foilDetected={item.match?.foilDetected}
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

/** Whether a key press on `el` is someone typing (text boxes keep their keys; checkboxes don't). */
function isTypingTarget(el: HTMLElement): boolean {
  if (el.isContentEditable || el.tagName === 'TEXTAREA' || el.tagName === 'SELECT') return true;
  if (el.tagName !== 'INPUT') return false;
  const type = (el as HTMLInputElement).type;
  return !['checkbox', 'radio', 'button', 'submit', 'reset', 'range', 'color', 'file'].includes(type);
}

/** The review keyboard shortcuts, as [keys, i18n key of what they do]. */
const SHORTCUTS: [string[], string][] = [
  [['Enter'], 'scan.keyboard.keys.confirm'],
  [['Enter', '…'], 'scan.keyboard.keys.holdConfirm'],
  [['↓', 'J'], 'scan.keyboard.keys.next'],
  [['↑', 'K'], 'scan.keyboard.keys.previous'],
  [['S', '/'], 'scan.keyboard.keys.search'],
  [['Esc'], 'scan.keyboard.keys.cancelSearch'],
  [['X'], 'scan.keyboard.keys.exclude'],
  [['Ctrl', 'Z'], 'scan.keyboard.keys.undo'],
  [['?'], 'scan.keyboard.keys.help'],
];

/** "Off" or "0.5 s per card" for a hold-Enter pace. */
function holdEnterLabel(t: TFunction, fmt: ReturnType<typeof useFormatters>, ms: number) {
  return ms <= 0
    ? t('scan.keyboard.holdEnter.off')
    : t('scan.keyboard.holdEnter.perCard', { seconds: fmt.number(ms / 1000, { maximumFractionDigits: 2 }) });
}

/** How fast holding Enter moves through the cards (remembered per user). */
function HoldEnterSpeed() {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const [prefs, setPrefs] = useScanReviewPrefs();
  const saved = holdEnterPace(prefs.holdEnterMs);
  const [index, setIndex] = useState<number | null>(null);
  const shown = index ?? Math.max(0, HOLD_ENTER_STEPS.indexOf(saved));
  return (
    <Box>
      <Typography variant="subtitle2" id="hold-enter-speed">
        {t('scan.keyboard.holdEnter.title')}
      </Typography>
      <Typography variant="body2" color="text.secondary">
        {t('scan.keyboard.holdEnter.help')}
      </Typography>
      <Stack direction="row" spacing={2} alignItems="center" sx={{ pt: 0.5 }}>
        <Slider
          aria-labelledby="hold-enter-speed"
          min={0}
          max={HOLD_ENTER_STEPS.length - 1}
          step={1}
          marks
          value={shown}
          onChange={(_, v) => setIndex(v as number)}
          onChangeCommitted={(_, v) => {
            setIndex(null);
            setPrefs({ holdEnterMs: HOLD_ENTER_STEPS[v as number] });
          }}
          getAriaValueText={(i) => holdEnterLabel(t, fmt, HOLD_ENTER_STEPS[i])}
          sx={{ flex: 1 }}
        />
        <Typography variant="body2" sx={{ minWidth: 104, fontWeight: 600 }}>
          {holdEnterLabel(t, fmt, HOLD_ENTER_STEPS[shown])}
        </Typography>
      </Stack>
    </Box>
  );
}

function ShortcutsDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const { t } = useTranslation();
  return (
    <Dialog open={open} onClose={onClose} maxWidth="xs" fullWidth>
      <DialogTitle>{t('scan.keyboard.title')}</DialogTitle>
      <DialogContent>
        <Stack spacing={1.25}>
          {SHORTCUTS.map(([keys, label]) => (
            <Stack key={label} direction="row" spacing={2} alignItems="center">
              <Stack direction="row" spacing={0.5} sx={{ minWidth: 96 }}>
                {keys.map((k) => (
                  <Kbd key={k}>{k}</Kbd>
                ))}
              </Stack>
              <Typography variant="body2">{t(label)}</Typography>
            </Stack>
          ))}
          <Typography variant="caption" color="text.secondary" sx={{ pt: 1 }}>
            {t('scan.keyboard.footnote')}
          </Typography>
          <Divider />
          <HoldEnterSpeed />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('common.actions.close')}</Button>
      </DialogActions>
    </Dialog>
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
            <SelectField
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
            </SelectField>,
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
              <SelectField
                size="small"
                label={t('scan.bulk.mode')}
                value={state.tagsMode}
                onChange={(e) => setState((s) => ({ ...s, tagsMode: e.target.value as 'add' | 'replace' }))}
                sx={{ width: 120 }}
              >
                <MenuItem value="add">{t('scan.bulk.modeAdd')}</MenuItem>
                <MenuItem value="replace">{t('scan.bulk.modeReplace')}</MenuItem>
              </SelectField>
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
  /** Scan a seller's cards for an intake quote instead of adding them to a location (see IntakePage). */
  intake?: ScanIntakeMode;
}

/** One confirmed scan handed to the intake quote. */
export interface IntakeScanCard {
  /** The scan's key on the Scan page (pass it back in {@link ScanIntakeMode.boughtKeys}). */
  key: string;
  item: ScanCommitItem;
  /** The scan photo (a blob URL that lives as long as the Scan page). */
  previewUrl: string;
}

/** Intake: "Complete" hands the confirmed cards to the quote; nothing is written by the Scan page. */
export interface ScanIntakeMode {
  onComplete: (cards: IntakeScanCard[]) => void;
  /** Cards that were bought — they're dropped from the scan list (a new array each time). */
  boughtKeys?: string[];
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

export function ScanPage({ lockedContainerId, auditMode = false, onAuditCommitted, batch, intake }: ScanPageProps = {}) {
  const { t } = useTranslation();
  const fmt = useFormatters();
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
  const [findingCards, setFindingCards] = useState(false);
  const [bulkOpen, setBulkOpen] = useState(false);
  const [items, setItems] = useState<ScanItem[]>([]);
  const [selectedKey, setSelectedKey] = useState<string | null>(null);
  const [controls, setControls] = useState<ScanListControls>(DEFAULT_CONTROLS);
  // Scan validation: the query typed (or loaded from a saved rule) and the last run's outcome. Every
  // run replaces the outcome wholesale, so an earlier pass can never vouch for a card.
  const [validationQuery, setValidationQuery] = useState('');
  const [validation, setValidation] = useState<ScanValidationResult | null>(null);
  const [validationErrors, setValidationErrors] = useState<string[]>([]);
  const [validating, setValidating] = useState(false);
  const validationRun = useRef(0);
  const fileInput = useRef<HTMLInputElement>(null);
  const cameraInput = useRef<HTMLInputElement>(null);

  const selectedItem = items.find((it) => it.key === selectedKey) ?? null;
  // Phone: the list and the detail can't sit side by side, so tapping a card opens its detail full
  // screen; the review actions there move on through the cards as the keyboard flow does.
  const isPhone = useIsPhone();
  const [detailOpen, setDetailOpen] = useState(false);
  const [filtersOpen, setFiltersOpen] = useState(false);
  const openRow = useCallback(
    (key: string) => {
      setSelectedKey(key);
      if (isPhone) setDetailOpen(true);
    },
    [isPhone],
  );

  // The filtered + sorted view of the scans. Everything the toolbar acts on (Confirm / Add, the
  // select-all header, bulk edit, and the batch counts) is scoped to these visible items, so a
  // filtered list only ever confirms/commits what the user can actually see.
  const visibleItems = useMemo(
    () => applyControls(items, controls, validation?.failed),
    [items, controls, validation],
  );
  const visibleKeys = useMemo(() => new Set(visibleItems.map((it) => it.key)), [visibleItems]);
  // Each scan's number: its place in the whole list, so a card keeps its number through a filter, a
  // sort, or a refresh (which clears them).
  const scanNumbers = useMemo(() => new Map(items.map((it, i) => [it.key, i + 1])), [items]);
  const selectedNumber = selectedKey === null ? null : (scanNumbers.get(selectedKey) ?? null);
  const controlsActive =
    controls.checked !== 'all' ||
    controls.failedOnly ||
    controls.buyListOnly ||
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

  // Bring a row on screen in the list: Go to, and the card restored after a refresh. Asked for by key so
  // it runs once the row has rendered (the filters may just have been cleared to show it).
  const [scrollToKey, setScrollToKey] = useState<string | null>(null);
  useEffect(() => {
    if (!scrollToKey || !visibleKeys.has(scrollToKey)) return;
    document.querySelector(`[data-scan-row="${scrollToKey}"]`)?.scrollIntoView({ block: 'center' });
    setScrollToKey(null);
  }, [scrollToKey, visibleKeys]);
  const goToNumber = (number: number) => {
    const item = items[number - 1];
    if (!item) return;
    if (!visibleKeys.has(item.key)) setControls(DEFAULT_CONTROLS);
    setSelectedKey(item.key);
    setScrollToKey(item.key);
  };

  const updateItem = (key: string, patch: Partial<ScanItem>) =>
    setItems((prev) => prev.map((it) => (it.key === key ? { ...it, ...patch } : it)));

  // Anchor for Shift-click range selection: the key of the last checkbox toggled by a plain click.
  // A Shift-click sets every selectable row between the anchor and the clicked row to the clicked
  // row's new state; the anchor only moves on a plain click, so consecutive Shift-clicks re-range
  // from the same origin (standard file-explorer / Gmail behaviour).
  const anchorKeyRef = useRef<string | null>(null);
  // Stable for the memoized master rows; always runs the latest toggleInclude.
  const toggleIncludeRef = useRef<typeof toggleInclude>(() => {});
  const toggleRow = useCallback(
    (key: string, checked: boolean, shiftKey: boolean) => toggleIncludeRef.current(key, checked, shiftKey),
    [],
  );
  const toggleInclude = (key: string, checked: boolean, shiftKey: boolean) => {
    // Range selection walks the VISIBLE order so a Shift-click never toggles rows hidden by a filter.
    const idx = visibleItems.findIndex((it) => it.key === key);
    const anchorIdx = anchorKeyRef.current
      ? visibleItems.findIndex((it) => it.key === anchorKeyRef.current)
      : -1;
    if (shiftKey && anchorIdx !== -1 && idx !== -1) {
      const [lo, hi] = anchorIdx <= idx ? [anchorIdx, idx] : [idx, anchorIdx];
      const rangeKeys = new Set(visibleItems.slice(lo, hi + 1).map((it) => it.key));
      setItems((prev) => prev.map((it) => (rangeKeys.has(it.key) ? { ...it, include: checked } : it)));
      return; // Keep the anchor where it is so the range can be adjusted with another Shift-click.
    }
    setItems((prev) => prev.map((it) => (it.key === key ? { ...it, include: checked } : it)));
    anchorKeyRef.current = key;
  };
  toggleIncludeRef.current = toggleInclude;

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

  // Intake: once the quote is bought, its cards leave the scan list.
  const boughtKeys = intake?.boughtKeys;
  useEffect(() => {
    if (!boughtKeys?.length) return;
    const bought = new Set(boughtKeys);
    dropItems((it) => bought.has(it.key));
    // The buy counted copies off the Buy List, so what's left is checked again.
    setItems((prev) => prev.map((it) => ({ ...it, buyListSig: undefined })));
    // dropItems only wraps setItems; re-run solely when a new set of bought keys arrives.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [boughtKeys]);

  // Free every remaining preview URL when the page unmounts (navigating away mid-session).
  const itemsRef = useRef(items);
  itemsRef.current = items;
  useEffect(() => () => itemsRef.current.forEach((it) => URL.revokeObjectURL(it.previewUrl)), []);

  const gamesQuery = useQuery({ queryKey: ['games'], queryFn: api.games });
  const setsQuery = useQuery({ queryKey: ['sets', game], queryFn: () => api.sets(game), enabled: !!game });
  // Intake buys into the admin's location, so it never picks one (and intake staff may not see locations).
  const locations = useQuery({ queryKey: ['locations', undefined], queryFn: () => api.locations(), enabled: !intake });
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

  // Reset the set filter whenever the game changes (a set only belongs to one game). Validation rules
  // are per game too.
  useEffect(() => {
    setArtSets([]);
    setLanguage('');
    validationRun.current++;
    setValidationQuery('');
    setValidation(null);
    setValidationErrors([]);
    setControls((c) => (c.failedOnly ? { ...c, failedOnly: false } : c));
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

  // Adding cards goes in chunks, so a long list shows real progress instead of looking frozen. Each
  // chunk's cards leave the list as soon as it's saved, so a failure part way never adds a card twice.
  const [commitProgress, setCommitProgress] = useState<{ done: number; total: number; indeterminate?: boolean } | null>(
    null,
  );
  /** Cards already added when a chunked commit failed part way (shown with the error). */
  const [commitPartial, setCommitPartial] = useState(0);
  /** Cards the running (or last) commit has saved so far. */
  const commitSaved = useRef(0);
  const commit = useMutation<ScanCommitResultDto | AuditCommitResultDto | ScanBatchCommitResultDto>({
    mutationFn: async () => {
      const toCommit = items.filter(isVisibleCommittable);
      const cid = containerId as number;
      commitSaved.current = 0;
      setCommitPartial(0);
      setCommitProgress({ done: 0, total: toCommit.length, indeterminate: auditMode });
      try {
        // Audit mode reconciles the whole location against the scan in one step: it can't be split.
        if (auditMode) {
          const res = await api.auditCommit(cid, toCommit.map((it) => toCommitItem(it, game)));
          const keys = new Set(toCommit.map((it) => it.key));
          dropItems((it) => keys.has(it.key));
          return res;
        }
        // The server commits a batch's saved copy, so flush pending edits first.
        if (batchStore && batchId != null) await batchStore.sync(itemsRef.current);
        let merged: ScanCommitResultDto & Partial<ScanBatchCommitResultDto> = { imported: 0 };
        for (let i = 0; i < toCommit.length; i += COMMIT_CHUNK_SIZE) {
          const chunk = toCommit.slice(i, i + COMMIT_CHUNK_SIZE);
          const res =
            batchStore && batchId != null
              ? await api.scanBatchCommit(batchId, cid, chunk.map((it) => it.batchItemId!))
              : await api.scanCommit(cid, chunk.map((it) => toCommitItem(it, game)), false, true);
          merged = mergeCommitResults(merged, res);
          // Drop exactly the cards just saved (cards confirmed meanwhile stay for the next add).
          const keys = new Set(chunk.map((it) => it.key));
          dropItems((it) => keys.has(it.key));
          commitSaved.current += chunk.length;
          setCommitProgress({ done: commitSaved.current, total: toCommit.length });
        }
        return merged;
      } catch (e) {
        setCommitPartial(commitSaved.current);
        throw e;
      } finally {
        setCommitProgress(null);
      }
    },
    onSettled: (res, err) => {
      // Refresh whatever the saved chunks changed, even when a later chunk failed.
      if (!res && commitSaved.current === 0) {
        if (batch && err instanceof ApiError && err.status === 409) batch.onClaimLost();
        return;
      }
      qc.invalidateQueries({ queryKey: ['collection'] });
      qc.invalidateQueries({ queryKey: ['locations'] });
      qc.invalidateQueries({ queryKey: ['dashboard'] });
      qc.invalidateQueries({ queryKey: ['buy-list'] });
      setItems((prev) => prev.map((it) => ({ ...it, buyListSig: undefined })));
      if (auditMode && res) {
        // The audited location's card lists change wholesale — refresh its detail view too.
        qc.invalidateQueries({ queryKey: ['location'] });
        onAuditCommitted?.(res as AuditCommitResultDto);
      }
      if (batch) {
        if (err instanceof ApiError && err.status === 409) batch.onClaimLost();
        else if ((res as ScanBatchCommitResultDto | undefined)?.batchClosed) batch.onClosed();
        else batch.onChanged();
      }
    },
  });

  /** Confirm every visible checked-and-matched item at once (batch verify). */
  const confirmChecked = () =>
    setItems((prev) =>
      prev.map((it) =>
        visibleKeys.has(it.key) && it.include && identityOf(it) ? { ...it, verified: true } : it,
      ),
    );

  // --- Master-list selection helpers (visible cards only). Any scan can be checked — unmatched or
  // still-matching ones too, so they can be deleted — but only checked + confirmed scans with a
  // resolved identity are ever added (isCommittable). ---
  const selectAll = () =>
    setItems((prev) => prev.map((it) => (visibleKeys.has(it.key) ? { ...it, include: true } : it)));
  const selectNone = () =>
    setItems((prev) => prev.map((it) => (visibleKeys.has(it.key) ? { ...it, include: false } : it)));
  const invertSelection = () =>
    setItems((prev) => prev.map((it) => (visibleKeys.has(it.key) ? { ...it, include: !it.include } : it)));

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

  // A camera photo is cut into one scanner-style image per card in it (one card, or several laid out
  // with gaps) before matching; a photo with no card found goes through untouched.
  async function handlePhotos(files: FileList | null) {
    const chosen = Array.from(files ?? []);
    if (chosen.length === 0) return;
    setFindingCards(true);
    const cards: File[] = [];
    try {
      for (const file of chosen) cards.push(...(await splitPhotoIntoCards(file, game)));
    } finally {
      setFindingCards(false);
    }
    await stageFiles(cards);
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
          // So does a finish read off the card (MTG star = foil, bullet = non-foil).
          if (match.foilDetected != null) {
            patch.isFoil = match.foilDetected;
            if (!match.foilDetected) patch.foilType = null;
          }
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
  const selectableCount = visibleItems.length;
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
  // Keyed on the mode, not the `intake` prop object (a new one every parent render), or the session would
  // be re-created — and re-restored on top of the current scans — on each render.
  const isIntake = !!intake;
  const sessionStore = useMemo(
    () =>
      isBatch
        ? null
        : new ScanSessionStore<ScanItem>(auditMode ? `audit-${lockedContainerId}` : isIntake ? 'intake' : 'scan'),
    [auditMode, lockedContainerId, isBatch, isIntake],
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
          // The Buy List may have changed since.
          buyList: undefined,
          buyListSig: undefined,
          // Blob URLs die with the tab; TIFFs never had one (their preview is the server's data URI).
          previewUrl:
            !it.file || isTiff(it.file) ? (it.match?.scanPreviewDataUri ?? '') : URL.createObjectURL(it.file),
        }));
        setGame(restored.game);
        setItems((prev) => [...prev, ...revived]);
        setRestoredKeys(new Set(revived.map((it) => it.key)));
        if (restored.selectedKey && revived.some((it) => it.key === restored.selectedKey)) {
          setSelectedKey(restored.selectedKey);
          setScrollToKey(restored.selectedKey);
        }
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
    const handle = window.setTimeout(() => void sessionStore.sync(game, items, selectedKey), 400);
    return () => window.clearTimeout(handle);
  }, [items, game, selectedKey, sessionStore]);
  // Flush on unmount (e.g. the audit navigates away right after committing) — the debounce would drop it.
  const gameRef = useRef(game);
  gameRef.current = game;
  const selectedKeyRef = useRef(selectedKey);
  selectedKeyRef.current = selectedKey;
  useEffect(
    () => () => {
      if (sessionStore && restoreDone.current)
        void sessionStore.sync(gameRef.current, itemsRef.current, selectedKeyRef.current);
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
            chunk.map(({ it }) => toRuleItem(it)),
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
  // --- Buy List: flag the cards on the game's list, checked by identity so a hand-corrected card counts.
  // The server decides which copies are really wanted when the cards are committed or bought. ---
  const buyListInFlight = useRef(new Set<string>());
  useEffect(() => {
    if (auditMode) return;
    const pending = items.flatMap((it) => {
      const sig = buyListSignature(it, game);
      return sig !== null && sig !== it.buyListSig && !buyListInFlight.current.has(`${it.key}|${sig}`) ? [{ it, sig }] : [];
    });
    if (pending.length === 0) return;
    const handle = window.setTimeout(() => {
      for (let i = 0; i < pending.length; i += 200) {
        const chunk = pending.slice(i, i + 200);
        const sigs = new Map(chunk.map(({ it, sig }) => [it.key, sig]));
        chunk.forEach(({ it, sig }) => buyListInFlight.current.add(`${it.key}|${sig}`));
        // Apply only to items still showing the card that was checked. A failed check marks the item as
        // checked anyway, so it isn't retried in a loop.
        const settle = (hits: Map<string, BuyListHitDto>) =>
          setItems((prev) =>
            prev.map((it) => {
              const sig = sigs.get(it.key);
              if (sig === undefined || buyListSignature(it, game) !== sig) return it;
              return { ...it, buyList: hits.get(it.key), buyListSig: sig };
            }),
          );
        api
          .scanBuyList(
            game,
            chunk.map(({ it }) => toRuleItem(it)),
          )
          .then((res) => settle(new Map(res.map((r) => [r.key, r.hit]))))
          .catch((e) => {
            console.warn('Checking the Buy List failed', e);
            settle(new Map());
          })
          .finally(() => chunk.forEach(({ it, sig }) => buyListInFlight.current.delete(`${it.key}|${sig}`)));
      }
    }, 300);
    return () => window.clearTimeout(handle);
  }, [items, game, auditMode]);
  const buyListCount = useMemo(() => items.filter((it) => it.buyList).length, [items]);
  // Nothing left to show: don't leave the list filtered down to nothing.
  useEffect(() => {
    if (buyListCount === 0) setControls((c) => (c.buyListOnly ? { ...c, buyListOnly: false } : c));
  }, [buyListCount]);
  const commitBuyList = (commit.data as ScanCommitResultDto | undefined)?.buyList;

  // Hold the commit until every card about to be added has its rule tags.
  const rulesPending =
    applyTagRules && !readOnly && items.some((it) => isVisibleCommittable(it) && ruleSignature(it, game) !== it.ruleSig);

  /** Check the whole scan list (not just the filtered view) against the validation query. Cards with no
   * identity fail; cards still matching aren't checked. */
  const runValidation = async () => {
    const run = ++validationRun.current;
    const query = validationQuery.trim();
    // Wipe the previous outcome first: nothing from an earlier run survives this one.
    setValidation(null);
    setValidationErrors([]);
    if (!query) return;
    const snapshot = itemsRef.current;
    const checkable = snapshot.filter((it) => it.status !== 'matching');
    const identified = checkable.filter((it) => identityOf(it));
    const failed = new Set(checkable.filter((it) => !identityOf(it)).map((it) => it.key));
    // Large batches go in chunks; an empty list still sends one request so the query gets checked.
    const chunks: ScanItem[][] = [];
    for (let i = 0; i < identified.length; i += 500) chunks.push(identified.slice(i, i + 500));
    if (chunks.length === 0) chunks.push([]);
    setValidating(true);
    try {
      for (const chunk of chunks) {
        const res = await api.scanValidate(game, query, chunk.map(toRuleItem));
        if (run !== validationRun.current) return;
        if (res.errors.length > 0) {
          setValidationErrors(res.errors);
          return;
        }
        res.failedKeys.forEach((k) => failed.add(k));
      }
      setValidation({
        query,
        checked: new Set(checkable.map((it) => it.key)),
        failed,
        skipped: snapshot.length - checkable.length,
      });
      // Nothing failed: don't leave the list filtered down to nothing.
      if (failed.size === 0) setControls((c) => (c.failedOnly ? { ...c, failedOnly: false } : c));
    } catch (e) {
      if (run === validationRun.current) setValidationErrors([(e as Error).message]);
    } finally {
      if (run === validationRun.current) setValidating(false);
    }
  };
  // Counts over the scans still in the list (committed / removed ones drop out).
  const validationFailedCount = validation ? items.filter((it) => validation.failed.has(it.key)).length : 0;
  const validationCheckedCount = validation ? items.filter((it) => validation.checked.has(it.key)).length : 0;

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

  // Delete every visible checked scan (after the confirm dialog). For a batch this also deletes the
  // server's stored images; the originals stay in the watched folder's _processed subfolder.
  const [confirmDelete, setConfirmDelete] = useState(false);
  const deleteChecked = () => {
    removeItems((it) => visibleKeys.has(it.key) && it.include);
    setConfirmDelete(false);
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

  // --- Save as batch: store every local scan on the server as a scan batch, released for anyone to
  // review, and clear them from this page. Images go up one per request (each under the server's
  // request limit); if any fails, the half-sent batch is thrown away and the scans stay here. ---
  const canSaveBatch = !auditMode && !batch && !intake;
  const savableItems = useMemo(() => items.filter((it) => it.file), [items]);
  const [saveBatchOpen, setSaveBatchOpen] = useState(false);
  const [saveBatchName, setSaveBatchName] = useState('');
  const [saveProgress, setSaveProgress] = useState<{ done: number; total: number } | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [savedBatch, setSavedBatch] = useState<ScanBatchUploadFinishedDto | null>(null);
  const saveAsBatch = async () => {
    // A snapshot: scans added while it uploads stay on the page.
    const toSave = itemsRef.current.filter((it) => it.file);
    if (toSave.length === 0) return;
    setSaveError(null);
    setSavedBatch(null);
    setSaveProgress({ done: 0, total: toSave.length });
    let batchIdSaving: number | null = null;
    try {
      const started = await api.scanBatchUploadStart({
        name: saveBatchName.trim() || null,
        game,
        isFoil,
        condition,
        language: language || null,
        setCodes: artSets.map((s) => s.setCode),
        defaultContainerId: containerId === '' ? null : containerId,
      });
      batchIdSaving = started.id;
      // The list's order (newest first) becomes the batch's order.
      const queue = toSave.map((it, i) => ({ it, sequence: i + 1 }));
      let done = 0;
      let failure: unknown = null;
      const worker = async () => {
        for (;;) {
          const next = queue.shift();
          if (!next) return;
          try {
            await api.scanBatchUploadItem(started.id, next.it.file!, toUploadItem(next.it, next.sequence));
            setSaveProgress({ done: ++done, total: toSave.length });
          } catch (e) {
            // Stop the other workers too, and only cancel once nothing is still in flight.
            failure ??= e;
            queue.length = 0;
          }
        }
      };
      await Promise.all(Array.from({ length: Math.min(3, queue.length) }, worker));
      if (failure) throw failure;
      const result = await api.scanBatchUploadFinish(started.id);
      batchIdSaving = null;
      const saved = new Set(toSave.map((it) => it.key));
      dropItems((it) => saved.has(it.key));
      setSelectedKey((cur) => (cur && saved.has(cur) ? null : cur));
      setSavedBatch(result);
      setSaveBatchOpen(false);
      setSaveBatchName('');
      invalidateScanBatches(qc);
    } catch (e) {
      if (batchIdSaving != null) void api.scanBatchUploadCancel(batchIdSaving).catch(() => undefined);
      setSaveError((e as Error).message);
    } finally {
      setSaveProgress(null);
    }
  };

  // --- Keyboard review: Enter confirms the selected card and moves to the next one still to review;
  // ↑/↓ (K/J) move, S or / opens the catalog search, X excludes and moves on, Ctrl+Z undoes. ---
  // The card whose catalog search is open. Keyed to the card (not a flag reset by an effect) so moving
  // on closes it in the same render instead of costing a second one.
  const [correctingKey, setCorrectingKey] = useState<string | null>(null);
  const correcting = correctingKey !== null && correctingKey === selectedKey;
  const setCorrecting = (open: boolean) => setCorrectingKey(open ? selectedKey : null);
  const [shortcutsOpen, setShortcutsOpen] = useState(false);
  const [reviewPrefs] = useScanReviewPrefs();
  const holdEnterMs = holdEnterPace(reviewPrefs.holdEnterMs);
  /** When Enter last confirmed a card (performance.now()), to pace a held Enter. */
  const lastEnterAt = useRef(0);
  // Cards excluded while reviewing (X / "Exclude from commit"). They count as reviewed, so the
  // review doesn't keep returning to them. Kept for this visit only.
  const [skippedKeys, setSkippedKeys] = useState<ReadonlySet<string>>(() => new Set());
  // What each confirm / correct / exclude changed, newest last, so Ctrl+Z can put it back.
  const undoStack = useRef<{ key: string; before: Partial<ScanItem>; wasSkipped: boolean }[]>([]);


  const isReviewed = (it: ScanItem) => !!it.verified || skippedKeys.has(it.key);
  const needsReview = (it: ScanItem) => !isReviewed(it) && it.status !== 'matching';

  /** The next visible card after `fromKey` that still needs review, wrapping round to the top. */
  const nextToReview = (fromKey: string | null): string | null => {
    const start = visibleItems.findIndex((it) => it.key === fromKey);
    for (let i = 1; i <= visibleItems.length; i++) {
      const it = visibleItems[(start + i) % visibleItems.length];
      if (it.key !== fromKey && needsReview(it)) return it.key;
    }
    return null;
  };

  const setSkipped = (key: string, skipped: boolean) =>
    setSkippedKeys((prev) => {
      if (prev.has(key) === skipped) return prev;
      const next = new Set(prev);
      if (skipped) next.add(key);
      else next.delete(key);
      return next;
    });

  /** Apply a review decision to a card (remembered for undo), then move on to the next card to review. */
  const decide = (item: ScanItem, patch: Partial<ScanItem>, skip = false) => {
    undoStack.current.push({
      key: item.key,
      before: {
        verified: item.verified,
        include: item.include,
        override: item.override,
        language: item.language,
        isFoil: item.isFoil,
        foilType: item.foilType,
      },
      wasSkipped: skippedKeys.has(item.key),
    });
    if (undoStack.current.length > 200) undoStack.current.shift();
    const next = nextToReview(item.key);
    updateItem(item.key, patch);
    setSkipped(item.key, skip);
    if (next) setSelectedKey(next);
  };

  const confirmItem = (item: ScanItem) => decide(item, { verified: true, include: true });
  // Picking a printing also fixes what the copy says about it: the copy takes the printing's language
  // (so a scan matched to a Japanese row and corrected to the English one becomes English), and a printing
  // made in one finish only sets the foil flag. A catalog that doesn't track language leaves the copy's
  // language alone; so does an English row while the session is set to another language (that language's
  // rows may simply not be downloaded).
  const correctItem = (item: ScanItem, r: ScanSearchResultDto) => {
    const sessionLanguage = batch?.data.language || language;
    const keepSessionLanguage = r.language === ENGLISH && !!sessionLanguage && sessionLanguage !== ENGLISH;
    const patch: Partial<ScanItem> = { override: r, include: true, verified: true };
    if (r.language && !keepSessionLanguage) patch.language = r.language;
    else if (r.language === ENGLISH && keepSessionLanguage) patch.language = sessionLanguage;
    if (r.foil === true) {
      patch.isFoil = true;
      patch.foilType = r.foilType ?? (item.isFoil ? item.foilType : null);
    } else if (r.foil === false) {
      patch.isFoil = false;
      patch.foilType = null;
    }
    decide(item, patch);
  };
  const excludeItem = (item: ScanItem) => decide(item, { include: false }, true);
  const includeItem = (item: ScanItem) => {
    if (identityOf(item)) updateItem(item.key, { include: true });
    setSkipped(item.key, false);
  };

  /** Ctrl+Z: put back the last confirm / correct / exclude and return to that card. */
  const undoLast = () => {
    for (let last = undoStack.current.pop(); last; last = undoStack.current.pop()) {
      if (!items.some((it) => it.key === last!.key)) continue; // Since committed or removed.
      updateItem(last.key, last.before);
      setSkipped(last.key, last.wasSkipped);
      setSelectedKey(last.key);
      return;
    }
  };

  const moveSelection = (delta: number) => {
    if (visibleItems.length === 0) return;
    const at = visibleItems.findIndex((it) => it.key === selectedKey);
    const to = at === -1 ? 0 : Math.max(0, Math.min(visibleItems.length - 1, at + delta));
    setSelectedKey(visibleItems[to].key);
  };

  const reviewedCount = visibleItems.filter(isReviewed).length;
  const confirmedCount = visibleItems.filter((it) => it.verified).length;
  const stillMatchingCount = visibleItems.filter((it) => !isReviewed(it) && it.status === 'matching').length;
  const reviewComplete = visibleItems.length > 0 && reviewedCount === visibleItems.length;

  // Warm the browser cache with the next few cards' images so the compare panes appear instantly.
  const preloaded = useRef(new Set<string>());
  useEffect(() => {
    const at = visibleItems.findIndex((it) => it.key === selectedKey);
    const nextKey = nextToReview(selectedKey);
    const upcoming = [
      ...visibleItems.slice(at + 1, at + 4),
      ...visibleItems.filter((it) => it.key === nextKey),
    ];
    for (const it of upcoming) {
      for (const url of [it.previewUrl, identityOf(it)?.imageUri]) {
        if (!url || url.startsWith('data:') || url.startsWith('blob:') || preloaded.current.has(url)) continue;
        preloaded.current.add(url);
        // Download and decode ahead, so showing the card doesn't wait on either.
        const img = new Image();
        img.src = url;
        img.decode().catch(() => {});
      }
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- nextToReview is derived from these.
  }, [selectedKey, visibleItems, skippedKeys]);

  const closeWhenDone = (item: ScanItem, act: () => void) => {
    const done = !nextToReview(item.key);
    act();
    if (done) setDetailOpen(false);
  };
  const detailPanel = (item: ScanItem) => (
    <DetailPanel
      item={item}
      game={game}
      artSets={artSets}
      foilTypeOptions={foilTypeOptions}
      tagOptions={tagOptions}
      badgeSettings={badgeSettings}
      onToggle={(v) => (v ? includeItem(item) : closeWhenDone(item, () => excludeItem(item)))}
      onVerify={() => closeWhenDone(item, () => confirmItem(item))}
      onCorrect={(r) => closeWhenDone(item, () => correctItem(item, r))}
      correcting={correcting}
      onCorrectingChange={setCorrecting}
      onRemove={() => removeItems((it) => it.key === item.key)}
      onProps={(patch) => updateProps(item.key, patch)}
      readOnly={readOnly}
      failedValidationQuery={validation?.failed.has(item.key) ? validation.query : null}
      intake={!!intake}
    />
  );

  // One window listener that always calls the latest handler (it closes over this render's state).
  const keyHandler = useRef<(e: KeyboardEvent) => void>(() => {});
  keyHandler.current = (e: KeyboardEvent) => {
    if (e.defaultPrevented || e.altKey || e.metaKey || items.length === 0) return;
    const target = e.target instanceof HTMLElement ? e.target : null;
    if (target && isTypingTarget(target)) return;
    // Leave keys alone inside dialogs, menus, dropdowns and the open search.
    if (target?.closest('[role="dialog"], [role="presentation"], [role="menu"], [role="listbox"], [role="combobox"]'))
      return;
    if (confirmDelete || bulkOpen || pickerOpen || webcamOpen || zipOpen || exportAnchor || shortcutsOpen) return;
    // Holding a key repeats the moves, and Enter at the user's chosen pace (handled below); holding X or
    // any other key must not run through a string of cards.
    const isMove = ['ArrowDown', 'ArrowUp', 'j', 'J', 'k', 'K'].includes(e.key);
    if (e.repeat && !isMove && e.key !== 'Enter') return;

    if (e.ctrlKey) {
      if (e.key.toLowerCase() === 'z' && !e.shiftKey && !readOnly) {
        e.preventDefault();
        undoLast();
      }
      return;
    }
    const item = selectedItem;
    switch (e.key) {
      case 'Enter':
        // A focused button (e.g. Search catalog after a Tab) keeps its normal Enter behaviour.
        if (target?.closest('button, a, [role="button"]') || !item || readOnly) return;
        e.preventDefault();
        if (e.repeat) {
          // Held Enter: confirm the next card once the chosen time has passed since the last one. It stops
          // (until Enter is pressed again) at a card it can't confirm: no match, still matching, or already
          // reviewed (the end of the list), and never opens the search.
          if (holdEnterMs <= 0 || !identityOf(item) || isReviewed(item)) return;
          const now = performance.now();
          if (now - lastEnterAt.current < holdEnterMs) return;
          lastEnterAt.current = now;
          confirmItem(item);
          return;
        }
        lastEnterAt.current = performance.now();
        if (identityOf(item)) confirmItem(item);
        else if (item.status !== 'matching') setCorrecting(true);
        return;
      case 'ArrowDown':
      case 'j':
      case 'J':
        e.preventDefault();
        moveSelection(1);
        return;
      case 'ArrowUp':
      case 'k':
      case 'K':
        e.preventDefault();
        moveSelection(-1);
        return;
      case 's':
      case 'S':
      case '/':
        if (!item || readOnly) return;
        e.preventDefault();
        setCorrecting(true);
        return;
      case 'x':
      case 'X':
        if (!item || readOnly) return;
        e.preventDefault();
        if (item.include || !skippedKeys.has(item.key)) excludeItem(item);
        else includeItem(item);
        return;
      case 'Escape':
        if (correcting) {
          e.preventDefault();
          setCorrecting(false);
        }
        return;
      case '?':
        e.preventDefault();
        setShortcutsOpen(true);
        return;
    }
  };
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => keyHandler.current(e);
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  return (
    <Stack spacing={3}>
      {/* In audit, batch and intake mode the wrapping page supplies its own header/banner. */}
      {!auditMode && !batch && !intake && (
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
            <SelectField
              size="small"
              label={t('common.labels.game')}
              value={game}
              onChange={(e) => setGame(e.target.value)}
              sx={{ minWidth: { xs: '100%', sm: 200 } }}
            >
              {gamesQuery.data?.map((g) => (
                <MenuItem key={g.id} value={g.id}>
                  {g.displayName}
                </MenuItem>
              ))}
            </SelectField>
            <SetMultiSelect
              game={game}
              value={artSets}
              onChange={setArtSets}
              label={t('scan.controls.artSets')}
              sx={{ minWidth: { xs: '100%', sm: 260 }, flex: '1 1 260px' }}
            />
            <SelectField
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
            </SelectField>
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
              startIcon={findingCards ? <CircularProgress size={18} color="inherit" /> : <CameraAltIcon />}
              disabled={findingCards}
              // Warm up the card finder while the camera app is open.
              onPointerDown={() => void ensureCvReady().catch(() => undefined)}
              onClick={() => cameraInput.current?.click()}
            >
              {findingCards ? t('scan.controls.findingCards') : t('scan.controls.takePhoto')}
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
                void handlePhotos(e.target.files);
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

      <ScanValidationBar
        game={game}
        query={validationQuery}
        onQueryChange={(q) => {
          setValidationQuery(q);
          setValidationErrors([]);
        }}
        onValidate={() => void runValidation()}
        validating={validating}
        canValidate={items.length > 0}
        errors={validationErrors}
        result={validation}
        failedCount={validationFailedCount}
        checkedCount={validationCheckedCount}
        failedOnly={controls.failedOnly}
        onFailedOnlyChange={(on) => setControls((c) => ({ ...c, failedOnly: on }))}
      />

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

      {savedBatch && (
        <Alert severity="success" onClose={() => setSavedBatch(null)}>
          {t('scan.saveBatch.success', { count: savedBatch.items, name: savedBatch.name })}
          {savedBatch.duplicates > 0 && <> {t('scan.saveBatch.duplicates', { count: savedBatch.duplicates })}</>}
        </Alert>
      )}

      <Dialog
        open={saveBatchOpen}
        onClose={() => saveProgress == null && setSaveBatchOpen(false)}
        maxWidth="sm"
        fullWidth
      >
        <DialogTitle>{t('scan.saveBatch.title')}</DialogTitle>
        <DialogContent>
          <Stack spacing={2}>
            <DialogContentText>
              {t('scan.saveBatch.body', {
                count: savableItems.length,
                game: gamesQuery.data?.find((g) => g.id === game)?.displayName ?? game,
              })}{' '}
              {t('scan.saveBatch.released')}
            </DialogContentText>
            {savableItems.some((it) => it.status !== 'done') && (
              <Typography variant="body2" color="text.secondary">
                {t('scan.saveBatch.stillMatching', {
                  count: savableItems.filter((it) => it.status !== 'done').length,
                })}
              </Typography>
            )}
            {selectedLocationName && (
              <Typography variant="body2" color="text.secondary">
                {t('scan.saveBatch.location', { location: selectedLocationName })}
              </Typography>
            )}
            <TextField
              autoFocus
              label={t('scan.saveBatch.name')}
              helperText={t('scan.saveBatch.nameHelp')}
              value={saveBatchName}
              disabled={saveProgress != null}
              onChange={(e) => setSaveBatchName(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter' && saveProgress == null) void saveAsBatch();
              }}
              slotProps={{ htmlInput: { maxLength: 190 } }}
            />
            {saveProgress && (
              <Box>
                <LinearProgress
                  variant="determinate"
                  value={saveProgress.total ? (saveProgress.done / saveProgress.total) * 100 : 0}
                />
                <Typography variant="caption" color="text.secondary">
                  {t('scan.saveBatch.saving', {
                    done: fmt.number(saveProgress.done),
                    total: fmt.number(saveProgress.total),
                  })}
                </Typography>
              </Box>
            )}
            {saveError && <Alert severity="error">{t('scan.saveBatch.failed', { error: saveError })}</Alert>}
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button disabled={saveProgress != null} onClick={() => setSaveBatchOpen(false)}>
            {t('common.actions.cancel')}
          </Button>
          <Button
            variant="contained"
            startIcon={<CloudUploadIcon />}
            disabled={saveProgress != null || savableItems.length === 0}
            onClick={() => void saveAsBatch()}
          >
            {t('scan.saveBatch.save')}
          </Button>
        </DialogActions>
      </Dialog>

      {items.length > 0 && (
        <Paper variant="outlined" sx={{ p: 2, position: { xs: 'static', sm: 'sticky' }, top: 56, zIndex: 1 }}>
          <Stack direction="row" spacing={2} alignItems="center" flexWrap="wrap" useFlexGap>
            {/* Audit is locked to one location: show it read-only instead of the picker. */}
            {readOnly || intake ? null : lockedContainerId != null ? (
              <Chip
                icon={<LockIcon />}
                variant="outlined"
                label={selectedLocationName ?? t('scan.commit.addToLocation')}
                sx={{ minWidth: { xs: '100%', sm: 220 }, justifyContent: 'flex-start' }}
              />
            ) : (
              <Button
                variant="outlined"
                startIcon={<PlaceIcon />}
                onClick={() => setPickerOpen(true)}
                sx={{ minWidth: { xs: '100%', sm: 220 }, justifyContent: 'flex-start', textTransform: 'none' }}
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
                {intake ? (
                  <Button
                    variant="contained"
                    startIcon={<RequestQuoteIcon />}
                    disabled={committableCount === 0 || stillMatching || rulesPending}
                    onClick={() =>
                      intake.onComplete(
                        items
                          .filter(isVisibleCommittable)
                          .map((it) => ({ key: it.key, item: toCommitItem(it, game), previewUrl: it.previewUrl })),
                      )
                    }
                  >
                    {t('intake.scan.complete', { count: committableCount })}
                  </Button>
                ) : (
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
                )}
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
            {canExport && !intake && (
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
            {canSaveBatch && (
              <Tooltip title={t('scan.saveBatch.hint')}>
                <span>
                  <Button
                    variant="outlined"
                    startIcon={<CloudUploadIcon />}
                    disabled={savableItems.length === 0 || saveProgress != null}
                    onClick={() => {
                      setSaveError(null);
                      setSaveBatchOpen(true);
                    }}
                  >
                    {t('scan.saveBatch.button')}
                  </Button>
                </span>
              </Tooltip>
            )}
            <Typography variant="caption" color="text.secondary" sx={{ display: { xs: 'none', sm: 'block' } }}>
              {auditMode
                ? t('scan.audit.commitHint')
                : intake
                  ? t('intake.scan.completeHint')
                  : controlsActive
                  ? t('scan.commit.addedNoteFiltered')
                  : t('scan.commit.addedNote')}
            </Typography>
            {commitProgress && (
              <Box sx={{ flexBasis: '100%' }} role="status" aria-live="polite">
                <Typography variant="body2" sx={{ mb: 0.5 }}>
                  {commitProgress.indeterminate
                    ? t('scan.commit.progressAudit', { count: commitProgress.total })
                    : t('scan.commit.progress', {
                        done: fmt.number(commitProgress.done),
                        total: fmt.number(commitProgress.total),
                      })}
                </Typography>
                <LinearProgress
                  variant={commitProgress.indeterminate ? 'indeterminate' : 'determinate'}
                  value={commitProgress.total ? (commitProgress.done / commitProgress.total) * 100 : 0}
                  sx={{ height: 8, borderRadius: 4 }}
                />
              </Box>
            )}
            {commit.error && (
              <Alert severity="error">
                {commitPartial > 0 && <>{t('scan.commit.partial', { count: commitPartial })} </>}
                {(commit.error as Error).message}
              </Alert>
            )}
            {exportNotice && (
              <Alert severity={exportNotice.severity} onClose={() => setExportNotice(null)}>
                {exportNotice.text}
              </Alert>
            )}
            {!auditMode && commit.data && (
              <Alert severity="success">
                {t('scan.commit.success', { count: (commit.data as { imported: number }).imported })}
                {commitBuyList && commitBuyList.matchedCopies > 0 && (
                  <>
                    {' '}
                    {t('buyList.scan.committed', { count: commitBuyList.matchedCopies })}
                  </>
                )}
                {commitBuyList && commitBuyList.setAsideCopies > 0 && (
                  <>
                    {' '}
                    {t('buyList.scan.setAsideDone', {
                      count: commitBuyList.setAsideCopies,
                      locations: commitBuyList.setAsideLocations.join(', '),
                    })}
                  </>
                )}
              </Alert>
            )}
            {!auditMode && commitBuyList && commitBuyList.notSetAside > 0 && (
              <Alert severity="warning">{t('buyList.scan.notSetAside', { count: commitBuyList.notSetAside })}</Alert>
            )}
          </Stack>
        </Paper>
      )}

      {buyListCount > 0 && (
        <Alert
          severity="warning"
          icon={<TrackChangesIcon />}
          action={
            <Button
              color="inherit"
              size="small"
              onClick={() => setControls((c) => ({ ...c, buyListOnly: !c.buyListOnly }))}
            >
              {controls.buyListOnly ? t('buyList.scan.showAll') : t('buyList.scan.showOnly')}
            </Button>
          }
        >
          {t('buyList.scan.banner', { count: buyListCount })}
        </Alert>
      )}

      {items.length > 0 && isPhone && !filtersOpen && (
        <Stack direction="row" spacing={1} alignItems="center">
          <Button variant="outlined" size="small" startIcon={<SearchIcon />} onClick={() => setFiltersOpen(true)}>
            {t('scan.filter.filterAndSort')}
          </Button>
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
      )}

      {items.length > 0 && (!isPhone || filtersOpen) && (
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
            <SelectField
              size="small"
              label={t('scan.filter.show')}
              value={controls.checked}
              onChange={(e) => setControls((c) => ({ ...c, checked: e.target.value as CheckedFilter }))}
              sx={{ minWidth: 150 }}
            >
              <MenuItem value="all">{t('scan.filter.all')}</MenuItem>
              <MenuItem value="checked">{t('scan.filter.checkedOnly')}</MenuItem>
              <MenuItem value="unchecked">{t('scan.filter.uncheckedOnly')}</MenuItem>
            </SelectField>
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
            <SelectField
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
            </SelectField>
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
            {isPhone && (
              <Button size="small" onClick={() => setFiltersOpen(false)}>
                {t('common.actions.done')}
              </Button>
            )}
          </Stack>
        </Paper>
      )}

      {visibleItems.length > 0 &&
        (reviewComplete ? (
          <Alert
            severity="success"
            action={
              isPhone ? undefined : (
                <Button color="inherit" size="small" startIcon={<KeyboardIcon />} onClick={() => setShortcutsOpen(true)}>
                  {t('scan.keyboard.shortcuts')}
                </Button>
              )
            }
          >
            {t('scan.keyboard.complete', { count: visibleItems.length, confirmed: confirmedCount })}
          </Alert>
        ) : (
          <Paper variant="outlined" sx={{ px: 2, py: 1 }}>
            <Stack direction="row" spacing={2} alignItems="center" flexWrap="wrap" useFlexGap>
              <Typography variant="body2" sx={{ fontWeight: 600 }}>
                {t('scan.keyboard.progress', { reviewed: reviewedCount, total: visibleItems.length })}
              </Typography>
              <LinearProgress
                variant="determinate"
                value={(reviewedCount / visibleItems.length) * 100}
                sx={{ flex: '1 1 120px', height: 6, borderRadius: 3 }}
              />
              <Typography variant="caption" color="text.secondary">
                {stillMatchingCount > 0 && !visibleItems.some(needsReview)
                  ? t('scan.keyboard.waiting', { count: stillMatchingCount })
                  : isPhone
                    ? t('scan.keyboard.tapHint')
                    : t('scan.keyboard.hint')}
              </Typography>
              {!isPhone && (
                <Button size="small" startIcon={<KeyboardIcon />} onClick={() => setShortcutsOpen(true)}>
                  {t('scan.keyboard.shortcuts')}
                </Button>
              )}
            </Stack>
          </Paper>
        ))}

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
          <Paper
            variant="outlined"
            data-scan-list
            sx={{ overflow: 'hidden', maxHeight: { md: '75vh' }, overflowY: { md: 'auto' } }}
          >
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
              <GoToScan selected={selectedNumber} total={items.length} onGo={goToNumber} />
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
              <Button
                size="small"
                variant="outlined"
                color="error"
                startIcon={<DeleteIcon />}
                disabled={checkedCount === 0 || readOnly}
                onClick={() => setConfirmDelete(true)}
              >
                {t('scan.master.delete')}
              </Button>
            </Box>
            <Stack divider={<Divider />}>
              {visibleItems.map((item) => (
                <MasterRow
                  key={item.key}
                  item={item}
                  number={scanNumbers.get(item.key) ?? 0}
                  selected={item.key === selectedKey}
                  badgeSettings={badgeSettings}
                  onSelect={openRow}
                  onToggle={toggleRow}
                  readOnly={readOnly}
                  failedValidation={!!validation?.failed.has(item.key)}
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
          {isPhone ? null : selectedItem ? (
            // Not keyed by card: moving through the list updates the panel in place, which is much
            // cheaper than rebuilding its form fields for every card.
            detailPanel(selectedItem)
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

      {isPhone && (
        <Dialog fullScreen open={detailOpen && !!selectedItem} onClose={() => setDetailOpen(false)}>
          <Toolbar variant="dense" sx={{ gap: 0.5, borderBottom: 1, borderColor: 'divider', px: 1 }}>
            <IconButton edge="start" aria-label={t('common.actions.close')} onClick={() => setDetailOpen(false)}>
              <CloseIcon />
            </IconButton>
            <Typography variant="subtitle1" sx={{ flexGrow: 1 }} noWrap>
              {t('scan.detail.position', { index: selectedNumber ?? 0, total: items.length })}
            </Typography>
            <IconButton aria-label={t('scan.detail.previous')} onClick={() => moveSelection(-1)}>
              <ArrowUpwardIcon />
            </IconButton>
            <IconButton aria-label={t('scan.detail.next')} onClick={() => moveSelection(1)}>
              <ArrowDownwardIcon />
            </IconButton>
          </Toolbar>
          <Box sx={{ overflowY: 'auto', pb: 'env(safe-area-inset-bottom)' }}>
            {selectedItem && detailPanel(selectedItem)}
          </Box>
        </Dialog>
      )}

      <Dialog open={confirmDelete} onClose={() => setConfirmDelete(false)}>
        <DialogTitle>{t('scan.master.deleteTitle', { count: checkedCount })}</DialogTitle>
        <DialogContent>
          <DialogContentText>
            {t(batch ? 'scan.master.deleteBatchBody' : 'scan.master.deleteBody', { count: checkedCount })}
          </DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setConfirmDelete(false)}>{t('common.actions.cancel')}</Button>
          <Button color="error" variant="contained" autoFocus onClick={deleteChecked}>
            {t('scan.master.delete')}
          </Button>
        </DialogActions>
      </Dialog>

      <ShortcutsDialog open={shortcutsOpen} onClose={() => setShortcutsOpen(false)} />

      <BulkEditDialog
        open={bulkOpen}
        game={game}
        count={checkedCount}
        foilTypeOptions={foilTypeOptions}
        tagOptions={tagOptions}
        onApply={applyBulkEdit}
        onClose={() => setBulkOpen(false)}
      />

      {lockedContainerId == null && !intake && (
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
        game={game}
        onCapture={(file) => void stageFiles([file])}
        onClose={() => setWebcamOpen(false)}
      />
    </Stack>
  );
}
