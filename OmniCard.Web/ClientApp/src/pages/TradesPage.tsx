import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import {
  Accordion,
  AccordionDetails,
  AccordionSummary,
  Chip,
  CircularProgress,
  Divider,
  Stack,
  Typography,
} from '@mui/material';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import PhotoCameraIcon from '@mui/icons-material/PhotoCamera';
import { api } from '../api/client';
import { TradeBuilder } from '../components/dialogs/TradeBuilder';
import { useFormatters } from '../i18n/format';

export function TradesPage() {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const money = (n?: number | null) => (n == null ? '—' : fmt.money(n));
  const { data, isLoading } = useQuery({ queryKey: ['trades'], queryFn: api.trades });

  return (
    <Stack spacing={2}>
      <Typography variant="h4">{t('trades.title')}</Typography>

      <TradeBuilder />

      <Typography variant="h6" sx={{ mt: 1 }}>
        {t('trades.history')}
      </Typography>
      <Typography variant="body2" color="text.secondary" sx={{ mt: -1 }}>
        {t('trades.historyCaption')}
      </Typography>

      {isLoading || !data ? (
        <CircularProgress />
      ) : data.length === 0 ? (
        <Typography color="text.secondary">{t('trades.noneYet')}</Typography>
      ) : (
        data.map((trade) => (
          <Accordion key={trade.id} variant="outlined" disableGutters>
            <AccordionSummary expandIcon={<ExpandMoreIcon />}>
              <Stack
                direction="row"
                spacing={1}
                alignItems="center"
                sx={{ width: '100%', flexWrap: 'wrap' }}
                useFlexGap
              >
                <Typography sx={{ flexGrow: 1, fontWeight: 600 }}>{trade.label}</Typography>
                {trade.hasPhoto && <PhotoCameraIcon fontSize="small" color="disabled" />}
                {trade.valueDelta != null && (
                  <Chip
                    size="small"
                    color={trade.valueDelta >= 0 ? 'success' : 'warning'}
                    label={`${trade.valueDelta >= 0 ? '+' : ''}${fmt.money(trade.valueDelta)}`}
                  />
                )}
                {trade.replacementCount > 0 && (
                  <Chip
                    size="small"
                    variant="outlined"
                    label={t('trades.replaced', { count: trade.replacementCount })}
                  />
                )}
                <Typography variant="caption" color="text.secondary">
                  {fmt.date(trade.createdAt)}
                </Typography>
              </Stack>
            </AccordionSummary>
            <AccordionDetails>
              <Stack spacing={1}>
                <Stack direction="row" spacing={2}>
                  <Typography variant="body2">
                    {t('trades.out')} <strong>{money(trade.outgoingValue)}</strong>
                  </Typography>
                  <Typography variant="body2">
                    {t('trades.received')} <strong>{money(trade.receivedValue)}</strong>
                  </Typography>
                </Stack>
                {trade.note && (
                  <Typography variant="body2" color="text.secondary">
                    {trade.note}
                  </Typography>
                )}
                <Divider />
                <Typography variant="subtitle2">{t('trades.tradedAway')}</Typography>
                {trade.outgoingCards.map((c, i) => (
                  <Typography key={i} variant="body2" color="text.secondary">
                    {c.cardName}
                    {c.setCode ? ` · ${c.setCode.toUpperCase()}` : ''}
                    {c.collectorNumber ? ` #${c.collectorNumber}` : ''}
                    {c.foil ? t('trades.foilSuffix') : ''}
                    {c.isOffDatabase ? t('trades.offCatalogSuffix') : ''}
                    {c.estimatedValue != null ? ` — ${fmt.money(c.estimatedValue)}` : ''}
                  </Typography>
                ))}
              </Stack>
            </AccordionDetails>
          </Accordion>
        ))
      )}
    </Stack>
  );
}
