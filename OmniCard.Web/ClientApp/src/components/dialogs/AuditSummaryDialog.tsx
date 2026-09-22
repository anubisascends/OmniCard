import { useTranslation } from 'react-i18next';
import {
  Accordion,
  AccordionDetails,
  AccordionSummary,
  Alert,
  Button,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  List,
  ListItem,
  ListItemText,
  Stack,
  Typography,
} from '@mui/material';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import RemoveCircleIcon from '@mui/icons-material/RemoveCircle';
import AddCircleIcon from '@mui/icons-material/AddCircle';
import type { AuditLineDto, AuditCommitResultDto } from '../../api/types';

/** Post-commit summary of a location audit: what was matched, removed (not found), and added.
 * Purely presentational — the reconcile has already been persisted server-side. */
export function AuditSummaryDialog({
  open,
  result,
  locationName,
  onClose,
}: {
  open: boolean;
  result: AuditCommitResultDto | null;
  locationName: string;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  if (!result) return null;

  const count = (lines: AuditLineDto[]) => lines.reduce((sum, l) => sum + l.quantity, 0);
  const matchedCount = count(result.matched);
  const notFoundCount = count(result.notFound);
  const addedCount = count(result.added);

  return (
    <Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth>
      <DialogTitle>{t('scan.audit.summary.title', { location: locationName })}</DialogTitle>
      <DialogContent dividers>
        <Stack spacing={2}>
          <Typography variant="body2" color="text.secondary">
            {t('scan.audit.summary.intro', {
              matched: matchedCount,
              added: addedCount,
              notFound: notFoundCount,
            })}
          </Typography>
          {result.updatedCount > 0 && (
            <Alert severity="info">
              {t('scan.audit.summary.updated', { count: result.updatedCount })}
            </Alert>
          )}

          <AuditSection
            title={t('scan.audit.summary.matched', { count: matchedCount })}
            color="success"
            icon={<CheckCircleIcon color="success" />}
            lines={result.matched}
            emptyText={t('scan.audit.summary.matchedEmpty')}
          />
          <AuditSection
            title={t('scan.audit.summary.notFound', { count: notFoundCount })}
            color="error"
            icon={<RemoveCircleIcon color="error" />}
            lines={result.notFound}
            emptyText={t('scan.audit.summary.notFoundEmpty')}
          />
          <AuditSection
            title={t('scan.audit.summary.added', { count: addedCount })}
            color="primary"
            icon={<AddCircleIcon color="primary" />}
            lines={result.added}
            emptyText={t('scan.audit.summary.addedEmpty')}
          />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button variant="contained" onClick={onClose}>
          {t('common.actions.done')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

function AuditSection({
  title,
  icon,
  lines,
  emptyText,
}: {
  title: string;
  color: 'success' | 'error' | 'primary';
  icon: React.ReactNode;
  lines: AuditLineDto[];
  emptyText: string;
}) {
  const { t } = useTranslation();
  return (
    <Accordion disableGutters defaultExpanded={lines.length > 0}>
      <AccordionSummary expandIcon={<ExpandMoreIcon />}>
        <Stack direction="row" spacing={1} alignItems="center">
          {icon}
          <Typography>{title}</Typography>
        </Stack>
      </AccordionSummary>
      <AccordionDetails sx={{ p: 0 }}>
        {lines.length === 0 ? (
          <Typography variant="body2" color="text.secondary" sx={{ p: 2 }}>
            {emptyText}
          </Typography>
        ) : (
          <List dense disablePadding>
            {lines.map((l, i) => (
              <ListItem
                key={`${l.setCode}-${l.collectorNumber}-${i}`}
                secondaryAction={l.quantity > 1 ? <Chip size="small" label={`×${l.quantity}`} /> : null}
              >
                <ListItemText
                  primary={l.name}
                  secondary={[
                    [l.setCode, l.collectorNumber].filter(Boolean).join(' '),
                    l.condition,
                    l.isFoil ? t('common.labels.foil') : null,
                  ]
                    .filter(Boolean)
                    .join(' · ')}
                />
              </ListItem>
            ))}
          </List>
        )}
      </AccordionDetails>
    </Accordion>
  );
}
