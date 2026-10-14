import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  Collapse,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  Divider,
  IconButton,
  LinearProgress,
  Paper,
  Stack,
  Typography,
} from '@mui/material';
import ExpandLessIcon from '@mui/icons-material/ExpandLess';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import FolderOpenIcon from '@mui/icons-material/FolderOpen';
import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import { api } from '../api/client';
import type { ScanBatchSummaryDto } from '../api/types';
import { useFormatters } from '../i18n/format';
import { usePermissions } from '../context/usePermissions';

const isOpenStatus = (b: ScanBatchSummaryDto) => b.status !== 'Committed' && b.status !== 'Discarded';
/** Still waiting to start: its files are in the watched folder, untouched, and there's nothing to review yet. */
const isWaiting = (b: ScanBatchSummaryDto) => (b.status === 'Collecting' || b.status === 'Starting') && b.total === 0;

// Whether the panel is expanded is a per-browser preference; storage can be unavailable (private mode).
const EXPANDED_KEY = 'omnicard.scanBatches.expanded';
function loadExpanded(): boolean {
  try {
    return localStorage.getItem(EXPANDED_KEY) !== 'false';
  } catch {
    return true;
  }
}
function saveExpanded(expanded: boolean) {
  try {
    localStorage.setItem(EXPANDED_KEY, String(expanded));
  } catch {
    /* not persisted */
  }
}

/** Invalidate everything that shows batch state (this list, the nav badge, an open batch). */
export function invalidateScanBatches(qc: QueryClient) {
  void qc.invalidateQueries({ queryKey: ['scan-batches'] });
  void qc.invalidateQueries({ queryKey: ['scan-batches-count'] });
  void qc.invalidateQueries({ queryKey: ['scan-batch'] });
}

/**
 * The Scan page's list of background batches from the watched scan folders: each batch's progress,
 * who is reviewing it, and Open / Release / Discard / Run. Opening claims the batch and goes to its
 * review page. A batch that hasn't started shows its file count and, with automatic processing on, a
 * countdown to when it starts by itself; Run starts it now — the only way it starts when automatic
 * processing is off. Hidden when there are no batches.
 */
