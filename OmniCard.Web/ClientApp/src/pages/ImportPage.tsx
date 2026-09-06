import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  Checkbox,
  Chip,
  Divider,
  FormControlLabel,
  MenuItem,
  Paper,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import DownloadIcon from '@mui/icons-material/Download';
import UploadFileIcon from '@mui/icons-material/UploadFile';
import { api } from '../api/client';
import { useGame } from '../context/GameContext';

const money = (n: number) => n.toLocaleString(undefined, { style: 'currency', currency: 'USD' });

const EXPORT_FORMATS = [
  { key: 'appnative', label: 'App-native' },
  { key: 'tcgplayer', label: 'TCGplayer' },
  { key: 'moxfield', label: 'Moxfield' },
  { key: 'manabox', label: 'Manabox' },
];

function ExportSection() {
  const { game } = useGame();
  return (
    <Paper variant="outlined" sx={{ p: 2 }}>
      <Typography variant="h6" gutterBottom>
        Export collection (CSV)
      </Typography>
      <Typography variant="body2" color="text.secondary" gutterBottom>
        Downloads the current game filter{game ? ` (${game})` : ' (all games)'}.
      </Typography>
      <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
        {EXPORT_FORMATS.map((f) => (
          <Button
            key={f.key}
            variant="outlined"
            startIcon={<DownloadIcon />}
            component="a"
            href={api.exportUrl(f.key, game)}
          >
            {f.label}
          </Button>
        ))}
      </Stack>
    </Paper>
  );
}

function ImportSection() {
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
        Import collection (CSV)
      </Typography>
      <Typography variant="body2" color="text.secondary" gutterBottom>
        Auto-detects app-native, TCGplayer, Moxfield, and Manabox formats.
      </Typography>
      <Stack spacing={2} sx={{ maxWidth: 480 }}>
        <Button variant="outlined" component="label" startIcon={<UploadFileIcon />}>
          {file ? file.name : 'Choose CSV file'}
          <input
            type="file"
            accept=".csv,text/csv"
            hidden
            onChange={(e) => setFile(e.target.files?.[0] ?? null)}
          />
        </Button>
        <FormControlLabel
          control={<Checkbox checked={skipDuplicates} onChange={(e) => setSkipDuplicates(e.target.checked)} />}
          label="Skip duplicates already in collection"
        />
        <TextField
          select
          size="small"
          label="Target location (optional)"
          value={targetContainerId}
          onChange={(e) => setTargetContainerId(e.target.value === '' ? '' : Number(e.target.value))}
        >
          <MenuItem value="">— none —</MenuItem>
          {locations.data?.map((l) => (
            <MenuItem key={l.id} value={l.id}>
              {l.name}
            </MenuItem>
          ))}
        </TextField>
        <Button
          variant="contained"
          disabled={!file || importMut.isPending}
          onClick={() => importMut.mutate()}
        >
          {importMut.isPending ? 'Importing…' : 'Import'}
        </Button>
        {importMut.error && <Alert severity="error">{(importMut.error as Error).message}</Alert>}
        {importMut.data && (
          <Alert severity="success">
            Imported {importMut.data.imported} of {importMut.data.totalRows} rows (
            {importMut.data.detectedFormat}).
            {importMut.data.warnings.length > 0 && ` ${importMut.data.warnings.length} warning(s).`}
          </Alert>
        )}
      </Stack>
    </Paper>
  );
}

function DecklistSection() {
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
        Check a decklist
      </Typography>
      <Typography variant="body2" color="text.secondary" gutterBottom>
        Paste a Moxfield/Archidekt URL, or a decklist, to see owned vs. missing against{' '}
        {game ?? 'MTG'}.
      </Typography>
      <Stack spacing={2} sx={{ maxWidth: 560 }}>
        <TextField
          size="small"
          label="Decklist URL (Moxfield / Archidekt)"
          value={url}
          onChange={(e) => setUrl(e.target.value)}
        />
        <Divider>or paste</Divider>
        <TextField
          label="Decklist text"
          multiline
          minRows={4}
          value={text}
          onChange={(e) => setText(e.target.value)}
          placeholder={'4 Lightning Bolt\n2 Counterspell'}
          disabled={!!url}
        />
        <Button
          variant="contained"
          disabled={(!text && !url) || check.isPending}
          onClick={() => check.mutate()}
        >
          {check.isPending ? 'Checking…' : 'Check'}
        </Button>
        {check.error && <Alert severity="error">{(check.error as Error).message}</Alert>}
        {check.data && (
          <Box>
            <Stack direction="row" spacing={1} sx={{ mb: 1 }}>
              <Chip color="success" label={`${check.data.totalOwned} owned`} />
              <Chip color="warning" label={`${check.data.totalMissing} missing`} />
              <Chip label={`${money(check.data.estimatedCost)} to complete`} />
            </Stack>
            {check.data.missing.length > 0 && (
              <>
                <Typography variant="subtitle2">Missing</Typography>
                {check.data.missing.map((m, i) => (
                  <Typography key={i} variant="body2" color="text.secondary">
                    {m.quantityNeeded}× {m.cardName}
                    {m.marketPrice != null ? ` — ${money(m.marketPrice)}` : ''}
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
  return (
    <Stack spacing={3}>
      <Typography variant="h4">Import / Export</Typography>
      <ImportSection />
      <ExportSection />
      <DecklistSection />
    </Stack>
  );
}
