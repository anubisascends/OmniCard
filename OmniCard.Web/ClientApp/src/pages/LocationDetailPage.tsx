import { useState } from 'react';
import { useParams, Link as RouterLink } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Box,
  Breadcrumbs,
  Button,
  Chip,
  Link,
  Stack,
  ToggleButton,
  ToggleButtonGroup,
  Typography,
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import ViewListIcon from '@mui/icons-material/ViewList';
import ViewColumnIcon from '@mui/icons-material/ViewColumn';
import { api } from '../api/client';
import { useGame } from '../context/GameContext';
import { CardTable } from '../components/CardTable';
import { AddCardDialog } from '../components/dialogs/AddCardDialog';
import { DeckBoxPanel } from '../components/DeckBoxPanel';
import { DeckStackView } from '../components/deckstack/DeckStackView';
import { SearchBox } from '../components/SearchBox';

const money = (n: number) => n.toLocaleString(undefined, { style: 'currency', currency: 'USD' });
const VIEW_KEY = 'omnicard.location.view';

export function LocationDetailPage() {
  const { id } = useParams();
  const locationId = Number(id);
  const { game } = useGame();
  const qc = useQueryClient();
  const [search, setSearch] = useState('');
  const [q, setQ] = useState('');
  const [addOpen, setAddOpen] = useState(false);
  // Table (flat grid) vs. Stacks (Archidekt-style grouped stacks). Persisted; location-only feature.
  const [view, setView] = useState<'table' | 'stacks'>(
    () => (localStorage.getItem(VIEW_KEY) === 'stacks' ? 'stacks' : 'table'),
  );
  const setViewMode = (v: 'table' | 'stacks') => {
    setView(v);
    localStorage.setItem(VIEW_KEY, v);
  };

  const locQuery = useQuery({ queryKey: ['location', locationId], queryFn: () => api.location(locationId) });
  const isDeckBox = locQuery.data?.type === 'Deck Box';
  // A game-locked deck box forces its game onto the add dialog.
  const deckBoxGame = isDeckBox ? locQuery.data?.game ?? undefined : undefined;
  const refresh = () => {
    qc.invalidateQueries({ queryKey: ['location', locationId] });
    qc.invalidateQueries({ queryKey: ['locations'] });
  };

  return (
    <Stack spacing={2} sx={{ height: 'calc(100vh - 120px)' }}>
      <Breadcrumbs>
        <Link component={RouterLink} to="/locations">
          Locations
        </Link>
        <Typography color="text.primary">{locQuery.data?.name ?? '…'}</Typography>
      </Breadcrumbs>
      <Stack direction="row" spacing={2} alignItems="center">
        <Typography variant="h4">{locQuery.data?.name ?? 'Location'}</Typography>
        {locQuery.data && <Chip label={locQuery.data.type} />}
        {locQuery.data?.type === 'Binder' && (
          <Link component={RouterLink} to={`/binder/${locationId}`}>
            Open binder view
          </Link>
        )}
        <Box sx={{ flexGrow: 1 }} />
        <Button variant="contained" startIcon={<AddIcon />} onClick={() => setAddOpen(true)}>
          Add card
        </Button>
      </Stack>
      {locQuery.data && (
        <Typography variant="body2" color="text.secondary">
          {locQuery.data.cardCount.toLocaleString()} cards · {money(locQuery.data.totalMarketValue)} market
        </Typography>
      )}
      {isDeckBox && locQuery.data && <DeckBoxPanel loc={locQuery.data} onChanged={refresh} />}
      <Stack direction="row" spacing={1} alignItems="center">
        <Box sx={{ flexGrow: 1 }}>
          <SearchBox value={search} onChange={setSearch} onSubmit={setQ} />
        </Box>
        <ToggleButtonGroup
          size="small"
          exclusive
          value={view}
          onChange={(_, v) => v && setViewMode(v)}
          aria-label="View mode"
        >
          <ToggleButton value="table" aria-label="Table view">
            <ViewListIcon fontSize="small" />
          </ToggleButton>
          <ToggleButton value="stacks" aria-label="Stacked view">
            <ViewColumnIcon fontSize="small" />
          </ToggleButton>
        </ToggleButtonGroup>
      </Stack>
      {view === 'stacks' ? (
        <DeckStackView containerId={locationId} game={game} q={q} />
      ) : (
        <CardTable game={game} q={q} containerId={locationId} />
      )}

      <AddCardDialog
        open={addOpen}
        locationId={locationId}
        locationName={locQuery.data?.name ?? 'this location'}
        defaultGame={deckBoxGame ?? game}
        lockGame={isDeckBox && !!deckBoxGame}
        onClose={() => setAddOpen(false)}
      />
    </Stack>
  );
}
