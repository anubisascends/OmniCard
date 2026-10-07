import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Autocomplete,
  Box,
  Button,
  Chip,
  CircularProgress,
  Divider,
  FormControlLabel,
  MenuItem,
  Paper,
  Stack,
  Switch,
  TextField,
  Typography,
} from '@mui/material';
import { api } from '../../api/client';
import type { ScanFolderConfigDto, ScanFolderSettingsDto, ScanFolderStatusDto } from '../../api/types';
import { usePermissions } from '../../context/usePermissions';
import { LanguageSelect } from '../../lib/cardLanguages';
import { LocationPickerDialog } from '../dialogs/LocationPickerDialog';

const CONDITIONS = ['NM', 'LP', 'MP', 'HP', 'DMG'];

const blankFolder = (game: string): ScanFolderConfigDto => ({
  game,
  path: '',
  enabled: true,
  isFoil: false,
  condition: 'NM',
  language: null,
  setCodes: [],
  defaultContainerId: null,
});

/**
 * Settings ▸ Scan: the per-game folders the server watches for scanner output. Each subfolder of a
 * game's folder becomes a background scan batch, matched with that game's settings below. Admin only
 * (the server enforces it too) — these point the server at arbitrary paths and move files out of them.
 */
export function ScanFoldersCard() {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const { isAdmin } = usePermissions();
  const gamesQuery = useQuery({ queryKey: ['games'], queryFn: api.games });
  const settingsQuery = useQuery({
    queryKey: ['scan-folder-settings'],
    queryFn: api.scanFolderSettings,
    enabled: isAdmin,
  });

  const [form, setForm] = useState<ScanFolderSettingsDto | null>(null);
  const [saved, setSaved] = useState(false);

  // Seed the form once loaded (and after a save re-fetches the normalized values).
  useEffect(() => {
    if (settingsQuery.data) setForm(settingsQuery.data.settings);
  }, [settingsQuery.data]);

  const save = useMutation({
    mutationFn: (body: ScanFolderSettingsDto) => api.scanFolderSettingsUpdate(body),
    onMutate: () => setSaved(false),
    onSuccess: () => {
      setSaved(true);
      void qc.invalidateQueries({ queryKey: ['scan-folder-settings'] });
    },
  });

  if (!isAdmin) return null;

  const games = gamesQuery.data ?? [];
  const folderFor = (game: string) => form?.folders.find((f) => f.game === game) ?? blankFolder(game);
  const statusFor = (game: string) => settingsQuery.data?.status.find((s) => s.game === game);
  const setFolder = (game: string, patch: Partial<ScanFolderConfigDto>) =>
    setForm((f) => {
      if (!f) return f;
      const next = { ...folderFor(game), ...patch };
      const others = f.folders.filter((x) => x.game !== game);
      return { ...f, folders: [...others, next] };
    });

  const submit = () => {
    if (!form) return;
    save.mutate({ ...form, folders: form.folders.filter((f) => f.path.trim() !== '') });
  };

  return (
    <Paper variant="outlined" sx={{ p: 2, maxWidth: 960 }}>
      <Typography variant="h6" gutterBottom>
        {t('settings.scanFolders.title')}
      </Typography>
      <Typography variant="body2" color="text.secondary" gutterBottom>
        {t('settings.scanFolders.description')}
      </Typography>
      <Alert severity="info" sx={{ my: 1 }}>
        {t('settings.scanFolders.iisHint')}
      </Alert>

      {!form ? (
        <CircularProgress size={24} sx={{ mt: 1 }} />
      ) : (
        <Stack spacing={2} sx={{ mt: 1 }}>
          <Stack direction="row" spacing={2} alignItems="center" flexWrap="wrap" useFlexGap>
            <FormControlLabel
              control={
                <Switch checked={form.enabled} onChange={(e) => setForm({ ...form, enabled: e.target.checked })} />
              }
              label={t('settings.scanFolders.enabled')}
            />
            <TextField
              size="small"
              type="number"
              label={t('settings.scanFolders.quietPeriod')}
              helperText={t('settings.scanFolders.quietPeriodHelp')}
              value={form.quietPeriodSeconds}
              onChange={(e) => setForm({ ...form, quietPeriodSeconds: Number(e.target.value) || 0 })}
              inputProps={{ min: 10, max: 3600 }}
              sx={{ width: 240 }}
            />
            <TextField
              size="small"
              type="number"
              label={t('settings.scanFolders.retention')}
              helperText={t('settings.scanFolders.retentionHelp')}
              value={form.retentionDays}
              onChange={(e) => setForm({ ...form, retentionDays: Number(e.target.value) || 0 })}
              inputProps={{ min: 1, max: 365 }}
              sx={{ width: 240 }}
            />
          </Stack>

          {games.map((g) => (
            <Box key={g.id}>
              <Divider textAlign="left" sx={{ mb: 1.5 }}>
                <Typography variant="subtitle2">{g.displayName}</Typography>
              </Divider>
              <FolderRow
                game={g.id}
                folder={folderFor(g.id)}
                status={statusFor(g.id)}
                onChange={(patch) => setFolder(g.id, patch)}
              />
            </Box>
          ))}

          {save.error && <Alert severity="error">{(save.error as Error).message}</Alert>}
          {saved && <Alert severity="success">{t('settings.scanFolders.saved')}</Alert>}
          <Box>
            <Button variant="contained" disabled={save.isPending} onClick={submit}>
              {save.isPending ? t('common.states.saving') : t('common.actions.save')}
            </Button>
          </Box>
        </Stack>
      )}
    </Paper>
  );
}

