import { useParams, Link as RouterLink } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { Breadcrumbs, Chip, Link, Stack, Typography } from '@mui/material';
import { api } from '../api/client';
import { useGame } from '../context/GameContext';
import { CardTable } from '../components/CardTable';

const money = (n: number) => n.toLocaleString(undefined, { style: 'currency', currency: 'USD' });

export function LocationDetailPage() {
  const { id } = useParams();
  const locationId = Number(id);
  const { game } = useGame();

  const locQuery = useQuery({ queryKey: ['location', locationId], queryFn: () => api.location(locationId) });

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
      </Stack>
      {locQuery.data && (
        <Typography variant="body2" color="text.secondary">
          {locQuery.data.cardCount.toLocaleString()} cards · {money(locQuery.data.totalMarketValue)} market
        </Typography>
      )}
      <CardTable game={game} containerId={locationId} />
    </Stack>
  );
}
