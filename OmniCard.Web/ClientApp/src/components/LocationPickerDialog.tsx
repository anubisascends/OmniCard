import { useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Button,
  Chip,
  CircularProgress,
  Dialog,
  DialogContent,
  DialogTitle,
  Divider,
  List,
  ListItemButton,
  ListSubheader,
  MenuItem,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import SearchIcon from '@mui/icons-material/Search';
import AddIcon from '@mui/icons-material/Add';
import { InputAdornment } from '@mui/material';
import { api } from '../api/client';
import { groupLocations } from '../lib/locationGroups';

const TYPES = [
  { value: 'Binder', label: 'Binder' },
  { value: 'Box', label: 'Box' },
  { value: 'DeckBox', label: 'Deck Box' },
  { value: 'DisplayCase', label: 'Display Case' },
];

/** Inline "create a new location" section, revealed from the picker so callers never have to leave. */
function CreateLocationSection({ onCreated }: { onCreated: (id: number) => void }) {
  const qc = useQueryClient();
  const [open, setOpen] = useState(false);
  const [name, setName] = useState('');
  const [type, setType] = useState('Box');

  const trimmed = name.trim();
  const nameCheck = useQuery({
    queryKey: ['loc-name-available', trimmed],
    queryFn: () => api.locationNameAvailable(trimmed),
    enabled: open && trimmed.length > 0,
  });
  const taken = trimmed.length > 0 && nameCheck.data?.available === false;

  const create = useMutation({
    mutationFn: () => api.locationCreate({ name: trimmed, type }),
    onSuccess: (loc) => {
      qc.invalidateQueries({ queryKey: ['locations'] });
      setName('');
      setOpen(false);
      onCreated(loc.id);
    },
  });

  return (
    <>
      <Divider sx={{ my: 1 }} />
      {open ? (
        <Stack spacing={1} sx={{ pt: 0.5 }}>
          <TextField
            size="small"
            autoFocus
            label="New location name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            error={taken}
            helperText={taken ? 'This name is already in use' : ' '}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && trimmed.length > 0 && !taken && !create.isPending) create.mutate();
            }}
          />
          <TextField select size="small" label="Type" value={type} onChange={(e) => setType(e.target.value)}>
            {TYPES.map((t) => (
              <MenuItem key={t.value} value={t.value}>
                {t.label}
              </MenuItem>
            ))}
          </TextField>
          {create.error && (
            <Typography variant="caption" color="error">
              {(create.error as Error).message}
            </Typography>
          )}
          <Stack direction="row" spacing={1} justifyContent="flex-end">
            <Button size="small" onClick={() => setOpen(false)}>
              Cancel
            </Button>
            <Button
              size="small"
              variant="contained"
              startIcon={<AddIcon />}
              disabled={trimmed.length === 0 || taken || create.isPending}
              onClick={() => create.mutate()}
            >
              Create &amp; select
            </Button>
          </Stack>
        </Stack>
      ) : (
        <Button size="small" startIcon={<AddIcon />} onClick={() => setOpen(true)} sx={{ alignSelf: 'flex-start' }}>
          New location
        </Button>
      )}
    </>
  );
}

/**
 * Grouped, searchable location picker. Locations are grouped (Always Available first, then by type)
 * and sorted A→Z, with a search box to filter by name. Used anywhere a card (or cards) is moved to a
 * different location. When `allowCreate` (the default), a new location can be created inline and is
 * auto-selected — so callers (e.g. the scan page) never have to navigate away and lose their work.
 */
export function LocationPickerDialog({
  open,
  title = 'Move to location',
  excludeId,
  allowCreate = true,
  onPick,
  onClose,
}: {
  open: boolean;
  title?: string;
  excludeId?: number;
  allowCreate?: boolean;
  onPick: (id: number) => void;
  onClose: () => void;
}) {
  const { data, isLoading } = useQuery({ queryKey: ['locations', undefined], queryFn: () => api.locations(), enabled: open });
  const [search, setSearch] = useState('');

  const groups = useMemo(() => {
    const term = search.trim().toLowerCase();
    const filtered = (data ?? [])
      .filter((l) => l.id !== excludeId)
      .filter((l) => (term ? l.name.toLowerCase().includes(term) : true));
    return groupLocations(filtered);
  }, [data, search, excludeId]);

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle sx={{ pb: 1 }}>{title}</DialogTitle>
      <DialogContent>
        <TextField
          fullWidth
          size="small"
          autoFocus
          placeholder="Search locations…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          slotProps={{
            input: {
              startAdornment: (
                <InputAdornment position="start">
                  <SearchIcon fontSize="small" />
                </InputAdornment>
              ),
            },
          }}
          sx={{ mb: 1 }}
        />
        {isLoading ? (
          <Stack alignItems="center" sx={{ py: 3 }}>
            <CircularProgress size={24} />
          </Stack>
        ) : groups.length === 0 ? (
          <Typography variant="body2" color="text.secondary" sx={{ py: 2 }}>
            No matching locations.
          </Typography>
        ) : (
          <List dense disablePadding sx={{ maxHeight: '55vh', overflowY: 'auto' }}>
            {groups.map((g) => (
              <li key={g.key}>
                <ul style={{ padding: 0 }}>
                  <ListSubheader disableSticky sx={{ bgcolor: 'transparent', lineHeight: '28px' }}>
                    {g.heading}
                  </ListSubheader>
                  {g.items.map((l) => (
                    <ListItemButton
                      key={l.id}
                      onClick={() => onPick(l.id)}
                      sx={{ borderRadius: 1 }}
                    >
                      <Stack direction="row" spacing={1} alignItems="center" sx={{ width: '100%' }}>
                        <Typography variant="body2" sx={{ flexGrow: 1 }} noWrap>
                          {l.name}
                        </Typography>
                        <Typography variant="caption" color="text.secondary">
                          {l.cardCount.toLocaleString()}
                        </Typography>
                        <Chip size="small" variant="outlined" label={l.type} />
                      </Stack>
                    </ListItemButton>
                  ))}
                </ul>
              </li>
            ))}
          </List>
        )}
        {allowCreate && !isLoading && <CreateLocationSection onCreated={onPick} />}
      </DialogContent>
    </Dialog>
  );
}
