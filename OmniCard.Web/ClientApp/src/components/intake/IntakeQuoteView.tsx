import { useEffect, useMemo, useState } from 'react';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Autocomplete,
  Box,
  Button,
  Checkbox,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Divider,
  FormControlLabel,
  InputAdornment,
  LinearProgress,
  Paper,
  Radio,
  RadioGroup,
  Stack,
  TextField,
  ToggleButton,
  ToggleButtonGroup,
  Typography,
} from '@mui/material';
import ArrowBackIcon from '@mui/icons-material/ArrowBack';
import PersonIcon from '@mui/icons-material/Person';
import ShoppingCartCheckoutIcon from '@mui/icons-material/ShoppingCartCheckout';
import { useTranslation } from 'react-i18next';
import { api, ApiError } from '../../api/client';
import type {
  CustomerDto,
  IntakeAcceptResultDto,
  IntakePayout,
  IntakePriceBasis,
  IntakeQuoteDto,
  IntakeQuoteLineDto,
  IntakeQuoteLineRequest,
  IntakeSettingsDto,
} from '../../api/types';
import { useFormatters } from '../../i18n/format';
import { usePermissions } from '../../context/usePermissions';
import { useIsPhone } from '../../lib/responsive';
import { CardImage } from '../CardImage';
import type { IntakeScanCard } from '../../pages/ScanPage';

/** Per-card offers keep sub-cent precision when only the totals are rounded; show them rounded DOWN
 * like the totals, so a card never displays more than it will pay. */
const floorCents = (value: number) => Math.floor(value * 100 + 1e-9) / 100;

/** The staff's per-card choices on a quote, keyed by the scan's key. */
export interface IntakeLineChoice {
  payout?: IntakePayout;
  /** undefined = the default (bulk under the Exclude rule starts left out). */
  excluded?: boolean;
  manualPrice?: number | null;
}

/** The finished buy, as the page reports it. */
export interface IntakeBought {
  result: IntakeAcceptResultDto;
  customerName: string | null;
}

/**
 * Step two of an intake: the quote for the confirmed scans. Prices come from the server (market or
 * low, the admin's percentages, condition, bulk and rounding rules) and refresh as the staff member
 * changes the basis, leaves cards out, types a missing price or picks cash/trade per card. From here
 * they can hand the phone to the seller (Customer view) and buy the cards.
 */
