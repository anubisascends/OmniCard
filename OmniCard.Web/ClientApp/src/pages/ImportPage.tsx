import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Button,
  Checkbox,
  FormControlLabel,
  MenuItem,
  Paper,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import DownloadIcon from '@mui/icons-material/Download';
import LinkIcon from '@mui/icons-material/Link';
import UploadFileIcon from '@mui/icons-material/UploadFile';
import { api } from '../api/client';
import { locationSelectOptions } from '../components/LocationSelectOptions';
import { useGame } from '../context/GameContext';

// Server format identifiers — not translated. Display labels resolve via importing.export.formats.
const EXPORT_FORMATS = ['appnative', 'tcgplayer', 'moxfield', 'manabox'];
const CONDITIONS = ['NM', 'LP', 'MP', 'HP', 'DMG'];

function ExportSection() {
  const { t } = useTranslation();
  const { game } = useGame();
  return (
    <Paper variant="outlined" sx={{ p: 2 }}>
      <Typography variant="h6" gutterBottom>
        {t('importing.export.title')}
      </Typography>
      <Typography variant="body2" color="text.secondary" gutterBottom>
        {game
          ? t('importing.export.descriptionGame', { game })
          : t('importing.export.descriptionAll')}
      </Typography>
      <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
        {EXPORT_FORMATS.map((key) => (
          <Button
            key={key}
            variant="outlined"
            startIcon={<DownloadIcon />}
            component="a"
            href={api.exportUrl(key, game)}
          >
            {t(`importing.export.formats.${key}`)}
          </Button>
        ))}
      </Stack>
    </Paper>
  );
}

function ImportSection() {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const [file, setFile] = useState<File | null>(null);
  const [skipDuplicates, setSkipDuplicates] = useState(true);
  const [targetContainerId, setTargetContainerId] = useState<number | ''>('');
  const locations = useQuery({ queryKey: ['locations', undefined], queryFn: () => api.locations() });

  const importMut = useMutation({
    mutationFn: () => api.importCsv(file!, skipDuplicates, targetContainerId === '' ? undefined : (targetContainerId as number)),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['collection'] });
      qc.invalidateQueries({ queryKey: ['locations'] });
      qc.invalidateQueries({ queryKey: ['dashboard'] });
    },
  });

  return (
    <Paper variant="outlined" sx={{ p: 2 }}>
      <Typography variant="h6" gutterBottom>
        {t('importing.import.title')}
      </Typography>
      <Typography variant="body2" color="text.secondary" gutterBottom>
        {t('importing.import.description')}
      </Typography>
      <Stack spacing={2} sx={{ maxWidth: 480 }}>
        <Button variant="outlined" component="label" startIcon={<UploadFileIcon />}>
          {file ? file.name : t('importing.import.chooseFile')}
          <input
            type="file"
            accept=".csv,text/csv"
            hidden
            onChange={(e) => setFile(e.target.files?.[0] ?? null)}
          />
        </Button>
        <FormControlLabel
          control={<Checkbox checked={skipDuplicates} onChange={(e) => setSkipDuplicates(e.target.checked)} />}
          label={t('importing.import.skipDuplicates')}
        />
        <TextField
          select
          size="small"
          label={t('importing.import.targetLocationOptional')}
          value={targetContainerId}
          onChange={(e) => setTargetContainerId(e.target.value === '' ? '' : Number(e.target.value))}
        >
          {locationSelectOptions(locations.data, { label: t('importing.import.none') })}
        </TextField>
        <Button
          variant="contained"
          disabled={!file || importMut.isPending}
          onClick={() => importMut.mutate()}
        >
          {importMut.isPending ? t('importing.import.importing') : t('common.actions.import')}
        </Button>
        {importMut.error && <Alert severity="error">{(importMut.error as Error).message}</Alert>}
        {importMut.data && (
          <Alert severity="success">
            {t('importing.import.result', {
              imported: importMut.data.imported,
              count: importMut.data.totalRows,
              format: importMut.data.detectedFormat,
            })}
            {importMut.data.warnings.length > 0 &&
              t('importing.import.warnings', { count: importMut.data.warnings.length })}
          </Alert>
        )}
      </Stack>
    </Paper>
  );
}

