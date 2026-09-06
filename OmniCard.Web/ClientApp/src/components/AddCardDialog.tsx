import { useEffect, useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Box,
  Button,
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
import { api } from '../api/client';
import type { ScanSearchResultDto } from '../api/types';

const CONDITIONS = ['NM', 'LP', 'MP', 'HP', 'DMG'];

/**
 * Manually add a card to a location by searching the game's CATALOG (not the collection). Search by
 * name and/or set code + collector number; pick a printing; set condition/foil/quantity/price; add.
 * Backed by the existing /api/scan/search (catalog) and /api/scan/commit (writes a loose lot) endpoints.
 * Stays open after each add (with a running count) so a stack of cards can be entered in one sitting.
 */
export function AddCardDialog({
  open,
  locationId,
  locationName,
  defaultGame,
  onClose,
}: {
  open: boolean;
  locationId: number;
  locationName: string;
  defaultGame?: string;
  onClose: () => void;
}) {
  const qc = useQueryClient();
  const games = useQuery({ queryKey: ['games'], queryFn: api.games, enabled: open });

  const [game, setGame] = useState(defaultGame ?? 'Mtg');
  const [name, setName] = useState('');
  const [set, setSet] = useState('');
  const [cn, setCn] = useState('');
  const [selected, setSelected] = useState<ScanSearchResultDto | null>(null);
  const [condition, setCondition] = useState('NM');
  const [isFoil, setIsFoil] = useState(false);
  const [quantity, setQuantity] = useState(1);
  const [purchasePrice, setPurchasePrice] = useState('');
  const [addedCount, setAddedCount] = useState(0);

  // Keep the game aligned with the page's game whenever the dialog (re)opens.
  const [wasOpen, setWasOpen] = useState(false);
  if (open && !wasOpen) {
    setWasOpen(true);
    setGame(defaultGame ?? 'Mtg');
    setAddedCount(0);
  }
  if (!open && wasOpen) setWasOpen(false);

  // Compose the same set:/cn: token grammar the catalog search understands.
  const composed = useMemo(() => {
    const parts: string[] = [];
    if (name.trim()) parts.push(name.trim());
    if (set.trim()) parts.push(`set:${set.trim()}`);
    if (cn.trim()) parts.push(`cn:${cn.trim()}`);
    return parts.join(' ');
  }, [name, set, cn]);

  // Debounce so each keystroke doesn't hit the server.
  const [debounced, setDebounced] = useState('');
  useEffect(() => {
    const t = setTimeout(() => setDebounced(composed), 250);
    return () => clearTimeout(t);
  }, [composed]);

  const results = useQuery({
    queryKey: ['catalog-search', game, debounced],
    queryFn: () => api.scanSearch(game, debounced),
    enabled: open && debounced.trim().length >= 2,
  });

  const add = useMutation({
    mutationFn: () =>
      api.scanCommit(locationId, [
        {
          game,
          gameCardId: selected!.gameCardId,
          name: selected!.name,
          setCode: selected!.setCode,
          setName: selected!.setName,
          collectorNumber: selected!.collectorNumber,
          rarity: selected!.rarity,
          imageUri: selected!.imageUri,
          condition,
          isFoil,
          quantity: Math.max(1, quantity),
          purchasePrice: purchasePrice === '' ? null : Number(purchasePrice),
        },
      ]),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['location', locationId] });
      qc.invalidateQueries({ queryKey: ['collection'] });
      qc.invalidateQueries({ queryKey: ['location-cards'] });
      qc.invalidateQueries({ queryKey: ['locations'] });
      qc.invalidateQueries({ queryKey: ['dashboard'] });
      setAddedCount((n) => n + Math.max(1, quantity));
      // Reset for the next card, keeping game/condition/foil so a batch stays fast.
      setSelected(null);
      setName('');
      setSet('');
      setCn('');
      setQuantity(1);
      setPurchasePrice('');
    },
  });

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>Add card to {locationName}</DialogTitle>
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
            sx={{ maxWidth: 220 }}
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
            <TextField size="small" label="Set" value={set} onChange={(e) => setSet(e.target.value)} sx={{ width: 120 }} />
            <TextField size="small" label="Collector #" value={cn} onChange={(e) => setCn(e.target.value)} sx={{ width: 120 }} />
          </Stack>

          {/* Results */}
          {debounced.trim().length >= 2 && !selected && (
            <Box>
              {results.isFetching ? (
                <Typography variant="body2" color="text.secondary">Searching…</Typography>
              ) : results.data && results.data.length > 0 ? (
                <List dense disablePadding sx={{ maxHeight: 260, overflowY: 'auto', border: 1, borderColor: 'divider', borderRadius: 1 }}>
                  {results.data.map((r) => (
                    <ListItemButton key={`${r.gameCardId}`} onClick={() => setSelected(r)}>
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
          )}

          {/* Selected card + add form */}
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
                  label="Quantity"
                  type="number"
                  value={quantity}
                  onChange={(e) => setQuantity(Math.max(1, Math.floor(Number(e.target.value) || 1)))}
                  slotProps={{ htmlInput: { min: 1, step: 1 } }}
                  sx={{ width: 110 }}
                />
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
              {add.error && <Typography color="error" variant="body2">{(add.error as Error).message}</Typography>}
            </>
          )}

          {addedCount > 0 && (
            <Typography variant="body2" color="success.main">
              Added {addedCount} card{addedCount === 1 ? '' : 's'} to {locationName}.
            </Typography>
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Done</Button>
        <Button variant="contained" disabled={!selected || add.isPending} onClick={() => add.mutate()}>
          {add.isPending ? 'Adding…' : 'Add card'}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