export function IntakeQuoteView({
  cards,
  choices,
  onChoicesChange,
  basis,
  onBasisChange,
  settings,
  onBack,
  onBought,
}: {
  cards: IntakeScanCard[];
  choices: Record<string, IntakeLineChoice>;
  onChoicesChange: (next: Record<string, IntakeLineChoice>) => void;
  basis: IntakePriceBasis;
  onBasisChange: (basis: IntakePriceBasis) => void;
  settings: IntakeSettingsDto;
  onBack: () => void;
  onBought: (bought: IntakeBought) => void;
}) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const { can } = usePermissions();
  const [customerViewOpen, setCustomerViewOpen] = useState(false);
  const [buyOpen, setBuyOpen] = useState(false);

  const lines: IntakeQuoteLineRequest[] = useMemo(
    () =>
      cards.map((c) => ({
        item: c.item,
        payout: choices[c.key]?.payout ?? 'Cash',
        excluded: choices[c.key]?.excluded ?? null,
        manualPrice: choices[c.key]?.manualPrice ?? null,
      })),
    [cards, choices],
  );
  const quote = useQuery({
    queryKey: ['intake-quote', basis, lines],
    queryFn: () => api.intakeQuote(basis, lines),
    placeholderData: keepPreviousData,
  });
  const q = quote.data;

  const choose = (key: string, patch: IntakeLineChoice) =>
    onChoicesChange({ ...choices, [key]: { ...choices[key], ...patch } });
  const payAll = (payout: IntakePayout) =>
    onChoicesChange(Object.fromEntries(cards.map((c) => [c.key, { ...choices[c.key], payout }])));

  const pct = (value: number) => fmt.percent(value / 100, 2);
  const canBuy = can('intake.buy');
  const buyable = !!q && q.cardCount > 0 && !q.needsPrices && !quote.isFetching && settings.locationId != null;

  if (cards.length === 0) {
    return (
      <Stack spacing={2} alignItems="flex-start">
        <Button startIcon={<ArrowBackIcon />} onClick={onBack}>
          {t('intake.quote.back')}
        </Button>
        <Alert severity="info">{t('intake.quote.noCards')}</Alert>
      </Stack>
    );
  }

  return (
    <Stack spacing={2}>
      <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
        <Button startIcon={<ArrowBackIcon />} onClick={onBack}>
          {t('intake.quote.back')}
        </Button>
        <Box sx={{ flexGrow: 1 }} />
        <Typography variant="body2" color="text.secondary">
          {t('intake.quote.basis')}
        </Typography>
        <ToggleButtonGroup
          size="small"
          exclusive
          value={basis}
          onChange={(_, v: IntakePriceBasis | null) => v && onBasisChange(v)}
        >
          <ToggleButton value="Market">{t('intake.quote.basisMarket')}</ToggleButton>
          <ToggleButton value="Low">{t('intake.quote.basisLow')}</ToggleButton>
        </ToggleButtonGroup>
      </Stack>

      {quote.isError && <Alert severity="error">{(quote.error as Error).message}</Alert>}
      {q && (
        <Paper variant="outlined" sx={{ p: 2, position: { xs: 'static', sm: 'sticky' }, top: 56, zIndex: 1 }}>
          {quote.isFetching && <LinearProgress sx={{ mb: 1 }} />}
          <QuoteTotals quote={q} />
          {(q.cashPaid > 0 || q.tradePaid > 0) && (
            <Typography variant="body2" sx={{ mt: 1 }}>
              {t('intake.quote.pay')}:{' '}
              {[
                q.cashPaid > 0 ? t('intake.quote.payCash', { amount: fmt.money(q.cashPaid) }) : null,
                q.tradePaid > 0 ? t('intake.quote.payTrade', { amount: fmt.money(q.tradePaid) }) : null,
              ]
                .filter(Boolean)
                .join(' + ')}
            </Typography>
          )}
          {q.needsPrices && (
            <Alert severity="warning" sx={{ mt: 1 }}>
              {t('intake.quote.needsPrices')}
            </Alert>
          )}
          <Stack direction="row" spacing={1} sx={{ mt: 2 }} flexWrap="wrap" useFlexGap>
            <Button size="small" onClick={() => payAll('Cash')}>
              {t('intake.quote.allCash')}
            </Button>
            <Button size="small" onClick={() => payAll('Trade')}>
              {t('intake.quote.allTrade')}
            </Button>
            <Box sx={{ flexGrow: 1 }} />
            <Button variant="outlined" startIcon={<PersonIcon />} onClick={() => setCustomerViewOpen(true)}>
              {t('intake.quote.customerView')}
            </Button>
            {canBuy && (
              <Button
                variant="contained"
                startIcon={<ShoppingCartCheckoutIcon />}
                disabled={!buyable}
                onClick={() => setBuyOpen(true)}
              >
                {t('intake.quote.buy')}
              </Button>
            )}
          </Stack>
          {!canBuy && (
            <Typography variant="caption" color="text.secondary" component="div" sx={{ mt: 1 }}>
              {t('intake.quote.noBuyPermission')}
            </Typography>
          )}
        </Paper>
      )}
      {!q && quote.isLoading && <LinearProgress />}

      {q && (
        <Stack spacing={1}>
          {cards.map((card, i) => {
            const line = q.lines[i];
            if (!line) return null;
            return (
              <QuoteLine
                key={card.key}
                card={card}
                line={line}
                singleOffer={q.singleOffer}
                payout={choices[card.key]?.payout ?? 'Cash'}
                onPayout={(payout) => choose(card.key, { payout })}
                onInclude={(include) => choose(card.key, { excluded: !include })}
                onManualPrice={(manualPrice) => choose(card.key, { manualPrice })}
                pct={pct}
              />
            );
          })}
        </Stack>
      )}

      {q && (
        <CustomerViewDialog
          open={customerViewOpen}
          onClose={() => setCustomerViewOpen(false)}
          cards={cards}
          quote={q}
          settings={settings}
        />
      )}
      {q && canBuy && (
        <BuyDialog
          open={buyOpen}
          onClose={() => setBuyOpen(false)}
          quote={q}
          basis={basis}
          lines={lines}
          settings={settings}
          onRequote={() => void quote.refetch()}
          onBought={(bought) => {
            setBuyOpen(false);
            onBought(bought);
          }}
        />
      )}
    </Stack>
  );
}

