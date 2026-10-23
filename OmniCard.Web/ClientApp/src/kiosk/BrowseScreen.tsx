import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  AppBar,
  Badge,
  Box,
  Button,
  Card,
  CardActionArea,
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
  Snackbar,
  Stack,
  TextField,
  ToggleButton,
  ToggleButtonGroup,
  Toolbar,
  Typography,
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import RemoveIcon from '@mui/icons-material/Remove';
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutline';
import SearchIcon from '@mui/icons-material/Search';
import ShoppingBasketIcon from '@mui/icons-material/ShoppingBasket';
import CloseIcon from '@mui/icons-material/Close';
import { useTranslation } from 'react-i18next';
import type { KioskCardDto, KioskSessionDto, KioskSessionItemDto, KioskStatusDto, KioskSubmitResultDto } from '../api/types';
import { CardImage } from '../components/CardImage';
import { useFormatters } from '../i18n/format';
import { kioskApi, KioskApiError } from './kioskApi';

const PAGE_SIZE = 48;

type Sort = 'name' | 'price-desc' | 'price-asc';

/**
 * Browsing for one customer: pick a game, search by name/set, tap a card for details, add copies to the
 * pick list (each add is a server-side hold), then send the list to the counter. Server answers replace
 * the session in place; a `no-session` answer (idle timeout, device revoked) sends the kiosk back to
 * the welcome screen.
 */
