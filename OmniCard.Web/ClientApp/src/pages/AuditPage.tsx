import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useParams, Link as RouterLink } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { Alert, Breadcrumbs, Link, Stack, Typography } from '@mui/material';
import { api } from '../api/client';
import { ScanPage } from './ScanPage';
import { AuditSummaryDialog } from '../components/dialogs/AuditSummaryDialog';
import type { AuditCommitResultDto } from '../api/types';

/**
 * Location audit: a Scan view locked to one location. The user scans/imports/photographs every card
 * physically present, confirms the matches, then commits — which makes the scan the source of truth
 * for the location (matched cards kept, absent cards deleted, new cards added). The reconcile runs
 * server-side; on success we show a {@link AuditSummaryDialog} of the changes.
 */
export function AuditPage() {
  const { t } = useTranslation();
  const { locationId } = useParams();
  const id = Number(locationId);
  const [summary, setSummary] = useState<AuditCommitResultDto | null>(null);

  const locQuery = useQuery({ queryKey: ['location', id], queryFn: () => api.location(id) });
  const locationName = locQuery.data?.name ?? t('locations.detail.fallbackName');

  return (
    <Stack spacing={2}>
      <Breadcrumbs>
        <Link component={RouterLink} to="/locations">
          {t('locations.title')}
        </Link>
        <Link component={RouterLink} to={`/location/${id}`}>
          {locQuery.data?.name ?? '…'}
        </Link>
        <Typography color="text.primary">{t('scan.audit.breadcrumb')}</Typography>
      </Breadcrumbs>

      <Typography variant="h4">{t('scan.audit.title', { location: locationName })}</Typography>
      <Alert severity="warning">{t('scan.audit.banner')}</Alert>

      <ScanPage
        lockedContainerId={id}
        auditMode
        onAuditCommitted={(result) => setSummary(result)}
      />

      <AuditSummaryDialog
        open={summary != null}
        result={summary}
        locationName={locationName}
        onClose={() => setSummary(null)}
      />
    </Stack>
  );
}