/** Market total plus the cash/trade offers — or one offer when both pay the same percentage. */
function QuoteTotals({ quote, large = false }: { quote: IntakeQuoteDto; large?: boolean }) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const amountVariant = large ? 'h3' : 'h5';
  const offers = quote.singleOffer
    ? [{ label: t('intake.quote.offer'), amount: quote.cashOffer, percent: quote.cashPercent }]
    : [
        { label: t('intake.quote.cashOffer'), amount: quote.cashOffer, percent: quote.cashPercent },
        { label: t('intake.quote.tradeOffer'), amount: quote.tradeOffer, percent: quote.tradePercent },
      ];
  return (
    <Stack direction="row" spacing={3} flexWrap="wrap" useFlexGap>
      <Box>
        <Typography variant="overline" color="text.secondary">
          {t('intake.quote.marketTotal')}
        </Typography>
        <Typography variant={amountVariant}>{fmt.money(quote.marketTotal)}</Typography>
        <Typography variant="caption" color="text.secondary">
          {t('intake.quote.cards', { count: quote.cardCount })}
        </Typography>
      </Box>
      {offers.map((o) => (
        <Box key={o.label}>
          <Typography variant="overline" color="text.secondary">
            {o.label}
          </Typography>
          <Typography variant={amountVariant} color="primary">
            {fmt.money(o.amount)}
          </Typography>
          <Typography variant="caption" color="text.secondary">
            {t('intake.quote.percentOf', { percent: fmt.percent(o.percent / 100, 2) })}
          </Typography>
        </Box>
      ))}
    </Stack>
  );
}

function cardSubtitle(card: IntakeScanCard) {
  const it = card.item;
  return [`${it.setCode.toUpperCase()} #${it.collectorNumber}`, it.isFoil ? (it.foilType ?? 'Foil') : null]
    .filter(Boolean)
    .join(' · ');
}

function QuoteLine({
  card,
  line,
  singleOffer,
  payout,
  onPayout,
  onInclude,
  onManualPrice,
  pct,
}: {
  card: IntakeScanCard;
  line: IntakeQuoteLineDto;
  singleOffer: boolean;
  payout: IntakePayout;
  onPayout: (payout: IntakePayout) => void;
  onInclude: (include: boolean) => void;
  onManualPrice: (price: number | null) => void;
  pct: (value: number) => string;
}) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const it = card.item;
  const qty = Math.max(1, it.quantity);
  const showPriceField = line.needsPrice || line.isManualPrice;
  return (
    <Paper variant="outlined" sx={{ p: 1.5, opacity: line.excluded ? 0.55 : 1 }}>
      <Stack direction="row" spacing={1.5} alignItems="flex-start">
        <CardImage
          src={it.imageUri ?? card.previewUrl}
          alt={it.name}
          foil={it.isFoil}
          wrapperSx={{ flexShrink: 0 }}
          sx={{ width: 56, borderRadius: 1 }}
        />
        <Box sx={{ flexGrow: 1, minWidth: 0 }}>
          <Stack direction="row" alignItems="flex-start" spacing={1}>
            <Box sx={{ flexGrow: 1, minWidth: 0 }}>
              <Typography variant="subtitle2" noWrap>
                {it.name}
                {qty > 1 && (
                  <Typography component="span" variant="body2" color="text.secondary">
                    {' '}
                    {t('intake.quote.times', { count: qty })}
                  </Typography>
                )}
              </Typography>
              <Typography variant="caption" color="text.secondary" component="div" noWrap>
                {cardSubtitle(card)} ·{' '}
                {t('intake.quote.condition', { condition: it.condition, percent: pct(line.conditionPercent) })}
              </Typography>
            </Box>
            <FormControlLabel
              sx={{ mr: 0 }}
              control={<Checkbox size="small" checked={!line.excluded} onChange={(e) => onInclude(e.target.checked)} />}
              label=""
              aria-label={t('intake.quote.include')}
            />
          </Stack>

          <Stack direction="row" spacing={0.5} flexWrap="wrap" useFlexGap sx={{ mt: 0.5 }}>
            {line.marketPrice != null ? (
              <Chip size="small" variant="outlined" label={t('intake.quote.market', { price: fmt.money(line.marketPrice) })} />
            ) : (
              <Chip size="small" color="warning" variant="outlined" label={t('intake.quote.noPrice')} />
            )}
            {line.lowPrice != null && (
              <Chip size="small" variant="outlined" label={t('intake.quote.low', { price: fmt.money(line.lowPrice) })} />
            )}
            {line.usedMarketFallback && <Chip size="small" label={t('intake.quote.fallback')} />}
            {line.isBulk && <Chip size="small" color="info" label={t('intake.quote.bulk')} />}
            {line.isManualPrice && <Chip size="small" color="secondary" label={t('intake.quote.manual')} />}
            {line.excluded && <Chip size="small" label={t('intake.quote.excluded')} />}
          </Stack>

          {showPriceField && !line.excluded && (
            <ManualPriceField value={line.isManualPrice ? (line.basePrice ?? null) : null} onCommit={onManualPrice} />
          )}

          {!line.excluded && !line.needsPrice && (
            <Stack direction="row" spacing={1} alignItems="center" sx={{ mt: 1 }} flexWrap="wrap" useFlexGap>
              <Typography variant="body2" sx={{ flexGrow: 1 }}>
                {singleOffer
                  ? t('intake.quote.offerEach', { price: fmt.money(floorCents(line.cashUnit)) })
                  : `${t('intake.quote.cashEach', { price: fmt.money(floorCents(line.cashUnit)) })} · ${t('intake.quote.tradeEach', {
                      price: fmt.money(floorCents(line.tradeUnit)),
                    })}`}
              </Typography>
              <ToggleButtonGroup
                size="small"
                exclusive
                value={payout}
                aria-label={t('intake.quote.payout')}
                onChange={(_, v: IntakePayout | null) => v && onPayout(v)}
              >
                <ToggleButton value="Cash">{t('intake.quote.cashOffer')}</ToggleButton>
                <ToggleButton value="Trade">{t('intake.quote.tradeOffer')}</ToggleButton>
              </ToggleButtonGroup>
            </Stack>
          )}
        </Box>
      </Stack>
    </Paper>
  );
}