export function BrowseScreen({
  status,
  session,
  logo,
  onSession,
  onSessionLost,
  onSubmitted,
  onLeave,
}: {
  status: KioskStatusDto;
  session: KioskSessionDto;
  logo: ReactNode;
  onSession: (session: KioskSessionDto) => void;
  onSessionLost: () => void;
  onSubmitted: (result: KioskSubmitResultDto) => void;
  onLeave: () => void;
}) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const [game, setGame] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const query = useDebounced(search.trim(), 300);
  const [sort, setSort] = useState<Sort>('name');
  const [detail, setDetail] = useState<KioskCardDto | null>(null);
  const [listOpen, setListOpen] = useState(false);
  const [confirmLeave, setConfirmLeave] = useState(false);
  const [toast, setToast] = useState<{ message: string; error?: boolean } | null>(null);

  const games = useQuery({ queryKey: ['kiosk-games'], queryFn: kioskApi.games });
  const activeGame = game ?? games.data?.[0]?.id ?? null;

  const cards = useInfiniteQuery({
    queryKey: ['kiosk-cards', activeGame, query, sort],
    enabled: activeGame !== null,
    initialPageParam: 0,
    queryFn: ({ pageParam }) =>
      kioskApi.cards({ game: activeGame!, q: query, sort, skip: pageParam, take: PAGE_SIZE }),
    getNextPageParam: (last) => (last.skip + last.items.length < last.total ? last.skip + last.items.length : undefined),
  });
  const rows = useMemo(() => cards.data?.pages.flatMap((p) => p.items) ?? [], [cards.data]);
  const total = cards.data?.pages[0]?.total ?? 0;

  const onError = (err: unknown) => {
    if (err instanceof KioskApiError && err.code === 'no-session') {
      onSessionLost();
      return;
    }
    setToast({ message: err instanceof Error ? err.message : t('kiosk.app.error'), error: true });
    void qc.invalidateQueries({ queryKey: ['kiosk-cards'] });
  };
  const afterChange = (next: KioskSessionDto) => {
    onSession(next);
    void qc.invalidateQueries({ queryKey: ['kiosk-cards'] });
  };

  const add = useMutation({
    mutationFn: (card: { key: string; name: string }) => kioskApi.addHold(card.key),
    onSuccess: (next, card) => {
      afterChange(next);
      setToast({ message: t('kiosk.app.browse.added', { name: card.name }) });
    },
    onError,
  });
  const remove = useMutation({
    mutationFn: ({ key, quantity }: { key: string; quantity: number }) => kioskApi.removeHold(key, quantity),
    onSuccess: afterChange,
    onError,
  });
  const submit = useMutation({ mutationFn: kioskApi.submit, onSuccess: onSubmitted, onError });
  const leave = useMutation({ mutationFn: kioskApi.endSession, onSettled: onLeave });

  const busy = add.isPending || remove.isPending;
  const inList = (key: string) => session.items.find((i) => i.key === key)?.quantity ?? 0;

  // The detail dialog shows the row as it was opened; keep its counts current as holds change.
  const liveDetail = detail ? (rows.find((r) => r.key === detail.key) ?? detail) : null;

  return (
    <Box sx={{ minHeight: '100vh', display: 'flex', flexDirection: 'column' }}>
      <AppBar position="sticky" color="default" elevation={1}>
        <Toolbar sx={{ gap: 2, flexWrap: 'wrap', py: 1 }}>
          {logo}
          <Typography variant="h6" sx={{ flexGrow: 1 }} noWrap>
            {t('kiosk.app.browse.greeting', { name: session.customerName })}
          </Typography>
          <Button color="inherit" onClick={() => setConfirmLeave(true)}>
            {t('kiosk.app.browse.startOver')}
          </Button>
          <Button
            variant="contained"
            size="large"
            startIcon={
              <Badge badgeContent={session.totalQuantity} color="secondary" max={99}>
                <ShoppingBasketIcon />
              </Badge>
            }
            onClick={() => setListOpen(true)}
          >
            {t('kiosk.app.browse.myList')}
          </Button>
        </Toolbar>
      </AppBar>

      <Stack spacing={2} sx={{ p: 2, flexGrow: 1 }}>
        {games.data && games.data.length > 1 && (
          <ToggleButtonGroup
            exclusive
            value={activeGame}
            onChange={(_, v: string | null) => v && setGame(v)}
            sx={{ flexWrap: 'wrap' }}
          >
            {games.data.map((g) => (
              <ToggleButton key={g.id} value={g.id} sx={{ px: 3 }}>
                {g.displayName}
              </ToggleButton>
            ))}
          </ToggleButtonGroup>
        )}
        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
          <TextField
            fullWidth
            placeholder={t('kiosk.app.browse.search')}
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            autoComplete="off"
            InputProps={{
              startAdornment: (
                <InputAdornment position="start">
                  <SearchIcon />
                </InputAdornment>
              ),
              endAdornment: search ? (
                <InputAdornment position="end">
                  <IconButton onClick={() => setSearch('')} aria-label={t('common.actions.clear')}>
                    <CloseIcon />
                  </IconButton>
                </InputAdornment>
              ) : undefined,
            }}
          />
          <TextField
            select
            label={t('kiosk.app.browse.sort')}
            value={sort}
            onChange={(e) => setSort(e.target.value as Sort)}
            sx={{ minWidth: 220 }}
          >
            <MenuItem value="name">{t('kiosk.app.browse.sortName')}</MenuItem>
            {status.showPrices && <MenuItem value="price-desc">{t('kiosk.app.browse.sortPriceDesc')}</MenuItem>}
            {status.showPrices && <MenuItem value="price-asc">{t('kiosk.app.browse.sortPriceAsc')}</MenuItem>}
          </TextField>
        </Stack>

        {cards.isSuccess && (
          <Typography color="text.secondary">{t('kiosk.app.browse.results', { count: total })}</Typography>
        )}
        {(cards.isLoading || games.isLoading) && (
          <Box sx={{ display: 'grid', placeItems: 'center', py: 6 }}>
            <CircularProgress />
          </Box>
        )}
        {cards.isSuccess && rows.length === 0 && (
          <Typography variant="h6" color="text.secondary" sx={{ textAlign: 'center', py: 6 }}>
            {t('kiosk.app.browse.noResults')}
          </Typography>
        )}

        <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: 'repeat(auto-fill, minmax(170px, 1fr))' }}>
          {rows.map((card) => (
            <CardTile
              key={card.key}
              card={card}
              showPrice={status.showPrices}
              disabled={busy}
              onOpen={() => setDetail(card)}
              onAdd={() => add.mutate(card)}
            />
          ))}
        </Box>
        {cards.hasNextPage && (
          <Box sx={{ textAlign: 'center' }}>
            <Button size="large" variant="outlined" onClick={() => void cards.fetchNextPage()} disabled={cards.isFetchingNextPage}>
              {t('kiosk.app.browse.loadMore')}
            </Button>
          </Box>
        )}
      </Stack>

      <CardDialog
        card={liveDetail}
        inList={liveDetail ? inList(liveDetail.key) : 0}
        showPrice={status.showPrices}
        busy={busy}
        onClose={() => setDetail(null)}
        onAdd={(c) => add.mutate(c)}
        onRemove={(c) => remove.mutate({ key: c.key, quantity: 1 })}
      />

      <ListDrawer
        open={listOpen}
        session={session}
        showPrice={status.showPrices}
        busy={busy || submit.isPending}
        onClose={() => setListOpen(false)}
        onAdd={(i) => add.mutate(i)}
        onRemove={(i, quantity) => remove.mutate({ key: i.key, quantity })}
        onSubmit={() => submit.mutate()}
      />

      <Dialog open={confirmLeave} onClose={() => setConfirmLeave(false)}>
        <DialogContent>
          <Typography>{t('kiosk.app.browse.startOverConfirm')}</Typography>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setConfirmLeave(false)}>{t('kiosk.app.list.keepBrowsing')}</Button>
          <Button color="error" variant="contained" onClick={() => leave.mutate()} disabled={leave.isPending}>
            {t('kiosk.app.browse.startOver')}
          </Button>
        </DialogActions>
      </Dialog>

      <IdleGuard timeoutMinutes={status.idleTimeoutMinutes} onLost={onSessionLost} onLeave={() => leave.mutate()} />

      <Snackbar
        open={toast !== null}
        autoHideDuration={toast?.error ? 5000 : 2000}
        onClose={() => setToast(null)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
      >
        <Alert severity={toast?.error ? 'warning' : 'success'} variant="filled" onClose={() => setToast(null)}>
          {toast?.message}
        </Alert>
      </Snackbar>
    </Box>
  );
}

