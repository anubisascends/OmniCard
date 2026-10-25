import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Autocomplete,
  Box,
  Button,
  Chip,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  IconButton,
  Paper,
  Radio,
  RadioGroup,
  FormControlLabel,
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
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutline';
import RemoveIcon from '@mui/icons-material/Remove';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import type { CustomerDto, KioskQueueEntryDto, KioskQueueLineDto } from '../../api/types';
import { useFormatters } from '../../i18n/format';
import { usePermissions } from '../../context/usePermissions';
import { CardImage } from '../CardImage';
import { OrderDetailDrawer } from '../dialogs/OrderDetailDrawer';

const QUEUE_KEY = ['kiosk-queue'];

/**
 * Sales ▸ Kiosk: pick lists customers sent from a kiosk, oldest first, each line showing where the card
 * is stored. Staff claim a list, pull the cards (dropping any that can't be found or the customer
 * declines), then create the order — or cancel the list, which frees its cards. Customers still
 * browsing are shown below, read-only.
 */
export function KioskQueue() {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const { can } = usePermissions();
  const canFulfill = can('sales.kiosk.fulfill');
  const queue = useQuery({ queryKey: QUEUE_KEY, queryFn: api.kioskQueue, refetchInterval: 10_000 });
  const [orderFor, setOrderFor] = useState<KioskQueueEntryDto | null>(null);
  const [cancelFor, setCancelFor] = useState<KioskQueueEntryDto | null>(null);
  const [openOrderId, setOpenOrderId] = useState<number | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const refresh = () => {
    void qc.invalidateQueries({ queryKey: QUEUE_KEY });
    void qc.invalidateQueries({ queryKey: ['kiosk-queue-count'] });
  };
  const claim = useMutation({ mutationFn: api.kioskClaim, onSuccess: refresh });
  const unclaim = useMutation({ mutationFn: api.kioskUnclaim, onSuccess: refresh });
  const removeHold = useMutation({
    mutationFn: (v: { id: number; holdId: number; quantity?: number }) => api.kioskRemoveHold(v.id, v.holdId, v.quantity),
    onSuccess: refresh,
  });
  const cancel = useMutation({
    mutationFn: api.kioskCancel,
    onSuccess: () => {
      setCancelFor(null);
      refresh();
    },
  });
  const error = [claim, unclaim, removeHold, cancel].find((m) => m.isError)?.error;

  if (queue.isLoading) return <CircularProgress />;
  if (queue.isError) return <Alert severity="error">{(queue.error as Error).message}</Alert>;

  const entries = queue.data ?? [];
  const submitted = entries.filter((e) => e.status !== 'Browsing');
  const browsing = entries.filter((e) => e.status === 'Browsing');

  return (
    <Stack spacing={2}>
      {error && <Alert severity="error">{(error as Error).message}</Alert>}
      {notice && (
        <Alert severity="success" onClose={() => setNotice(null)}>
          {notice}
        </Alert>
      )}
      {entries.length === 0 && <Typography color="text.secondary">{t('kiosk.queue.empty')}</Typography>}
      {submitted.length > 0 && (
        <Typography variant="body2" color="text.secondary">
          {t('kiosk.queue.printHint')}
        </Typography>
      )}
      {submitted.map((entry) => (
        <QueueEntry
          key={entry.id}
          entry={entry}
          canFulfill={canFulfill}
          onClaim={() => claim.mutate(entry.id)}
          onUnclaim={() => unclaim.mutate(entry.id)}
          onRemove={(line, quantity) => removeHold.mutate({ id: entry.id, holdId: line.holdId, quantity })}
          onOrder={() => setOrderFor(entry)}
          onCancel={() => setCancelFor(entry)}
        />
      ))}
      {browsing.map((entry) => (
        <QueueEntry key={entry.id} entry={entry} canFulfill={false} />
      ))}

      <CreateOrderDialog
        entry={orderFor}
        onClose={() => setOrderFor(null)}
        onCreated={(orderId) => {
          setOrderFor(null);
          setNotice(t('kiosk.queue.orderDialog.created', { id: orderId }));
          setOpenOrderId(orderId);
          refresh();
        }}
      />

      <Dialog open={cancelFor !== null} onClose={() => setCancelFor(null)}>
        <DialogContent>
          <Typography>{t('kiosk.queue.cancelConfirm', { name: cancelFor?.customerName ?? '' })}</Typography>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setCancelFor(null)}>{t('common.actions.no')}</Button>
          <Button color="error" variant="contained" onClick={() => cancelFor && cancel.mutate(cancelFor.id)} disabled={cancel.isPending}>
            {t('kiosk.queue.cancel')}
          </Button>
        </DialogActions>
      </Dialog>

      <OrderDetailDrawer orderId={openOrderId} onClose={() => setOpenOrderId(null)} />
    </Stack>
  );
}

