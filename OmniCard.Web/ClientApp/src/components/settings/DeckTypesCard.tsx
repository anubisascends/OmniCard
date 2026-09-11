import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Button,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControlLabel,
  IconButton,
  MenuItem,
  Paper,
  Stack,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  TextField,
  Typography,
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import EditIcon from '@mui/icons-material/Edit';
import DeleteIcon from '@mui/icons-material/DeleteOutline';
import { api } from '../../api/client';
import type { DeckTypeDto, DeckTypeUpsertRequest } from '../../api/types';

const emptyRules = (game: string): DeckTypeUpsertRequest => ({
  game,
  name: '',
  deckSizeMin: null,
  deckSizeMax: null,
  maxCopiesPerCard: null,
  singleton: false,
  basicLandsExempt: false,
  commanderSlots: 0,
});

function numOrNull(v: string): number | null {
  const n = Number(v);
  return v.trim() === '' || Number.isNaN(n) ? null : n;
}

/** Add/edit dialog for a deck type's name + advisory build rules. */
function DeckTypeDialog({
  open,
  game,
  existing,
  onClose,
  onSaved,
}: {
  open: boolean;
  game: string;
  existing: DeckTypeDto | null;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [form, setForm] = useState<DeckTypeUpsertRequest>(emptyRules(game));

  // Seed the form when the dialog opens (edit → prefill; add → blank for the current game).
  const [seededFor, setSeededFor] = useState<number | 'new' | null>(null);
  const key = existing?.id ?? 'new';
  if (open && seededFor !== key) {
    setSeededFor(key);
    setForm(
      existing
        ? {
            game: existing.game,
            name: existing.name,
            deckSizeMin: existing.deckSizeMin ?? null,
            deckSizeMax: existing.deckSizeMax ?? null,
            maxCopiesPerCard: existing.maxCopiesPerCard ?? null,
            singleton: existing.singleton,
            basicLandsExempt: existing.basicLandsExempt,
            commanderSlots: existing.commanderSlots,
          }
        : emptyRules(game),
    );
  }
  if (!open && seededFor !== null) setSeededFor(null);

  const save = useMutation({
    mutationFn: async () => {
      if (existing) await api.deckTypeUpdate(existing.id, form);
      else await api.deckTypeCreate(form);
    },
    onSuccess: () => {
      onSaved();
      onClose();
    },
  });

  const set = (patch: Partial<DeckTypeUpsertRequest>) => setForm((f) => ({ ...f, ...patch }));

  return (
    <Dialog open={open} onClose={onClose} maxWidth="xs" fullWidth>
      <DialogTitle>{existing ? 'Edit deck type' : 'New deck type'}</DialogTitle>
      <DialogContent>
        <Stack spacing={1.5} sx={{ mt: 1 }}>
          <TextField
            label="Name"
            size="small"
            value={form.name}
            onChange={(e) => set({ name: e.target.value })}
            autoFocus
          />
          <Stack direction="row" spacing={1}>
            <TextField
              label="Deck size min"
              size="small"
              type="number"
              value={form.deckSizeMin ?? ''}
              onChange={(e) => set({ deckSizeMin: numOrNull(e.target.value) })}
            />
            <TextField
              label="Deck size max"
              size="small"
              type="number"
              value={form.deckSizeMax ?? ''}
              onChange={(e) => set({ deckSizeMax: numOrNull(e.target.value) })}
            />
          </Stack>
          <Stack direction="row" spacing={1}>
            <TextField
              label="Max copies / card"
              size="small"
              type="number"
              value={form.maxCopiesPerCard ?? ''}
              onChange={(e) => set({ maxCopiesPerCard: numOrNull(e.target.value) })}
              disabled={form.singleton}
              helperText={form.singleton ? 'Singleton = 1' : ' '}
            />
            <TextField
              label="Commander/leader slots"
              size="small"
              type="number"
              value={form.commanderSlots}
              onChange={(e) => set({ commanderSlots: numOrNull(e.target.value) ?? 0 })}
            />
          </Stack>
          <FormControlLabel
            control={<Switch checked={form.singleton} onChange={(e) => set({ singleton: e.target.checked })} />}
            label="Singleton (max 1 of each card)"
          />
          <FormControlLabel
            control={
              <Switch
                checked={form.basicLandsExempt}
                onChange={(e) => set({ basicLandsExempt: e.target.checked })}
              />
            }
            label="Basic lands exempt from copy limit"
          />
          {save.isError && (
            <Typography variant="caption" color="error">
              {(save.error as Error).message}
            </Typography>
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button
          variant="contained"
          disabled={form.name.trim().length === 0 || save.isPending}
          onClick={() => save.mutate()}
        >
          Save
        </Button>
      </DialogActions>
    </Dialog>
  );
}

/** Settings tab: per-game deck formats (built-in + custom) with editable build rules. */
export function DeckTypesCard() {
  const qc = useQueryClient();
  const games = useQuery({ queryKey: ['games'], queryFn: api.games });
  const [game, setGame] = useState('Mtg');
  const [dialog, setDialog] = useState<{ open: boolean; existing: DeckTypeDto | null }>({
    open: false,
    existing: null,
  });

  const deckTypes = useQuery({ queryKey: ['deck-types', game], queryFn: () => api.deckTypes(game) });
  const remove = useMutation({
    mutationFn: (id: number) => api.deckTypeDelete(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['deck-types', game] }),
  });
  const refresh = () => qc.invalidateQueries({ queryKey: ['deck-types'] });

  const rows = deckTypes.data ?? [];

  return (
    <Paper variant="outlined" sx={{ p: 2, maxWidth: 720 }}>
      <Typography variant="h6" gutterBottom>
        Deck types
      </Typography>
      <Typography variant="body2" color="text.secondary" gutterBottom>
        The deck formats a deck box can be assigned, per game. Built-ins can be edited; add your own for
        formats we didn't include. Rules drive advisory legality warnings only — they never block.
      </Typography>

      <Stack direction="row" spacing={1} alignItems="center" sx={{ mt: 1, mb: 1 }}>
        <TextField
          select
          size="small"
          label="Game"
          value={game}
          onChange={(e) => setGame(e.target.value)}
          sx={{ minWidth: 200 }}
        >
          {games.data?.map((g) => (
            <MenuItem key={g.id} value={g.id}>
              {g.displayName}
            </MenuItem>
          ))}
        </TextField>
        <Button
          variant="outlined"
          startIcon={<AddIcon />}
          onClick={() => setDialog({ open: true, existing: null })}
        >
          Add deck type
        </Button>
      </Stack>

      <Table size="small">
        <TableHead>
          <TableRow>
            <TableCell>Name</TableCell>
            <TableCell>Size</TableCell>
            <TableCell>Copies</TableCell>
            <TableCell>Commander</TableCell>
            <TableCell align="right">Actions</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {rows.map((d) => (
            <TableRow key={d.id}>
              <TableCell>
                {d.name}
                {d.isBuiltIn && <Chip label="built-in" size="small" variant="outlined" sx={{ ml: 1 }} />}
              </TableCell>
              <TableCell>
                {d.deckSizeMin ?? '—'}
                {d.deckSizeMax != null && d.deckSizeMax !== d.deckSizeMin ? `–${d.deckSizeMax}` : ''}
              </TableCell>
              <TableCell>{d.singleton ? '1 (singleton)' : d.maxCopiesPerCard ?? '—'}</TableCell>
              <TableCell>{d.commanderSlots > 0 ? d.commanderSlots : '—'}</TableCell>
              <TableCell align="right">
                <IconButton size="small" onClick={() => setDialog({ open: true, existing: d })}>
                  <EditIcon fontSize="small" />
                </IconButton>
                <IconButton
                  size="small"
                  onClick={() => {
                    if (confirm(`Delete deck type "${d.name}"? Deck boxes using it will be unset.`))
                      remove.mutate(d.id);
                  }}
                >
                  <DeleteIcon fontSize="small" />
                </IconButton>
              </TableCell>
            </TableRow>
          ))}
          {rows.length === 0 && (
            <TableRow>
              <TableCell colSpan={5}>
                <Typography variant="body2" color="text.secondary">
                  No deck types for this game yet.
                </Typography>
              </TableCell>
            </TableRow>
          )}
        </TableBody>
      </Table>

      <DeckTypeDialog
        open={dialog.open}
        game={game}
        existing={dialog.existing}
        onClose={() => setDialog({ open: false, existing: null })}
        onSaved={refresh}
      />
    </Paper>
  );
}
