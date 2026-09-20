import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link as RouterLink } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  IconButton,
  InputAdornment,
  Link,
  Paper,
  Stack,
  Tab,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Tabs,
  TextField,
  MenuItem,
  Tooltip,
  Typography,
} from '@mui/material';
import LinkOffIcon from '@mui/icons-material/LinkOff';
import AddIcon from '@mui/icons-material/Add';
import CheckIcon from '@mui/icons-material/Check';
import EditIcon from '@mui/icons-material/Edit';
import DeleteIcon from '@mui/icons-material/Delete';
import DownloadIcon from '@mui/icons-material/Download';
import { api, type CustomerFields, type ListingFields } from '../api/client';
import { useGame } from '../context/GameContext';
import type { CustomerDto, ListingDetailDto, OrderDto, WorkflowLaneDto } from '../api/types';
import { OrderDetailDrawer } from '../components/dialogs/OrderDetailDrawer';
import { useFormatters } from '../i18n/format';

function laneOf(order: OrderDto, lanes: WorkflowLaneDto[]): string {
  const byKey = lanes.find((l) => l.key === order.stageKey);
  if (byKey) return byKey.key;
  const byBehavior = lanes.find((l) => l.behavior === order.status);
  return byBehavior?.key ?? lanes[0]?.key ?? '';
}

