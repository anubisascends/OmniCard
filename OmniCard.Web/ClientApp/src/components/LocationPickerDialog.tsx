import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import {
  Chip,
  CircularProgress,
  Dialog,
  DialogContent,
  DialogTitle,
  List,
  ListItemButton,
  ListSubheader,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import SearchIcon from '@mui/icons-material/Search';
import { InputAdornment } from '@mui/material';
import { api } from '../api/client';
import { groupLocations } from '../lib/locationGroups';

/**
 * Grouped, searchable location picker. Locations are grouped (Always Available first, then by type)
 * and sorted A→Z, with a search box to filter by name. Used anywhere a card (or cards) is moved to a
 * different location.
 */
export function LocationPickerDialog({
  open,
  title = 'Move to location',
  excludeId,
  onPick,
  onClose,
}: {
  open: boolean;
  title?: string;
  excludeId?: number;
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
      </DialogContent>
    </Dialog>
  );
}
