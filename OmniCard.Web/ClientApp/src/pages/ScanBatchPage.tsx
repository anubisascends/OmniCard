import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams, Link as RouterLink } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  AlertTitle,
  Breadcrumbs,
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  Link,
  Stack,
  Typography,
} from '@mui/material';
import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import { api } from '../api/client';
import type { ScanBatchDuplicateRunDto } from '../api/types';
import { usePermissions } from '../context/usePermissions';
import { BatchStatusChip, invalidateScanBatches } from '../components/ScanBatchesPanel';
import { ScanPage } from './ScanPage';

/**
 * Review one background scan batch: the Scan view fed from the server's copy of the batch instead of
 * local uploads. Opening an unclaimed batch claims it; a batch someone else is reviewing opens
 * read-only (admins can take it over). The batch is re-polled while it's still matching, and the
 * page returns to Scan once everything in it has been committed or removed.
 */
export function ScanBatchPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const qc = useQueryClient();
  const { batchId } = useParams();
  const id = Number(batchId);
  const { isAdmin, can } = usePermissions();
  const [notice, setNotice] = useState<{ severity: 'error' | 'warning'; text: string } | null>(null);
  const [confirmDiscard, setConfirmDiscard] = useState(false);

  const batchQuery = useQuery({
    queryKey: ['scan-batch', id],
    queryFn: () => api.scanBatch(id),
    // Re-poll while files are still arriving or being matched, so new matches stream in.
    refetchInterval: (q) => {
      const s = q.state.data?.summary;
      return s && (s.pending > 0 || s.status === 'Collecting' || s.status === 'Starting' || s.status === 'Matching')
        ? 3000
        : false;
    },
  });
  const summary = batchQuery.data?.summary;
  const closed = summary?.status === 'Committed' || summary?.status === 'Discarded';

  const claim = useMutation({
    mutationFn: (force: boolean) => api.scanBatchClaim(id, force),
    onMutate: () => setNotice(null),
    onSuccess: () => invalidateScanBatches(qc),
    onError: (e) => setNotice({ severity: 'error', text: (e as Error).message }),
  });
  const release = useMutation({
    mutationFn: () => api.scanBatchRelease(id),
    onSuccess: () => {
      invalidateScanBatches(qc);
      navigate('/scan');
    },
    onError: (e) => setNotice({ severity: 'error', text: (e as Error).message }),
  });
  const run = useMutation({
    mutationFn: () => api.scanBatchRun([id]),
    onMutate: () => setNotice(null),
    onSuccess: () => invalidateScanBatches(qc),
    onError: (e) => setNotice({ severity: 'error', text: (e as Error).message }),
  });
  const discard = useMutation({
    mutationFn: () => api.scanBatchDiscard(id),
    onSuccess: () => {
      invalidateScanBatches(qc);
      navigate('/scan');
    },
    onError: (e) => {
      setConfirmDiscard(false);
      setNotice({ severity: 'error', text: (e as Error).message });
    },
  });

  // Opening a link to an unclaimed batch claims it (once — a later release doesn't re-claim).
  const triedClaim = useRef(false);
  useEffect(() => {
    if (!summary || triedClaim.current || closed) return;
    triedClaim.current = true;
    if (!summary.claimedBy) claim.mutate(false);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [summary, closed]);

  if (batchQuery.isLoading) return <CircularProgress />;
  if (batchQuery.error || !batchQuery.data || !summary)
    return <Alert severity="error">{(batchQuery.error as Error | null)?.message ?? t('common.states.error')}</Alert>;

  const readOnly = !summary.claimedByMe || closed;

  return (
    <Stack spacing={2}>
      <Breadcrumbs>
        <Link component={RouterLink} to="/scan">
          {t('scan.title')}
        </Link>
        <Typography color="text.primary">{t('scan.batches.breadcrumb')}</Typography>
      </Breadcrumbs>

      <Stack direction="row" spacing={2} alignItems="center" flexWrap="wrap" useFlexGap>
        <Typography variant="h4">{t('scan.batches.title', { name: summary.name })}</Typography>
        <BatchStatusChip batch={summary} />
        <Stack direction="row" spacing={1} sx={{ ml: 'auto' }}>
          {summary.claimedByMe && !closed && (
            <Button disabled={release.isPending} onClick={() => release.mutate()}>
              {t('scan.batches.release')}
            </Button>
          )}
          {!closed && can('scan.commit') && (summary.claimedByMe || !summary.claimedBy || isAdmin) && (
            <Button color="error" onClick={() => setConfirmDiscard(true)}>
              {t('scan.batches.discard')}
            </Button>
          )}
        </Stack>
      </Stack>

      {notice && (
        <Alert severity={notice.severity} onClose={() => setNotice(null)}>
          {notice.text}
        </Alert>
      )}
      {closed ? (
        <Alert severity="info">{t('scan.batches.closed')}</Alert>
      ) : summary.claimedBy && !summary.claimedByMe ? (
        <Alert
          severity="warning"
          action={
            isAdmin && (
              <Button color="inherit" size="small" disabled={claim.isPending} onClick={() => claim.mutate(true)}>
                {t('scan.batches.takeOver')}
              </Button>
            )
          }
        >
          {t('scan.batches.readOnly', { name: summary.claimedBy })}
        </Alert>
      ) : (
        !summary.claimedBy && (
          <Alert
            severity="info"
            action={
              <Button color="inherit" size="small" disabled={claim.isPending} onClick={() => claim.mutate(false)}>
                {t('scan.batches.claim')}
              </Button>
            }
          >
            {t('scan.batches.readOnlyUnclaimed')}
          </Alert>
        )
      )}

      {!closed && summary.awaitingRun && (
        <Alert
          severity="warning"
          action={
            <Button
              color="inherit"
              size="small"
              startIcon={<PlayArrowIcon />}
              disabled={run.isPending}
              onClick={() => run.mutate()}
            >
              {t('scan.batches.runBatch')}
            </Button>
          }
        >
          {t('scan.batches.awaitingRunNotice')}
        </Alert>
      )}

      {summary.duplicates > 0 && (
        <DuplicatesNotice count={summary.duplicates} runs={batchQuery.data.duplicateRuns} />
      )}

      <ScanPage
        key={id}
        batch={{
          data: batchQuery.data,
          readOnly,
          onClaimLost: () => {
            setNotice({ severity: 'warning', text: t('scan.batches.claimLost') });
            invalidateScanBatches(qc);
          },
          onChanged: () => void qc.invalidateQueries({ queryKey: ['scan-batch', id] }),
          onClosed: () => {
            invalidateScanBatches(qc);
            navigate('/scan');
          },
        }}
      />

      <Dialog open={confirmDiscard} onClose={() => setConfirmDiscard(false)}>
        <DialogTitle>{t('scan.batches.discardTitle')}</DialogTitle>
        <DialogContent>
          <DialogContentText>
            {t('scan.batches.discardBody', { name: summary.name, count: summary.total })}{' '}
            {summary.deletesOriginalsOnClose
              ? t('scan.batches.discardOriginalsDeleted')
              : t('scan.batches.discardOriginalsKept')}
          </DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setConfirmDiscard(false)}>{t('common.actions.cancel')}</Button>
          <Button color="error" variant="contained" disabled={discard.isPending} onClick={() => discard.mutate()}>
            {t('scan.batches.discard')}
          </Button>
        </DialogActions>
      </Dialog>
    </Stack>
  );
}

