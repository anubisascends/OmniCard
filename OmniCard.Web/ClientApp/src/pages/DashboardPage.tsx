import { useQuery } from '@tanstack/react-query';
import {
  Box,
  Card,
  CardContent,
  CircularProgress,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material';
import { api } from '../api/client';
import type { ValuationLineDto } from '../api/types';

const money = (n: number) =>
  n.toLocaleString(undefined, { style: 'currency', currency: 'USD' });

function Stat({ label, value, color }: { label: string; value: string; color?: string }) {
  return (
    <Card sx={{ minWidth: 180, flex: 1 }}>
      <CardContent>
        <Typography variant="overline" color="text.secondary">
          {label}
        </Typography>
        <Typography variant="h5" sx={{ color }}>
          {value}
        </Typography>
      </CardContent>
    </Card>
  );
}

function ValuationTable({ title, rows }: { title: string; rows: ValuationLineDto[] }) {
  return (
    <Paper sx={{ p: 2, flex: 1, minWidth: 320 }}>
      <Typography variant="h6" gutterBottom>
        {title}
      </Typography>
      <Table size="small">
        <TableHead>
          <TableRow>
            <TableCell>Group</TableCell>
            <TableCell align="right">Units</TableCell>
            <TableCell align="right">Cost</TableCell>
            <TableCell align="right">Market</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {rows.map((r) => (
            <TableRow key={r.key}>
              <TableCell>{r.key}</TableCell>
              <TableCell align="right">{r.units.toLocaleString()}</TableCell>
              <TableCell align="right">{money(r.cost)}</TableCell>
              <TableCell align="right">{money(r.market)}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </Paper>
  );
}

export function DashboardPage() {
  const { data, isLoading } = useQuery({ queryKey: ['dashboard'], queryFn: api.dashboard });

  if (isLoading || !data) return <CircularProgress />;

  return (
    <Stack spacing={3}>
      <Typography variant="h4">Dashboard</Typography>
      <Stack direction="row" spacing={2} flexWrap="wrap" useFlexGap>
        <Stat label="Total Units" value={data.totalUnits.toLocaleString()} />
        <Stat label="Cost" value={money(data.totalCost)} />
        <Stat label="Market" value={money(data.totalMarket)} />
        <Stat
          label="Unrealized"
          value={money(data.unrealizedDelta)}
          color={data.unrealizedDelta >= 0 ? 'success.main' : 'error.main'}
        />
        <Stat
          label="Realized Profit"
          value={money(data.realized.profit)}
          color={data.realized.profit >= 0 ? 'success.main' : 'error.main'}
        />
      </Stack>
      <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap' }}>
        <ValuationTable title="By Game" rows={data.byGame} />
        <ValuationTable title="By Category" rows={data.byCategory} />
      </Box>
    </Stack>
  );
}