/** Import a Moxfield/Archidekt deck URL straight into a location (both sites are MTG-only). */
function UrlImportSection() {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const [url, setUrl] = useState('');
  const [containerId, setContainerId] = useState<number | ''>('');
  const [condition, setCondition] = useState('NM');
  const [skipDuplicates, setSkipDuplicates] = useState(false);
  const locations = useQuery({ queryKey: ['locations', undefined], queryFn: () => api.locations() });

  const importMut = useMutation({
    mutationFn: () =>
      api.importUrl({ url: url.trim(), game: 'Mtg', containerId: containerId as number, condition, skipDuplicates }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['collection'] });
      qc.invalidateQueries({ queryKey: ['locations'] });
      qc.invalidateQueries({ queryKey: ['dashboard'] });
    },
  });
  const data = importMut.data;

  return (
    <Paper variant="outlined" sx={{ p: 2 }}>
      <Typography variant="h6" gutterBottom>
        {t('importing.url.title')}
      </Typography>
      <Typography variant="body2" color="text.secondary" gutterBottom>
        {t('importing.url.description')}
      </Typography>
      <Stack spacing={2} sx={{ maxWidth: 560 }}>
        <TextField
          size="small"
          label={t('importing.url.urlLabel')}
          placeholder="https://moxfield.com/decks/…"
          value={url}
          onChange={(e) => setUrl(e.target.value)}
        />
        <Stack direction="row" spacing={1}>
          <TextField
            select
            size="small"
            label={t('importing.url.targetLocation')}
            value={containerId}
            onChange={(e) => setContainerId(e.target.value === '' ? '' : Number(e.target.value))}
            sx={{ flex: 1 }}
          >
            {locationSelectOptions(locations.data)}
          </TextField>
          <TextField
            select
            size="small"
            label={t('common.labels.condition')}
            value={condition}
            onChange={(e) => setCondition(e.target.value)}
            sx={{ width: 120 }}
          >
            {CONDITIONS.map((c) => (
              <MenuItem key={c} value={c}>
                {t(`common.conditions.${c}`)}
              </MenuItem>
            ))}
          </TextField>
        </Stack>
        <FormControlLabel
          control={<Checkbox checked={skipDuplicates} onChange={(e) => setSkipDuplicates(e.target.checked)} />}
          label={t('importing.import.skipDuplicates')}
        />
        <Button
          variant="contained"
          startIcon={<LinkIcon />}
          disabled={!url.trim() || containerId === '' || importMut.isPending}
          onClick={() => importMut.mutate()}
        >
          {importMut.isPending ? t('importing.url.importing') : t('common.actions.import')}
        </Button>
        {importMut.error && <Alert severity="error">{(importMut.error as Error).message}</Alert>}
        {data && (
          <Alert severity={data.unresolvedNames.length > 0 ? 'warning' : 'success'}>
            {t('importing.url.result', { imported: data.imported, count: data.totalCards, deck: data.deckName })}
            {data.skipped > 0 && t('importing.url.skipped', { count: data.skipped })}
            {data.substitutedNames.length > 0 && (
              <Typography variant="body2" sx={{ mt: 1 }}>
                {t('importing.url.substituted', { count: data.substitutedNames.length })}{' '}
                {data.substitutedNames.join(', ')}
              </Typography>
            )}
            {data.unresolvedNames.length > 0 && (
              <Typography variant="body2" sx={{ mt: 1 }}>
                {t('importing.url.unresolved', { count: data.unresolvedNames.length })}{' '}
                {data.unresolvedNames.join(', ')}
              </Typography>
            )}
          </Alert>
        )}
      </Stack>
    </Paper>
  );
}

export function ImportPage() {
  const { t } = useTranslation();
  return (
    <Stack spacing={3}>
      <Typography variant="h4">{t('importing.pageTitle')}</Typography>
      <ImportSection />
      <UrlImportSection />
      <ExportSection />
    </Stack>
  );
}
