import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery } from '@tanstack/react-query';
import {
  Alert,
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  List,
  ListItemButton,
  ListItemText,
  Stack,
} from '@mui/material';
import ArrowBackIcon from '@mui/icons-material/ArrowBack';
import PictureAsPdfIcon from '@mui/icons-material/PictureAsPdf';
import { api } from '../../api/client';
import { useFormatters } from '../../i18n/format';
import { DeckReturnLinesTable } from '../DeckReturnLinesTable';

/** A deck box's saved put-back reports (deconstructions and returns), newest first: view one again or print it. */
export function DeckReturnReportsDialog({
  open,
  deckBoxId,
  deckBoxName,
  onClose,
}: {
  open: boolean;
  deckBoxId: number;
  deckBoxName: string;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const [viewing, setViewing] = useState<number | null>(null);
  const reports = useQuery({
    queryKey: ['deck-return-reports', deckBoxId],
    queryFn: () => api.deckReturnReports(deckBoxId),
    enabled: open,
  });
  const lines = useQuery({
    queryKey: ['deck-return-report', deckBoxId, viewing],
    queryFn: () => api.deckReturnReport(deckBoxId, viewing!),
    enabled: open && viewing != null,
  });
  const pdf = useMutation({ mutationFn: (reportId: number) => api.deckReturnReportPdf(deckBoxId, reportId) });

  const close = () => {
    setViewing(null);
    onClose();
  };

  return (
    <Dialog open={open} onClose={close} maxWidth={viewing != null ? 'md' : 'sm'} fullWidth>
      <DialogTitle>{t('deckbox.return.reportsTitle', { name: deckBoxName })}</DialogTitle>
      <DialogContent dividers>
        <Stack spacing={2}>
          {(reports.isLoading || lines.isLoading) && <CircularProgress size={24} />}
          {reports.error && <Alert severity="error">{(reports.error as Error).message}</Alert>}
          {pdf.error && <Alert severity="error">{(pdf.error as Error).message}</Alert>}
          {viewing != null ? (
            lines.data && <DeckReturnLinesTable lines={lines.data} />
          ) : reports.data?.length === 0 ? (
            <Alert severity="info">{t('deckbox.return.noReports')}</Alert>
          ) : (
            <List dense disablePadding>
              {reports.data?.map((r) => (
                <ListItemButton key={r.id} onClick={() => setViewing(r.id)}>
                  <ListItemText
                    primary={t(r.wholeDeck ? 'deckbox.return.reportDeconstructed' : 'deckbox.return.reportReturned', {
                      count: r.cardCount,
                      countText: fmt.number(r.cardCount),
                    })}
                    secondary={
                      r.createdBy
                        ? t('deckbox.return.reportWhenBy', { date: fmt.dateTime(r.createdUtc), user: r.createdBy })
                        : fmt.dateTime(r.createdUtc)
                    }
                  />
                </ListItemButton>
              ))}
            </List>
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        {viewing != null && (
          <>
            <Button startIcon={<ArrowBackIcon />} onClick={() => setViewing(null)} sx={{ mr: 'auto' }}>
              {t('deckbox.return.allReports')}
            </Button>
            <Button startIcon={<PictureAsPdfIcon />} onClick={() => pdf.mutate(viewing)} disabled={pdf.isPending}>
              {t('deckbox.return.downloadPdf')}
            </Button>
          </>
        )}
        <Button variant="contained" onClick={close}>
          {t('common.actions.close')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
