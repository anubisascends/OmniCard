import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
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
import { useFormatters } from '../i18n/format';

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
  const { t } = useTranslation();
  const fmt = useFormatters();
  return (
    <Paper sx={{ p: 2, flex: 1, minWidth: 320 }}>
      <Typography variant="h6" gutterBottom>
        {title}
      </Typography>
      <Table size="small">
        <TableHead>
          <TableRow>
            <TableCell>{t('dashboard.columns.group')}</TableCell>
            <TableCell align="right">{t('dashboard.columns.units')}</TableCell>
            <TableCell align="right">{t('dashboard.columns.cost')}</TableCell>
            <TableCell align="right">{t('dashboard.columns.market')}</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {rows.map((r) => (
            <TableRow key={r.key}>
              <TableCell>{r.key}</TableCell>
              <TableCell align="right">{fmt.number(r.units)}</TableCell>
              <TableCell align="right">{fmt.money(r.cost)}</TableCell>
              <TableCell align="right">{fmt.money(r.market)}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </Paper>
  );
}

export function DashboardPage() {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const { data, isLoading } = useQuery({ queryKey: ['dashboard'], queryFn: api.dashboard });

  if (isLoading || !data) return <CircularProgress />;

  return (
    <Stack spacing={3}>
      <Typography variant="h4">{t('dashboard.title')}</Typography>
      <Stack direction="row" spacing={2} flexWrap="wrap" useFlexGap>
        <Stat label={t('dashboard.stats.totalUnits')} value={fmt.number(data.totalUnits)} />
        <Stat label={t('dashboard.stats.cost')} value={fmt.money(data.totalCost)} />
        <Stat label={t('dashboard.stats.market')} value={fmt.money(data.totalMarket)} />
        <Stat
          label={t('dashboard.stats.unrealized')}
          value={fmt.money(data.unrealizedDelta)}
          color={data.unrealizedDelta >= 0 ? 'success.main' : 'error.main'}
        />
        <Stat
          label={t('dashboard.stats.realizedProfit')}
          value={fmt.money(data.realized.profit)}
          color={data.realized.profit >= 0 ? 'success.main' : 'error.main'}
        />
      </Stack>
      <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap' }}>
        <ValuationTable title={t('dashboard.tables.byGame')} rows={data.byGame} />
        <ValuationTable title={t('dashboard.tables.byCategory')} rows={data.byCategory} />
      </Box>
    </Stack>
  );
}
