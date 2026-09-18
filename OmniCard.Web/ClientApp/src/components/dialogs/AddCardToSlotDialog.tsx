import { useEffect, useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Box,
  Button,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Divider,
  FormControlLabel,
  InputAdornment,
  List,
  ListItemButton,
  MenuItem,
  Stack,
  Switch,
  TextField,
  Typography,
} from '@mui/material';
import SearchIcon from '@mui/icons-material/Search';
import CollectionsBookmarkIcon from '@mui/icons-material/CollectionsBookmark';
import TravelExploreIcon from '@mui/icons-material/TravelExplore';
import { api } from '../../api/client';
import type { CardDto, ScanSearchResultDto } from '../../api/types';

const CONDITIONS = ['NM', 'LP', 'MP', 'HP', 'DMG'];

/**
 * Fill a single binder pocket (page + slot) with a card. Search results come in two groups:
 *  - **In your collection** — owned lots (via /api/collection). Picking one relocates a single copy
 *    straight into the pocket (splitting a stack, displacing any current occupant to the Unplaced pool)
 *    via /api/binder/card/place-owned. This is the "move a card from one location to another" path.
 *  - **In the catalog** — printings you may not own yet (via /api/scan/search). Picking one reveals a
 *    condition/foil/price form and creates a new loose lot in the pocket via /api/binder/card/add-missing.
 * A pocket holds one card, so the dialog closes after a successful add.
 */