function CardTile({
  card,
  showPrice,
  disabled,
  onOpen,
  onAdd,
}: {
  card: KioskCardDto;
  showPrice: boolean;
  disabled: boolean;
  onOpen: () => void;
  onAdd: () => void;
}) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  return (
    <Card variant="outlined" sx={{ display: 'flex', flexDirection: 'column' }}>
      <CardActionArea onClick={onOpen} sx={{ p: 1, flexGrow: 1, display: 'flex', flexDirection: 'column', alignItems: 'stretch' }}>
        <CardImage
          src={card.imageUri ?? undefined}
          alt={card.name}
          foil={card.isFoil}
          wrapperSx={{ width: '100%' }}
          sx={{ width: '100%', aspectRatio: '0.716', objectFit: 'contain', borderRadius: 1 }}
        />
        <Typography variant="subtitle1" sx={{ mt: 1, fontWeight: 600, lineHeight: 1.2 }}>
          {card.name}
        </Typography>
        <Typography variant="body2" color="text.secondary" noWrap>
          {card.setCode} · {card.number}
        </Typography>
        <CardChips card={card} />
        {showPrice && (
          <Typography variant="h6" sx={{ mt: 0.5 }}>
            {card.price != null ? fmt.money(card.price) : t('kiosk.app.browse.noPrice')}
          </Typography>
        )}
        <Typography variant="body2" color={card.available > 0 ? 'text.secondary' : 'warning.main'}>
          {card.available > 0 ? t('kiosk.app.browse.available', { count: card.available }) : t('kiosk.app.browse.soldOut')}
        </Typography>
      </CardActionArea>
      <Box sx={{ p: 1, pt: 0 }}>
        {card.inMyList > 0 && (
          <Typography variant="body2" color="primary" sx={{ mb: 0.5 }}>
            {t('kiosk.app.browse.inList', { count: card.inMyList })}
          </Typography>
        )}
        <Button fullWidth variant="contained" startIcon={<AddIcon />} disabled={disabled || card.available === 0} onClick={onAdd}>
          {t('kiosk.app.browse.add')}
        </Button>
      </Box>
    </Card>
  );
}

