import { useState } from 'react';
import { useParams, Link as RouterLink } from 'react-router-dom';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { Box, Breadcrumbs, Chip, Link, Stack, Typography } from '@mui/material';
import { DataGrid, type GridColDef, type GridPaginationModel } from '@mui/x-data-grid';
import { api } from '../api/client';
import type { CardDto } from '../api/types';
import { useGame } from '../context/GameContext';

const money = (n: number) => n.toLocaleString(undefined, { style: 'currency', currency: 'USD' });

const columns: GridColDef<CardDto>[] = [
  { field: 'name', headerName: 'Name', flex: 2, minWidth: 200 },
  { field: 'setCode', headerName: 'Set', width: 90 },
  { field: 'number', headerName: 'No.', width: 80 },
  { field: 'condition', headerName: 'Cond', width: 80 },
  { field: 'isFoil', headerName: 'Foil', width: 70, type: 'boolean' },
  {
    field: 'marketPrice',
    headerName: 'Market',
    width: 110,
    align: 'right',
    headerAlign: 'right',
    valueFormatter: (v: number) => (v ? money(v) : ''),
  },
];

export function LocationDetailPage() {
  const { id } = useParams();
  const locationId = Number(id);
  const { game } = useGame();
  const [pagination, setPagination] = useState<GridPaginationModel>({ page: 0, pageSize: 50 });

  const locQuery = useQuery({
    queryKey: ['location', locationId],
    queryFn: () => api.location(locationId),
  });

  const cardsQuery = useQuery({
    queryKey: ['location-cards', locationId, game, pagination.page, pagination.pageSize],
    queryFn: () =>
      api.collection({
        game,
        containerId: locationId,
        skip: pagination.page * pagination.pageSize,
        take: pagination.pageSize,
      }),
    placeholderData: keepPreviousData,
  });

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
      <Box sx={{ flexGrow: 1 }}>
        <DataGrid
          rows={cardsQuery.data?.items ?? []}
          columns={columns}
          rowCount={cardsQuery.data?.total ?? 0}
          loading={cardsQuery.isFetching}
          paginationMode="server"
          paginationModel={pagination}
          onPaginationModelChange={setPagination}
          pageSizeOptions={[25, 50, 100]}
          disableRowSelectionOnClick
          density="compact"
        />
      </Box>
    </Stack>
  );
}
