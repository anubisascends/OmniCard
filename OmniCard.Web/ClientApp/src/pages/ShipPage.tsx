import { useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  AlertTitle,
  Box,
  Button,
  Chip,
  FormControlLabel,
  List,
  ListItem,
  ListItemText,
  Paper,
  Stack,
  Switch,
  TextField,
  Typography,
} from '@mui/material';
import LocalShippingIcon from '@mui/icons-material/LocalShipping';
import { api } from '../api/client';
import type { OrderDto, ShipScanResultDto } from '../api/types';
import { BarcodeScanner } from '../components/BarcodeScanner';
import { OrderDetailDrawer } from '../components/dialogs/OrderDetailDrawer';
import { useFormatters } from '../i18n/format';

const AUTO_KEY = 'omnicard.ship.auto';
const CAMERA_KEY = 'omnicard.ship.camera';

type Outcome = ShipScanResultDto['outcome'] | 'error';

interface LogEntry {
  key: number;
  at: Date;
  tracking: string;
  outcome: Outcome;
  order?: OrderDto;
}

function readBool(key: string, fallback: boolean): boolean {
  try {
    const v = localStorage.getItem(key);
    return v == null ? fallback : v === 'true';
  } catch {
    return fallback;
  }
}

function writeBool(key: string, value: boolean) {
  try {
    localStorage.setItem(key, String(value));
  } catch {
    // Per-browser convenience only.
  }
}

let audioCtx: AudioContext | null = null;

/** Short beep + vibration so the user can scan without looking at the screen. */
function signal(kind: 'good' | 'attention' | 'bad') {
  try {
    audioCtx ??= new AudioContext();
    if (audioCtx.state === 'suspended') void audioCtx.resume();
    const tones = kind === 'good' ? [880] : kind === 'attention' ? [660, 660] : [220, 180];
    tones.forEach((freq, i) => {
      const osc = audioCtx!.createOscillator();
      const gain = audioCtx!.createGain();
      osc.frequency.value = freq;
      gain.gain.value = 0.15;
      osc.connect(gain).connect(audioCtx!.destination);
      const start = audioCtx!.currentTime + i * 0.18;
      osc.start(start);
      osc.stop(start + (kind === 'bad' ? 0.25 : 0.12));
    });
  } catch {
    // No audio available — vibration / on-screen result still apply.
  }
  navigator.vibrate?.(kind === 'good' ? 80 : kind === 'attention' ? [60, 60, 60] : [250, 80, 250]);
}

const SIGNAL: Record<Outcome, 'good' | 'attention' | 'bad'> = {
  shipped: 'good',
  ready: 'attention',
  ambiguous: 'attention',
  alreadyShipped: 'attention',
  notFound: 'bad',
  error: 'bad',
};

/**
 * Ship page: scan each package's shipping-label barcode as it leaves (phone at the mailbox, or a webcam /
 * handheld scanner at a shipping desk) and the order with that tracking number moves to Shipped. With
 * Auto-ship on, a single open match ships immediately; otherwise each one waits for a "Mark shipped" tap.
 */
