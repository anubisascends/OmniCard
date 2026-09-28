import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  AlertTitle,
  Box,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  List,
  ListItem,
  ListItemText,
  MenuItem,
  Stack,
  Tab,
  Tabs,
  TextField,
  Typography,
} from '@mui/material';
import LinkIcon from '@mui/icons-material/Link';
import UploadFileIcon from '@mui/icons-material/UploadFile';
import { api, ApiError } from '../../api/client';
import type { LocationImportFailureDto, LocationImportIssueDto, LocationImportResultDto } from '../../api/types';
import { useFormatters } from '../../i18n/format';

const CONDITIONS = ['NM', 'LP', 'MP', 'HP', 'DMG'];
/** Long error lists are capped in the UI; the summary still states the full count. */
const MAX_LISTED = 200;

type Mode = 'csv' | 'url';

/** A 422 from the location import carries every problem; anything else is a plain error message. */
function failureOf(error: unknown): LocationImportFailureDto | null {
  if (error instanceof ApiError && error.status === 422) {
    const body = error.body as Partial<LocationImportFailureDto> | undefined;
    if (body?.error) return { error: body.error, errors: body.errors ?? [] };
  }
  return null;
}

function IssueList({ issues }: { issues: LocationImportIssueDto[] }) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const shown = issues.slice(0, MAX_LISTED);
  return (
    <Box sx={{ maxHeight: 280, overflowY: 'auto', mt: 1 }}>
      <List dense disablePadding>
        {shown.map((issue, i) => {
          const where = [
            issue.row != null ? t('importing.location.row', { row: fmt.number(issue.row) }) : null,
            issue.card,
          ]
            .filter(Boolean)
            .join(' · ');
          return (
            <ListItem key={i} disableGutters sx={{ alignItems: 'flex-start' }}>
              <ListItemText
                primary={where || undefined}
                secondary={issue.message}
                primaryTypographyProps={{ variant: 'body2', fontWeight: 600 }}
                secondaryTypographyProps={{ variant: 'body2', color: 'text.primary' }}
              />
            </ListItem>
          );
        })}
      </List>
      {issues.length > shown.length && (
        <Typography variant="body2" color="text.secondary">
          {t('importing.location.more', { count: issues.length - shown.length })}
        </Typography>
      )}
    </Box>
  );
}

/** The Location view's Import: a CSV file or Moxfield/Archidekt deck URL, imported all-or-nothing
 *  into this location. There's no location picker and no duplicate skipping — every line lands here,
 *  and if any line has a problem nothing is written and every problem is listed. */
export function LocationImportDialog({
  open,
  locationId,
  locationName,
  onClose,
}: {
  open: boolean;
  locationId: number;
  locationName: string;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const qc = useQueryClient();
  const [mode, setMode] = useState<Mode>('csv');
  const [file, setFile] = useState<File | null>(null);
  const [url, setUrl] = useState('');
  const [condition, setCondition] = useState('NM');

  const importMut = useMutation<LocationImportResultDto, Error>({
    mutationFn: () =>
      mode === 'csv'
        ? api.importCsvToLocation(locationId, file!)
        : api.importUrlToLocation(locationId, { url: url.trim(), condition }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['location', locationId] });
      qc.invalidateQueries({ queryKey: ['collection'] });
      qc.invalidateQueries({ queryKey: ['location-cards'] });
      qc.invalidateQueries({ queryKey: ['deck-stack'] });
      qc.invalidateQueries({ queryKey: ['locations'] });
      qc.invalidateQueries({ queryKey: ['dashboard'] });
    },
  });
  const { reset } = importMut;

  // Fresh dialog every time it opens.
  useEffect(() => {
    if (!open) return;
    setMode('csv');
    setFile(null);
    setUrl('');
    setCondition('NM');
    reset();
  }, [open, reset]);

  const result = importMut.data;
  const failure = failureOf(importMut.error);
  const canImport = !importMut.isPending && (mode === 'csv' ? !!file : !!url.trim());

  return (
    <Dialog open={open} onClose={importMut.isPending ? undefined : onClose} maxWidth="sm" fullWidth>
      <DialogTitle>{t('importing.location.title', { name: locationName })}</DialogTitle>
      <DialogContent>
        {result ? (
          <Alert severity={result.substitutions.length > 0 ? 'warning' : 'success'}>
            <AlertTitle>
              {t('importing.location.success', {
                count: result.copies,
                copies: fmt.number(result.copies),
                lines: fmt.number(result.lines),
                source: result.source,
                name: locationName,
              })}
            </AlertTitle>
            {result.substitutions.length > 0 && (
              <>
                {t('importing.location.substituted', { count: result.substitutions.length })}
                <IssueList issues={result.substitutions} />
              </>
            )}
          </Alert>
        ) : (
          <Stack spacing={2}>
            <Tabs
              value={mode}
              onChange={(_, v: Mode) => {
                setMode(v);
                reset();
              }}
            >
              <Tab value="csv" label={t('importing.location.csvTab')} icon={<UploadFileIcon />} iconPosition="start" />
              <Tab value="url" label={t('importing.location.urlTab')} icon={<LinkIcon />} iconPosition="start" />
            </Tabs>
            <Alert severity="info">{t('importing.location.allOrNothing', { name: locationName })}</Alert>
            {mode === 'csv' ? (
              <Stack spacing={1}>
                <Button variant="outlined" component="label" startIcon={<UploadFileIcon />}>
                  {file ? file.name : t('importing.import.chooseFile')}
                  <input
                    type="file"
                    accept=".csv,text/csv"
                    hidden
                    onChange={(e) => {
                      setFile(e.target.files?.[0] ?? null);
                      e.target.value = '';
                      reset();
                    }}
                  />
                </Button>
                <Typography variant="body2" color="text.secondary">
                  {t('importing.location.csvHelp')}
                </Typography>
              </Stack>
            ) : (
              <Stack spacing={2}>
                <TextField
                  size="small"
                  label={t('importing.url.urlLabel')}
                  placeholder="https://moxfield.com/decks/…"
                  helperText={t('importing.location.urlHelp')}
                  value={url}
                  onChange={(e) => {
                    setUrl(e.target.value);
                    reset();
                  }}
                  autoFocus
                />
                <TextField
                  select
                  size="small"
                  label={t('common.labels.condition')}
                  value={condition}
                  onChange={(e) => setCondition(e.target.value)}
                  sx={{ width: 200 }}
                >
                  {CONDITIONS.map((c) => (
                    <MenuItem key={c} value={c}>
                      {t(`common.conditions.${c}`)}
                    </MenuItem>
                  ))}
                </TextField>
              </Stack>
            )}
            {failure ? (
              <Alert severity="error">
                <AlertTitle>{failure.error}</AlertTitle>
                {failure.errors.length > 0 && <IssueList issues={failure.errors} />}
              </Alert>
            ) : (
              importMut.error && <Alert severity="error">{importMut.error.message}</Alert>
            )}
          </Stack>
        )}
      </DialogContent>
      <DialogActions>
        {result ? (
          <Button variant="contained" onClick={onClose}>
            {t('common.actions.done')}
          </Button>
        ) : (
          <>
            <Button onClick={onClose} disabled={importMut.isPending}>
              {t('common.actions.cancel')}
            </Button>
            <Button variant="contained" disabled={!canImport} onClick={() => importMut.mutate()}>
              {importMut.isPending ? t('importing.import.importing') : t('common.actions.import')}
            </Button>
          </>
        )}
      </DialogActions>
    </Dialog>
  );
}
