import { Link as RouterLink } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import {
  Box,
  Card,
  CardActionArea,
  CardContent,
  CardMedia,
  Chip,
  CircularProgress,
  Stack,
  Typography,
} from '@mui/material';
import { api } from '../api/client';
import { useGame } from '../context/GameContext';

const money = (n: number) => n.toLocaleString(undefined, { style: 'currency', currency: 'USD' });

export function LocationsPage() {
  const { game } = useGame();
  const { data, isLoading } = useQuery({
    queryKey: ['locations', game],
    queryFn: () => api.locations(game),
  });

  if (isLoading || !data) return <CircularProgress />;

  return (
    <Stack spacing={3}>
      <Typography variant="h4">Locations</Typography>
      <Box
        sx={{
          display: 'grid',
          gap: 2,
          gridTemplateColumns: 'repeat(auto-fill, minmax(240px, 1fr))',
        }}
      >
        {data.map((loc) => (
          <Card key={loc.id}>
            <CardActionArea
              component={RouterLink}
              to={loc.type === 'Binder' ? `/binder/${loc.id}` : `/location/${loc.id}`}
            >
              {loc.coverImageUri && (
                <CardMedia
                  component="img"
                  image={loc.coverImageUri}
                  sx={{ height: 140, objectFit: 'contain', bgcolor: 'action.hover' }}
                />
              )}
              <CardContent>
                <Stack direction="row" justifyContent="space-between" alignItems="center">
                  <Typography variant="h6" noWrap>
                    {loc.name}
                  </Typography>
                  <Chip size="small" label={loc.type} />
                </Stack>
                <Typography variant="body2" color="text.secondary">
                  {loc.cardCount.toLocaleString()} cards · {loc.uniquePrintCount.toLocaleString()} unique
                </Typography>
                <Typography variant="body2">{money(loc.totalMarketValue)} market</Typography>
                {loc.isAlwaysAvailable && (
                  <Chip size="small" color="secondary" label="Always Available" sx={{ mt: 1 }} />
                )}
              </CardContent>
            </CardActionArea>
          </Card>
        ))}
      </Box>
    </Stack>
  );
}
