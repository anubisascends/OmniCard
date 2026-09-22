import type { ReactNode } from 'react';
import { Alert, Box, CircularProgress } from '@mui/material';
import { useTranslation } from 'react-i18next';
import { usePermissions } from '../context/usePermissions';

/**
 * Route guard: renders {children} only when the signed-in user holds at least one of `anyOf`.
 * Otherwise shows a "no access" notice. This is UX only — the API independently enforces the same
 * permission, so a user who reaches a route directly still can't perform its actions.
 */
export function RequirePermission({ anyOf, children }: { anyOf: string[]; children: ReactNode }) {
  const { t } = useTranslation();
  const { canAny, isLoading } = usePermissions();

  if (isLoading) {
    return (
      <Box sx={{ display: 'grid', placeItems: 'center', minHeight: '40vh' }}>
        <CircularProgress />
      </Box>
    );
  }

  if (!canAny(...anyOf)) {
    return (
      <Box sx={{ maxWidth: 640, mx: 'auto', mt: 4 }}>
        <Alert severity="warning">{t('common.noAccess')}</Alert>
      </Box>
    );
  }

  return <>{children}</>;
}
