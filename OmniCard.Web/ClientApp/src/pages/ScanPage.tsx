import { useMemo, useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  CardMedia,
  Checkbox,
  Chip,
  CircularProgress,
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
import CloseIcon from '@mui/icons-material/Close';
import EditIcon from '@mui/icons-material/Edit';
import { api } from '../api/client';
import { useGame } from '../context/GameContext';
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
  include: boolean;
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

function ScanItemCard({
  item,
  game,
  onRemove,
  onToggle,
  onCorrect,
}: {
  item: ScanItem;
  game: string;
  onRemove: () => void;
  onToggle: (v: boolean) => void;
  onCorrect: (r: ScanSearchResultDto) => void;
}) {
  const [correcting, setCorrecting] = useState(false);
  const id = identityOf(item);
  const art = id?.imageUri ?? item.previewUrl;

  return (
    <Card variant="outlined" sx={{ display: 'flex', position: 'relative' }}>
      <CardMedia
        component="img"
        image={art}
        alt={id?.name ?? item.fileName}
        sx={{ width: 88, objectFit: 'contain', bgcolor: 'action.hover' }}
      />
      <CardContent sx={{ flex: 1, py: 1.5 }}>
        <Stack direction="row" alignItems="center" spacing={1} sx={{ mb: 0.5 }}>
          <Checkbox
            size="small"
            checked={item.include}
            disabled={!id}
            onChange={(e) => onToggle(e.target.checked)}
            sx={{ p: 0 }}
          />
          <ConfidenceChip item={item} />
          <Box sx={{ flexGrow: 1 }} />
          <IconButton size="small" onClick={() => setCorrecting((v) => !v)} title="Correct match">
            <EditIcon fontSize="small" />
          </IconButton>
          <IconButton size="small" onClick={onRemove} title="Remove">
            <CloseIcon fontSize="small" />
          </IconButton>
        </Stack>
        {id ? (
          <>
            <Typography variant="subtitle2" noWrap>
              {id.name}
            </Typography>
            <Typography variant="body2" color="text.secondary" noWrap>
              {id.setName} · {id.setCode.toUpperCase()} #{id.collectorNumber}
              {id.rarity ? ` · ${id.rarity}` : ''}
            </Typography>
          </>
        ) : (
          <Typography variant="body2" color="text.secondary" noWrap>
            {item.error ?? item.fileName}
          </Typography>
        )}
        {correcting && (
          <CorrectionSearch
            game={game}
            onPick={(r) => {
              onCorrect(r);
              setCorrecting(false);
            }}
          />
        )}
      </CardContent>
    </Card>
  );
}

export function ScanPage() {
  const qc = useQueryClient();
  const { game: contextGame } = useGame();
  const [game, setGame] = useState(contextGame ?? 'Mtg');
  const [isFoil, setIsFoil] = useState(false);
  const [condition, setCondition] = useState('NM');
  const [containerId, setContainerId] = useState<number | ''>('');
  const [items, setItems] = useState<ScanItem[]>([]);
  const fileInput = useRef<HTMLInputElement>(null);
  const cameraInput = useRef<HTMLInputElement>(null);

  const gamesQuery = useQuery({ queryKey: ['games'], queryFn: api.games });
  const locations = useQuery({ queryKey: ['locations', undefined], queryFn: () => api.locations() });

  const commit = useMutation({
    mutationFn: () => {
      const payload = items
        .filter((it) => it.include && identityOf(it))
        .map((it) => {
          const id = identityOf(it)!;
          return { ...id, game, condition, isFoil, quantity: 1, purchasePrice: null };
        });
      return api.scanCommit(containerId as number, payload);
    },
    onSuccess: (res) => {
      // Drop the cards that were just committed; keep any the user left unchecked.
      setItems((prev) => prev.filter((it) => !(it.include && identityOf(it))));
      qc.invalidateQueries({ queryKey: ['collection'] });
      qc.invalidateQueries({ queryKey: ['locations'] });
      qc.invalidateQueries({ queryKey: ['dashboard'] });
      return res;
    },
  });

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

    // Match each concurrently; update rows as they resolve.
    await Promise.all(
      staged.map(async (staging) => {
        try {
          const match = await api.scanMatch(staging.file, game, isFoil);
          setItems((prev) =>
            prev.map((it) =>
              it.key === staging.key
                ? { ...it, status: 'done', match, include: match.matched }
                : it,
            ),
          );
        } catch (e) {
          setItems((prev) =>
            prev.map((it) =>
              it.key === staging.key
                ? { ...it, status: 'error', error: (e as Error).message }
                : it,
            ),
          );
        }
      }),
    );
  }

  const includableCount = useMemo(
    () => items.filter((it) => it.include && identityOf(it)).length,
    [items],
  );
  const stillMatching = items.some((it) => it.status === 'matching');

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
            <TextField
              select
              size="small"
              label="Add to location"
              value={containerId}
              onChange={(e) => setContainerId(e.target.value === '' ? '' : Number(e.target.value))}
              sx={{ minWidth: 220 }}
            >
              <MenuItem value="">— choose —</MenuItem>
              {locations.data?.map((l) => (
                <MenuItem key={l.id} value={l.id}>
                  {l.name}
                </MenuItem>
              ))}
            </TextField>
            <Button
              variant="contained"
              disabled={
                includableCount === 0 || containerId === '' || commit.isPending || stillMatching
              }
              onClick={() => commit.mutate()}
            >
              {commit.isPending
                ? 'Adding…'
                : `Add ${includableCount} card${includableCount === 1 ? '' : 's'}`}
            </Button>
            {commit.error && <Alert severity="error">{(commit.error as Error).message}</Alert>}
            {commit.data && (
              <Alert severity="success">Added {commit.data.imported} card(s) to your collection.</Alert>
            )}
          </Stack>
        </Paper>
      )}

      <Box
        sx={{
          display: 'grid',
          gap: 2,
          gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr', lg: '1fr 1fr 1fr' },
        }}
      >
        {items.map((item) => (
          <ScanItemCard
            key={item.key}
            item={item}
            game={game}
            onRemove={() => setItems((prev) => prev.filter((it) => it.key !== item.key))}
            onToggle={(v) =>
              setItems((prev) => prev.map((it) => (it.key === item.key ? { ...it, include: v } : it)))
            }
            onCorrect={(r) =>
              setItems((prev) =>
                prev.map((it) =>
                  it.key === item.key ? { ...it, override: r, include: true } : it,
                ),
              )
            }
          />
        ))}
      </Box>
    </Stack>
  );
}
