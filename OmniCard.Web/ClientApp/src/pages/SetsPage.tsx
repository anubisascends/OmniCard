import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import {
  Alert,
  Autocomplete,
  Box,
  Chip,
  LinearProgress,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import { DataGrid, type GridColDef } from '@mui/x-data-grid';
import { api } from '../api/client';
import type { SetChecklistCardDto, SetInfoDto } from '../api/types';
import { useGame } from '../context/GameContext';

const money = (n?: number | null) =>
  n == null ? '' : n.toLocaleString(undefined, { style: 'currency', currency: 'USD' });

const columns: GridColDef<SetChecklistCardDto>[] = [
  { field: 'collectorNumber', headerName: 'No.', width: 80 },
  { field: 'name', headerName: 'Name', flex: 2, minWidth: 200 },
  { field: 'rarity', headerName: 'Rarity', width: 110 },
  {
    field: 'ownedQuantity',
    headerName: 'Owned',
    width: 100,
    renderCell: (p) =>
      p.value > 0 ? <Chip size="small" color="success" label={`×${p.value}`} /> : '—',
  },
  {
    field: 'normalPrice',
    headerName: 'Normal',
    width: 100,
    align: 'right',
    headerAlign: 'right',
    valueFormatter: (v: number | null) => money(v),
  },
  {
    field: 'foilPrice',
    headerName: 'Foil',
    width: 100,
    align: 'right',
    headerAlign: 'right',
    valueFormatter: (v: number | null) => money(v),
  },
];

export function SetsPage() {
  const { game } = useGame();
  const [set, setSet] = useState<SetInfoDto | null>(null);

  const setsQuery = useQuery({
    queryKey: ['sets', game],
    queryFn: () => api.sets(game!),
    enabled: !!game,
  });

  const checklistQuery = useQuery({
    queryKey: ['set-checklist', game, set?.setCode],
    queryFn: () => api.setChecklist(game!, set!.setCode),
    enabled: !!game && !!set,
  });

  if (!game) {
    return (
      <Stack spacing={2}>
        <Typography variant="h4">Sets</Typography>
        <Alert severity="info">Pick a game in the top bar to browse its sets.</Alert>
      </Stack>
    );
  }

  const checklist = checklistQuery.data;

  return (
    <Stack spacing={2} sx={{ height: 'calc(100vh - 120px)' }}>
      <Typography variant="h4">Sets</Typography>
      <Autocomplete
        options={setsQuery.data ?? []}
        loading={setsQuery.isLoading}
        getOptionLabel={(o) => `${o.setName} (${o.setCode})`}
        value={set}
        onChange={(_, v) => setSet(v)}
        sx={{ maxWidth: 480 }}
        renderInput={(params) => <TextField {...params} label="Set" size="small" />}
      />

      {checklist && (
        <Box>
          <Typography variant="subtitle1">
            {checklist.setName} — {checklist.ownedCount}/{checklist.totalCount} owned (
            {checklist.completionPercent.toFixed(1)}%)
          </Typography>
          <LinearProgress
            variant="determinate"
            value={Math.min(100, checklist.completionPercent)}
            sx={{ my: 1, height: 8, borderRadius: 1 }}
          />
        </Box>
      )}

      {set && (
        <DataGrid
          rows={checklist?.cards ?? []}
          getRowId={(r) => r.gameCardId}
          columns={columns}
          loading={checklistQuery.isFetching}
          density="compact"
          disableRowSelectionOnClick
          getRowClassName={(p) => (p.row.ownedQuantity > 0 ? '' : 'unowned-row')}
          sx={{ '& .unowned-row': { opacity: 0.55 } }}
        />
      )}
    </Stack>
  );
}