/** The exact duplicates set aside at pickup, one line per repeated run (e.g. "0001.png – 1000.png
 * repeat 0001.png – 1000.png"), so a re-saved scan run is recognizable at a glance. */
function DuplicatesNotice({ count, runs }: { count: number; runs: ScanBatchDuplicateRunDto[] }) {
  const { t } = useTranslation();
  const shown = runs.reduce((n, r) => n + r.count, 0);
  return (
    <Alert severity="info">
      <AlertTitle>{t('scan.batches.duplicatesTitle', { count })}</AlertTitle>
      {t('scan.batches.duplicatesBody')}
      <Stack component="ul" spacing={0.25} sx={{ m: 0, mt: 0.5, pl: 2.5 }}>
        {runs.map((r) => (
          <Typography component="li" variant="body2" key={`${r.firstFile}-${r.originalFirstFile}`}>
            {t(r.originalBatchName ? 'scan.batches.duplicateRunOtherBatch' : 'scan.batches.duplicateRun', {
              count: r.count,
              first: r.firstFile,
              last: r.lastFile,
              originalFirst: r.originalFirstFile,
              originalLast: r.originalLastFile,
              batch: r.originalBatchName,
            })}
          </Typography>
        ))}
        {shown < count && (
          <Typography component="li" variant="body2">
            {t('scan.batches.duplicateRunsMore')}
          </Typography>
        )}
      </Stack>
    </Alert>
  );
}