export function ShipPage() {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const qc = useQueryClient();

  const [auto, setAuto] = useState(() => readBool(AUTO_KEY, false));
  const [cameraOn, setCameraOn] = useState(() => readBool(CAMERA_KEY, true));
  const [manual, setManual] = useState('');
  const [busy, setBusy] = useState(false);
  const [current, setCurrent] = useState<ShipScanResultDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [log, setLog] = useState<LogEntry[]>([]);
  const [openOrderId, setOpenOrderId] = useState<number | null>(null);
  const busyRef = useRef(false);
  const autoRef = useRef(auto);
  autoRef.current = auto;
  const manualRef = useRef<HTMLInputElement | null>(null);
  const logKey = useRef(0);

  const record = (tracking: string, outcome: Outcome, order?: OrderDto) => {
    setLog((l) => [{ key: ++logKey.current, at: new Date(), tracking, outcome, order }, ...l].slice(0, 100));
    signal(SIGNAL[outcome]);
    if (outcome === 'shipped') {
      qc.invalidateQueries({ queryKey: ['orders'] });
      qc.invalidateQueries({ queryKey: ['dashboard'] });
    }
  };

  const scan = async (code: string) => {
    const trimmed = code.trim();
    if (!trimmed || busyRef.current) return;
    busyRef.current = true;
    setBusy(true);
    setError(null);
    try {
      const result = await api.orderShipScan(trimmed, autoRef.current);
      setCurrent(result);
      record(result.tracking, result.outcome, result.orders[0]);
    } catch (e) {
      setCurrent(null);
      setError((e as Error).message);
      record(trimmed, 'error');
    } finally {
      busyRef.current = false;
      setBusy(false);
    }
  };

  const shipOne = async (order: OrderDto) => {
    if (busyRef.current || !current) return;
    busyRef.current = true;
    setBusy(true);
    setError(null);
    try {
      const shipped = await api.orderShip(order.id);
      setCurrent({ outcome: 'shipped', tracking: current.tracking, orders: [shipped] });
      record(current.tracking, 'shipped', shipped);
    } catch (e) {
      setError((e as Error).message);
      record(current.tracking, 'error', order);
    } finally {
      busyRef.current = false;
      setBusy(false);
    }
  };

  const submitManual = () => {
    void scan(manual);
    setManual('');
    manualRef.current?.focus();
  };

  const orderLabel = (o: OrderDto) => (o.orderNumber ? `#${o.orderNumber}` : t('sales.orderDetail.orderTitle', { id: o.id }));
  const orderSummary = (o: OrderDto) =>
    [
      orderLabel(o),
      o.customerName ?? t('sales.orders.customerFallback', { id: o.customerId }),
      t('sales.orders.itemCount', { count: o.lineItemCount }),
    ].join(' · ');

  const shippedCount = log.filter((e) => e.outcome === 'shipped').length;

  return (
    <Box sx={{ maxWidth: 760, mx: 'auto' }}>
      <Stack direction="row" spacing={1} alignItems="center" sx={{ mb: 1 }}>
        <LocalShippingIcon color="primary" />
        <Typography variant="h5">{t('sales.ship.title')}</Typography>
      </Stack>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
        {t('sales.ship.intro')}
      </Typography>

      <Paper variant="outlined" sx={{ p: 2, mb: 2 }}>
        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={{ xs: 1, sm: 3 }} sx={{ mb: 2 }}>
          <Box>
            <FormControlLabel
              control={
                <Switch
                  checked={auto}
                  onChange={(e) => {
                    setAuto(e.target.checked);
                    writeBool(AUTO_KEY, e.target.checked);
                  }}
                />
              }
              label={t('sales.ship.autoShip')}
            />
            <Typography variant="caption" color="text.secondary" display="block">
              {t('sales.ship.autoShipHelp')}
            </Typography>
          </Box>
          <Box>
            <FormControlLabel
              control={
                <Switch
                  checked={cameraOn}
                  onChange={(e) => {
                    setCameraOn(e.target.checked);
                    writeBool(CAMERA_KEY, e.target.checked);
                  }}
                />
              }
              label={t('sales.ship.cameraToggle')}
            />
            <Typography variant="caption" color="text.secondary" display="block">
              {t('sales.ship.cameraHelp')}
            </Typography>
          </Box>
        </Stack>

        {cameraOn && (
          <Box sx={{ mb: 2 }}>
            <BarcodeScanner onCode={(code) => void scan(code)} paused={busy} />
          </Box>
        )}

        <Stack direction="row" spacing={1} alignItems="flex-start">
          <TextField
            inputRef={manualRef}
            size="small"
            fullWidth
            autoFocus={!cameraOn}
            label={t('sales.ship.manualLabel')}
            helperText={t('sales.ship.manualHelp')}
            value={manual}
            onChange={(e) => setManual(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') {
                e.preventDefault();
                submitManual();
              }
            }}
          />
          <Button variant="outlined" disabled={busy || !manual.trim()} onClick={submitManual}>
            {t('sales.ship.lookUp')}
          </Button>
        </Stack>
      </Paper>

      {/* Current result */}
      <Box sx={{ mb: 2 }} aria-live="polite">
        {error ? (
          <Alert severity="error">{t('sales.ship.result.error', { message: error })}</Alert>
        ) : !current ? (
          <Alert severity="info" icon={false}>
            {t('sales.ship.waiting')}
          </Alert>
        ) : current.outcome === 'shipped' ? (
          <Alert severity="success" variant="filled" sx={{ fontSize: '1.05rem' }}>
            <AlertTitle>{t('sales.ship.result.shipped')}</AlertTitle>
            {orderSummary(current.orders[0])}
            <Typography variant="body2">{t('sales.ship.trackingLine', { tracking: current.tracking })}</Typography>
          </Alert>
        ) : current.outcome === 'ready' ? (
          <Alert
            severity="info"
            action={
              <Button variant="contained" disabled={busy} onClick={() => void shipOne(current.orders[0])}>
                {t('sales.ship.markShipped')}
              </Button>
            }
          >
            <AlertTitle>{t('sales.ship.result.ready')}</AlertTitle>
            {orderSummary(current.orders[0])}
            <Typography variant="body2">{t('sales.ship.trackingLine', { tracking: current.tracking })}</Typography>
          </Alert>
        ) : current.outcome === 'ambiguous' ? (
          <Alert severity="warning">
            <AlertTitle>{t('sales.ship.result.ambiguous', { count: current.orders.length })}</AlertTitle>
            <Stack spacing={1} sx={{ mt: 1 }}>
              {current.orders.map((o) => (
                <Stack key={o.id} direction="row" spacing={1} alignItems="center">
                  <Typography variant="body2" sx={{ flexGrow: 1 }}>
                    {orderSummary(o)}
                  </Typography>
                  <Button size="small" onClick={() => setOpenOrderId(o.id)}>
                    {t('sales.ship.openOrder')}
                  </Button>
                  <Button size="small" variant="contained" disabled={busy} onClick={() => void shipOne(o)}>
                    {t('sales.ship.markShipped')}
                  </Button>
                </Stack>
              ))}
            </Stack>
          </Alert>
        ) : current.outcome === 'alreadyShipped' ? (
          <Alert severity="info">
            <AlertTitle>
              {current.orders[0]?.shippedAt
                ? t('sales.ship.result.alreadyShippedOn', { date: fmt.dateTime(current.orders[0].shippedAt) })
                : t('sales.ship.result.alreadyShipped')}
            </AlertTitle>
            {current.orders.map((o) => (
              <div key={o.id}>{orderSummary(o)}</div>
            ))}
          </Alert>
        ) : (
          <Alert severity="error">
            <AlertTitle>{t('sales.ship.result.notFound', { tracking: current.tracking })}</AlertTitle>
            {t('sales.ship.result.notFoundHelp')}
          </Alert>
        )}
      </Box>

      {/* Session log */}
      <Paper variant="outlined">
        <Stack direction="row" alignItems="center" spacing={1} sx={{ px: 2, pt: 1.5 }}>
          <Typography variant="subtitle1" sx={{ flexGrow: 1 }}>
            {t('sales.ship.session.title')}
          </Typography>
          <Chip size="small" color="success" label={t('sales.ship.session.shippedCount', { count: shippedCount })} />
        </Stack>
        {log.length === 0 ? (
          <Typography variant="body2" color="text.secondary" sx={{ px: 2, py: 1.5 }}>
            {t('sales.ship.session.empty')}
          </Typography>
        ) : (
          <List dense>
            {log.map((e) => (
              <ListItem
                key={e.key}
                secondaryAction={
                  e.order && (
                    <Button size="small" onClick={() => setOpenOrderId(e.order!.id)}>
                      {t('sales.ship.openOrder')}
                    </Button>
                  )
                }
              >
                <ListItemText
                  primary={
                    <Stack direction="row" spacing={1} alignItems="center">
                      <Chip
                        size="small"
                        variant="outlined"
                        color={SIGNAL[e.outcome] === 'good' ? 'success' : SIGNAL[e.outcome] === 'bad' ? 'error' : 'warning'}
                        label={t(`sales.ship.outcome.${e.outcome}`)}
                      />
                      <span>{e.order ? orderSummary(e.order) : e.tracking}</span>
                    </Stack>
                  }
                  secondary={`${fmt.dateTime(e.at, { timeStyle: 'medium' })} · ${e.tracking}`}
                />
              </ListItem>
            ))}
          </List>
        )}
      </Paper>

      <OrderDetailDrawer orderId={openOrderId} onClose={() => setOpenOrderId(null)} />
    </Box>
  );
}
