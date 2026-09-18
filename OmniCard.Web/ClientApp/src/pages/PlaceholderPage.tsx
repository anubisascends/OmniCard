import { Alert, Stack, Typography } from '@mui/material';
import { useTranslation } from 'react-i18next';

/** Temporary stub for screens not yet built in the migration (Sets, Import, Sales, …). */
export function PlaceholderPage({ title }: { title: string }) {
  const { t } = useTranslation();
  return (
    <Stack spacing={2}>
      <Typography variant="h4">{title}</Typography>
      <Alert severity="info">{t('common.comingSoon')}</Alert>
    </Stack>
  );
}
