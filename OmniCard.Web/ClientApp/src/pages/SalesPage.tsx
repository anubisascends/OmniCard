import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Box,
  Card,
  CardContent,
  Chip,
  CircularProgress,
  IconButton,
  Paper,
  Stack,
  Tab,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Tabs,
  Tooltip,
  Typography,
} from '@mui/material';
import LinkOffIcon from '@mui/icons-material/LinkOff';
import { api } from '../api/client';
import type { OrderDto, WorkflowLaneDto } from '../api/types';

const money = (n: number) => n.toLocaleString(undefined, { style: 'currency', currency: 'USD' });

function laneOf(order: OrderDto, lanes: WorkflowLaneDto[]): string {
  const byKey = lanes.find((l) => l.key === order.stageKey);
  if (byKey) return byKey.key;
  const byBehavior = lanes.find((l) => l.behavior === order.status);
  return byBehavior?.key ?? lanes[0]?.key ?? '';
}

function OrdersBoard() {
  const qc = useQueryClient();
  const lanesQuery = useQuery({ queryKey: ['order-lanes'], queryFn: api.orderLanes });
  const ordersQuery = useQuery({ queryKey: ['orders'], queryFn: api.orders });

  const setStatus = useMutation({
    mutationFn: ({ id, status, stageKey }: { id: number; status: string; stageKey: string }) =>
      api.orderSetStatus(id, status, stageKey),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['orders'] });
      qc.invalidateQueries({ queryKey: ['dashboard'] });
    },
  });

  const [dragId, setDragId] = useState<number | null>(null);

  if (lanesQuery.isLoading || ordersQuery.isLoading) return <CircularProgress />;
  const lanes = lanesQuery.data ?? [];
  const orders = ordersQuery.data ?? [];

  return (
    <Box sx={{ display: 'flex', gap: 1.5, overflowX: 'auto', pb: 1 }}>
      {lanes.map((lane) => {
        const laneOrders = orders.filter((o) => laneOf(o, lanes) === lane.key);
        return (
          <Paper
            key={lane.key}
            variant="outlined"
            onDragOver={(e) => e.preventDefault()}
            onDrop={() => {
              if (dragId != null) {
                setStatus.mutate({ id: dragId, status: lane.behavior, stageKey: lane.key });
                setDragId(null);
              }
            }}
            sx={{ minWidth: 240, width: 240, flexShrink: 0, p: 1, bgcolor: 'background.default' }}
          >
            <Stack direction="row" alignItems="center" spacing={1} sx={{ mb: 1 }}>
              <Box sx={{ width: 10, height: 10, borderRadius: '50%', bgcolor: lane.color }} />
              <Typography variant="subtitle2">{lane.name}</Typography>
              <Chip size="small" label={laneOrders.length} />
            </Stack>
            <Stack spacing={1}>
              {laneOrders.map((o) => (
                <Card
                  key={o.id}
                  draggable
                  onDragStart={() => setDragId(o.id)}
                  sx={{ cursor: 'grab', borderLeft: `4px solid ${lane.color}` }}
                >
                  <CardContent sx={{ p: 1.5, '&:last-child': { pb: 1.5 } }}>
                    <Typography variant="body2" fontWeight={600} noWrap>
                      {o.customerName ?? `Customer #${o.customerId}`}
                    </Typography>
                    <Typography variant="caption" color="text.secondary" display="block" noWrap>
                      {o.channel}
                      {o.orderNumber ? ` · ${o.orderNumber}` : ''}
                    </Typography>
                    <Stack direction="row" justifyContent="space-between" sx={{ mt: 0.5 }}>
                      <Typography variant="caption">{o.lineItemCount} item(s)</Typography>
                      <Typography variant="caption" fontWeight={600}>
                        {money(o.lineTotal)}
                      </Typography>
                    </Stack>
                  </CardContent>
                </Card>
              ))}
            </Stack>
          </Paper>
        );
      })}
    </Box>
  );
}

function ListingsTable() {
  const qc = useQueryClient();
  const { data, isLoading } = useQuery({ queryKey: ['listings'], queryFn: () => api.listings() });
  const unlist = useMutation({
    mutationFn: (lotId: number) => api.listingUnlist(lotId),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['listings'] }),
  });
  if (isLoading || !data) return <CircularProgress />;

  return (
    <Paper variant="outlined">
      <Table size="small">
        <TableHead>
          <TableRow>
            <TableCell>Name</TableCell>
            <TableCell>Set</TableCell>
            <TableCell>Cond</TableCell>
            <TableCell align="right">Price</TableCell>
            <TableCell>Status</TableCell>
            <TableCell align="right"></TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {data.map((l) => (
            <TableRow key={l.lotId} hover>
              <TableCell>
                {l.name}
                {l.isFoil ? ' ✦' : ''}
              </TableCell>
              <TableCell>{l.setCode}</TableCell>
              <TableCell>{l.condition}</TableCell>
              <TableCell align="right">{money(l.listedPrice)}</TableCell>
              <TableCell>
                <Chip size="small" label={l.status} />
              </TableCell>
              <TableCell align="right">
                <Tooltip title="Unlist">
                  <IconButton size="small" onClick={() => unlist.mutate(l.lotId)}>
                    <LinkOffIcon fontSize="small" />
                  </IconButton>
                </Tooltip>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </Paper>
  );
}

function CustomersTable() {
  const { data, isLoading } = useQuery({ queryKey: ['customers'], queryFn: api.customers });
  if (isLoading || !data) return <CircularProgress />;
  return (
    <Paper variant="outlined">
      <Table size="small">
        <TableHead>
          <TableRow>
            <TableCell>Name</TableCell>
            <TableCell>Email</TableCell>
            <TableCell>Phone</TableCell>
            <TableCell>Location</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {data.map((c) => (
            <TableRow key={c.id} hover>
              <TableCell>{c.name}</TableCell>
              <TableCell>{c.email}</TableCell>
              <TableCell>{c.phone}</TableCell>
              <TableCell>{[c.city, c.state].filter(Boolean).join(', ')}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </Paper>
  );
}

export function SalesPage() {
  const [tab, setTab] = useState(0);
  return (
    <Stack spacing={2}>
      <Typography variant="h4">Sales</Typography>
      <Tabs value={tab} onChange={(_, v) => setTab(v)}>
        <Tab label="Orders" />
        <Tab label="Listings" />
        <Tab label="Customers" />
      </Tabs>
      {tab === 0 && <OrdersBoard />}
      {tab === 1 && <ListingsTable />}
      {tab === 2 && <CustomersTable />}
    </Stack>
  );
}