function FolderRow({
  game,
  folder,
  status,
  onChange,
}: {
  game: string;
  folder: ScanFolderConfigDto;
  status?: ScanFolderStatusDto;
  onChange: (patch: Partial<ScanFolderConfigDto>) => void;
}) {
  const { t } = useTranslation();
  const [pickerOpen, setPickerOpen] = useState(false);
  const hasPath = folder.path.trim() !== '';
  const setsQuery = useQuery({ queryKey: ['sets', game], queryFn: () => api.sets(game), enabled: hasPath });
  const locations = useQuery({ queryKey: ['locations', undefined], queryFn: () => api.locations(), enabled: hasPath });
  const sets = setsQuery.data ?? [];
  const chosenSets = sets.filter((s) => folder.setCodes.some((c) => c.toLowerCase() === s.setCode.toLowerCase()));
  const locationName =
    folder.defaultContainerId != null
      ? (locations.data?.find((l) => l.id === folder.defaultContainerId)?.name ?? `#${folder.defaultContainerId}`)
      : t('settings.scanFolders.noDefaultLocation');

  return (
    <Stack spacing={1.5}>
      <Stack direction="row" spacing={2} alignItems="center" flexWrap="wrap" useFlexGap>
        <TextField
          size="small"
          label={t('settings.scanFolders.folderPath')}
          placeholder={t('settings.scanFolders.folderPathPlaceholder', { game })}
          value={folder.path}
          onChange={(e) => onChange({ path: e.target.value })}
          sx={{ flex: '1 1 320px' }}
        />
        <FormControlLabel
          control={<Switch checked={folder.enabled} onChange={(e) => onChange({ enabled: e.target.checked })} />}
          label={t('settings.scanFolders.folderEnabled')}
        />
        {hasPath && status && <FolderStatusChip status={status} />}
      </Stack>
      {status?.lastError && (
        <Typography variant="caption" color="error">
          {status.lastError}
        </Typography>
      )}
      {hasPath && (
        <Stack direction="row" spacing={2} alignItems="center" flexWrap="wrap" useFlexGap>
          <Autocomplete
            multiple
            size="small"
            options={sets}
            getOptionLabel={(s) => s.setName}
            isOptionEqualToValue={(a, b) => a.setCode === b.setCode}
            value={chosenSets}
            onChange={(_, v) => onChange({ setCodes: v.map((s) => s.setCode) })}
            sx={{ minWidth: 260, flex: '1 1 260px' }}
            renderInput={(p) => (
              <TextField
                {...p}
                label={t('scan.controls.artSets')}
                placeholder={chosenSets.length ? '' : t('scan.controls.allSets')}
              />
            )}
          />
          <TextField
            select
            size="small"
            label={t('common.labels.condition')}
            value={folder.condition}
            onChange={(e) => onChange({ condition: e.target.value })}
            sx={{ minWidth: 120 }}
          >
            {CONDITIONS.map((c) => (
              <MenuItem key={c} value={c}>
                {c}
              </MenuItem>
            ))}
          </TextField>
          <LanguageSelect
            game={game}
            allowAuto
            label={t('scan.controls.language')}
            value={folder.language ?? ''}
            onChange={(language) => onChange({ language: language || null })}
          />
          <FormControlLabel
            control={<Switch checked={folder.isFoil} onChange={(e) => onChange({ isFoil: e.target.checked })} />}
            label={t('common.labels.foil')}
          />
          <Stack direction="row" spacing={1} alignItems="center">
            <Typography variant="body2" color="text.secondary">
              {t('settings.scanFolders.defaultLocation')}:
            </Typography>
            <Chip size="small" variant="outlined" label={locationName} />
            <Button size="small" onClick={() => setPickerOpen(true)}>
              {t('settings.scanFolders.chooseLocation')}
            </Button>
            {folder.defaultContainerId != null && (
              <Button size="small" onClick={() => onChange({ defaultContainerId: null })}>
                {t('settings.scanFolders.clearLocation')}
              </Button>
            )}
          </Stack>
        </Stack>
      )}
      <LocationPickerDialog
        open={pickerOpen}
        title={t('settings.scanFolders.defaultLocation')}
        allowCreate={false}
        onPick={(id) => {
          onChange({ defaultContainerId: id });
          setPickerOpen(false);
        }}
        onClose={() => setPickerOpen(false)}
      />
    </Stack>
  );
}

function FolderStatusChip({ status }: { status: ScanFolderStatusDto }) {
  const { t } = useTranslation();
  if (!status.exists) return <Chip size="small" color="error" label={t('settings.scanFolders.statusMissing')} />;
  if (!status.writable) return <Chip size="small" color="warning" label={t('settings.scanFolders.statusNoWrite')} />;
  return <Chip size="small" color="success" label={t('settings.scanFolders.statusFound')} />;
}
