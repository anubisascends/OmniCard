import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  Checkbox,
  Chip,
  Divider,
  FormControlLabel,
  Paper,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import DownloadIcon from '@mui/icons-material/Download';
import UploadFileIcon from '@mui/icons-material/UploadFile';
import { api } from '../api/client';
import { locationSelectOptions } from '../components/LocationSelectOptions';
import { useGame } from '../context/GameContext';
import { useFormatters } from '../i18n/format';

// Server format identifiers — not translated. Display labels resolve via importing.export.formats.
const EXPORT_FORMATS = ['appnative', 'tcgplayer', 'moxfield', 'manabox'];

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

function DecklistSection() {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const { game } = useGame();
  const [text, setText] = useState('');
  const [url, setUrl] = useState('');
  const check = useMutation({
    mutationFn: () =>
      api.decklistCheck({ game: game ?? 'Mtg', url: url || undefined, text: url ? undefined : text }),
  });

  return (
    <Paper variant="outlined" sx={{ p: 2 }}>
      <Typography variant="h6" gutterBottom>
        {t('importing.decklist.title')}
      </Typography>
      <Typography variant="body2" color="text.secondary" gutterBottom>
        {t('importing.decklist.descriptionPrefix')}
        {game ?? 'MTG'}.
      </Typography>
      <Stack spacing={2} sx={{ maxWidth: 560 }}>
        <TextField
          size="small"
          label={t('importing.decklist.urlLabel')}
          value={url}
          onChange={(e) => setUrl(e.target.value)}
        />
        <Divider>{t('importing.decklist.orPaste')}</Divider>
        <TextField
          label={t('importing.decklist.textLabel')}
          multiline
          minRows={4}
          value={text}
          onChange={(e) => setText(e.target.value)}
          placeholder={t('importing.decklist.textPlaceholder')}
          disabled={!!url}
        />
        <Button
          variant="contained"
          disabled={(!text && !url) || check.isPending}
          onClick={() => check.mutate()}
        >
          {check.isPending ? t('importing.decklist.checking') : t('importing.decklist.check')}
        </Button>
        {check.error && <Alert severity="error">{(check.error as Error).message}</Alert>}
        {check.data && (
          <Box>
            <Stack direction="row" spacing={1} sx={{ mb: 1 }}>
              <Chip color="success" label={t('importing.decklist.owned', { count: check.data.totalOwned })} />
              <Chip color="warning" label={t('importing.decklist.missing', { count: check.data.totalMissing })} />
              <Chip label={t('importing.decklist.toComplete', { cost: fmt.money(check.data.estimatedCost) })} />
            </Stack>
            {check.data.missing.length > 0 && (
              <>
                <Typography variant="subtitle2">{t('importing.decklist.missingHeading')}</Typography>
                {check.data.missing.map((m, i) => (
                  <Typography key={i} variant="body2" color="text.secondary">
                    {m.quantityNeeded}× {m.cardName}
                    {m.marketPrice != null ? ` — ${fmt.money(m.marketPrice)}` : ''}
                  </Typography>
                ))}
              </>
            )}
          </Box>
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
      <ExportSection />
      <DecklistSection />
    </Stack>
  );
}
