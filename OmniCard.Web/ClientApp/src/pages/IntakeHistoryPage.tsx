import { useState } from 'react';
import { Link as RouterLink, useSearchParams } from 'react-router-dom';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
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
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TablePagination,
  TableRow,
  Typography,
} from '@mui/material';
import ArrowBackIcon from '@mui/icons-material/ArrowBack';
import { useTranslation } from 'react-i18next';
import { api } from '../api/client';
import { useFormatters } from '../i18n/format';
import { useIsPhone } from '../lib/responsive';

/** Intake history (Intake ▸ History): every buy, newest first — who took the cards in, from whom, at what
 * price — with each buy's cards and the settings in force at the time. */
export function IntakeHistoryPage() {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const [params, setParams] = useSearchParams();
  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = useState(25);
  const history = useQuery({
    queryKey: ['intake-history', page, pageSize],
    queryFn: () => api.intakeHistory({ page, pageSize }),
    placeholderData: keepPreviousData,
  });
  const openId = Number(params.get('id')) || null;
  const open = (id: number | null) => {
    const next = new URLSearchParams(params);
    if (id == null) next.delete('id');
    else next.set('id', String(id));
    setParams(next, { replace: true });
  };

  return (
    <Stack spacing={2}>
      <Box>
        <Button component={RouterLink} to="/intake" startIcon={<ArrowBackIcon />} sx={{ mb: 1 }}>
          {t('intake.historyPage.back')}
        </Button>
        <Typography variant="h4">{t('intake.historyPage.title')}</Typography>
        <Typography variant="body2" color="text.secondary">
          {t('intake.historyPage.intro')}
        </Typography>
      </Box>
      {history.isError && <Alert severity="error">{(history.error as Error).message}</Alert>}
      {history.isLoading && <CircularProgress />}
      {history.data && history.data.total === 0 && <Alert severity="info">{t('intake.historyPage.empty')}</Alert>}
      {history.data && history.data.total > 0 && (
        <Paper variant="outlined">
          <TableContainer>
            <Table size="small">
              <TableHead>
                <TableRow>
                  <TableCell>{t('intake.historyPage.colDate')}</TableCell>
                  <TableCell>{t('intake.historyPage.colStaff')}</TableCell>
                  <TableCell>{t('intake.historyPage.colCustomer')}</TableCell>
                  <TableCell align="right">{t('intake.historyPage.colCards')}</TableCell>
                  <TableCell align="right">{t('intake.historyPage.colMarket')}</TableCell>
                  <TableCell align="right">{t('intake.historyPage.colCash')}</TableCell>
                  <TableCell align="right">{t('intake.historyPage.colTrade')}</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {history.data.items.map((i) => (
                  <TableRow key={i.id} hover sx={{ cursor: 'pointer' }} onClick={() => open(i.id)}>
                    <TableCell sx={{ whiteSpace: 'nowrap' }}>{fmt.dateTime(i.createdAt)}</TableCell>
                    <TableCell>{i.createdByName}</TableCell>
                    <TableCell>{i.customerName ?? t('intake.historyPage.noCustomer')}</TableCell>
                    <TableCell align="right">{fmt.number(i.cardCount)}</TableCell>
                    <TableCell align="right">{fmt.money(i.marketTotal)}</TableCell>
                    <TableCell align="right">{fmt.money(i.cashPaid)}</TableCell>
                    <TableCell align="right">{fmt.money(i.tradePaid)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
          <TablePagination
            component="div"
            count={history.data.total}
            page={page}
            rowsPerPage={pageSize}
            rowsPerPageOptions={[25, 50, 100]}
            onPageChange={(_, p) => setPage(p)}
            onRowsPerPageChange={(e) => {
              setPageSize(Number(e.target.value));
              setPage(0);
            }}
          />
        </Paper>
      )}
      <IntakeDetailDialog id={openId} onClose={() => open(null)} />
    </Stack>
  );
}

function IntakeDetailDialog({ id, onClose }: { id: number | null; onClose: () => void }) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const isPhone = useIsPhone();
  const detail = useQuery({ queryKey: ['intake-detail', id], queryFn: () => api.intakeDetail(id!), enabled: id != null });
  const d = detail.data;
  return (
    <Dialog open={id != null} onClose={onClose} maxWidth="lg" fullWidth fullScreen={isPhone}>
      <DialogTitle>{t('intake.historyPage.detailTitle', { id })}</DialogTitle>
      <DialogContent dividers>
        {detail.isLoading && <CircularProgress />}
        {detail.isError && <Alert severity="error">{(detail.error as Error).message}</Alert>}
        {d && (
          <Stack spacing={2}>
            <Stack direction="row" spacing={3} flexWrap="wrap" useFlexGap>
              <Field label={t('intake.historyPage.colDate')} value={fmt.dateTime(d.summary.createdAt)} />
              <Field label={t('intake.historyPage.colStaff')} value={d.summary.createdByName} />
              <Field
                label={t('intake.historyPage.colCustomer')}
                value={d.summary.customerName ?? t('intake.historyPage.noCustomer')}
              />
              <Field label={t('intake.historyPage.location')} value={d.summary.locationName} />
              <Field
                label={t('intake.historyPage.basis')}
                value={d.summary.priceBasis === 'Low' ? t('intake.quote.basisLow') : t('intake.quote.basisMarket')}
              />
              <Field
                label={t('intake.quote.offer')}
                value={t('intake.historyPage.percents', {
                  cash: fmt.percent(d.cashPercent / 100, 2),
                  trade: fmt.percent(d.tradePercent / 100, 2),
                })}
              />
            </Stack>
            <Stack direction="row" spacing={3} flexWrap="wrap" useFlexGap>
              <Field label={t('intake.historyPage.colMarket')} value={fmt.money(d.summary.marketTotal)} />
              <Field label={t('intake.historyPage.colCash')} value={fmt.money(d.summary.cashPaid)} />
              <Field label={t('intake.historyPage.colTrade')} value={fmt.money(d.summary.tradePaid)} />
            </Stack>
            {d.note && <Field label={t('intake.historyPage.note')} value={d.note} />}
            <TableContainer component={Paper} variant="outlined">
              {/* Scrolls sideways on a phone rather than squeezing the card names into a column. */}
              <Table size="small" sx={{ minWidth: 760 }}>
                <TableHead>
                  <TableRow>
                    <TableCell>{t('intake.historyPage.colCard')}</TableCell>
                    <TableCell>{t('intake.historyPage.colCondition')}</TableCell>
                    <TableCell align="right">{t('intake.historyPage.colQty')}</TableCell>
                    <TableCell align="right">{t('intake.historyPage.colMarket')}</TableCell>
                    <TableCell align="right">{t('intake.historyPage.colBase')}</TableCell>
                    <TableCell align="right">{t('intake.historyPage.colPaid')}</TableCell>
                    <TableCell>{t('intake.historyPage.colPayout')}</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {d.lines.map((l) => (
                    <TableRow key={l.id}>
                      <TableCell>
                        {l.lotId ? (
                          <RouterLink to={`/card/${l.lotId}`}>{l.name}</RouterLink>
                        ) : (
                          l.name
                        )}
                        <Typography variant="caption" color="text.secondary" component="div">
                          {l.setCode.toUpperCase()} #{l.collectorNumber}
                          {l.isFoil ? ` · ${l.foilType ?? 'Foil'}` : ''}
                        </Typography>
                      </TableCell>
                      <TableCell>
                        {l.condition} ({fmt.percent(l.conditionPercent / 100, 2)})
                      </TableCell>
                      <TableCell align="right">{fmt.number(l.quantity)}</TableCell>
                      <TableCell align="right">{fmt.money(l.marketPrice)}</TableCell>
                      <TableCell align="right">
                        {fmt.money(l.basePrice)}
                        {l.isManualPrice && (
                          <Chip size="small" label={t('intake.historyPage.manual')} sx={{ ml: 0.5 }} />
                        )}
                        {l.isBulk && <Chip size="small" label={t('intake.historyPage.bulk')} sx={{ ml: 0.5 }} />}
                      </TableCell>
                      <TableCell align="right">{fmt.money(l.unitPaid)}</TableCell>
                      <TableCell>
                        {l.payout === 'Trade' ? t('intake.historyPage.payoutTrade') : t('intake.historyPage.payoutCash')}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>
          </Stack>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('common.actions.close')}</Button>
      </DialogActions>
    </Dialog>
  );
}

function Field({ label, value }: { label: string; value: string }) {
  return (
    <Box>
      <Typography variant="overline" color="text.secondary" component="div">
        {label}
      </Typography>
      <Typography variant="body2">{value}</Typography>
    </Box>
  );
}