function CardChips({ card }: { card: { condition: string; language: string; isFoil: boolean } }) {
  const { t } = useTranslation();
  return (
    <Stack direction="row" spacing={0.5} sx={{ mt: 0.5, flexWrap: 'wrap', gap: 0.5 }}>
      <Chip size="small" label={t(`common.conditions.${card.condition}`, { defaultValue: card.condition })} />
      {card.isFoil && <Chip size="small" color="secondary" label={t('kiosk.app.browse.foil')} />}
      {card.language !== 'en' && (
        <Chip size="small" variant="outlined" label={t(`common.languages.${card.language}`, { defaultValue: card.language })} />
      )}
    </Stack>
  );
}

function CardDialog({
  card,
  inList,
  showPrice,
  busy,
  onClose,
  onAdd,
  onRemove,
}: {
  card: KioskCardDto | null;
  inList: number;
  showPrice: boolean;
  busy: boolean;
  onClose: () => void;
  onAdd: (card: KioskCardDto) => void;
  onRemove: (card: KioskCardDto) => void;
}) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  if (!card) return null;
  const rows: [string, ReactNode][] = [
    [t('kiosk.app.card.set'), `${card.setName} (${card.setCode})`],
    [t('kiosk.app.card.number'), card.number],
    [t('kiosk.app.card.rarity'), card.rarity],
    [t('kiosk.app.card.condition'), t(`common.conditions.${card.condition}`, { defaultValue: card.condition })],
    [t('kiosk.app.card.language'), t(`common.languages.${card.language}`, { defaultValue: card.language })],
  ];
  if (showPrice) rows.push([t('kiosk.app.card.price'), card.price != null ? fmt.money(card.price) : t('kiosk.app.browse.noPrice')]);

  return (
    <Dialog open onClose={onClose} maxWidth="md" fullWidth>
      <DialogTitle sx={{ pr: 6 }}>
        {card.name}
        <IconButton onClick={onClose} sx={{ position: 'absolute', right: 8, top: 8 }} aria-label={t('common.actions.close')}>
          <CloseIcon />
        </IconButton>
      </DialogTitle>
      <DialogContent>
        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={3}>
          <CardImage
            src={card.imageUri ?? undefined}
            alt={card.name}
            foil={card.isFoil}
            wrapperSx={{ flexShrink: 0, alignSelf: 'center' }}
            sx={{ width: { xs: 260, sm: 320 }, aspectRatio: '0.716', objectFit: 'contain', borderRadius: 2 }}
          />
          <Stack spacing={1.5} sx={{ flexGrow: 1 }}>
            {rows.map(([label, value]) => (
              <Box key={label}>
                <Typography variant="body2" color="text.secondary">
                  {label}
                </Typography>
                <Typography variant="h6">{value || '—'}</Typography>
              </Box>
            ))}
            {card.isFoil && <Chip color="secondary" label={card.foilType || t('kiosk.app.browse.foil')} sx={{ alignSelf: 'flex-start' }} />}
            <Divider />
            <Typography>
              {card.available > 0 ? t('kiosk.app.browse.available', { count: card.available }) : t('kiosk.app.browse.soldOut')}
            </Typography>
            {inList > 0 && <Typography color="primary">{t('kiosk.app.card.inList', { count: inList })}</Typography>}
          </Stack>
        </Stack>
      </DialogContent>
      <DialogActions sx={{ p: 2, gap: 1 }}>
        {inList > 0 && (
          <Button size="large" startIcon={<RemoveIcon />} onClick={() => onRemove(card)} disabled={busy}>
            {t('kiosk.app.card.removeOne')}
          </Button>
        )}
        <Button
          size="large"
          variant="contained"
          startIcon={<AddIcon />}
          onClick={() => onAdd(card)}
          disabled={busy || card.available === 0}
        >
          {t('kiosk.app.card.addToList')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

function ListDrawer({
  open,
  session,
  showPrice,
  busy,
  onClose,
  onAdd,
  onRemove,
  onSubmit,
}: {
  open: boolean;
  session: KioskSessionDto;
  showPrice: boolean;
  busy: boolean;
  onClose: () => void;
  onAdd: (item: KioskSessionItemDto) => void;
  onRemove: (item: KioskSessionItemDto, quantity: number) => void;
  onSubmit: () => void;
}) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const [confirm, setConfirm] = useState(false);

  return (
    <Drawer anchor="right" open={open} onClose={onClose} PaperProps={{ sx: { width: { xs: '100%', sm: 460 } } }}>
      <Stack sx={{ height: '100%' }}>
        <Toolbar sx={{ gap: 1 }}>
          <Typography variant="h5" sx={{ flexGrow: 1 }}>
            {t('kiosk.app.list.title')}
          </Typography>
          <IconButton onClick={onClose} aria-label={t('common.actions.close')}>
            <CloseIcon />
          </IconButton>
        </Toolbar>
        <Divider />
        <Box sx={{ flexGrow: 1, overflowY: 'auto', p: 2 }}>
          {session.items.length === 0 ? (
            <Typography color="text.secondary">{t('kiosk.app.list.empty')}</Typography>
          ) : (
            <Stack spacing={2} divider={<Divider />}>
              {session.items.map((item) => (
                <Stack key={item.key} direction="row" spacing={2} alignItems="center">
                  <CardImage
                    src={item.imageUri ?? undefined}
                    alt={item.name}
                    foil={item.isFoil}
                    sx={{ width: 64, aspectRatio: '0.716', objectFit: 'contain', borderRadius: 0.5 }}
                  />
                  <Box sx={{ flexGrow: 1, minWidth: 0 }}>
                    <Typography sx={{ fontWeight: 600 }}>{item.name}</Typography>
                    <Typography variant="body2" color="text.secondary" noWrap>
                      {item.setCode} · {item.number}
                    </Typography>
                    <CardChips card={item} />
                    {showPrice && item.unitPrice != null && (
                      <Typography variant="body2" sx={{ mt: 0.5 }}>
                        {fmt.money(item.unitPrice)}
                      </Typography>
                    )}
                  </Box>
                  <Stack alignItems="center" spacing={0.5}>
                    <Stack direction="row" alignItems="center">
                      <IconButton onClick={() => onRemove(item, 1)} disabled={busy} aria-label={t('kiosk.app.list.decrease')}>
                        <RemoveIcon />
                      </IconButton>
                      <Typography variant="h6" sx={{ minWidth: 28, textAlign: 'center' }}>
                        {fmt.number(item.quantity)}
                      </Typography>
                      <IconButton onClick={() => onAdd(item)} disabled={busy} aria-label={t('kiosk.app.list.increase')}>
                        <AddIcon />
                      </IconButton>
                    </Stack>
                    <Button
                      size="small"
                      color="error"
                      startIcon={<DeleteOutlineIcon />}
                      onClick={() => onRemove(item, item.quantity)}
                      disabled={busy}
                    >
                      {t('kiosk.app.list.removeAll')}
                    </Button>
                  </Stack>
                </Stack>
              ))}
            </Stack>
          )}
        </Box>
        <Divider />
        <Stack spacing={1.5} sx={{ p: 2 }}>
          {showPrice && session.totalPrice != null && (
            <>
              <Stack direction="row" justifyContent="space-between">
                <Typography variant="h6">{t('kiosk.app.list.total')}</Typography>
                <Typography variant="h6">{fmt.money(session.totalPrice)}</Typography>
              </Stack>
              <Typography variant="caption" color="text.secondary">
                {t('kiosk.app.list.estimate')}
              </Typography>
            </>
          )}
          <Button
            variant="contained"
            size="large"
            sx={{ py: 1.5 }}
            disabled={busy || session.items.length === 0}
            onClick={() => setConfirm(true)}
          >
            {t('kiosk.app.list.submit')}
          </Button>
        </Stack>
      </Stack>

      <Dialog open={confirm} onClose={() => setConfirm(false)}>
        <DialogTitle>{t('kiosk.app.list.submitConfirmTitle')}</DialogTitle>
        <DialogContent>
          <Typography>{t('kiosk.app.list.submitConfirmBody', { count: session.totalQuantity })}</Typography>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setConfirm(false)}>{t('kiosk.app.list.keepBrowsing')}</Button>
          <Button
            variant="contained"
            disabled={busy}
            onClick={() => {
              setConfirm(false);
              onSubmit();
            }}
          >
            {t('kiosk.app.list.submit')}
          </Button>
        </DialogActions>
      </Dialog>
    </Drawer>
  );
}

