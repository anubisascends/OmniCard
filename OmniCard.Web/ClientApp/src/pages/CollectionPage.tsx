import { useMemo, useState } from 'react';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { Box, Stack, TextField, Typography } from '@mui/material';
import { DataGrid, type GridColDef, type GridPaginationModel } from '@mui/x-data-grid';
import { api } from '../api/client';
import type { CardDto } from '../api/types';
import { useGame } from '../context/GameContext';

const money = (n: number) => n.toLocaleString(undefined, { style: 'currency', currency: 'USD' });

const columns: GridColDef<CardDto>[] = [
  { field: 'name', headerName: 'Name', flex: 2, minWidth: 200 },
  { field: 'setCode', headerName: 'Set', width: 90 },
  { field: 'number', headerName: 'No.', width: 80 },
  { field: 'rarity', headerName: 'Rarity', width: 90 },
  { field: 'condition', headerName: 'Cond', width: 80 },
  {
    field: 'isFoil',
    headerName: 'Foil',
    width: 70,
    type: 'boolean',
  },
  {
    field: 'marketPrice',
    headerName: 'Market',
    width: 110,
    valueFormatter: (v: number) => (v ? money(v) : ''),
    align: 'right',
    headerAlign: 'right',
  },
  { field: 'containerName', headerName: 'Location', flex: 1, minWidth: 120 },
];

export function CollectionPage() {
  const { game } = useGame();
  const [search, setSearch] = useState('');
  const [q, setQ] = useState('');
  const [pagination, setPagination] = useState<GridPaginationModel>({ page: 0, pageSize: 50 });

  const query = useQuery({
    queryKey: ['collection', game, q, pagination.page, pagination.pageSize],
    queryFn: () =>
      api.collection({
        game,
        q,
        skip: pagination.page * pagination.pageSize,
        take: pagination.pageSize,
      }),
    placeholderData: keepPreviousData,
  });

  const rows = useMemo(() => query.data?.items ?? [], [query.data]);

  return (
    <Stack spacing={2} sx={{ height: 'calc(100vh - 120px)' }}>
      <Typography variant="h4">Collection</Typography>
      <Box
        component="form"
        onSubmit={(e) => {
          e.preventDefault();
          setPagination((p) => ({ ...p, page: 0 }));
          setQ(search);
        }}
      >
        <TextField
          fullWidth
          size="small"
          placeholder="Search — try name, set:dom, cn:123, c:u, r:rare, is:foil, tag:trade"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
      </Box>
      <DataGrid
        rows={rows}
        columns={columns}
        rowCount={query.data?.total ?? 0}
        loading={query.isFetching}
        paginationMode="server"
        paginationModel={pagination}
        onPaginationModelChange={setPagination}
        pageSizeOptions={[25, 50, 100]}
        disableRowSelectionOnClick
        density="compact"
      />
    </Stack>
  );
}
