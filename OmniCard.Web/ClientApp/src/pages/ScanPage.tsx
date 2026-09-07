import { useEffect, useMemo, useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  Checkbox,
  Chip,
  CircularProgress,
  Dialog,
  DialogContent,
  Divider,
  FormControlLabel,
  IconButton,
  MenuItem,
  Paper,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import AddPhotoAlternateIcon from '@mui/icons-material/AddPhotoAlternate';
import CameraAltIcon from '@mui/icons-material/CameraAlt';
import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import CloseIcon from '@mui/icons-material/Close';
import PlaceIcon from '@mui/icons-material/Place';
import SearchIcon from '@mui/icons-material/Search';
import ZoomInIcon from '@mui/icons-material/ZoomIn';
import { api } from '../api/client';
import { useGame } from '../context/GameContext';
import { LocationPickerDialog } from '../components/LocationPickerDialog';
import type { ScanMatchDto, ScanSearchResultDto } from '../api/types';

const CONDITIONS = ['NM', 'LP', 'MP', 'HP', 'DMG'];

type ItemStatus = 'matching' | 'done' | 'error';

interface ScanItem {
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

/** Inline catalog search used to correct a bad/absent match. */
function CorrectionSearch({
  game,
  onPick,
}: {
  game: string;
  onPick: (r: ScanSearchResultDto) => void;
}) {
  const [q, setQ] = useState('');
  const search = useQuery({
    queryKey: ['scan-search', game, q],
    queryFn: () => api.scanSearch(game, q),
    enabled: q.trim().length >= 2,
  });
  return (
    <Box sx={{ mt: 1 }}>
      <TextField
        size="small"
        fullWidth
        autoFocus
        placeholder={`Search ${game} catalog…`}
        value={q}
        onChange={(e) => setQ(e.target.value)}
      />
      {search.data && search.data.length > 0 && (
        <Stack sx={{ mt: 1, maxHeight: 220, overflowY: 'auto' }} spacing={0.5}>
          {search.data.map((r) => (
            <Button
              key={r.gameCardId}
              size="small"
              variant="text"
              sx={{ justifyContent: 'flex-start', textTransform: 'none' }}
              onClick={() => onPick(r)}
            >
              {r.name} · {r.setCode.toUpperCase()} #{r.collectorNumber}
            </Button>
          ))}
        </Stack>
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

/** Master-list row: scan thumbnail beside matched-art thumbnail + identity + status. */
function MasterRow({
  item,
  selected,
  onSelect,
  onToggle,
}: {
  item: ScanItem;
  selected: boolean;
  onSelect: () => void;
  onToggle: (v: boolean) => void;
}) {
  const id = identityOf(item);
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
        onClick={(e) => e.stopPropagation()}
        onChange={(e) => onToggle(e.target.checked)}
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
        <Box sx={{ mt: 0.75 }}>
          <ConfidenceChip item={item} />
        </Box>
      </Box>
    </Box>
  );
}

/** Detail panel for the selected scan: identity + verify/correct/remove + a zoom-to-read scan popup. */
function DetailPanel({
  item,
  game,
  onToggle,
  onVerify,
  onCorrect,
  onRemove,
}: {
  item: ScanItem;
  game: string;
  onToggle: (v: boolean) => void;
  onVerify: () => void;
  onCorrect: (r: ScanSearchResultDto) => void;
  onRemove: () => void;
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

export function ScanPage() {
  const qc = useQueryClient();
  const { game: contextGame } = useGame();
  const [game, setGame] = useState(contextGame ?? 'Mtg');
  const [setCode, setSetCode] = useState('');
  const [isFoil, setIsFoil] = useState(false);
  const [condition, setCondition] = useState('NM');
  const [containerId, setContainerId] = useState<number | ''>('');
  const [pickerOpen, setPickerOpen] = useState(false);
  const [items, setItems] = useState<ScanItem[]>([]);
  const [selectedKey, setSelectedKey] = useState<string | null>(null);
  const fileInput = useRef<HTMLInputElement>(null);
  const cameraInput = useRef<HTMLInputElement>(null);

  const selectedItem = items.find((it) => it.key === selectedKey) ?? null;

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

  const gamesQuery = useQuery({ queryKey: ['games'], queryFn: api.games });
  const setsQuery = useQuery({ queryKey: ['sets', game], queryFn: () => api.sets(game), enabled: !!game });
  const locations = useQuery({ queryKey: ['locations', undefined], queryFn: () => api.locations() });

  // Reset the set filter whenever the game changes (a set only belongs to one game).
  useEffect(() => {
    setSetCode('');
  }, [game]);

  // A scan is committable only when it is BOTH confirmed (verified) AND checked (include).
  const isCommittable = (it: ScanItem) => it.include && it.verified && !!identityOf(it);

  const commit = useMutation({
    mutationFn: () => {
      const payload = items.filter(isCommittable).map((it) => {
        const id = identityOf(it)!;
        return { ...id, game, condition, isFoil, quantity: 1, purchasePrice: null };
      });
      return api.scanCommit(containerId as number, payload);
    },
    onSuccess: (res) => {
      // Drop the cards that were just committed; keep everything else (unchecked or unconfirmed).
      setItems((prev) => prev.filter((it) => !isCommittable(it)));
      qc.invalidateQueries({ queryKey: ['collection'] });
      qc.invalidateQueries({ queryKey: ['locations'] });
      qc.invalidateQueries({ queryKey: ['dashboard'] });
      return res;
    },
  });

  /** Confirm every checked-and-matched item at once (batch verify). */
  const confirmChecked = () =>
    setItems((prev) =>
      prev.map((it) => (it.include && identityOf(it) ? { ...it, verified: true } : it)),
    );

  async function handleFiles(files: FileList | null) {
    if (!files) return;
    const chosen = Array.from(files);
    const staged: ScanItem[] = chosen.map((file) => ({
      key: `s${seq++}`,
      fileName: file.name,
      previewUrl: URL.createObjectURL(file),
      file,
      status: 'matching',
      include: false,
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
    async function worker() {
      for (;;) {
        const staging = queue.shift();
        if (!staging) return;
        try {
          const match = await api.scanMatch(staging.file, game, isFoil, setCode || undefined);
          updateItem(staging.key, { status: 'done', match, include: match.matched });
        } catch (e) {
          updateItem(staging.key, { status: 'error', error: (e as Error).message });
        }
      }
    }
    await Promise.all(Array.from({ length: Math.min(MAX_IN_FLIGHT, staged.length) }, worker));
  }

  const committableCount = useMemo(() => items.filter(isCommittable).length, [items]);
  // Checked + matched but not yet confirmed — the batch "Confirm checked" button targets these.
  const confirmableCount = useMemo(
    () => items.filter((it) => it.include && !it.verified && identityOf(it)).length,
    [items],
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
          <TextField
            select
            size="small"
            label="Set (art fallback)"
            value={setCode}
            onChange={(e) => setSetCode(e.target.value)}
            sx={{ minWidth: 220 }}
          >
            <MenuItem value="">All sets</MenuItem>
            {setsQuery.data?.map((s) => (
              <MenuItem key={s.setCode} value={s.setCode}>
                {s.setName}
              </MenuItem>
            ))}
          </TextField>
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
            accept="image/jpeg,image/png"
            multiple
            hidden
            onChange={(e) => {
              void handleFiles(e.target.files);
              e.target.value = '';
            }}
          />
        </Stack>
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
              Only confirmed &amp; checked scans are added.
            </Typography>
            {commit.error && <Alert severity="error">{(commit.error as Error).message}</Alert>}
            {commit.data && (
              <Alert severity="success">Added {commit.data.imported} card(s) to your collection.</Alert>
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
            <Stack divider={<Divider />}>
              {items.map((item) => (
                <MasterRow
                  key={item.key}
                  item={item}
                  selected={item.key === selectedKey}
                  onSelect={() => setSelectedKey(item.key)}
                  onToggle={(v) => updateItem(item.key, { include: v })}
                />
              ))}
            </Stack>
          </Paper>

          {/* Detail: the selected card's full-size compare + verify/correct/remove. */}
          {selectedItem ? (
            <DetailPanel
              key={selectedItem.key}
              item={selectedItem}
              game={game}
              onToggle={(v) => updateItem(selectedItem.key, { include: v })}
              onVerify={() => updateItem(selectedItem.key, { verified: true, include: true })}
              onCorrect={(r) =>
                updateItem(selectedItem.key, { override: r, include: true, verified: true })
              }
              onRemove={() => setItems((prev) => prev.filter((it) => it.key !== selectedItem.key))}
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

      <LocationPickerDialog
        open={pickerOpen}
        title="Add scanned cards to location"
        onPick={(id) => {
          setContainerId(id);
          setPickerOpen(false);
        }}
        onClose={() => setPickerOpen(false)}
      />
    </Stack>
  );
}