function CreateOrderDialog({ open, onClose, onCreated }: { open: boolean; onClose: () => void; onCreated: (id: number) => void }) {
  const { t } = useTranslation();
  const customers = useQuery({ queryKey: ['customers'], queryFn: api.customers, enabled: open });
  const [customerId, setCustomerId] = useState<number | ''>('');
  const [channel, setChannel] = useState('Manual');
  const [orderNumber, setOrderNumber] = useState('');

  const create = useMutation({
    mutationFn: () =>
      api.orderCreate({ customerId: customerId as number, channel, orderNumber: orderNumber || undefined }),
    onSuccess: (o) => {
      setCustomerId('');
      setOrderNumber('');
      onCreated(o.id);
    },
  });

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>{t('sales.createOrder.title')}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <TextField
            select
            label={t('sales.createOrder.customer')}
            required
            value={customerId}
            onChange={(e) => setCustomerId(e.target.value === '' ? '' : Number(e.target.value))}
          >
            {customers.data?.map((c) => (
              <MenuItem key={c.id} value={c.id}>{c.name}</MenuItem>
            ))}
          </TextField>
          <TextField select label={t('common.labels.channel')} value={channel} onChange={(e) => setChannel(e.target.value)}>
            {['Manual', 'TcgPlayer', 'Ebay'].map((c) => (
              <MenuItem key={c} value={c}>{t(`common.channels.${c}`)}</MenuItem>
            ))}
          </TextField>
          <TextField label={t('sales.createOrder.orderNumberOptional')} value={orderNumber} onChange={(e) => setOrderNumber(e.target.value)} />
          {create.error && <Typography color="error" variant="body2">{(create.error as Error).message}</Typography>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('common.actions.cancel')}</Button>
        <Button variant="contained" disabled={customerId === '' || create.isPending} onClick={() => create.mutate()}>
          {create.isPending ? t('sales.createOrder.creating') : t('common.actions.create')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

function OrdersBoard() {
  const { t } = useTranslation();
  const fmt = useFormatters();
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
  const [detailOrderId, setDetailOrderId] = useState<number | null>(null);
  const [createOpen, setCreateOpen] = useState(false);

  if (lanesQuery.isLoading || ordersQuery.isLoading) return <CircularProgress />;
  const lanes = lanesQuery.data ?? [];
  const orders = ordersQuery.data ?? [];

  return (
    <Stack spacing={1.5} alignItems="flex-start">
      <Button variant="contained" startIcon={<AddIcon />} onClick={() => setCreateOpen(true)}>
        {t('sales.orders.newOrder')}
      </Button>
      <Box sx={{ display: 'flex', gap: 1.5, overflowX: 'auto', pb: 1, alignSelf: 'stretch' }}>
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
                  onClick={() => setDetailOrderId(o.id)}
                  sx={{ cursor: 'pointer', borderLeft: `4px solid ${lane.color}` }}
                >
                  <CardContent sx={{ p: 1.5, '&:last-child': { pb: 1.5 } }}>
                    <Typography variant="body2" fontWeight={600} noWrap>
                      {o.customerName ?? t('sales.orders.customerFallback', { id: o.customerId })}
                    </Typography>
                    <Typography variant="caption" color="text.secondary" display="block" noWrap>
                      {t(`common.channels.${o.channel}`, o.channel)}
                      {o.orderNumber ? ` · ${o.orderNumber}` : ''}
                    </Typography>
                    <Stack direction="row" justifyContent="space-between" sx={{ mt: 0.5 }}>
                      <Typography variant="caption">{t('sales.orders.itemCount', { count: o.lineItemCount })}</Typography>
                      <Typography variant="caption" fontWeight={600}>
                        {fmt.money(o.lineTotal)}
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
      <CreateOrderDialog
        open={createOpen}
        onClose={() => setCreateOpen(false)}
        onCreated={(id) => {
          setCreateOpen(false);
          qc.invalidateQueries({ queryKey: ['orders'] });
          setDetailOrderId(id);
        }}
      />
      <OrderDetailDrawer orderId={detailOrderId} onClose={() => setDetailOrderId(null)} />
    </Stack>
  );
}

const LISTING_CHANNELS = ['Manual', 'TcgPlayer', 'Ebay'];

function ListingDialog({
  open,
  initial,
  onClose,
}: {
  open: boolean;
  initial: ListingDetailDto | null;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const [fields, setFields] = useState<ListingFields>({ listedPrice: 0, channel: 'Manual', quantity: 1, note: '' });
  // The price field keeps its own text so the user can type freely; it reformats to two decimals on blur.
  const [priceText, setPriceText] = useState('0.00');

  // Reset the form whenever the dialog opens for a different listing.
  const key = initial?.id ?? 'none';
  const [formKey, setFormKey] = useState<string | number>(key);
  if (open && formKey !== key) {
    setFormKey(key);
    setFields(
      initial
        ? { listedPrice: initial.listedPrice, channel: initial.channel, quantity: initial.quantity, note: initial.note ?? '' }
        : { listedPrice: 0, channel: 'Manual', quantity: 1, note: '' },
    );
    setPriceText((initial?.listedPrice ?? 0).toFixed(2));
  }

  const save = useMutation({
    mutationFn: async () => {
      if (initial) await api.listingUpdate(initial.id, fields);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['listings'] });
      onClose();
    },
  });

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>{initial ? t('sales.listingDialog.titleNamed', { name: initial.name }) : t('sales.listingDialog.title')}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <TextField
            label={t('common.labels.price')}
            required
            value={priceText}
            onChange={(e) => {
              const text = e.target.value;
              setPriceText(text);
              const n = Number(text);
              if (Number.isFinite(n)) setFields((f) => ({ ...f, listedPrice: n }));
            }}
            onBlur={() => {
              const n = Number(priceText);
              const price = Number.isFinite(n) && n >= 0 ? n : 0;
              setPriceText(price.toFixed(2));
              setFields((f) => ({ ...f, listedPrice: price }));
            }}
            slotProps={{
              input: { startAdornment: <InputAdornment position="start">$</InputAdornment> },
              htmlInput: { inputMode: 'decimal' },
            }}
            autoFocus
          />
          <TextField
            select
            label={t('common.labels.channel')}
            value={fields.channel}
            onChange={(e) => setFields((f) => ({ ...f, channel: e.target.value }))}
          >
            {LISTING_CHANNELS.map((c) => (
              <MenuItem key={c} value={c}>{t(`common.channels.${c}`)}</MenuItem>
            ))}
          </TextField>
          <TextField
            label={t('common.labels.quantity')}
            type="number"
            required
            value={fields.quantity}
            onChange={(e) => setFields((f) => ({ ...f, quantity: Number(e.target.value) }))}
            slotProps={{ htmlInput: { min: 1, step: 1 } }}
          />
          <TextField
            label={t('common.labels.note')}
            value={fields.note ?? ''}
            onChange={(e) => setFields((f) => ({ ...f, note: e.target.value }))}
            multiline
            minRows={2}
          />
          {save.error && <Typography color="error" variant="body2">{(save.error as Error).message}</Typography>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('common.actions.cancel')}</Button>
        <Button
          variant="contained"
          disabled={fields.listedPrice < 0 || fields.quantity < 1 || save.isPending}
          onClick={() => save.mutate()}
        >
          {save.isPending ? t('common.states.saving') : t('common.actions.save')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

function ListingsTable() {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const qc = useQueryClient();
  const { game } = useGame();
  const { data, isLoading } = useQuery({ queryKey: ['listings'], queryFn: () => api.listingDetails() });
  const [editing, setEditing] = useState<ListingDetailDto | null>(null);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [pickError, setPickError] = useState<string | null>(null);
  const invalidateAfterPick = () => {
    qc.invalidateQueries({ queryKey: ['listings'] });
    qc.invalidateQueries({ queryKey: ['collection'] });
    qc.invalidateQueries({ queryKey: ['location-cards'] });
    qc.invalidateQueries({ queryKey: ['locations'] });
    qc.invalidateQueries({ queryKey: ['dashboard'] });
  };
  const unlist = useMutation({
    mutationFn: (lotId: number) => api.listingUnlist(lotId),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['listings'] }),
  });
  const pick = useMutation({
    mutationFn: (lotIds: number[]) => api.listingPick(lotIds),
    onSuccess: () => {
      setPickError(null);
      invalidateAfterPick();
    },
    onError: (e) => setPickError((e as Error).message),
  });
  if (isLoading || !data) return <CircularProgress />;

  const listedLotIds = data.filter((l) => l.status === 'Listed').map((l) => l.lotId);

  return (
    <Stack spacing={1} alignItems="flex-start">
      <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
        <Button
          variant="outlined"
          startIcon={<DownloadIcon />}
          component="a"
          href={api.pickListPdfUrl(game)}
        >
          {t('sales.listings.pickListPdf')}
        </Button>
        <Button
          variant="outlined"
          startIcon={<CheckIcon />}
          disabled={listedLotIds.length === 0 || pick.isPending}
          onClick={() => pick.mutate(listedLotIds)}
        >
          {t('sales.listings.markAllPicked', { count: listedLotIds.length })}
        </Button>
      </Stack>
      {pickError && (
        <Alert severity="error" onClose={() => setPickError(null)} sx={{ alignSelf: 'stretch' }}>
          {pickError} — {t('sales.listings.setSalesLocationIn')}{' '}
          <Link component={RouterLink} to="/settings">{t('sales.listings.settings')}</Link>.
        </Alert>
      )}
      <Paper variant="outlined" sx={{ alignSelf: 'stretch' }}>
      <Table size="small">
        <TableHead>
          <TableRow>
            <TableCell>{t('common.labels.name')}</TableCell>
            <TableCell>{t('common.labels.set')}</TableCell>
            <TableCell>{t('sales.listings.cond')}</TableCell>
            <TableCell>{t('common.labels.channel')}</TableCell>
            <TableCell align="right">{t('sales.listings.qty')}</TableCell>
            <TableCell align="right">{t('common.labels.price')}</TableCell>
            <TableCell>{t('common.labels.status')}</TableCell>
            <TableCell align="right"></TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {data.map((l) => (
            <TableRow key={l.id} hover>
              <TableCell>
                {l.name}
                {l.isFoil ? ' ✦' : ''}
              </TableCell>
              <TableCell>{l.setCode}</TableCell>
              <TableCell>{l.condition ? t(`common.conditions.${l.condition}`, l.condition) : ''}</TableCell>
              <TableCell>{t(`common.channels.${l.channel}`, l.channel)}</TableCell>
              <TableCell align="right">{l.quantity}</TableCell>
              <TableCell align="right">{fmt.money(l.listedPrice)}</TableCell>
              <TableCell>
                <Chip size="small" label={l.status} />
              </TableCell>
              <TableCell align="right">
                {l.status === 'Listed' && (
                  <Tooltip title={t('sales.listings.markPicked')}>
                    <IconButton size="small" disabled={pick.isPending} onClick={() => pick.mutate([l.lotId])}>
                      <CheckIcon fontSize="small" />
                    </IconButton>
                  </Tooltip>
                )}
                <Tooltip title={t('common.actions.edit')}>
                  <IconButton
                    size="small"
                    onClick={() => {
                      setEditing(l);
                      setDialogOpen(true);
                    }}
                  >
                    <EditIcon fontSize="small" />
                  </IconButton>
                </Tooltip>
                <Tooltip title={t('sales.listings.unlist')}>
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
      <ListingDialog open={dialogOpen} initial={editing} onClose={() => setDialogOpen(false)} />
    </Stack>
  );
}

const EMPTY_CUSTOMER: CustomerFields = {
  name: '', email: '', phone: '',
  addressLine1: '', addressLine2: '', city: '', state: '', postalCode: '', country: '',
};

function CustomerDialog({
  open,
  initial,
  onClose,
}: {
  open: boolean;
  initial: CustomerDto | null;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const [fields, setFields] = useState<CustomerFields>(EMPTY_CUSTOMER);

  // Reset the form whenever the dialog opens for a different customer (or for "new").
  const key = initial?.id ?? 'new';
  const [formKey, setFormKey] = useState<string | number>(key);
  if (open && formKey !== key) {
    setFormKey(key);
    setFields(
      initial
        ? {
            name: initial.name,
            email: initial.email ?? '',
            phone: initial.phone ?? '',
            addressLine1: initial.addressLine1 ?? '',
            addressLine2: initial.addressLine2 ?? '',
            city: initial.city ?? '',
            state: initial.state ?? '',
            postalCode: initial.postalCode ?? '',
            country: initial.country ?? '',
          }
        : EMPTY_CUSTOMER,
    );
  }

  const save = useMutation({
    mutationFn: async () => {
      if (initial) await api.customerUpdate(initial.id, fields);
      else await api.customerCreate(fields);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['customers'] });
      onClose();
    },
  });

  const set = (k: keyof CustomerFields) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setFields((f) => ({ ...f, [k]: e.target.value }));

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>{initial ? t('sales.customers.editCustomer') : t('sales.customers.newCustomer')}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <TextField label={t('common.labels.name')} required value={fields.name} onChange={set('name')} autoFocus />
          <TextField label={t('common.labels.email')} value={fields.email ?? ''} onChange={set('email')} />
          <TextField label={t('common.labels.phone')} value={fields.phone ?? ''} onChange={set('phone')} />
          <TextField label={t('sales.customers.addressLine1')} value={fields.addressLine1 ?? ''} onChange={set('addressLine1')} fullWidth />
          <TextField label={t('sales.customers.addressLine2')} value={fields.addressLine2 ?? ''} onChange={set('addressLine2')} fullWidth />
          <Stack direction="row" spacing={2}>
            <TextField label={t('common.labels.city')} value={fields.city ?? ''} onChange={set('city')} fullWidth />
            <TextField label={t('common.labels.state')} value={fields.state ?? ''} onChange={set('state')} sx={{ width: 100 }} />
          </Stack>
          <Stack direction="row" spacing={2}>
            <TextField label={t('sales.customers.postalCode')} value={fields.postalCode ?? ''} onChange={set('postalCode')} sx={{ width: 140 }} />
            <TextField label={t('sales.customers.country')} value={fields.country ?? ''} onChange={set('country')} fullWidth />
          </Stack>
          {save.error && <Typography color="error" variant="body2">{(save.error as Error).message}</Typography>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('common.actions.cancel')}</Button>
        <Button
          variant="contained"
          disabled={!fields.name.trim() || save.isPending}
          onClick={() => save.mutate()}
        >
          {save.isPending ? t('common.states.saving') : t('common.actions.save')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

function CustomersTable() {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const { data, isLoading } = useQuery({ queryKey: ['customers'], queryFn: api.customers });
  const [editing, setEditing] = useState<CustomerDto | null>(null);
  const [dialogOpen, setDialogOpen] = useState(false);

  const del = useMutation({
    mutationFn: (id: number) => api.customerDelete(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['customers'] }),
  });

  if (isLoading || !data) return <CircularProgress />;
  return (
    <Stack spacing={1} alignItems="flex-start">
      <Button
        startIcon={<AddIcon />}
        variant="outlined"
        onClick={() => {
          setEditing(null);
          setDialogOpen(true);
        }}
      >
        {t('sales.customers.newCustomer')}
      </Button>
      <Paper variant="outlined" sx={{ alignSelf: 'stretch' }}>
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>{t('common.labels.name')}</TableCell>
              <TableCell>{t('common.labels.email')}</TableCell>
              <TableCell>{t('common.labels.phone')}</TableCell>
              <TableCell>{t('common.labels.location')}</TableCell>
              <TableCell align="right"></TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {data.map((c) => (
              <TableRow key={c.id} hover>
                <TableCell>{c.name}</TableCell>
                <TableCell>{c.email}</TableCell>
                <TableCell>{c.phone}</TableCell>
                <TableCell>{[c.city, c.state].filter(Boolean).join(', ')}</TableCell>
                <TableCell align="right">
                  <IconButton
                    size="small"
                    onClick={() => {
                      setEditing(c);
                      setDialogOpen(true);
                    }}
                  >
                    <EditIcon fontSize="small" />
                  </IconButton>
                  <IconButton
                    size="small"
                    onClick={() => {
                      if (confirm(t('sales.customers.deleteConfirm', { name: c.name }))) del.mutate(c.id);
                    }}
                  >
                    <DeleteIcon fontSize="small" />
                  </IconButton>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </Paper>
      <CustomerDialog open={dialogOpen} initial={editing} onClose={() => setDialogOpen(false)} />
    </Stack>
  );
}

export function SalesPage() {
  const { t } = useTranslation();
  const [tab, setTab] = useState(0);
  return (
    <Stack spacing={2}>
      <Typography variant="h4">{t('sales.title')}</Typography>
      <Tabs value={tab} onChange={(_, v) => setTab(v)}>
        <Tab label={t('sales.tabs.orders')} />
        <Tab label={t('sales.tabs.listings')} />
        <Tab label={t('sales.tabs.customers')} />
      </Tabs>
      {tab === 0 && <OrdersBoard />}
      {tab === 1 && <ListingsTable />}
      {tab === 2 && <CustomersTable />}
    </Stack>
  );
}
