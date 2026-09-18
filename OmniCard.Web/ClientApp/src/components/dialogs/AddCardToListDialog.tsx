import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
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

/**
 * Add a card to a saved list. Mirrors the binder's AddCardToSlotDialog, but the target is a list, not a
 * pocket — so adding never moves or mutates anything. Search results come in two groups:
 *  - **In your collection** — owned lots (via /api/collection). Picking one records a *reference* to that
 *    lot on the list (via /api/lists/{id}/items/from-collection). The physical copy stays put; only when the
 *    list is later committed to a location is that copy relocated (rather than duplicated).
 *  - **In the catalog** — printings you may not own yet (via /api/scan/search). Picking one reveals a
 *    foil/quantity form and records a "wholly new" printing on the list (via /api/lists/{id}/items). On
 *    commit these become brand-new owned lots.
 * The dialog stays open after each add so several cards can be added in one sitting.
 */
export function AddCardToListDialog({
  open,
  listId,
  defaultGame,
  onClose,
  onDone,
}: {
  open: boolean;
  listId: number;
  defaultGame: string;
  onClose: () => void;
  /** Called after each successful add so the caller can refresh the list. */
  onDone: () => void;
}) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const games = useQuery({ queryKey: ['games'], queryFn: api.games, enabled: open });

  const [game, setGame] = useState(defaultGame);
  const [name, setName] = useState('');
  const [set, setSet] = useState('');
  const [cn, setCn] = useState('');
  const [selected, setSelected] = useState<ScanSearchResultDto | null>(null);
  const [isFoil, setIsFoil] = useState(false);
  const [quantity, setQuantity] = useState('1');
  const [error, setError] = useState<string | null>(null);
  const [addedCount, setAddedCount] = useState(0);

  // Reset the form each time the dialog opens.
  const [wasOpen, setWasOpen] = useState(false);
  if (open && !wasOpen) {
    setWasOpen(true);
    setGame(defaultGame);
    setName('');
    setSet('');
    setCn('');
    setSelected(null);
    setIsFoil(false);
    setQuantity('1');
    setError(null);
    setAddedCount(0);
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
    const h = setTimeout(() => setDebounced(composed), 250);
    return () => clearTimeout(h);
  }, [composed]);

  const canSearch = open && !selected && debounced.trim().length >= 2;

  const owned = useQuery({
    queryKey: ['collection', 'list-add', game, debounced],
    queryFn: () => api.collection({ game, q: debounced, take: 25 }),
    enabled: canSearch,
  });
  const ownedItems = owned.data?.items ?? [];
  const catalog = useQuery({
    queryKey: ['catalog-search', game, debounced],
    queryFn: () => api.scanSearch(game, debounced),
    enabled: canSearch,
  });

  const invalidate = () => {
    qc.invalidateQueries({ queryKey: ['list-items', listId] });
    qc.invalidateQueries({ queryKey: ['lists'] });
  };

  const qty = () => Math.max(1, Number(quantity) || 1);

  const addOwned = useMutation({
    mutationFn: (card: CardDto) => api.listAddFromCollection(listId, card.id, 1),
    onSuccess: () => {
      setAddedCount((c) => c + 1);
      invalidate();
      onDone();
    },
    onError: (e) => setError((e as Error).message),
  });

  const addCatalog = useMutation({
    mutationFn: () =>
      api.listAddItem(listId, {
        gameCardId: selected!.gameCardId,
        name: selected!.name,
        setCode: selected!.setCode,
        setName: selected!.setName,
        collectorNumber: selected!.collectorNumber,
        rarity: selected!.rarity,
        imageUri: selected!.imageUri,
        isFoil,
        quantity: qty(),
      }),
    onSuccess: () => {
      setAddedCount((c) => c + 1);
      invalidate();
      onDone();
      // Ready the form for the next card rather than closing — lists usually grow in bunches.
      setSelected(null);
      setIsFoil(false);
      setQuantity('1');
      setName('');
      setSet('');
      setCn('');
    },
    onError: (e) => setError((e as Error).message),
  });

  const busy = addOwned.isPending || addCatalog.isPending;

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>
        {t('dialogs.addToList.title')}
        {addedCount > 0 && (
          <Typography variant="body2" color="text.secondary">
            {t('dialogs.addToList.addedSoFar', { count: addedCount })}
          </Typography>
        )}
      </DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <TextField
            select
            size="small"
            label={t('common.labels.game')}
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
              label={t('common.labels.name')}
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
            <TextField size="small" label={t('common.labels.set')} value={set} onChange={(e) => setSet(e.target.value)} sx={{ width: 110 }} />
            <TextField size="small" label={t('common.labels.collectorNumber')} value={cn} onChange={(e) => setCn(e.target.value)} sx={{ width: 110 }} />
          </Stack>

          {/* Search results — collection first, then catalog. */}
          {canSearch && (
            <Stack spacing={1.5}>
              <Box>
                <Stack direction="row" spacing={1} alignItems="center" sx={{ mb: 0.5 }}>
                  <CollectionsBookmarkIcon fontSize="small" color="action" />
                  <Typography variant="subtitle2">
                    {t('dialogs.addToList.inCollection')} {owned.data ? `(${ownedItems.length})` : ''}
                  </Typography>
                </Stack>
                {owned.isFetching ? (
                  <Typography variant="body2" color="text.secondary">{t('common.states.searching')}</Typography>
                ) : ownedItems.length > 0 ? (
                  <List dense disablePadding sx={{ maxHeight: 200, overflowY: 'auto', border: 1, borderColor: 'divider', borderRadius: 1 }}>
                    {ownedItems.map((c) => (
                      <ListItemButton key={c.id} onClick={() => addOwned.mutate(c)} disabled={busy}>
                        <Stack direction="row" spacing={1} alignItems="center" sx={{ width: '100%' }}>
                          {c.imageUri && (
                            <Box component="img" src={c.imageUri} alt="" sx={{ width: 32, aspectRatio: '0.72', objectFit: 'contain', flexShrink: 0 }} />
                          )}
                          <Box sx={{ minWidth: 0, flexGrow: 1 }}>
                            <Typography variant="body2" noWrap>{c.name}</Typography>
                            <Typography variant="caption" color="text.secondary" noWrap sx={{ display: 'block' }}>
                              {c.setCode.toUpperCase()} · #{c.number} · {c.condition}
                              {c.isFoil ? ` · ${t('common.labels.foil')}` : ''}
                            </Typography>
                          </Box>
                          <Chip
                            size="small"
                            variant="outlined"
                            label={c.containerName ?? t('dialogs.addToList.unfiled')}
                            sx={{ flexShrink: 0, maxWidth: 130 }}
                          />
                        </Stack>
                      </ListItemButton>
                    ))}
                  </List>
                ) : (
                  <Typography variant="body2" color="text.secondary">
                    {t('dialogs.addToList.noOwnedCopies')}
                  </Typography>
                )}
              </Box>

              <Box>
                <Stack direction="row" spacing={1} alignItems="center" sx={{ mb: 0.5 }}>
                  <TravelExploreIcon fontSize="small" color="action" />
                  <Typography variant="subtitle2">
                    {t('dialogs.addToList.inCatalog')} {catalog.data ? `(${catalog.data.length})` : ''}
                  </Typography>
                </Stack>
                {catalog.isFetching ? (
                  <Typography variant="body2" color="text.secondary">{t('common.states.searching')}</Typography>
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
                  <Typography variant="body2" color="text.secondary">{t('dialogs.addToList.noCatalogMatches', { game })}</Typography>
                )}
              </Box>
            </Stack>
          )}

          {/* Selected catalog card → foil/quantity before adding it as a new list item. */}
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
                <Button size="small" onClick={() => setSelected(null)}>{t('common.actions.change')}</Button>
              </Stack>

              <Stack direction="row" spacing={1} alignItems="center">
                <TextField
                  size="small"
                  label={t('lists.detail.columns.qty')}
                  type="number"
                  value={quantity}
                  onChange={(e) => setQuantity(e.target.value)}
                  slotProps={{ htmlInput: { min: 1, step: 1 } }}
                  sx={{ width: 100 }}
                />
                <FormControlLabel
                  control={<Switch checked={isFoil} onChange={(e) => setIsFoil(e.target.checked)} />}
                  label={t('common.labels.foil')}
                />
              </Stack>
            </>
          )}

          {error && <Alert severity="error">{error}</Alert>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{selected ? t('common.actions.cancel') : t('common.actions.close')}</Button>
        <Button variant="contained" disabled={!selected || busy} onClick={() => addCatalog.mutate()}>
          {addCatalog.isPending ? t('common.states.adding') : t('dialogs.addToList.addToList')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
