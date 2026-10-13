import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Button,
  CircularProgress,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableRow,
  TextField,
  Typography,
} from '@mui/material';
import RestartAltIcon from '@mui/icons-material/RestartAlt';
import { api, ApiError } from '../../api/client';
import type { CollectionResetCountsDto } from '../../api/types';
import { useFormatters } from '../../i18n/format';

type CountKey = Exclude<keyof CollectionResetCountsDto, 'confirmationPhrase'>;

const REMOVED: CountKey[] = [
  'lots',
  'units',
  'locations',
  'sites',
  'lists',
  'trades',
  'tradeSessions',
  'tags',
  'tagRules',
  'products',
  'movements',
];
const KEPT: CountKey[] = ['keptListedLots', 'keptLocations', 'keptOrders', 'keptCustomers'];

/**
 * Admin-only "Reset collection": previews and then wipes every owned card (singles and sealed),
 * location, non-default site, list, trade, tag and tag rule, keeping all sales data and anything still
 * listed for sale. Guarded by typing an exact confirmation phrase; the only undo is a database restore.
 */
export function ResetCollectionCard() {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const qc = useQueryClient();
  const preview = useQuery({ queryKey: ['collection-reset-preview'], queryFn: api.collectionResetPreview });
  const [phrase, setPhrase] = useState('');

  const reset = useMutation({
    mutationFn: () => api.collectionReset(phrase),
    onSuccess: () => {
      setPhrase('');
      // Practically every cached page shows collection data; refetch all of it.
      qc.invalidateQueries();
    },
  });

  const counts = preview.data;
  const required = counts?.confirmationPhrase ?? '';
  const nothingToRemove = counts !== undefined && REMOVED.every((k) => counts[k] === 0);

  const rows = (keys: CountKey[], data: CollectionResetCountsDto) =>
    keys.map((k) => (
      <TableRow key={k}>
        <TableCell>{t(`settings.reset.counts.${k}`)}</TableCell>
        <TableCell align="right">{fmt.number(data[k])}</TableCell>
      </TableRow>
    ));

  return (
    <Paper variant="outlined" sx={{ p: 2, maxWidth: 820, borderColor: 'error.main' }}>
      <Typography variant="h6" color="error">
        {t('settings.reset.title')}
      </Typography>
      <Typography variant="body2" color="text.secondary" gutterBottom>
        {t('settings.reset.description')}
      </Typography>
      <Alert severity="warning" sx={{ my: 1.5 }}>
        {t('settings.reset.backupWarning')}
      </Alert>

      {reset.isSuccess && (
        <Alert severity="success" sx={{ mb: 1.5 }}>
          {t('settings.reset.done', { count: reset.data.lots, formatted: fmt.number(reset.data.lots) })}
        </Alert>
      )}
      {reset.isError && (
        <Alert severity="error" sx={{ mb: 1.5 }}>
          {reset.error instanceof ApiError ? reset.error.message : t('settings.reset.failed')}
        </Alert>
      )}

      {preview.isLoading ? (
        <CircularProgress size={24} />
      ) : preview.isError || !counts ? (
        <Alert severity="error">{t('settings.reset.previewFailed')}</Alert>
      ) : (
        <Stack spacing={2}>
          <Stack direction={{ xs: 'column', md: 'row' }} spacing={2}>
            <Stack sx={{ flex: 1 }}>
              <Typography variant="subtitle2">{t('settings.reset.removedHeading')}</Typography>
              <Table size="small">
                <TableBody>{rows(REMOVED, counts)}</TableBody>
              </Table>
            </Stack>
            <Stack sx={{ flex: 1 }}>
              <Typography variant="subtitle2">{t('settings.reset.keptHeading')}</Typography>
              <Table size="small">
                <TableBody>{rows(KEPT, counts)}</TableBody>
              </Table>
              <Typography variant="caption" color="text.secondary" sx={{ mt: 1 }}>
                {t('settings.reset.alwaysKept')}
              </Typography>
            </Stack>
          </Stack>

          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} alignItems={{ sm: 'center' }}>
            <TextField
              size="small"
              label={t('settings.reset.confirmLabel', { phrase: required })}
              value={phrase}
              onChange={(e) => setPhrase(e.target.value)}
              disabled={reset.isPending || nothingToRemove}
              autoComplete="off"
              sx={{ minWidth: 280 }}
            />
            <Button
              variant="contained"
              color="error"
              startIcon={reset.isPending ? <CircularProgress size={16} color="inherit" /> : <RestartAltIcon />}
              disabled={reset.isPending || nothingToRemove || phrase.trim() !== required}
              onClick={() => reset.mutate()}
            >
              {reset.isPending ? t('settings.reset.running') : t('settings.reset.button')}
            </Button>
          </Stack>
          {nothingToRemove && (
            <Typography variant="body2" color="text.secondary">
              {t('settings.reset.nothingToRemove')}
            </Typography>
          )}
        </Stack>
      )}
    </Paper>
  );
}
