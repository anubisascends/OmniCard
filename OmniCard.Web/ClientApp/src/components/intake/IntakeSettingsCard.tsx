import { useEffect, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  CircularProgress,
  Divider,
  FormControlLabel,
  InputAdornment,
  MenuItem,
  Paper,
  Stack,
  Switch,
  TextField,
  Typography,
} from '@mui/material';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import type { IntakeCustomerViewDto, IntakeSettingsDto } from '../../api/types';
import { useFormatters } from '../../i18n/format';
import { LocationPickerField } from '../LocationPickerField';

const CONDITIONS = ['NM', 'LP', 'MP', 'HP', 'DMG'];
const ROUNDING_STEPS = [0, 0.05, 0.1, 0.25, 0.5, 1];
const VIEW_FIELDS: (keyof IntakeCustomerViewDto)[] = [
  'showCardImages',
  'showCardDetails',
  'showCardPrices',
  'showCardOffers',
  'showMarketTotal',
];

/** Settings ▸ Intake (admin only): the cash and trade percentages, condition adjustment, bulk handling,
 * rounding, where bought cards go, and what the seller sees on the customer view. */
export function IntakeSettingsCard() {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const qc = useQueryClient();
  const settings = useQuery({ queryKey: ['intake-settings'], queryFn: api.intakeSettings });
  const [draft, setDraft] = useState<IntakeSettingsDto | null>(null);

  useEffect(() => {
    if (settings.data) setDraft(settings.data);
  }, [settings.data]);

  const save = useMutation({
    mutationFn: (body: IntakeSettingsDto) => api.intakeSettingsUpdate(body),
    onSuccess: (saved) => {
      qc.setQueryData(['intake-settings'], saved);
      setDraft(saved);
    },
  });

  if (settings.isError) return <Alert severity="error">{(settings.error as Error).message}</Alert>;
  if (!draft) return <CircularProgress />;

  const dirty = JSON.stringify(draft) !== JSON.stringify(settings.data);
  const update = (patch: Partial<IntakeSettingsDto>) => {
    save.reset();
    setDraft({ ...draft, ...patch });
  };
  const num = (value: string) => (value.trim() === '' ? 0 : Number(value) || 0);
  const percentAdornment = { endAdornment: <InputAdornment position="end">%</InputAdornment> };
  const moneyAdornment = { startAdornment: <InputAdornment position="start">$</InputAdornment> };

  return (
    <Stack spacing={2} sx={{ maxWidth: 720 }}>
      <Paper variant="outlined" sx={{ p: 2 }}>
        <Typography variant="h6" gutterBottom>
          {t('intake.settings.title')}
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          {t('intake.settings.intro')}
        </Typography>

        <Stack spacing={2}>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <TextField
              type="number"
              label={t('intake.settings.cashPercent')}
              value={draft.cashPercent}
              onChange={(e) => update({ cashPercent: num(e.target.value) })}
              inputProps={{ min: 0, max: 200, step: 1 }}
              InputProps={percentAdornment}
            />
            <TextField
              type="number"
              label={t('intake.settings.tradePercent')}
              value={draft.tradePercent}
              onChange={(e) => update({ tradePercent: num(e.target.value) })}
              inputProps={{ min: 0, max: 200, step: 1 }}
              InputProps={percentAdornment}
            />
          </Stack>
          <Typography variant="caption" color="text.secondary" sx={{ mt: -1 }}>
            {t('intake.settings.samePercentHint')}
          </Typography>

          <Divider />
          <Box>
            <Typography variant="subtitle2">{t('intake.settings.conditions')}</Typography>
            <Typography variant="caption" color="text.secondary" component="div" sx={{ mb: 1 }}>
              {t('intake.settings.conditionsHelp')}
            </Typography>
            <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
              {CONDITIONS.map((c) => (
                <TextField
                  key={c}
                  type="number"
                  size="small"
                  label={c}
                  value={draft.conditionPercents[c] ?? 100}
                  onChange={(e) =>
                    update({ conditionPercents: { ...draft.conditionPercents, [c]: num(e.target.value) } })
                  }
                  inputProps={{ min: 0, max: 100, step: 1 }}
                  InputProps={percentAdornment}
                  sx={{ width: 110 }}
                />
              ))}
            </Stack>
          </Box>

          <Divider />
          <Typography variant="subtitle2">{t('intake.settings.bulk')}</Typography>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <TextField
              type="number"
              label={t('intake.settings.bulkThreshold')}
              helperText={t('intake.settings.bulkThresholdHelp')}
              value={draft.bulkThreshold}
              onChange={(e) => update({ bulkThreshold: num(e.target.value) })}
              inputProps={{ min: 0, step: 0.01 }}
              InputProps={moneyAdornment}
            />
            <TextField
              select
              label={t('intake.settings.bulkMode')}
              value={draft.bulkMode}
              onChange={(e) => update({ bulkMode: e.target.value as IntakeSettingsDto['bulkMode'] })}
              sx={{ minWidth: 200 }}
            >
              <MenuItem value="FlatRate">{t('intake.settings.bulkFlatRate')}</MenuItem>
              <MenuItem value="Exclude">{t('intake.settings.bulkExclude')}</MenuItem>
            </TextField>
            {draft.bulkMode === 'FlatRate' && (
              <TextField
                type="number"
                label={t('intake.settings.bulkRate')}
                value={draft.bulkRate}
                onChange={(e) => update({ bulkRate: num(e.target.value) })}
                inputProps={{ min: 0, step: 0.01 }}
                InputProps={moneyAdornment}
              />
            )}
          </Stack>

          <Divider />
          <Typography variant="subtitle2">{t('intake.settings.rounding')}</Typography>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <TextField
              select
              label={t('intake.settings.roundingStep')}
              value={draft.roundingStep}
              onChange={(e) => update({ roundingStep: Number(e.target.value) })}
              sx={{ minWidth: 200 }}
            >
              {ROUNDING_STEPS.map((step) => (
                <MenuItem key={step} value={step}>
                  {step === 0 ? t('intake.settings.roundingCents') : fmt.money(step)}
                </MenuItem>
              ))}
            </TextField>
            <TextField
              select
              label={t('intake.settings.roundingScope')}
              value={draft.roundingScope}
              onChange={(e) => update({ roundingScope: e.target.value as IntakeSettingsDto['roundingScope'] })}
              sx={{ minWidth: 200 }}
            >
              <MenuItem value="PerCard">{t('intake.settings.roundingPerCard')}</MenuItem>
              <MenuItem value="Total">{t('intake.settings.roundingTotal')}</MenuItem>
            </TextField>
          </Stack>

          <Divider />
          <Box>
            <LocationPickerField
              label={t('intake.settings.location')}
              value={draft.locationId ?? null}
              onChange={(id) => update({ locationId: id })}
              clearable
              emptyText={t('intake.settings.locationNone')}
              sx={{ maxWidth: 400 }}
            />
            <Typography variant="caption" color="text.secondary" component="div" sx={{ mt: 0.5 }}>
              {t('intake.settings.locationHelp')}
            </Typography>
          </Box>

          <Divider />
          <Box>
            <Typography variant="subtitle2">{t('intake.settings.customerView')}</Typography>
            <Typography variant="caption" color="text.secondary" component="div" sx={{ mb: 1 }}>
              {t('intake.settings.customerViewHelp')}
            </Typography>
            <Stack>
              {VIEW_FIELDS.map((field) => (
                <FormControlLabel
                  key={field}
                  control={
                    <Switch
                      checked={draft.customerView[field]}
                      onChange={(e) => update({ customerView: { ...draft.customerView, [field]: e.target.checked } })}
                    />
                  }
                  label={t(`intake.settings.${field}`)}
                />
              ))}
            </Stack>
          </Box>

          {save.isError && <Alert severity="error">{(save.error as Error).message}</Alert>}
          {save.isSuccess && !dirty && <Alert severity="success">{t('intake.settings.saved')}</Alert>}
          <Box>
            <Button variant="contained" disabled={!dirty || save.isPending} onClick={() => save.mutate(draft)}>
              {t('intake.settings.save')}
            </Button>
          </Box>
        </Stack>
      </Paper>
    </Stack>
  );
}
