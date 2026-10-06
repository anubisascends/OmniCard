import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  Divider,
  LinearProgress,
  Paper,
  Stack,
  Typography,
} from '@mui/material';
import FolderOpenIcon from '@mui/icons-material/FolderOpen';
import { api } from '../api/client';
import type { ScanBatchSummaryDto } from '../api/types';
import { useFormatters } from '../i18n/format';
import { usePermissions } from '../context/usePermissions';

const isOpenStatus = (b: ScanBatchSummaryDto) => b.status !== 'Committed' && b.status !== 'Discarded';

/** Invalidate everything that shows batch state (this list, the nav badge, an open batch). */
export function invalidateScanBatches(qc: QueryClient) {
  void qc.invalidateQueries({ queryKey: ['scan-batches'] });
  void qc.invalidateQueries({ queryKey: ['scan-batches-count'] });
  void qc.invalidateQueries({ queryKey: ['scan-batch'] });
}

/**
 * The Scan page's list of background batches from the watched scan folders: each batch's progress,
 * who is reviewing it, and Open / Release / Discard. Opening claims the batch and goes to its review
 * page. Hidden when there are no batches.
 */
export function ScanBatchesPanel() {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const navigate = useNavigate();
  const qc = useQueryClient();
  const { can, isAdmin } = usePermissions();
  const [error, setError] = useState<string | null>(null);
  const [confirmDiscard, setConfirmDiscard] = useState<ScanBatchSummaryDto | null>(null);

  const gamesQuery = useQuery({ queryKey: ['games'], queryFn: api.games });
  const batchesQuery = useQuery({
    queryKey: ['scan-batches'],
    queryFn: api.scanBatches,
    // Poll faster while something is still being picked up or matched.
    refetchInterval: (q) =>
      q.state.data?.some((b) => b.status === 'Collecting' || b.status === 'Matching') ? 10_000 : 30_000,
  });

  const onError = (e: unknown) => setError((e as Error).message);
  const claim = useMutation({
    mutationFn: (id: number) => api.scanBatchClaim(id),
    onMutate: () => setError(null),
    onSuccess: (s) => {
      invalidateScanBatches(qc);
      navigate(`/scan/batch/${s.id}`);
    },
    onError,
  });
  const release = useMutation({
    mutationFn: (id: number) => api.scanBatchRelease(id),
    onMutate: () => setError(null),
    onSuccess: () => invalidateScanBatches(qc),
    onError,
  });
  const discard = useMutation({
    mutationFn: (id: number) => api.scanBatchDiscard(id),
    onMutate: () => setError(null),
    onSuccess: () => {
      setConfirmDiscard(null);
      invalidateScanBatches(qc);
    },
    onError: (e) => {
      setConfirmDiscard(null);
      onError(e);
    },
  });

  const batches = batchesQuery.data ?? [];
  if (batches.length === 0) return null;

  const gameName = (id: string) => gamesQuery.data?.find((g) => g.id === id)?.displayName ?? id;

  return (
    <Paper variant="outlined" sx={{ p: 2 }}>
      <Stack direction="row" spacing={1} alignItems="center">
        <FolderOpenIcon color="action" />
        <Typography variant="h6">{t('scan.batches.panelTitle')}</Typography>
      </Stack>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
        {t('scan.batches.intro')}
      </Typography>
      {error && (
        <Alert severity="error" onClose={() => setError(null)} sx={{ mb: 1 }}>
          {error}
        </Alert>
      )}
      <Stack divider={<Divider />}>
        {batches.map((b) => {
          const open = isOpenStatus(b);
          const claimedByOther = !!b.claimedBy && !b.claimedByMe;
          const done = b.total - b.pending;
          return (
            <Stack
              key={b.id}
              direction={{ xs: 'column', sm: 'row' }}
              spacing={1.5}
              alignItems={{ sm: 'center' }}
              sx={{ py: 1.25 }}
            >
              <Box sx={{ minWidth: 0, flex: 1 }}>
                <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
                  <Typography variant="subtitle1" sx={{ fontWeight: 600 }} noWrap>
                    {b.name}
                  </Typography>
                  <Chip size="small" variant="outlined" label={gameName(b.game)} />
                  <BatchStatusChip batch={b} />
                </Stack>
                <Typography variant="body2" color="text.secondary">
                  {[
                    t('scan.batches.counts', { count: b.total }),
                    b.errors > 0 ? t('scan.batches.errors', { count: b.errors }) : null,
                    b.committed > 0 ? t('scan.batches.committedCount', { count: b.committed }) : null,
                    t('scan.batches.lastFile', { time: fmt.dateTime(b.lastFileUtc) }),
                  ]
                    .filter(Boolean)
                    .join(' · ')}
                </Typography>
                {b.claimedBy && (
                  <Typography variant="caption" color={b.claimedByMe ? 'primary' : 'text.secondary'}>
                    {b.claimedByMe
                      ? t('scan.batches.reviewedByMe')
                      : t('scan.batches.reviewedBy', { name: b.claimedBy })}
                  </Typography>
                )}
                {b.status === 'Matching' && b.total > 0 && (
                  <LinearProgress
                    variant="determinate"
                    value={(done / b.total) * 100}
                    sx={{ mt: 0.75, maxWidth: 360 }}
                  />
                )}
              </Box>
              <Stack direction="row" spacing={1} flexShrink={0}>
                {open && !claimedByOther && (
                  <Button
                    variant="contained"
                    size="small"
                    disabled={claim.isPending}
                    onClick={() => claim.mutate(b.id)}
                  >
                    {b.claimedByMe ? t('scan.batches.continue') : t('scan.batches.open')}
                  </Button>
                )}
                {open && claimedByOther && (
                  <Button variant="outlined" size="small" onClick={() => navigate(`/scan/batch/${b.id}`)}>
                    {t('scan.batches.view')}
                  </Button>
                )}
                {open && b.claimedBy && (b.claimedByMe || isAdmin) && (
                  <Button size="small" disabled={release.isPending} onClick={() => release.mutate(b.id)}>
                    {t('scan.batches.release')}
                  </Button>
                )}
                {open && can('scan.commit') && (!claimedByOther || isAdmin) && (
                  <Button size="small" color="error" onClick={() => setConfirmDiscard(b)}>
                    {t('scan.batches.discard')}
                  </Button>
                )}
              </Stack>
            </Stack>
          );
        })}
      </Stack>

      <Dialog open={!!confirmDiscard} onClose={() => setConfirmDiscard(null)}>
        <DialogTitle>{t('scan.batches.discardTitle')}</DialogTitle>
        <DialogContent>
          <DialogContentText>
            {confirmDiscard &&
              t('scan.batches.discardBody', { name: confirmDiscard.name, count: confirmDiscard.total })}
          </DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setConfirmDiscard(null)}>{t('common.actions.cancel')}</Button>
          <Button
            color="error"
            variant="contained"
            disabled={discard.isPending}
            onClick={() => confirmDiscard && discard.mutate(confirmDiscard.id)}
          >
            {t('scan.batches.discard')}
          </Button>
        </DialogActions>
      </Dialog>
    </Paper>
  );
}

/** Status chip for a batch: a spinner while collecting/matching, green when ready. */
export function BatchStatusChip({ batch }: { batch: ScanBatchSummaryDto }) {
  const { t } = useTranslation();
  const label = t(`scan.batches.status.${batch.status}`, {
    done: batch.total - batch.pending,
    total: batch.total,
  });
  if (batch.status === 'Collecting' || batch.status === 'Matching')
    return (
      <Chip
        size="small"
        color="info"
        variant="outlined"
        icon={<CircularProgress size={12} sx={{ ml: 0.75 }} />}
        label={label}
      />
    );
  return (
    <Chip
      size="small"
      color={batch.status === 'Ready' ? 'success' : 'default'}
      variant={batch.status === 'Ready' ? 'filled' : 'outlined'}
      label={label}
    />
  );
}