export function AddCardToSlotDialog({
  open,
  containerId,
  page,
  slot,
  occupantName,
  defaultGame,
  onClose,
  onDone,
}: {
  open: boolean;
  containerId: number;
  page: number;
  slot: number;
  /** Name of the card currently in this pocket, if any — shown as a "will be replaced" hint. */
  occupantName?: string | null;
  defaultGame: string;
  onClose: () => void;
  /** Called after a card is successfully placed so the caller can refresh the binder. */
  onDone: () => void;
}) {
  const qc = useQueryClient();
  const games = useQuery({ queryKey: ['games'], queryFn: api.games, enabled: open });

  const [game, setGame] = useState(defaultGame);
  const [name, setName] = useState('');
  const [set, setSet] = useState('');
  const [cn, setCn] = useState('');
  const [selected, setSelected] = useState<ScanSearchResultDto | null>(null);
  const [condition, setCondition] = useState('NM');
  const [isFoil, setIsFoil] = useState(false);
  const [purchasePrice, setPurchasePrice] = useState('');
  const [error, setError] = useState<string | null>(null);

  // Reset the form each time the dialog opens onto a (possibly different) pocket.
  const [wasOpen, setWasOpen] = useState(false);
  if (open && !wasOpen) {
    setWasOpen(true);
    setGame(defaultGame);
    setName('');
    setSet('');
    setCn('');
    setSelected(null);
    setCondition('NM');
    setIsFoil(false);
    setPurchasePrice('');
    setError(null);
  }
  if (!open && wasOpen) setWasOpen(false);

  // Compose the same set:/cn: token grammar both collection and catalog search understand.
  const composed = useMemo(() => {
    const parts: string[] = [];
    if (name.trim()) parts.push(name.trim());
    if (set.trim()) parts.push(`set:${set.trim()}`);
    if (cn.trim()) parts.push(`cn:${cn.trim()}`);
    return parts.join(' ');
  }, [name, set, cn]);

  const [debounced, setDebounced] = useState('');
  useEffect(() => {
    const t = setTimeout(() => setDebounced(composed), 250);
    return () => clearTimeout(t);
  }, [composed]);

  const canSearch = open && !selected && debounced.trim().length >= 2;

  const owned = useQuery({
    queryKey: ['collection', 'slot-add', game, debounced],
    queryFn: () => api.collection({ game, q: debounced, take: 25 }),
    enabled: canSearch,
  });
  // Only offer copies that live somewhere *else* — a lot already in this binder has nothing to
  // relocate (place unplaced-pool copies by dragging them from the sidebar instead).
  const ownedElsewhere = (owned.data?.items ?? []).filter((c) => c.containerId !== containerId);
  const catalog = useQuery({
    queryKey: ['catalog-search', game, debounced],
    queryFn: () => api.scanSearch(game, debounced),
    enabled: canSearch,
  });

  const invalidate = () => {
    qc.invalidateQueries({ queryKey: ['collection'] });
    qc.invalidateQueries({ queryKey: ['location-cards'] });
    qc.invalidateQueries({ queryKey: ['locations'] });
    qc.invalidateQueries({ queryKey: ['dashboard'] });
  };

  const placeOwned = useMutation({
    mutationFn: (card: CardDto) => api.binderPlaceOwned(card.id, containerId, page, slot),
    onSuccess: () => {
      invalidate();
      onDone();
      onClose();
    },
    onError: (e) => setError((e as Error).message),
  });

  const addCatalog = useMutation({
    mutationFn: () =>
      api.binderAddMissing({
        containerId,
        page,
        slot,
        game,
        gameSpecificId: selected!.gameCardId,
        name: selected!.name,
        setCode: selected!.setCode,
        setName: selected!.setName,
        collectorNumber: selected!.collectorNumber,
        rarity: selected!.rarity,
        imageUri: selected!.imageUri,
        condition,
        isFoil,
        purchasePrice: purchasePrice === '' ? null : Number(purchasePrice),
      }),
    onSuccess: () => {
      invalidate();
      onDone();
      onClose();
    },
    onError: (e) => setError((e as Error).message),
  });

  const busy = placeOwned.isPending || addCatalog.isPending;

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>
        Add card to pocket
        <Typography variant="body2" color="text.secondary">
          Page {page} · slot {slot + 1}
          {occupantName ? ` — replaces “${occupantName}”` : ''}
        </Typography>
      </DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <TextField
            select
            size="small"
            label="Game"
            value={game}
            onChange={(e) => {
              setGame(e.target.value);
              setSelected(null);
            }}
            sx={{ maxWidth: 240 }}
          >
            {(games.data ?? [{ id: 'Mtg', displayName: 'Magic: The Gathering' }]).map((g) => (
              <MenuItem key={g.id} value={g.id}>
                {g.displayName}
              </MenuItem>
            ))}
          </TextField>

          <Stack direction="row" spacing={1}>
            <TextField
              size="small"
              label="Name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              fullWidth
              autoFocus
              slotProps={{
                input: {
                  startAdornment: (
                    <InputAdornment position="start">
                      <SearchIcon fontSize="small" />
                    </InputAdornment>
                  ),
                },
              }}
            />
            <TextField size="small" label="Set" value={set} onChange={(e) => setSet(e.target.value)} sx={{ width: 110 }} />
            <TextField size="small" label="Collector #" value={cn} onChange={(e) => setCn(e.target.value)} sx={{ width: 110 }} />
          </Stack>

          {/* Search results — collection first, then catalog. */}
          {canSearch && (
            <Stack spacing={1.5}>
              <Box>
                <Stack direction="row" spacing={1} alignItems="center" sx={{ mb: 0.5 }}>
                  <CollectionsBookmarkIcon fontSize="small" color="action" />
                  <Typography variant="subtitle2">
                    In your collection {owned.data ? `(${ownedElsewhere.length})` : ''}
                  </Typography>
                </Stack>
                {owned.isFetching ? (
                  <Typography variant="body2" color="text.secondary">Searching…</Typography>
                ) : ownedElsewhere.length > 0 ? (
                  <List dense disablePadding sx={{ maxHeight: 200, overflowY: 'auto', border: 1, borderColor: 'divider', borderRadius: 1 }}>
                    {ownedElsewhere.map((c) => (
                      <ListItemButton key={c.id} onClick={() => placeOwned.mutate(c)} disabled={busy}>
                        <Stack direction="row" spacing={1} alignItems="center" sx={{ width: '100%' }}>
                          {c.imageUri && (
                            <Box component="img" src={c.imageUri} alt="" sx={{ width: 32, aspectRatio: '0.72', objectFit: 'contain', flexShrink: 0 }} />
                          )}
                          <Box sx={{ minWidth: 0, flexGrow: 1 }}>
                            <Typography variant="body2" noWrap>{c.name}</Typography>
                            <Typography variant="caption" color="text.secondary" noWrap sx={{ display: 'block' }}>
                              {c.setCode.toUpperCase()} · #{c.number} · {c.condition}
                              {c.isFoil ? ' · Foil' : ''}
                            </Typography>
                          </Box>
                          <Chip
                            size="small"
                            variant="outlined"
                            label={c.containerName ?? 'Unfiled'}
                            sx={{ flexShrink: 0, maxWidth: 130 }}
                          />
                        </Stack>
                      </ListItemButton>
                    ))}
                  </List>
                ) : (
                  <Typography variant="body2" color="text.secondary">
                    No copies in other locations.
                  </Typography>
                )}
              </Box>

              <Box>
                <Stack direction="row" spacing={1} alignItems="center" sx={{ mb: 0.5 }}>
                  <TravelExploreIcon fontSize="small" color="action" />
                  <Typography variant="subtitle2">
                    In the catalog {catalog.data ? `(${catalog.data.length})` : ''}
                  </Typography>
                </Stack>
                {catalog.isFetching ? (
                  <Typography variant="body2" color="text.secondary">Searching…</Typography>
                ) : catalog.data && catalog.data.length > 0 ? (
                  <List dense disablePadding sx={{ maxHeight: 200, overflowY: 'auto', border: 1, borderColor: 'divider', borderRadius: 1 }}>
                    {catalog.data.map((r) => (
                      <ListItemButton key={r.gameCardId} onClick={() => setSelected(r)}>
                        <Stack direction="row" spacing={1} alignItems="center" sx={{ width: '100%' }}>
                          {r.imageUri && (
                            <Box component="img" src={r.imageUri} alt="" sx={{ width: 32, aspectRatio: '0.72', objectFit: 'contain', flexShrink: 0 }} />
                          )}
                          <Box sx={{ minWidth: 0, flexGrow: 1 }}>
                            <Typography variant="body2" noWrap>{r.name}</Typography>
                            <Typography variant="caption" color="text.secondary" noWrap sx={{ display: 'block' }}>
                              {r.setCode.toUpperCase()} · #{r.collectorNumber} · {r.rarity}
                            </Typography>
                          </Box>
                        </Stack>
                      </ListItemButton>
                    ))}
                  </List>
                ) : (
                  <Typography variant="body2" color="text.secondary">No matches in the {game} catalog.</Typography>
                )}
              </Box>
            </Stack>
          )}

          {/* Selected catalog card → condition/foil/price before adding a new copy. */}
          {selected && (
            <>
              <Divider />
              <Stack direction="row" spacing={1} alignItems="center">
                {selected.imageUri && (
                  <Box component="img" src={selected.imageUri} alt="" sx={{ width: 48, aspectRatio: '0.72', objectFit: 'contain' }} />
                )}
                <Box sx={{ flexGrow: 1, minWidth: 0 }}>
                  <Typography variant="subtitle2" noWrap>{selected.name}</Typography>
                  <Typography variant="caption" color="text.secondary">
                    {selected.setName} · #{selected.collectorNumber} · {selected.rarity}
                  </Typography>
                </Box>
                <Button size="small" onClick={() => setSelected(null)}>Change</Button>
              </Stack>

              <Stack direction="row" spacing={1}>
                <TextField select size="small" label="Condition" value={condition} onChange={(e) => setCondition(e.target.value)} sx={{ width: 120 }}>
                  {CONDITIONS.map((c) => (
                    <MenuItem key={c} value={c}>{c}</MenuItem>
                  ))}
                </TextField>
                <TextField
                  size="small"
                  label="Purchase price"
                  type="number"
                  value={purchasePrice}
                  onChange={(e) => setPurchasePrice(e.target.value)}
                  slotProps={{ htmlInput: { min: 0, step: '0.01' } }}
                  sx={{ width: 140 }}
                />
                <FormControlLabel
                  control={<Switch checked={isFoil} onChange={(e) => setIsFoil(e.target.checked)} />}
                  label="Foil"
                />
              </Stack>
            </>
          )}

          {error && <Typography color="error" variant="body2">{error}</Typography>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" disabled={!selected || busy} onClick={() => addCatalog.mutate()}>
          {addCatalog.isPending ? 'Adding…' : 'Add to pocket'}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