function QueueEntry({
  entry,
  canFulfill,
  onClaim,
  onUnclaim,
  onRemove,
  onOrder,
  onCancel,
}: {
  entry: KioskQueueEntryDto;
  canFulfill: boolean;
  onClaim?: () => void;
  onUnclaim?: () => void;
  onRemove?: (line: KioskQueueLineDto, quantity?: number) => void;
  onOrder?: () => void;
  onCancel?: () => void;
}) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const browsing = entry.status === 'Browsing';
  const statusColor = entry.status === 'Picking' ? 'primary' : entry.status === 'Queued' ? 'warning' : 'default';

  return (
    <Paper variant="outlined" sx={{ p: 2, opacity: browsing ? 0.75 : 1 }}>
      <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} alignItems={{ md: 'center' }} sx={{ mb: 1 }}>
        <Box sx={{ flexGrow: 1 }}>
          <Stack direction="row" spacing={1} alignItems="center" sx={{ flexWrap: 'wrap', gap: 1 }}>
            <Typography variant="h6">{entry.customerName}</Typography>
            <Chip size="small" color={statusColor} label={t(`kiosk.queue.status.${entry.status}`, { defaultValue: entry.status })} />
            {entry.claimedByName && <Chip size="small" variant="outlined" label={t('kiosk.queue.pickedBy', { name: entry.claimedByName })} />}
          </Stack>
          <Typography variant="body2" color="text.secondary">
            {[
              entry.deviceName ? t('kiosk.queue.kiosk', { name: entry.deviceName }) : null,
              entry.submittedAt
                ? t('kiosk.queue.submittedAt', { time: fmt.dateTime(entry.submittedAt) })
                : t('kiosk.queue.lastActive', { time: fmt.dateTime(entry.lastActivityAt) }),
              t('kiosk.queue.cards', { count: entry.totalQuantity }),
              fmt.money(entry.totalPrice),
            ]
              .filter(Boolean)
              .join(' · ')}
          </Typography>
          {browsing && (
            <Typography variant="caption" color="text.secondary">
              {t('kiosk.queue.browsingNote')}
            </Typography>
          )}
        </Box>
        {canFulfill && !browsing && (
          <Stack direction="row" spacing={1} sx={{ flexWrap: 'wrap', gap: 1 }}>
            {entry.status === 'Queued' ? (
              <Button variant="outlined" onClick={onClaim}>
                {t('kiosk.queue.claim')}
              </Button>
            ) : (
              <Button onClick={onUnclaim}>{t('kiosk.queue.unclaim')}</Button>
            )}
            <Button variant="contained" onClick={onOrder} disabled={entry.lines.length === 0}>
              {t('kiosk.queue.createOrder')}
            </Button>
            <Button color="error" onClick={onCancel}>
              {t('kiosk.queue.cancel')}
            </Button>
          </Stack>
        )}
      </Stack>
      {entry.lines.length > 0 && (
        <Box sx={{ overflowX: 'auto' }}>
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell />
                <TableCell>{t('common.labels.name')}</TableCell>
                <TableCell>{t('common.labels.set')}</TableCell>
                <TableCell>{t('common.labels.condition')}</TableCell>
                <TableCell>{t('kiosk.queue.where')}</TableCell>
                <TableCell align="right">{t('common.labels.quantity')}</TableCell>
                <TableCell align="right">{t('common.labels.price')}</TableCell>
                {canFulfill && !browsing && <TableCell />}
              </TableRow>
            </TableHead>
            <TableBody>
              {entry.lines.map((line) => (
                <TableRow key={line.holdId}>
                  <TableCell sx={{ width: 48 }}>
                    <CardImage
                      src={line.imageUri ?? undefined}
                      alt={line.name}
                      foil={line.isFoil}
                      sx={{ width: 36, aspectRatio: '0.716', objectFit: 'contain' }}
                    />
                  </TableCell>
                  <TableCell>
                    {line.name}
                    {line.isFoil && <Chip size="small" label={t('common.labels.foil')} sx={{ ml: 1 }} />}
                  </TableCell>
                  <TableCell>
                    {line.setCode} · {line.number}
                  </TableCell>
                  <TableCell>
                    {t(`common.conditions.${line.condition}`, { defaultValue: line.condition })}
                    {line.language !== 'en' && ` · ${t(`common.languages.${line.language}`, { defaultValue: line.language })}`}
                  </TableCell>
                  <TableCell>
                    <WhereText line={line} />
                  </TableCell>
                  <TableCell align="right">{fmt.number(line.quantity)}</TableCell>
                  <TableCell align="right">{fmt.money(line.unitPrice)}</TableCell>
                  {canFulfill && !browsing && (
                    <TableCell align="right" sx={{ whiteSpace: 'nowrap' }}>
                      {line.quantity > 1 && (
                        <Tooltip title={t('kiosk.queue.removeOne')}>
                          <IconButton size="small" onClick={() => onRemove?.(line, 1)}>
                            <RemoveIcon fontSize="small" />
                          </IconButton>
                        </Tooltip>
                      )}
                      <Tooltip title={t('kiosk.queue.removeLine')}>
                        <IconButton size="small" color="error" onClick={() => onRemove?.(line)}>
                          <DeleteOutlineIcon fontSize="small" />
                        </IconButton>
                      </Tooltip>
                    </TableCell>
                  )}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Box>
      )}
    </Paper>
  );
}

function WhereText({ line }: { line: KioskQueueLineDto }) {
  const { t } = useTranslation();
  if (!line.locationName) return <>{t('kiosk.queue.noLocation')}</>;
  const parts = [line.locationName];
  if (line.section) parts.push(line.section);
  if (line.page != null) {
    parts.push(
      line.slot != null
        ? `${t('kiosk.queue.page', { page: line.page })}, ${t('kiosk.queue.slot', { slot: line.slot })}`
        : t('kiosk.queue.page', { page: line.page }),
    );
  }
  return <>{parts.join(' · ')}</>;
}

function CreateOrderDialog({
  entry,
  onClose,
  onCreated,
}: {
  entry: KioskQueueEntryDto | null;
  onClose: () => void;
  onCreated: (orderId: number) => void;
}) {
  const { t } = useTranslation();
  const [mode, setMode] = useState<'new' | 'existing'>('new');
  const [name, setName] = useState('');
  const [customer, setCustomer] = useState<CustomerDto | null>(null);
  const customers = useQuery({ queryKey: ['customers'], queryFn: api.customers, enabled: entry !== null && mode === 'existing' });
  const create = useMutation({
    mutationFn: () =>
      api.kioskCreateOrder(entry!.id, mode === 'existing' ? { customerId: customer?.id } : { customerName: name.trim() || null }),
    onSuccess: (r) => onCreated(r.orderId),
  });

  const [forId, setForId] = useState<number | null>(null);
  if (entry && entry.id !== forId) {
    // A different list was opened: reset the form to its customer's name.
    setForId(entry.id);
    setMode('new');
    setName(entry.customerName);
    setCustomer(null);
    create.reset();
  }

  return (
    <Dialog open={entry !== null} onClose={onClose} maxWidth="sm" fullWidth>
      <DialogTitle>{t('kiosk.queue.orderDialog.title', { name: entry?.customerName ?? '' })}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ pt: 1 }}>
          <Typography variant="body2" color="text.secondary">
            {t('kiosk.queue.orderDialog.body')}
          </Typography>
          <RadioGroup row value={mode} onChange={(e) => setMode(e.target.value as 'new' | 'existing')}>
            <FormControlLabel value="new" control={<Radio />} label={t('kiosk.queue.orderDialog.newCustomer')} />
            <FormControlLabel value="existing" control={<Radio />} label={t('kiosk.queue.orderDialog.existingCustomer')} />
          </RadioGroup>
          {mode === 'new' ? (
            <TextField label={t('kiosk.queue.orderDialog.customerName')} value={name} onChange={(e) => setName(e.target.value)} />
          ) : (
            <Autocomplete
              options={customers.data ?? []}
              loading={customers.isLoading}
              value={customer}
              onChange={(_, v) => setCustomer(v)}
              getOptionLabel={(c) => c.name}
              isOptionEqualToValue={(a, b) => a.id === b.id}
              renderInput={(params) => <TextField {...params} label={t('kiosk.queue.orderDialog.customer')} />}
            />
          )}
          {create.isError && <Alert severity="error">{(create.error as Error).message}</Alert>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('common.actions.cancel')}</Button>
        <Button
          variant="contained"
          disabled={create.isPending || (mode === 'existing' ? customer === null : !name.trim())}
          onClick={() => create.mutate()}
        >
          {t('kiosk.queue.createOrder')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
