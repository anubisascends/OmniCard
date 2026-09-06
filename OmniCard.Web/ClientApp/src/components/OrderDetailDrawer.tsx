import { useEffect, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  Divider,
  Drawer,
  IconButton,
  MenuItem,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  TextField,
  Typography,
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import DeleteIcon from '@mui/icons-material/Delete';
import { api } from '../api/client';
import { useGame } from '../context/GameContext';

const money = (n?: number | null) =>
  n == null ? '—' : n.toLocaleString(undefined, { style: 'currency', currency: 'USD' });
const CHANNELS = ['Manual', 'TcgPlayer', 'Ebay'];

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
        label="Add a card (search your collection)"
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

export function OrderDetailDrawer({ orderId, onClose }: { orderId: number | null; onClose: () => void }) {
  const qc = useQueryClient();
  const open = orderId != null;
  const detail = useQuery({
    queryKey: ['order', orderId],
    queryFn: () => api.order(orderId!),
    enabled: open,
  });

  const [form, setForm] = useState<HeaderForm | null>(null);
  useEffect(() => {
    if (detail.data) {
      const o = detail.data.order;
      setForm({
        channel: o.channel,
        orderNumber: o.orderNumber ?? '',
        trackingNumber: o.trackingNumber ?? '',
        carrier: '',
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
      <Box sx={{ width: 460, p: 2 }}>
        {detail.isLoading || !detail.data || !form ? (
          <CircularProgress />
        ) : (
          <Stack spacing={2}>
            <Stack direction="row" alignItems="center" spacing={1}>
              <Typography variant="h6" sx={{ flexGrow: 1 }}>
                Order #{detail.data.order.id}
              </Typography>
              <Chip size="small" label={status} />
            </Stack>
            <Typography variant="body2" color="text.secondary">
              {detail.data.order.customerName ?? `Customer #${detail.data.order.customerId}`}
            </Typography>

            {!editable && (
              <Alert severity="info">
                This order is {status?.toLowerCase()} — header and line edits are locked (change the
                lane back to Created/Packed to edit).
              </Alert>
            )}

            {/* Header */}
            <Stack direction="row" spacing={2}>
              <TextField select size="small" label="Channel" value={form.channel} onChange={set('channel')} disabled={!editable} sx={{ minWidth: 130 }}>
                {CHANNELS.map((c) => (
                  <MenuItem key={c} value={c}>{c}</MenuItem>
                ))}
              </TextField>
              <TextField size="small" label="Order #" value={form.orderNumber} onChange={set('orderNumber')} disabled={!editable} fullWidth />
            </Stack>
            <Stack direction="row" spacing={2}>
              <TextField size="small" label="Tracking" value={form.trackingNumber} onChange={set('trackingNumber')} disabled={!editable} fullWidth />
            </Stack>
            <Stack direction="row" spacing={2}>
              <TextField size="small" type="number" label="Ship charged" value={form.shippingChargedToBuyer} onChange={setNum('shippingChargedToBuyer')} disabled={!editable} />
              <TextField size="small" type="number" label="Ship cost" value={form.shippingCost} onChange={setNum('shippingCost')} disabled={!editable} />
              <TextField size="small" type="number" label="Fees" value={form.marketplaceFees} onChange={setNum('marketplaceFees')} disabled={!editable} />
            </Stack>
            <TextField size="small" label="Notes" value={form.notes} onChange={set('notes')} disabled={!editable} multiline minRows={2} />
            {editable && (
              <Button variant="contained" disabled={save.isPending} onClick={() => save.mutate()}>
                {save.isPending ? 'Saving…' : 'Save header'}
              </Button>
            )}
            {save.error && <Alert severity="error">{(save.error as Error).message}</Alert>}

            <Divider />

            {/* Lines */}
            <Typography variant="subtitle1">
              Items ({detail.data.lines.reduce((s, l) => s + l.quantity, 0)}) —{' '}
              {money(detail.data.lines.reduce((s, l) => s + l.unitSalePrice * l.quantity, 0))}
            </Typography>
            {detail.data.lines.length > 0 && (
              <Table size="small">
                <TableHead>
                  <TableRow>
                    <TableCell>Card</TableCell>
                    <TableCell align="right">Qty</TableCell>
                    <TableCell align="right">Price</TableCell>
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
                      <TableCell align="right">{money(l.unitSalePrice)}</TableCell>
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
            <Stack direction="row" justifyContent="space-between">
              <Button onClick={onClose}>Close</Button>
              {editable && (
                <Button
                  color="error"
                  startIcon={<DeleteIcon />}
                  disabled={del.isPending}
                  onClick={() => {
                    if (confirm(`Delete order #${orderId}?`)) del.mutate();
                  }}
                >
                  Delete order
                </Button>
              )}
            </Stack>
            {del.error && <Alert severity="error">{(del.error as Error).message}</Alert>}
          </Stack>
        )}
      </Box>
    </Drawer>
  );
}