/** A price typed for a card the catalog can't price; committed on blur / Enter so typing doesn't
 * re-quote on every keystroke. Clearing it goes back to the catalog price. */
function ManualPriceField({ value, onCommit }: { value: number | null; onCommit: (price: number | null) => void }) {
  const { t } = useTranslation();
  const [text, setText] = useState(value == null ? '' : String(value));
  useEffect(() => setText(value == null ? '' : String(value)), [value]);
  const commit = () => {
    const trimmed = text.trim();
    const parsed = trimmed === '' ? null : Number(trimmed);
    if (parsed !== null && (Number.isNaN(parsed) || parsed < 0)) return;
    if (parsed !== value) onCommit(parsed);
  };
  return (
    <TextField
      size="small"
      type="number"
      label={t('intake.quote.setPrice')}
      value={text}
      onChange={(e) => setText(e.target.value)}
      onBlur={commit}
      onKeyDown={(e) => {
        if (e.key === 'Enter') commit();
      }}
      inputProps={{ min: 0, step: 0.01, inputMode: 'decimal' }}
      InputProps={{ startAdornment: <InputAdornment position="start">$</InputAdornment> }}
      sx={{ mt: 1, width: 160 }}
    />
  );
}

/** Full screen, for handing the phone to the seller: the offer totals, plus whatever per-card detail the
 * admin chose to show (Settings ▸ Intake ▸ Customer view). */
