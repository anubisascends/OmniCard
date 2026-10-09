import { lazy, Suspense, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Divider,
  Drawer,
  IconButton,
  InputAdornment,
  MenuItem,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import DeleteIcon from '@mui/icons-material/Delete';
import ReceiptLongIcon from '@mui/icons-material/ReceiptLong';
import QrCodeScannerIcon from '@mui/icons-material/QrCodeScanner';
import { api } from '../../api/client';
import { useGame } from '../../context/GameContext';
import { useFormatters } from '../../i18n/format';

const CHANNELS = ['Manual', 'TcgPlayer', 'Ebay'];

// The barcode decoder is sizeable — only fetched when a label scan is opened.
const BarcodeScanner = lazy(() => import('../BarcodeScanner').then((m) => ({ default: m.BarcodeScanner })));

interface HeaderForm {
  channel: string;
  orderNumber: string;
  trackingNumber: string;
  carrier: string;
  shippingChargedToBuyer: number;
  shippingCost: number;
  marketplaceFees: number;
  notes: string;
}

/** Add-line search: find an owned single by name, set a sale price, add it to the order. */
function AddLineSearch({ orderId, onAdded }: { orderId: number; onAdded: () => void }) {
  const { t } = useTranslation();
  const { game } = useGame();
  const [q, setQ] = useState('');
  const search = useQuery({
    queryKey: ['order-addline-search', game, q],
    queryFn: () => api.collection({ game, q, take: 8 }),
    enabled: q.trim().length >= 2,
  });
  const add = useMutation({
    mutationFn: ({ lotId, price }: { lotId: number; price: number }) => api.orderAddLine(orderId, lotId, price),
    onSuccess: onAdded,
  });

  return (
    <Box>
      <TextField
        size="small"
        fullWidth
        label={t('sales.orderDetail.addCard')}
        value={q}
        onChange={(e) => setQ(e.target.value)}
      />
      {search.data && search.data.items.length > 0 && (
        <Stack spacing={0.5} sx={{ mt: 1, maxHeight: 220, overflowY: 'auto' }}>
          {search.data.items.map((c) => (
            <AddLineRow key={c.id} card={c} onAdd={(price) => add.mutate({ lotId: c.id, price })} />
          ))}
        </Stack>
      )}
    </Box>
  );
}

function AddLineRow({
  card,
  onAdd,
}: {
  card: { id: number; name: string; setCode: string; condition: string; marketPrice: number };
  onAdd: (price: number) => void;
}) {
  const [price, setPrice] = useState(card.marketPrice || 0);
  return (
    <Stack direction="row" spacing={1} alignItems="center">
      <Typography variant="body2" sx={{ flexGrow: 1 }} noWrap>
        {card.name} · {card.setCode?.toUpperCase()} · {card.condition}
      </Typography>
      <TextField
        size="small"
        type="number"
        value={price}
        onChange={(e) => setPrice(Number(e.target.value))}
        sx={{ width: 90 }}
      />
      <IconButton size="small" color="primary" onClick={() => onAdd(price)}>
        <AddIcon fontSize="small" />
      </IconButton>
    </Stack>
  );
}

/** Camera dialog that reads a shipping-label barcode and hands back the scan. */
function ScanLabelDialog({ open, onClose, onCode }: { open: boolean; onClose: () => void; onCode: (code: string) => void }) {
  const { t } = useTranslation();
  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>{t('sales.orderDetail.scanLabelTitle')}</DialogTitle>
      <DialogContent>
        {open && (
          <Suspense fallback={<CircularProgress />}>
            <BarcodeScanner onCode={onCode} height={280} />
          </Suspense>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('common.actions.cancel')}</Button>
      </DialogActions>
    </Dialog>
  );
}

export function OrderDetailDrawer({ orderId, onClose }: { orderId: number | null; onClose: () => void }) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const qc = useQueryClient();
  const open = orderId != null;
  const detail = useQuery({
    queryKey: ['order', orderId],
    queryFn: () => api.order(orderId!),
    enabled: open,
  });

  const [form, setForm] = useState<HeaderForm | null>(null);
  const [scanOpen, setScanOpen] = useState(false);
  const [trackingWarning, setTrackingWarning] = useState<string | null>(null);
  useEffect(() => {
    if (detail.data) {
      const o = detail.data.order;
      setTrackingWarning(null);
      setForm({
        channel: o.channel,
        orderNumber: o.orderNumber ?? '',
        trackingNumber: o.trackingNumber ?? '',
        carrier: o.carrier ?? '',
        shippingChargedToBuyer: o.shippingChargedToBuyer,
        shippingCost: o.shippingCost,
        marketplaceFees: o.marketplaceFees,
        notes: o.notes ?? '',
      });
    }
  }, [detail.data]);

  const invalidate = () => {
    qc.invalidateQueries({ queryKey: ['order', orderId] });
    qc.invalidateQueries({ queryKey: ['orders'] });
    qc.invalidateQueries({ queryKey: ['dashboard'] });
  };

  // A scanned label barcode carries carrier routing data around the tracking number — let the server
  // extract it (the same rules the Ship page matches with), and flag it if another order already has it.
  const onLabelScanned = async (code: string) => {
    setScanOpen(false);
    try {
      const result = await api.orderShipScan(code, false);
      setForm((f) => (f ? { ...f, trackingNumber: result.tracking } : f));
      const others = result.orders.filter((o) => o.id !== orderId);
      setTrackingWarning(
        others.length > 0
          ? t('sales.orderDetail.trackingInUse', {
              orders: others.map((o) => t('sales.orderDetail.orderTitle', { id: o.id })).join(', '),
            })
          : null,
      );
    } catch (e) {
      setTrackingWarning((e as Error).message);
    }
  };

  const save = useMutation({
    mutationFn: () => api.orderUpdate(orderId!, { ...form!, orderNumber: form!.orderNumber || null, notes: form!.notes || null }),
    onSuccess: invalidate,
  });
  const removeLine = useMutation({
    mutationFn: (lineId: number) => api.orderRemoveLine(lineId),
    onSuccess: invalidate,
  });
  const del = useMutation({
    mutationFn: () => api.orderDelete(orderId!),
    onSuccess: () => {
      invalidate();
      onClose();
    },
  });

  const status = detail.data?.order.status;
  const editable = status === 'Created' || status === 'Packed';

  const set = (k: keyof HeaderForm) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setForm((f) => (f ? { ...f, [k]: e.target.value } : f));
  const setNum = (k: keyof HeaderForm) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setForm((f) => (f ? { ...f, [k]: Number(e.target.value) } : f));

  return (
    <Drawer anchor="right" open={open} onClose={onClose}>
      <Box sx={{ width: { xs: '100vw', sm: 460 }, maxWidth: '100vw', p: 2 }}>
        {detail.isLoading || !detail.data || !form ? (
          <CircularProgress />
        ) : (
          <Stack spacing={2}>
            <Stack direction="row" alignItems="center" spacing={1}>
              <Typography variant="h6" sx={{ flexGrow: 1 }}>
                {t('sales.orderDetail.orderTitle', { id: detail.data.order.id })}
              </Typography>
              <Chip size="small" label={status} />
            </Stack>
            <Typography variant="body2" color="text.secondary">
              {detail.data.order.customerName ?? t('sales.orders.customerFallback', { id: detail.data.order.customerId })}
            </Typography>

            {!editable && (
              <Alert severity="info">
                {t('sales.orderDetail.locked', { status: status?.toLowerCase() })}
              </Alert>
            )}

            {/* Header */}
            <Stack direction="row" spacing={2}>
              <TextField select size="small" label={t('common.labels.channel')} value={form.channel} onChange={set('channel')} disabled={!editable} sx={{ minWidth: 130 }}>
                {CHANNELS.map((c) => (
                  <MenuItem key={c} value={c}>{t(`common.channels.${c}`)}</MenuItem>
                ))}
              </TextField>
              <TextField size="small" label={t('sales.orderDetail.orderNumberShort')} value={form.orderNumber} onChange={set('orderNumber')} disabled={!editable} fullWidth />
            </Stack>
            <Stack direction="row" spacing={2}>
              <TextField
                size="small"
                label={t('sales.orderDetail.tracking')}
                value={form.trackingNumber}
                onChange={set('trackingNumber')}
                disabled={!editable}
                fullWidth
                InputProps={{
                  endAdornment: editable && (
                    <InputAdornment position="end">
                      <Tooltip title={t('sales.orderDetail.scanLabel')}>
                        <IconButton size="small" edge="end" onClick={() => setScanOpen(true)}>
                          <QrCodeScannerIcon fontSize="small" />
                        </IconButton>
                      </Tooltip>
                    </InputAdornment>
                  ),
                }}
              />
              <TextField size="small" label={t('sales.orderDetail.carrier')} value={form.carrier} onChange={set('carrier')} disabled={!editable} sx={{ width: 130 }} />
            </Stack>
            {trackingWarning && <Alert severity="warning">{trackingWarning}</Alert>}
            <Stack direction="row" spacing={2}>
              <TextField size="small" type="number" label={t('sales.orderDetail.shipCharged')} value={form.shippingChargedToBuyer} onChange={setNum('shippingChargedToBuyer')} disabled={!editable} />
              <TextField size="small" type="number" label={t('sales.orderDetail.shipCost')} value={form.shippingCost} onChange={setNum('shippingCost')} disabled={!editable} />
              <TextField size="small" type="number" label={t('sales.orderDetail.fees')} value={form.marketplaceFees} onChange={setNum('marketplaceFees')} disabled={!editable} />
            </Stack>
            <TextField size="small" label={t('common.labels.notes')} value={form.notes} onChange={set('notes')} disabled={!editable} multiline minRows={2} />
            {editable && (
              <Button variant="contained" disabled={save.isPending} onClick={() => save.mutate()}>
                {save.isPending ? t('common.states.saving') : t('sales.orderDetail.saveHeader')}
              </Button>
            )}
            {save.error && <Alert severity="error">{(save.error as Error).message}</Alert>}

            <Divider />

            {/* Lines */}
            <Typography variant="subtitle1">
              {t('sales.orderDetail.items', {
                n: detail.data.lines.reduce((s, l) => s + l.quantity, 0),
                total: fmt.money(detail.data.lines.reduce((s, l) => s + l.unitSalePrice * l.quantity, 0)),
              })}
            </Typography>
            {detail.data.lines.length > 0 && (
              <Table size="small">
                <TableHead>
                  <TableRow>
                    <TableCell>{t('sales.orderDetail.card')}</TableCell>
                    <TableCell align="right">{t('sales.listings.qty')}</TableCell>
                    <TableCell align="right">{t('common.labels.price')}</TableCell>
                    {editable && <TableCell />}
                  </TableRow>
                </TableHead>
                <TableBody>
                  {detail.data.lines.map((l) => (
                    <TableRow key={l.id}>
                      <TableCell>
                        {l.name}
                        {l.isFoil ? ' ✦' : ''}
                        {l.set ? ` · ${l.set}` : ''}
                        {l.condition ? ` · ${l.condition}` : ''}
                      </TableCell>
                      <TableCell align="right">{l.quantity}</TableCell>
                      <TableCell align="right">{fmt.money(l.unitSalePrice)}</TableCell>
                      {editable && (
                        <TableCell align="right">
                          <IconButton size="small" onClick={() => removeLine.mutate(l.id)}>
                            <DeleteIcon fontSize="small" />
                          </IconButton>
                        </TableCell>
                      )}
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
            {editable && <AddLineSearch orderId={orderId!} onAdded={invalidate} />}

            <Divider />
            <Stack direction="row" justifyContent="space-between" alignItems="center">
              <Button onClick={onClose}>{t('common.actions.close')}</Button>
              <Stack direction="row" spacing={1}>
                <Button
                  variant="outlined"
                  startIcon={<ReceiptLongIcon />}
                  component="a"
                  href={api.receiptHtmlUrl(orderId!)}
                  target="_blank"
                  rel="noopener"
                >
                  {t('sales.orderDetail.printReceipt')}
                </Button>
                <Button
                  variant="text"
                  size="small"
                  component="a"
                  href={api.receiptPdfUrl(orderId!)}
                  target="_blank"
                  rel="noopener"
                >
                  {t('sales.orderDetail.receiptPdf')}
                </Button>
                {editable && (
                  <Button
                    color="error"
                    startIcon={<DeleteIcon />}
                    disabled={del.isPending}
                    onClick={() => {
                      if (confirm(t('sales.orderDetail.deleteConfirm', { id: orderId }))) del.mutate();
                    }}
                  >
                    {t('sales.orderDetail.deleteOrder')}
                  </Button>
                )}
              </Stack>
            </Stack>
            {del.error && <Alert severity="error">{(del.error as Error).message}</Alert>}
          </Stack>
        )}
      </Box>
      <ScanLabelDialog open={scanOpen} onClose={() => setScanOpen(false)} onCode={(code) => void onLabelScanned(code)} />
    </Drawer>
  );
}