export function ScanBatchesPanel() {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const navigate = useNavigate();
  const qc = useQueryClient();
  const { can, isAdmin } = usePermissions();
  const [error, setError] = useState<string | null>(null);
  const [confirmDiscard, setConfirmDiscard] = useState<ScanBatchSummaryDto | null>(null);
  const [expanded, setExpanded] = useState(loadExpanded);
  const toggleExpanded = () =>
    setExpanded((v) => {
      saveExpanded(!v);
      return !v;
    });

  const gamesQuery = useQuery({ queryKey: ['games'], queryFn: api.games });
  const batchesQuery = useQuery({
    queryKey: ['scan-batches'],
    queryFn: api.scanBatches,
    // Poll faster while something is waiting, starting or being matched.
    refetchInterval: (q) =>
      q.state.data?.some((b) => b.status === 'Collecting' || b.status === 'Starting' || b.status === 'Matching')
        ? 5_000
        : 30_000,
  });

  // Tick once a second while a countdown is on screen.
  const counting = batchesQuery.data?.some((b) => b.status === 'Collecting' && b.startsUtc) ?? false;
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    if (!counting) return;
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, [counting]);

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
  const run = useMutation({
    mutationFn: (ids: number[]) => api.scanBatchRun(ids),
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
  const countdown = (startsUtc: string) => {
    const left = Math.ceil((new Date(startsUtc).getTime() - now) / 1000);
    if (left <= 0) return t('scan.batches.startingSoon');
    const minutes = Math.floor(left / 60);
    const seconds = left % 60;
    return t('scan.batches.startsIn', {
      time: `${fmt.number(minutes)}:${fmt.number(seconds, { minimumIntegerDigits: 2 })}`,
    });
  };
  // Shown in the header so a collapsed panel still says what's waiting.
  const readyCount = batches.filter((b) => b.status === 'Ready').length;
  const inProgressCount = batches.filter(
    (b) => (b.status === 'Collecting' && !b.awaitingRun) || b.status === 'Starting' || b.status === 'Matching',
  ).length;
  const awaitingRun = batches.filter((b) => b.awaitingRun);

  return (
    <Paper variant="outlined" sx={{ p: 2 }}>
      <Stack
        direction="row"
        spacing={1}
        alignItems="center"
        onClick={toggleExpanded}
        sx={{ cursor: 'pointer', userSelect: 'none' }}
      >
        <FolderOpenIcon color="action" />
        <Typography variant="h6">{t('scan.batches.panelTitle')}</Typography>
        {readyCount > 0 && (
          <Chip size="small" color="success" label={t('scan.batches.summaryReady', { count: readyCount })} />
        )}
        {inProgressCount > 0 && (
          <Chip
            size="small"
            color="info"
            variant="outlined"
            label={t('scan.batches.summaryInProgress', { count: inProgressCount })}
          />
        )}
        <Box sx={{ flexGrow: 1 }} />
        {awaitingRun.length > 0 && (
          <Button
            size="small"
            variant="contained"
            startIcon={<PlayArrowIcon />}
            disabled={run.isPending}
            onClick={(e) => {
              e.stopPropagation(); // the header row toggles the panel
              run.mutate(awaitingRun.map((b) => b.id));
            }}
          >
            {t('scan.batches.runAll', { count: awaitingRun.length })}
          </Button>
        )}
        <IconButton
          size="small"
          aria-expanded={expanded}
          aria-label={expanded ? t('scan.batches.collapse') : t('scan.batches.expand')}
        >
          {expanded ? <ExpandLessIcon /> : <ExpandMoreIcon />}
        </IconButton>
      </Stack>
      {error && (
        <Alert severity="error" onClose={() => setError(null)} sx={{ mt: 1 }}>
          {error}
        </Alert>
      )}
      <Collapse in={expanded}>
        <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5, mb: 1 }}>
          {t('scan.batches.intro')}
        </Typography>
        {awaitingRun.length > 0 && (
          <Alert severity="info" sx={{ mb: 1 }}>
            {t('scan.batches.runHint')}
          </Alert>
        )}
        <Stack divider={<Divider />}>
          {batches.map((b) => {
            const waiting = isWaiting(b);
            const open = isOpenStatus(b) && !waiting;
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
                    {(waiting
                      ? [
                          t('scan.batches.waitingFiles', { count: b.waitingFiles }),
                          b.status === 'Collecting' && b.startsUtc ? countdown(b.startsUtc) : null,
                          t('scan.batches.lastChange', { time: fmt.dateTime(b.lastFileUtc) }),
                        ]
                      : [
                          t('scan.batches.counts', { count: b.total }),
                          b.errors > 0 ? t('scan.batches.errors', { count: b.errors }) : null,
                          b.committed > 0 ? t('scan.batches.committedCount', { count: b.committed }) : null,
                          b.duplicates > 0 ? t('scan.batches.duplicatesCount', { count: b.duplicates }) : null,
                          t('scan.batches.lastFile', { time: fmt.dateTime(b.lastFileUtc) }),
                        ]
                    )
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
                  {b.status === 'Collecting' && (
                    <Button
                      variant={b.awaitingRun ? 'contained' : 'outlined'}
                      size="small"
                      startIcon={<PlayArrowIcon />}
                      disabled={run.isPending}
                      onClick={() => run.mutate([b.id])}
                    >
                      {b.awaitingRun ? t('scan.batches.run') : t('scan.batches.runNow')}
                    </Button>
                  )}
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
      </Collapse>

      <Dialog open={!!confirmDiscard} onClose={() => setConfirmDiscard(null)}>
        <DialogTitle>{t('scan.batches.discardTitle')}</DialogTitle>
        <DialogContent>
          <DialogContentText>
            {confirmDiscard &&
              `${t('scan.batches.discardBody', { name: confirmDiscard.name, count: confirmDiscard.total })} ${
                confirmDiscard.deletesOriginalsOnClose
                  ? t('scan.batches.discardOriginalsDeleted')
                  : t('scan.batches.discardOriginalsKept')
              }`}
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

/** Status chip for a batch: a spinner while waiting/starting/matching, green when ready, amber while
 * it waits for Run (automatic processing off). */
export function BatchStatusChip({ batch }: { batch: ScanBatchSummaryDto }) {
  const { t } = useTranslation();
  if (batch.awaitingRun)
    return <Chip size="small" color="warning" variant="outlined" label={t('scan.batches.status.AwaitingRun')} />;
  const label = t(`scan.batches.status.${batch.status}`, {
    done: batch.total - batch.pending,
    total: batch.total,
  });
  if (batch.status === 'Collecting' || batch.status === 'Starting' || batch.status === 'Matching')
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