function CustomerViewDialog({
  open,
  onClose,
  cards,
  quote,
  settings,
}: {
  open: boolean;
  onClose: () => void;
  cards: IntakeScanCard[];
  quote: IntakeQuoteDto;
  settings: IntakeSettingsDto;
}) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const view = settings.customerView;
  const shown = cards.map((card, i) => ({ card, line: quote.lines[i] })).filter((x) => x.line && !x.line.excluded);
  const showCards = view.showCardImages || view.showCardDetails || view.showCardPrices || view.showCardOffers;
  return (
    <Dialog open={open} onClose={onClose} fullScreen>
      <DialogTitle>{t('intake.customerView.title')}</DialogTitle>
      <DialogContent dividers>
        <Stack spacing={3} sx={{ maxWidth: 720, mx: 'auto' }}>
          <Stack direction="row" spacing={4} flexWrap="wrap" useFlexGap justifyContent="center">
            {view.showMarketTotal && (
              <Box textAlign="center">
                <Typography variant="overline" color="text.secondary">
                  {t('intake.customerView.marketTotal')}
                </Typography>
                <Typography variant="h4">{fmt.money(quote.marketTotal)}</Typography>
              </Box>
            )}
            {quote.singleOffer ? (
              <Box textAlign="center">
                <Typography variant="overline" color="text.secondary">
                  {t('intake.customerView.offer')}
                </Typography>
                <Typography variant="h3" color="primary">
                  {fmt.money(quote.cashOffer)}
                </Typography>
              </Box>
            ) : (
              <>
                <Box textAlign="center">
                  <Typography variant="overline" color="text.secondary">
                    {t('intake.customerView.cash')}
                  </Typography>
                  <Typography variant="h3" color="primary">
                    {fmt.money(quote.cashOffer)}
                  </Typography>
                </Box>
                <Box textAlign="center">
                  <Typography variant="overline" color="text.secondary">
                    {t('intake.customerView.trade')}
                  </Typography>
                  <Typography variant="h3" color="primary">
                    {fmt.money(quote.tradeOffer)}
                  </Typography>
                </Box>
              </>
            )}
          </Stack>
          <Typography variant="body2" color="text.secondary" textAlign="center">
            {t('intake.customerView.cardsSummary', { count: quote.cardCount })}
          </Typography>

          {showCards && (
            <Stack divider={<Divider flexItem />} spacing={1}>
              {shown.map(({ card, line }) => (
                <Stack key={card.key} direction="row" spacing={1.5} alignItems="center">
                  {view.showCardImages && (
                    <CardImage
                      src={card.item.imageUri ?? card.previewUrl}
                      alt={card.item.name}
                      foil={card.item.isFoil}
                      wrapperSx={{ flexShrink: 0 }}
                      sx={{ width: 64, borderRadius: 1 }}
                    />
                  )}
                  <Box sx={{ flexGrow: 1, minWidth: 0 }}>
                    <Typography variant="subtitle1" noWrap>
                      {card.item.name}
                      {card.item.quantity > 1 && ` × ${fmt.number(card.item.quantity)}`}
                    </Typography>
                    {view.showCardDetails && (
                      <Typography variant="body2" color="text.secondary" noWrap>
                        {cardSubtitle(card)} · {card.item.condition}
                      </Typography>
                    )}
                  </Box>
                  <Box textAlign="right" sx={{ flexShrink: 0 }}>
                    {view.showCardPrices && line.basePrice != null && (
                      <Typography variant="body2" color="text.secondary">
                        {fmt.money(line.basePrice)}
                      </Typography>
                    )}
                    {view.showCardOffers &&
                      (quote.singleOffer ? (
                        <Typography variant="body1">{fmt.money(floorCents(line.cashUnit))}</Typography>
                      ) : (
                        <>
                          <Typography variant="body2">
                            {t('intake.customerView.cardCash', { price: fmt.money(floorCents(line.cashUnit)) })}
                          </Typography>
                          <Typography variant="body2">
                            {t('intake.customerView.cardTrade', { price: fmt.money(floorCents(line.tradeUnit)) })}
                          </Typography>
                        </>
                      ))}
                  </Box>
                </Stack>
              ))}
            </Stack>
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button variant="contained" size="large" onClick={onClose}>
          {t('intake.customerView.close')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

type CustomerMode = 'existing' | 'new' | 'none';

/** Who the cards are bought from, plus a note; buys at the quoted totals (the server re-prices and
 * refuses with 409 if they moved). A customer is required whenever store credit is paid. */
function BuyDialog({
  open,
  onClose,
  quote,
  basis,
  lines,
  settings,
  onRequote,
  onBought,
}: {
  open: boolean;
  onClose: () => void;
  quote: IntakeQuoteDto;
  basis: IntakePriceBasis;
  lines: IntakeQuoteLineRequest[];
  settings: IntakeSettingsDto;
  onRequote: () => void;
  onBought: (bought: IntakeBought) => void;
}) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const qc = useQueryClient();
  const isPhone = useIsPhone();
  const needsCustomer = quote.tradePaid > 0;
  const [mode, setMode] = useState<CustomerMode>('existing');
  const [customer, setCustomer] = useState<CustomerDto | null>(null);
  const [name, setName] = useState('');
  const [note, setNote] = useState('');
  const customers = useQuery({ queryKey: ['customers'], queryFn: api.customers, enabled: open });

  useEffect(() => {
    if (needsCustomer && mode === 'none') setMode('existing');
  }, [needsCustomer, mode]);

  const buy = useMutation({
    mutationFn: async () => {
      let customerId: number | null = null;
      let customerName: string | null = null;
      if (mode === 'existing' && customer) {
        customerId = customer.id;
        customerName = customer.name;
      } else if (mode === 'new') {
        const created = await api.customerCreate({ name: name.trim() });
        customerId = created.id;
        customerName = created.name;
        // Switch to it so a retry (e.g. after a price change) doesn't create the customer twice.
        setCustomer(created);
        setMode('existing');
      }
      const result = await api.intakeAccept({
        priceBasis: basis,
        lines,
        customerId,
        note: note.trim() || null,
        expectedCashPaid: quote.cashPaid,
        expectedTradePaid: quote.tradePaid,
      });
      return { result, customerName };
    },
    onSuccess: (bought) => {
      void qc.invalidateQueries({ queryKey: ['customers'] });
      void qc.invalidateQueries({ queryKey: ['collection'] });
      void qc.invalidateQueries({ queryKey: ['locations'] });
      void qc.invalidateQueries({ queryKey: ['dashboard'] });
      void qc.invalidateQueries({ queryKey: ['intake-history'] });
      setNote('');
      onBought(bought);
    },
    onError: (e) => {
      if (e instanceof ApiError && e.status === 409) onRequote();
    },
  });

  const customerChosen = mode === 'existing' ? customer !== null : mode === 'new' ? name.trim() !== '' : true;
  const pricesChanged =
    buy.error instanceof ApiError && buy.error.status === 409 && !!(buy.error.body as { quote?: unknown })?.quote;

  return (
    <Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth fullScreen={isPhone}>
      <DialogTitle>{t('intake.buy.title', { count: quote.cardCount })}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ pt: 1 }}>
          <Typography variant="body1">
            {[
              quote.cashPaid > 0 ? t('intake.quote.payCash', { amount: fmt.money(quote.cashPaid) }) : null,
              quote.tradePaid > 0 ? t('intake.quote.payTrade', { amount: fmt.money(quote.tradePaid) }) : null,
            ]
              .filter(Boolean)
              .join(' + ')}
          </Typography>
          <RadioGroup row value={mode} onChange={(e) => setMode(e.target.value as CustomerMode)}>
            <FormControlLabel value="existing" control={<Radio />} label={t('intake.buy.existingCustomer')} />
            <FormControlLabel value="new" control={<Radio />} label={t('intake.buy.newCustomer')} />
            {!needsCustomer && <FormControlLabel value="none" control={<Radio />} label={t('intake.buy.noCustomer')} />}
          </RadioGroup>
          {mode === 'existing' && (
            <Autocomplete
              options={customers.data ?? []}
              loading={customers.isLoading}
              value={customer}
              onChange={(_, v) => setCustomer(v)}
              getOptionLabel={(c) => c.name}
              isOptionEqualToValue={(a, b) => a.id === b.id}
              renderOption={(props, c) => (
                <li {...props} key={c.id}>
                  <Box>
                    <Typography variant="body2">{c.name}</Typography>
                    {!!c.storeCredit && (
                      <Typography variant="caption" color="text.secondary">
                        {t('intake.buy.creditBalance', { amount: fmt.money(c.storeCredit) })}
                      </Typography>
                    )}
                  </Box>
                </li>
              )}
              renderInput={(params) => <TextField {...params} label={t('intake.buy.customer')} />}
            />
          )}
          {mode === 'new' && (
            <TextField label={t('intake.buy.customerName')} value={name} onChange={(e) => setName(e.target.value)} />
          )}
          {needsCustomer && !customerChosen && <Alert severity="info">{t('intake.buy.customerRequired')}</Alert>}
          <TextField
            label={t('intake.buy.note')}
            value={note}
            onChange={(e) => setNote(e.target.value)}
            multiline
            minRows={2}
            inputProps={{ maxLength: 500 }}
          />
          {settings.locationName && (
            <Typography variant="caption" color="text.secondary">
              {t('intake.buy.location', { location: settings.locationName })}
            </Typography>
          )}
          {buy.isError && (
            <Alert severity={pricesChanged ? 'warning' : 'error'}>
              {pricesChanged ? t('intake.buy.pricesChanged') : (buy.error as Error).message}
            </Alert>
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('common.actions.cancel')}</Button>
        <Button
          variant="contained"
          disabled={buy.isPending || !customerChosen}
          onClick={() => {
            buy.reset();
            buy.mutate();
          }}
        >
          {buy.isPending ? t('intake.buy.buying') : t('intake.buy.confirm')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
