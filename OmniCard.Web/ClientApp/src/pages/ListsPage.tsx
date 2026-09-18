import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  Divider,
  IconButton,
  MenuItem,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  TextField,
  Typography,
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import DeleteIcon from '@mui/icons-material/Delete';
import DownloadIcon from '@mui/icons-material/Download';
import EditIcon from '@mui/icons-material/Edit';
import RefreshIcon from '@mui/icons-material/Refresh';
import { api } from '../api/client';
import { locationSelectOptions } from '../components/LocationSelectOptions';
import { useGame } from '../context/GameContext';
import { useFormatters } from '../i18n/format';
import type { CardListDto } from '../api/types';

const CONDITIONS = ['NM', 'LP', 'MP', 'HP', 'DMG'];

function ListDetail({ list, onDeleted }: { list: CardListDto; onDeleted: () => void }) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const qc = useQueryClient();
  const items = useQuery({ queryKey: ['list-items', list.id], queryFn: () => api.listItems(list.id) });
  const locations = useQuery({ queryKey: ['locations', undefined], queryFn: () => api.locations() });
  const [containerId, setContainerId] = useState<number | ''>('');
  const [condition, setCondition] = useState('NM');
  const [addUrl, setAddUrl] = useState('');

  const invalidate = () => {
    qc.invalidateQueries({ queryKey: ['list-items', list.id] });
    qc.invalidateQueries({ queryKey: ['lists'] });
  };

  const importUrl = useMutation({
    mutationFn: () => api.listImportUrl(addUrl.trim(), list.game, list.id),
    onSuccess: (r) => {
      setAddUrl('');
      invalidate();
      return r;
    },
  });

  const removeItem = useMutation({ mutationFn: (itemId: number) => api.listRemoveItem(itemId), onSuccess: invalidate });
  const setQty = useMutation({
    mutationFn: ({ itemId, quantity }: { itemId: number; quantity: number }) =>
      api.listSetItemQuantity(itemId, quantity),
    onSuccess: invalidate,
  });
  const refreshPrices = useMutation({ mutationFn: () => api.listRefreshPrices(list.id), onSuccess: invalidate });
  const commit = useMutation({
    mutationFn: () => api.listCommit(list.id, containerId as number, condition),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['lists'] });
      qc.invalidateQueries({ queryKey: ['collection'] });
      qc.invalidateQueries({ queryKey: ['locations'] });
      onDeleted(); // the list is consumed + deleted on commit
    },
  });

  if (items.isLoading || !items.data) return <CircularProgress />;

  return (
    <Paper variant="outlined" sx={{ p: 2 }}>
      <Typography variant="h6" gutterBottom>
        {list.name}
      </Typography>

      <Stack direction="row" spacing={1} alignItems="center" sx={{ mb: 1 }} flexWrap="wrap" useFlexGap>
        <Button
          size="small"
          startIcon={<RefreshIcon />}
          disabled={refreshPrices.isPending || items.data.length === 0}
          onClick={() => refreshPrices.mutate()}
        >
          {t('lists.detail.refreshPrices')}
        </Button>
        <Box sx={{ flexGrow: 1 }} />
        <TextField
          select
          size="small"
          label={t('common.labels.location')}
          value={containerId}
          onChange={(e) => setContainerId(e.target.value === '' ? '' : Number(e.target.value))}
          sx={{ minWidth: 180 }}
        >
          {locationSelectOptions(locations.data, { label: t('lists.detail.chooseLocation') })}
        </TextField>
        <TextField
          select
          size="small"
          label={t('lists.detail.conditionLabel')}
          value={condition}
          onChange={(e) => setCondition(e.target.value)}
          sx={{ width: 90 }}
        >
          {CONDITIONS.map((c) => (
            <MenuItem key={c} value={c}>
              {t(`common.conditions.${c}`)}
            </MenuItem>
          ))}
        </TextField>
        <Button
          variant="contained"
          disabled={containerId === '' || items.data.length === 0 || commit.isPending}
          onClick={() => commit.mutate()}
        >
          {commit.isPending ? t('lists.detail.committing') : t('lists.detail.commit')}
        </Button>
      </Stack>
      {commit.error && <Alert severity="error">{(commit.error as Error).message}</Alert>}

      <Stack direction="row" spacing={1} alignItems="center" sx={{ mb: 1 }} flexWrap="wrap" useFlexGap>
        <TextField
          size="small"
          label={t('lists.detail.addFromUrlLabel')}
          placeholder="https://moxfield.com/decks/…"
          value={addUrl}
          onChange={(e) => setAddUrl(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter' && addUrl.trim() && !importUrl.isPending) importUrl.mutate();
          }}
          sx={{ flexGrow: 1, minWidth: 260 }}
        />
        <Button
          startIcon={<DownloadIcon />}
          disabled={!addUrl.trim() || importUrl.isPending}
          onClick={() => importUrl.mutate()}
        >
          {importUrl.isPending ? t('lists.importing') : t('common.actions.add')}
        </Button>
      </Stack>
      {importUrl.error && <Alert severity="error">{(importUrl.error as Error).message}</Alert>}
      {importUrl.data && (
        <Alert severity={importUrl.data.unresolvedNames.length ? 'warning' : 'success'} sx={{ mb: 1 }}>
          {t('lists.detail.added', { count: importUrl.data.addedCount })}
          {importUrl.data.unresolvedNames.length > 0 &&
            t('lists.couldNotMatch', { names: importUrl.data.unresolvedNames.join(', ') })}
        </Alert>
      )}

      <Divider sx={{ mb: 1 }} />

      {items.data.length === 0 ? (
        <Typography color="text.secondary" variant="body2">
          {t('lists.detail.empty')}
        </Typography>
      ) : (
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>{t('lists.detail.columns.card')}</TableCell>
              <TableCell>{t('common.labels.set')}</TableCell>
              <TableCell align="right">{t('lists.detail.columns.qty')}</TableCell>
              <TableCell align="right">{t('common.labels.price')}</TableCell>
              <TableCell align="right"></TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {items.data.map((it) => (
              <TableRow key={it.id} hover>
                <TableCell>
                  {it.cardName}
                  {it.isFoil ? ' ✦' : ''}
                </TableCell>
                <TableCell>
                  {it.setCode?.toUpperCase()}
                  {it.collectorNumber ? ` #${it.collectorNumber}` : ''}
                </TableCell>
                <TableCell align="right">
                  <TextField
                    type="number"
                    size="small"
                    value={it.quantity}
                    onChange={(e) => {
                      const q = Number(e.target.value);
                      if (q >= 1) setQty.mutate({ itemId: it.id, quantity: q });
                    }}
                    sx={{ width: 70 }}
                  />
                </TableCell>
                <TableCell align="right">{it.isUnpriced ? '—' : fmt.money(it.marketPrice)}</TableCell>
                <TableCell align="right">
                  <IconButton size="small" onClick={() => removeItem.mutate(it.id)}>
                    <DeleteIcon fontSize="small" />
                  </IconButton>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </Paper>
  );
}

export function ListsPage() {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const { game: contextGame } = useGame();
  const [game, setGame] = useState(contextGame ?? 'Mtg');
  const [newName, setNewName] = useState('');
  const [importUrl, setImportUrl] = useState('');
  const [selectedId, setSelectedId] = useState<number | null>(null);

  const games = useQuery({ queryKey: ['games'], queryFn: api.games });
  const lists = useQuery({ queryKey: ['lists', game], queryFn: () => api.lists(game) });

  const create = useMutation({
    mutationFn: () => api.listCreate(newName.trim(), game),
    onSuccess: (l) => {
      setNewName('');
      qc.invalidateQueries({ queryKey: ['lists'] });
      setSelectedId(l.id);
    },
  });
  const importNew = useMutation({
    mutationFn: () => api.listImportUrl(importUrl.trim(), game),
    onSuccess: (r) => {
      setImportUrl('');
      qc.invalidateQueries({ queryKey: ['lists'] });
      setSelectedId(r.listId);
    },
  });
  const rename = useMutation({
    mutationFn: ({ id, name }: { id: number; name: string }) => api.listRename(id, name),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['lists'] }),
  });
  const del = useMutation({
    mutationFn: (id: number) => api.listDelete(id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['lists'] });
      setSelectedId(null);
    },
  });

  const selected = lists.data?.find((l) => l.id === selectedId) ?? null;

  return (
    <Stack spacing={2}>
      <Typography variant="h4">{t('lists.title')}</Typography>

      <Paper variant="outlined" sx={{ p: 2 }}>
        <Stack direction="row" spacing={2} alignItems="center" flexWrap="wrap" useFlexGap>
          <TextField
            select
            size="small"
            label={t('common.labels.game')}
            value={game}
            onChange={(e) => {
              setGame(e.target.value);
              setSelectedId(null);
            }}
            sx={{ minWidth: 180 }}
          >
            {games.data?.map((g) => (
              <MenuItem key={g.id} value={g.id}>
                {g.displayName}
              </MenuItem>
            ))}
          </TextField>
          <Box sx={{ flexGrow: 1 }} />
          <TextField
            size="small"
            label={t('lists.newListName')}
            value={newName}
            onChange={(e) => setNewName(e.target.value)}
          />
          <Button
            variant="contained"
            startIcon={<AddIcon />}
            disabled={!newName.trim() || create.isPending}
            onClick={() => create.mutate()}
          >
            {t('common.actions.create')}
          </Button>
        </Stack>

        <Divider sx={{ my: 2 }}>{t('lists.orImportFromUrl')}</Divider>

        <Stack direction="row" spacing={2} alignItems="center" flexWrap="wrap" useFlexGap>
          <TextField
            size="small"
            label={t('lists.deckUrlLabel')}
            placeholder="https://moxfield.com/decks/…"
            value={importUrl}
            onChange={(e) => setImportUrl(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && importUrl.trim() && !importNew.isPending) importNew.mutate();
            }}
            sx={{ flexGrow: 1, minWidth: 280 }}
          />
          <Button
            variant="outlined"
            startIcon={<DownloadIcon />}
            disabled={!importUrl.trim() || importNew.isPending}
            onClick={() => importNew.mutate()}
          >
            {importNew.isPending ? t('lists.importing') : t('lists.importAsNewList')}
          </Button>
        </Stack>
        {importNew.error && (
          <Alert severity="error" sx={{ mt: 1 }}>
            {(importNew.error as Error).message}
          </Alert>
        )}
        {importNew.data && (
          <Alert
            severity={importNew.data.unresolvedNames.length ? 'warning' : 'success'}
            sx={{ mt: 1 }}
          >
            {t('lists.imported', {
              name: importNew.data.listName,
              count: importNew.data.addedCount,
            })}
            {importNew.data.unresolvedNames.length > 0 &&
              t('lists.couldNotMatch', { names: importNew.data.unresolvedNames.join(', ') })}
          </Alert>
        )}
      </Paper>

      {lists.isLoading || !lists.data ? (
        <CircularProgress />
      ) : lists.data.length === 0 ? (
        <Typography color="text.secondary">{t('lists.noListsYet')}</Typography>
      ) : (
        <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
          {lists.data.map((l) => (
            <Chip
              key={l.id}
              label={t('lists.chipLabel', { name: l.name, count: l.itemCount })}
              color={l.id === selectedId ? 'primary' : 'default'}
              onClick={() => setSelectedId(l.id)}
              onDelete={() => {
                if (confirm(t('lists.confirmDelete', { name: l.name }))) del.mutate(l.id);
              }}
              deleteIcon={<DeleteIcon />}
            />
          ))}
        </Stack>
      )}

      {selected && (
        <>
          <Stack direction="row" spacing={1}>
            <Button
              size="small"
              startIcon={<EditIcon />}
              onClick={() => {
                const name = prompt(t('lists.renamePrompt'), selected.name);
                if (name?.trim()) rename.mutate({ id: selected.id, name: name.trim() });
              }}
            >
              {t('common.actions.rename')}
            </Button>
          </Stack>
          <ListDetail list={selected} onDeleted={() => setSelectedId(null)} />
        </>
      )}
    </Stack>
  );
}
