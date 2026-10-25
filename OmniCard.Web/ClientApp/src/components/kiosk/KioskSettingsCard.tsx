import { useEffect, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  Divider,
  FormControlLabel,
  List,
  ListItem,
  ListItemText,
  Paper,
  Stack,
  Switch,
  TextField,
  Typography,
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import { useTranslation } from 'react-i18next';
import { api } from '../../api/client';
import type { KioskDeviceDto, KioskSettingsDto } from '../../api/types';
import { useFormatters } from '../../i18n/format';
import { LocationPickerDialog } from '../dialogs/LocationPickerDialog';

/** Settings ▸ Kiosk (admin only): whether kiosks are open, which locations customers browse, the idle
 *  timeout, prices, the welcome text — and the enrolled kiosk devices, which can be revoked. */
export function KioskSettingsCard() {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const qc = useQueryClient();
  const settings = useQuery({ queryKey: ['kiosk-settings'], queryFn: api.kioskSettings });
  const locations = useQuery({ queryKey: ['locations', undefined], queryFn: () => api.locations() });
  const devices = useQuery({ queryKey: ['kiosk-devices'], queryFn: api.kioskDevices });
  const [draft, setDraft] = useState<KioskSettingsDto | null>(null);
  const [pickOpen, setPickOpen] = useState(false);
  const [revoking, setRevoking] = useState<KioskDeviceDto | null>(null);

  useEffect(() => {
    if (settings.data) setDraft(settings.data);
  }, [settings.data]);

  const save = useMutation({
    mutationFn: (body: KioskSettingsDto) => api.kioskSettingsUpdate(body),
    onSuccess: (saved) => {
      qc.setQueryData(['kiosk-settings'], saved);
      setDraft(saved);
    },
  });
  const revoke = useMutation({
    mutationFn: api.kioskDeviceRevoke,
    onSuccess: () => {
      setRevoking(null);
      void qc.invalidateQueries({ queryKey: ['kiosk-devices'] });
    },
  });
  const remove = useMutation({
    mutationFn: api.kioskDeviceDelete,
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['kiosk-devices'] }),
  });

  if (settings.isLoading || !draft) return <CircularProgress />;
  if (settings.isError) return <Alert severity="error">{(settings.error as Error).message}</Alert>;

  const kioskUrl = `${window.location.origin}/kiosk`;
  const dirty = JSON.stringify(draft) !== JSON.stringify(settings.data);
  const locationName = (id: number) =>
    locations.data?.find((l) => l.id === id)?.name ?? t('kiosk.settings.unknownLocation', { id });
  const update = (patch: Partial<KioskSettingsDto>) => {
    save.reset();
    setDraft({ ...draft, ...patch });
  };

  return (
    <Stack spacing={2} sx={{ maxWidth: 720 }}>
      <Paper variant="outlined" sx={{ p: 2 }}>
        <Typography variant="h6" gutterBottom>
          {t('kiosk.settings.title')}
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          {t('kiosk.settings.intro', { url: kioskUrl })}
        </Typography>

        <Stack spacing={2}>
          <Box>
            <FormControlLabel
              control={<Switch checked={draft.enabled} onChange={(e) => update({ enabled: e.target.checked })} />}
              label={t('kiosk.settings.enabled')}
            />
            <Typography variant="caption" color="text.secondary" component="div">
              {t('kiosk.settings.enabledHelp')}
            </Typography>
          </Box>

          <Box>
            <Typography variant="subtitle2">{t('kiosk.settings.locations')}</Typography>
            <Typography variant="caption" color="text.secondary" component="div" sx={{ mb: 1 }}>
              {t('kiosk.settings.locationsHelp')}
            </Typography>
            <Stack direction="row" sx={{ flexWrap: 'wrap', gap: 1 }}>
              {draft.locationIds.length === 0 && (
                <Typography variant="body2" color="warning.main">
                  {t('kiosk.settings.noLocations')}
                </Typography>
              )}
              {draft.locationIds.map((id) => (
                <Chip
                  key={id}
                  label={locationName(id)}
                  onDelete={() => update({ locationIds: draft.locationIds.filter((x) => x !== id) })}
                />
              ))}
            </Stack>
            <Button size="small" startIcon={<AddIcon />} sx={{ mt: 1 }} onClick={() => setPickOpen(true)}>
              {t('kiosk.settings.addLocation')}
            </Button>
          </Box>

          <TextField
            type="number"
            label={t('kiosk.settings.idleTimeout')}
            helperText={t('kiosk.settings.idleTimeoutHelp')}
            value={draft.idleTimeoutMinutes}
            onChange={(e) => update({ idleTimeoutMinutes: Number(e.target.value) || 0 })}
            inputProps={{ min: 2, max: 240 }}
            sx={{ maxWidth: 320 }}
          />

          <FormControlLabel
            control={<Switch checked={draft.showPrices} onChange={(e) => update({ showPrices: e.target.checked })} />}
            label={t('kiosk.settings.showPrices')}
          />

          <TextField
            label={t('kiosk.settings.welcomeMessage')}
            helperText={t('kiosk.settings.welcomeMessageHelp')}
            value={draft.welcomeMessage ?? ''}
            onChange={(e) => update({ welcomeMessage: e.target.value })}
            multiline
            minRows={2}
            inputProps={{ maxLength: 300 }}
          />

          {save.isError && <Alert severity="error">{(save.error as Error).message}</Alert>}
          {save.isSuccess && !dirty && <Alert severity="success">{t('kiosk.settings.saved')}</Alert>}
          <Stack direction="row" spacing={1}>
            <Button variant="contained" disabled={!dirty || save.isPending} onClick={() => save.mutate(draft)}>
              {t('common.actions.save')}
            </Button>
            <Button disabled={!dirty} onClick={() => settings.data && setDraft(settings.data)}>
              {t('common.actions.reset')}
            </Button>
          </Stack>
        </Stack>
      </Paper>

      <Paper variant="outlined" sx={{ p: 2 }}>
        <Typography variant="h6" gutterBottom>
          {t('kiosk.settings.devices')}
        </Typography>
        {(devices.data ?? []).length === 0 ? (
          <Typography variant="body2" color="text.secondary">
            {t('kiosk.settings.noDevices')}
          </Typography>
        ) : (
          <List disablePadding>
            {devices.data!.map((d, i) => (
              <Box key={d.id}>
                {i > 0 && <Divider component="li" />}
                <ListItem
                  disableGutters
                  secondaryAction={
                    d.revoked ? (
                      <Button size="small" color="error" onClick={() => remove.mutate(d.id)}>
                        {t('kiosk.settings.deleteDevice')}
                      </Button>
                    ) : (
                      <Button size="small" color="error" onClick={() => setRevoking(d)}>
                        {t('kiosk.settings.revoke')}
                      </Button>
                    )
                  }
                >
                  <ListItemText
                    primary={
                      <Stack direction="row" spacing={1} alignItems="center">
                        <span>{d.name}</span>
                        {d.revoked && <Chip size="small" label={t('kiosk.settings.revoked')} />}
                      </Stack>
                    }
                    secondary={[
                      t('kiosk.settings.deviceCreated', { date: fmt.dateTime(d.createdAt), by: d.createdBy ?? '—' }),
                      d.lastSeenAt ? t('kiosk.settings.deviceLastSeen', { date: fmt.dateTime(d.lastSeenAt) }) : null,
                    ]
                      .filter(Boolean)
                      .join(' · ')}
                  />
                </ListItem>
              </Box>
            ))}
          </List>
        )}
      </Paper>

      <LocationPickerDialog
        open={pickOpen}
        allowCreate={false}
        includeReadOnly
        onClose={() => setPickOpen(false)}
        onPick={(id) => {
          setPickOpen(false);
          if (!draft.locationIds.includes(id)) update({ locationIds: [...draft.locationIds, id] });
        }}
      />

      <Dialog open={revoking !== null} onClose={() => setRevoking(null)}>
        <DialogContent>
          <Typography>{t('kiosk.settings.revokeConfirm', { name: revoking?.name ?? '' })}</Typography>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setRevoking(null)}>{t('common.actions.cancel')}</Button>
          <Button color="error" variant="contained" onClick={() => revoking && revoke.mutate(revoking.id)} disabled={revoke.isPending}>
            {t('kiosk.settings.revoke')}
          </Button>
        </DialogActions>
      </Dialog>
    </Stack>
  );
}