/**
 * Keeps the server session alive while the customer is active (a ping at most every 30 s after any touch,
 * key or scroll), and when they've been idle for all but the last minute of the timeout asks "Are you
 * still there?" with a countdown that clears the list.
 */
function IdleGuard({ timeoutMinutes, onLost, onLeave }: { timeoutMinutes: number; onLost: () => void; onLeave: () => void }) {
  const { t } = useTranslation();
  const lastActivity = useRef(Date.now());
  const lastPing = useRef(Date.now());
  const [warning, setWarning] = useState<number | null>(null); // seconds left
  const warnAfterMs = Math.max(30_000, (timeoutMinutes - 1) * 60_000);
  const graceSeconds = 60;

  const onLostRef = useRef(onLost);
  onLostRef.current = onLost;
  const onLeaveRef = useRef(onLeave);
  onLeaveRef.current = onLeave;

  const ping = () => {
    lastPing.current = Date.now();
    kioskApi
      .ping()
      .then((session) => {
        if (!session) onLostRef.current();
      })
      .catch(() => {
        /* offline for a moment — the next ping retries */
      });
  };

  useEffect(() => {
    const touch = () => {
      lastActivity.current = Date.now();
    };
    const events = ['pointerdown', 'keydown', 'wheel', 'touchmove'] as const;
    events.forEach((e) => window.addEventListener(e, touch, { passive: true }));
    const timer = window.setInterval(() => {
      const now = Date.now();
      const idleMs = now - lastActivity.current;
      if (idleMs >= warnAfterMs) {
        const left = graceSeconds - Math.floor((idleMs - warnAfterMs) / 1000);
        if (left <= 0) {
          setWarning(null);
          onLeaveRef.current();
        } else {
          setWarning(left);
        }
        return;
      }
      setWarning(null);
      if (lastActivity.current > lastPing.current && now - lastPing.current >= 30_000) ping();
    }, 1000);
    return () => {
      events.forEach((e) => window.removeEventListener(e, touch));
      window.clearInterval(timer);
    };
  }, [warnAfterMs]);

  return (
    <Dialog open={warning !== null}>
      <DialogTitle>{t('kiosk.app.idle.title')}</DialogTitle>
      <DialogContent>
        <Typography>{t('kiosk.app.idle.body', { seconds: warning ?? 0 })}</Typography>
      </DialogContent>
      <DialogActions>
        <Button color="error" onClick={() => onLeaveRef.current()}>
          {t('kiosk.app.idle.leave')}
        </Button>
        <Button
          variant="contained"
          size="large"
          onClick={() => {
            lastActivity.current = Date.now();
            setWarning(null);
            ping();
          }}
        >
          {t('kiosk.app.idle.stay')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

function useDebounced<T>(value: T, ms: number): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const timer = window.setTimeout(() => setDebounced(value), ms);
    return () => window.clearTimeout(timer);
  }, [value, ms]);
  return debounced;
}
