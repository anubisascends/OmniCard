import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Link as RouterLink } from 'react-router-dom';
import {
  Alert,
  Button,
  Checkbox,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControlLabel,
  Link,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import CallSplitIcon from '@mui/icons-material/CallSplit';
import PictureAsPdfIcon from '@mui/icons-material/PictureAsPdf';
import { api } from '../../api/client';
import type { LocationSummaryDto } from '../../api/types';
import { useFormatters } from '../../i18n/format';
import { LocationPickerField } from '../LocationPickerField';
import { DeckReturnLinesTable } from '../DeckReturnLinesTable';

/**
 * Deconstruct a deck box (no `lotIds`) or return some of its cards (`lotIds`) to where they came from.
 * Shows where every copy will go first — back to its binder pocket or box, or, when that isn't possible, to
 * Bulk with the reason — and the location for copies with no known origin (Bulk unless changed). A whole-deck
 * deconstruction can save the deck as a card list so it can be rebuilt later. After applying, the put-back
 * list stays on screen and prints as a PDF; it's also kept with the deck box's put-back reports.
 */
export function DeckReturnDialog({
  open,
  loc,
  lotIds,
  onClose,
}: {
  open: boolean;
  loc: LocationSummaryDto;
  /** Lots to return; omitted = the whole deck. */
  lotIds?: number[];
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const qc = useQueryClient();
  const whole = !lotIds || lotIds.length === 0;
  const [fallbackId, setFallbackId] = useState<number | null>(null);
  const [saveList, setSaveList] = useState(true);
  const [listName, setListName] = useState(() =>
    t('deckbox.return.defaultListName', { name: loc.name, date: fmt.date(new Date().toISOString()) }),
  );

  const request = useMemo(
    () => ({
      lotIds: whole ? null : lotIds,
      fallbackLocationId: fallbackId,
      saveAsListName: whole && saveList && listName.trim() ? listName.trim() : null,
    }),
    [whole, lotIds, fallbackId, saveList, listName],
  );

  const preview = useQuery({
    queryKey: ['deck-return-preview', loc.id, lotIds ?? null, fallbackId],
    queryFn: () => api.deckReturnPreview(loc.id, { lotIds: request.lotIds, fallbackLocationId: fallbackId }),
    enabled: open,
    refetchOnWindowFocus: false,
    gcTime: 0,
    retry: false,
  });

  const apply = useMutation({
    mutationFn: () => api.deckReturnApply(loc.id, request),
    onSuccess: () => {
      for (const key of ['collection', 'location-cards', 'deck-stack', 'locations', 'dashboard', 'lists'])
        qc.invalidateQueries({ queryKey: [key] });
      qc.invalidateQueries({ queryKey: ['location', loc.id] });
      qc.invalidateQueries({ queryKey: ['deck-legality', loc.id] });
      qc.invalidateQueries({ queryKey: ['deck-return-reports', loc.id] });
    },
  });
  const pdf = useMutation({ mutationFn: (reportId: number) => api.deckReturnReportPdf(loc.id, reportId) });

  const result = apply.data;
  const plan = preview.data;
  const lines = result?.lines ?? plan?.lines ?? [];
  const flagged = lines.filter((l) => l.issue !== 'None').reduce((n, l) => n + l.quantity, 0);
  const destinations = new Set(lines.map((l) => l.destinationId)).size;
  const total = lines.reduce((n, l) => n + l.quantity, 0);

  return (
    <Dialog open={open} onClose={apply.isPending ? undefined : onClose} maxWidth="md" fullWidth>
      <DialogTitle>
        {whole ? t('deckbox.return.titleDeconstruct', { name: loc.name }) : t('deckbox.return.titleReturn', { name: loc.name })}
      </DialogTitle>
      <DialogContent dividers>
        <Stack spacing={2}>
          {result ? (
            <Alert severity="success">
              {t('deckbox.return.done', { count: result.cardCount, countText: fmt.number(result.cardCount) })}
              {result.listId != null && (
                <>
                  {' '}
                  <Link component={RouterLink} to="/lists">
                    {t('deckbox.return.listSaved', { name: request.saveAsListName ?? '' })}
                  </Link>
                </>
              )}
            </Alert>
          ) : (
            <>
              <Typography variant="body2" color="text.secondary">
                {whole ? t('deckbox.return.introDeconstruct') : t('deckbox.return.introReturn')}
              </Typography>
              <LocationPickerField
                size="small"
                label={t('deckbox.return.fallback')}
                value={fallbackId ?? plan?.fallbackLocationId ?? null}
                onChange={setFallbackId}
                cardGames={loc.game ? [loc.game] : undefined}
                sx={{ maxWidth: 360 }}
              />
              {whole && (
                <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
                  <FormControlLabel
                    control={<Checkbox checked={saveList} onChange={(e) => setSaveList(e.target.checked)} />}
                    label={t('deckbox.return.saveAsList')}
                  />
                  {saveList && (
                    <TextField
                      size="small"
                      label={t('deckbox.return.listName')}
                      value={listName}
                      onChange={(e) => setListName(e.target.value)}
                      sx={{ minWidth: 280 }}
                    />
                  )}
                </Stack>
              )}
            </>
          )}

          {preview.isLoading && <CircularProgress size={24} />}
          {preview.error && !result && <Alert severity="error">{(preview.error as Error).message}</Alert>}
          {(plan || result) && (
            <Typography variant="body2">
              {t('deckbox.return.summary', {
                count: total,
                countText: fmt.number(total),
                locations: fmt.number(destinations),
              })}
            </Typography>
          )}
          {flagged > 0 && (
            <Alert severity="warning">
              {t('deckbox.return.flagged', { count: flagged, countText: fmt.number(flagged) })}
            </Alert>
          )}
          {(plan || result) && <DeckReturnLinesTable lines={lines} />}
          {apply.error && <Alert severity="error">{(apply.error as Error).message}</Alert>}
          {pdf.error && <Alert severity="error">{(pdf.error as Error).message}</Alert>}
        </Stack>
      </DialogContent>
      <DialogActions>
        {result ? (
          <>
            <Button startIcon={<PictureAsPdfIcon />} onClick={() => pdf.mutate(result.reportId)} disabled={pdf.isPending}>
              {t('deckbox.return.downloadPdf')}
            </Button>
            <Button variant="contained" onClick={onClose}>
              {t('common.actions.done')}
            </Button>
          </>
        ) : (
          <>
            <Button onClick={onClose} disabled={apply.isPending}>
              {t('common.actions.cancel')}
            </Button>
            <Button
              variant="contained"
              color={whole ? 'warning' : 'primary'}
              startIcon={<CallSplitIcon />}
              disabled={!plan || plan.lines.length === 0 || apply.isPending || preview.isFetching}
              onClick={() => apply.mutate()}
            >
              {apply.isPending
                ? t('deckbox.return.applying')
                : whole
                  ? t('deckbox.return.applyDeconstruct')
                  : t('deckbox.return.applyReturn', { count: total })}
            </Button>
          </>
        )}
      </DialogActions>
    </Dialog>
  );
}
