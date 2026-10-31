import { useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { Alert, Box, Button, CircularProgress, Stack, Typography } from '@mui/material';
import HistoryIcon from '@mui/icons-material/History';
import { useTranslation } from 'react-i18next';
import { api } from '../api/client';
import type { IntakePriceBasis } from '../api/types';
import { useFormatters } from '../i18n/format';
import { usePermissions } from '../context/usePermissions';
import { ScanPage, type IntakeScanCard } from './ScanPage';
import { IntakeQuoteView, type IntakeBought, type IntakeLineChoice } from '../components/intake/IntakeQuoteView';

/**
 * Intake — buying singles over the counter. Step one is the Scan page in intake mode (scan, match,
 * correct); "Complete" opens step two, the quote. The Scan page stays mounted (just hidden) while the
 * quote is open, so going back keeps every scan; once the cards are bought they leave the scan list.
 */
export function IntakePage() {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const { can } = usePermissions();
  const settings = useQuery({ queryKey: ['intake-settings'], queryFn: api.intakeSettings });
  const [cards, setCards] = useState<IntakeScanCard[] | null>(null);
  const [choices, setChoices] = useState<Record<string, IntakeLineChoice>>({});
  const [basis, setBasis] = useState<IntakePriceBasis>('Market');
  const [boughtKeys, setBoughtKeys] = useState<string[]>();
  const [bought, setBought] = useState<IntakeBought | null>(null);

  const onBought = (b: IntakeBought) => {
    const keys = (cards ?? []).map((c) => c.key);
    setBoughtKeys(keys);
    setChoices((prev) => Object.fromEntries(Object.entries(prev).filter(([k]) => !keys.includes(k))));
    setCards(null);
    setBought(b);
    window.scrollTo({ top: 0 });
  };

  return (
    <Stack spacing={2}>
      <Stack direction="row" alignItems="center" spacing={1}>
        <Typography variant="h4" sx={{ flexGrow: 1 }}>
          {t('intake.title')}
        </Typography>
        {can('intake.history') && (
          <Button component={RouterLink} to="/intake/history" startIcon={<HistoryIcon />}>
            {t('intake.history')}
          </Button>
        )}
      </Stack>
      {!cards && (
        <Typography variant="body2" color="text.secondary" sx={{ mt: -1 }}>
          {t('intake.intro')}
        </Typography>
      )}
      {settings.data && settings.data.locationId == null && can('intake.buy') && (
        <Alert severity="warning">{t('intake.noLocation')}</Alert>
      )}
      {bought && !cards && (
        <Alert
          severity="success"
          onClose={() => setBought(null)}
          action={
            can('intake.history') ? (
              <Button color="inherit" size="small" component={RouterLink} to={`/intake/history?id=${bought.result.intakeId}`}>
                {t('intake.buy.viewHistory')}
              </Button>
            ) : undefined
          }
        >
          {t('intake.buy.done', {
            count: bought.result.cardCount,
            cash: fmt.money(bought.result.cashPaid),
            trade: fmt.money(bought.result.tradePaid),
          })}
          {bought.customerName != null && bought.result.customerCreditBalance != null && bought.result.tradePaid > 0 && (
            <Box component="span" sx={{ display: 'block' }}>
              {t('intake.buy.newBalance', {
                name: bought.customerName,
                amount: fmt.money(bought.result.customerCreditBalance),
              })}
            </Box>
          )}
        </Alert>
      )}

      <Box sx={{ display: cards ? 'none' : 'block' }}>
        <ScanPage
          intake={{
            onComplete: (done) => {
              setBought(null);
              setCards(done);
              window.scrollTo({ top: 0 });
            },
            boughtKeys,
          }}
        />
      </Box>

      {cards &&
        (settings.data ? (
          <IntakeQuoteView
            cards={cards}
            choices={choices}
            onChoicesChange={setChoices}
            basis={basis}
            onBasisChange={setBasis}
            settings={settings.data}
            onBack={() => setCards(null)}
            onBought={onBought}
          />
        ) : settings.isError ? (
          <Alert severity="error">{(settings.error as Error).message}</Alert>
        ) : (
          <CircularProgress />
        ))}
    </Stack>
  );
}
