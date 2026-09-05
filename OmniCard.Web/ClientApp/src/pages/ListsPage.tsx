import { useState } from 'react';
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
import EditIcon from '@mui/icons-material/Edit';
import RefreshIcon from '@mui/icons-material/Refresh';
import { api } from '../api/client';
import { useGame } from '../context/GameContext';
import type { CardListDto } from '../api/types';

const money = (n?: number | null) =>
  n == null ? '—' : n.toLocaleString(undefined, { style: 'currency', currency: 'USD' });

const CONDITIONS = ['NM', 'LP', 'MP', 'HP', 'DMG'];

function ListDetail({ list, onDeleted }: { list: CardListDto; onDeleted: () => void }) {
  const qc = useQueryClient();
  const items = useQuery({ queryKey: ['list-items', list.id], queryFn: () => api.listItems(list.id) });
  const locations = useQuery({ queryKey: ['locations', undefined], queryFn: () => api.locations() });
  const [containerId, setContainerId] = useState<number | ''>('');
  const [condition, setCondition] = useState('NM');

  const invalidate = () => {
    qc.invalidateQueries({ queryKey: ['list-items', list.id] });
    qc.invalidateQueries({ queryKey: ['lists'] });
  };

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
          Refresh prices
        </Button>
        <Box sx={{ flexGrow: 1 }} />
        <TextField
          select
          size="small"
          label="Location"
          value={containerId}
          onChange={(e) => setContainerId(e.target.value === '' ? '' : Number(e.target.value))}
          sx={{ minWidth: 180 }}
        >
          <MenuItem value="">— choose —</MenuItem>
          {locations.data?.map((l) => (
            <MenuItem key={l.id} value={l.id}>
              {l.name}
            </MenuItem>
          ))}
        </TextField>
        <TextField
          select
          size="small"
          label="Cond"
          value={condition}
          onChange={(e) => setCondition(e.target.value)}
          sx={{ width: 90 }}
        >
          {CONDITIONS.map((c) => (
            <MenuItem key={c} value={c}>
              {c}
            </MenuItem>
          ))}
        </TextField>
        <Button
          variant="contained"
          disabled={containerId === '' || items.data.length === 0 || commit.isPending}
          onClick={() => commit.mutate()}
        >
          {commit.isPending ? 'Committing…' : 'Commit to location'}
        </Button>
      </Stack>
      {commit.error && <Alert severity="error">{(commit.error as Error).message}</Alert>}

      <Divider sx={{ mb: 1 }} />

      {items.data.length === 0 ? (
        <Typography color="text.secondary" variant="body2">
          This list is empty. (Cards are added to lists from the desktop scan/import flow.)
        </Typography>
      ) : (
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>Card</TableCell>
              <TableCell>Set</TableCell>
              <TableCell align="right">Qty</TableCell>
              <TableCell align="right">Price</TableCell>
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
                <TableCell align="right">{it.isUnpriced ? '—' : money(it.marketPrice)}</TableCell>
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
  const qc = useQueryClient();
  const { game: contextGame } = useGame();
  const [game, setGame] = useState(contextGame ?? 'Mtg');
  const [newName, setNewName] = useState('');
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
      <Typography variant="h4">Lists</Typography>

      <Paper variant="outlined" sx={{ p: 2 }}>
        <Stack direction="row" spacing={2} alignItems="center" flexWrap="wrap" useFlexGap>
          <TextField
            select
            size="small"
            label="Game"
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
            label="New list name"
            value={newName}
            onChange={(e) => setNewName(e.target.value)}
          />
          <Button
            variant="contained"
            startIcon={<AddIcon />}
            disabled={!newName.trim() || create.isPending}
            onClick={() => create.mutate()}
          >
            Create
          </Button>
        </Stack>
      </Paper>

      {lists.isLoading || !lists.data ? (
        <CircularProgress />
      ) : lists.data.length === 0 ? (
        <Typography color="text.secondary">No lists for this game yet.</Typography>
      ) : (
        <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
          {lists.data.map((l) => (
            <Chip
              key={l.id}
              label={`${l.name} (${l.itemCount})`}
              color={l.id === selectedId ? 'primary' : 'default'}
              onClick={() => setSelectedId(l.id)}
              onDelete={() => {
                if (confirm(`Delete list "${l.name}"?`)) del.mutate(l.id);
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
                const name = prompt('Rename list', selected.name);
                if (name?.trim()) rename.mutate({ id: selected.id, name: name.trim() });
              }}
            >
              Rename
            </Button>
          </Stack>
          <ListDetail list={selected} onDeleted={() => setSelectedId(null)} />
        </>
      )}
    </Stack>
  );
}
