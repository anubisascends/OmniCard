import { useQuery } from '@tanstack/react-query';
import { Box, Card, CardContent, CircularProgress, Stack, Typography } from '@mui/material';
import { DataGrid, type GridColDef } from '@mui/x-data-grid';
import { api } from '../api/client';
import type { ProductDto } from '../api/types';
import { useGame } from '../context/GameContext';

const money = (n?: number | null) =>
  n == null ? '' : n.toLocaleString(undefined, { style: 'currency', currency: 'USD' });

const columns: GridColDef<ProductDto>[] = [
  { field: 'name', headerName: 'Product', flex: 2, minWidth: 240 },
  { field: 'category', headerName: 'Type', width: 110 },
  { field: 'setCode', headerName: 'Set', width: 90 },
  { field: 'totalQuantity', headerName: 'Qty', width: 80, type: 'number' },
  {
    field: 'lastMarketPrice',
    headerName: 'Market',
    width: 120,
    align: 'right',
    headerAlign: 'right',
    valueFormatter: (v: number | null) => money(v),
  },
  { field: 'upc', headerName: 'UPC', width: 140 },
];

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <Card sx={{ minWidth: 160, flex: 1 }}>
      <CardContent>
        <Typography variant="overline" color="text.secondary">
          {label}
        </Typography>
        <Typography variant="h5">{value}</Typography>
      </CardContent>
    </Card>
  );
}

export function InventoryPage() {
  const { game } = useGame();
  const products = useQuery({
    queryKey: ['inventory-products', game],
    queryFn: () => api.inventoryProducts(game),
  });
  const valuation = useQuery({ queryKey: ['inventory-valuation'], queryFn: api.inventoryValuation });

  return (
    <Stack spacing={2} sx={{ height: 'calc(100vh - 120px)' }}>
      <Typography variant="h4">Sealed Inventory</Typography>
      <Stack direction="row" spacing={2}>
        <Stat label="Total Units" value={valuation.data?.totalUnits.toLocaleString() ?? '…'} />
        <Stat label="Cost" value={valuation.data ? money(valuation.data.totalCost) : '…'} />
        <Stat label="Market" value={valuation.data ? money(valuation.data.totalMarket) : '…'} />
      </Stack>
      <Box sx={{ flexGrow: 1 }}>
        {products.isLoading || !products.data ? (
          <CircularProgress />
        ) : (
          <DataGrid rows={products.data} columns={columns} density="compact" disableRowSelectionOnClick />
        )}
      </Box>
    </Stack>
  );
}
