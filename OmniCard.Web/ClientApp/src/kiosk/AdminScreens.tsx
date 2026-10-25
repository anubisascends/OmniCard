import { useState } from 'react';
import { useMutation } from '@tanstack/react-query';
import {
  Alert,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import { useTranslation } from 'react-i18next';
import type { KioskStatusDto } from '../api/types';
import { kioskApi, KioskApiError } from './kioskApi';
import { Centered } from './Centered';

function errorText(err: unknown, notAdmin: string, fallback: string) {
  if (err instanceof KioskApiError && err.code === 'not-admin') return notAdmin;
  return err instanceof Error ? err.message : fallback;
}

/** First run on a device: an administrator signs in here to turn it into a kiosk. The admin's
 *  credentials are only checked, never kept — the device gets a kiosk-only cookie. */
export function EnrollScreen({ onEnrolled }: { onEnrolled: (status: KioskStatusDto) => void }) {
  const { t } = useTranslation();
  const [deviceName, setDeviceName] = useState('');
  const [login, setLogin] = useState('');
  const [password, setPassword] = useState('');
  const enroll = useMutation({
    mutationFn: () => kioskApi.enroll(login.trim(), password, deviceName.trim()),
    onSuccess: onEnrolled,
  });

  return (
    <Centered>
      <Typography variant="h4">{t('kiosk.app.enroll.title')}</Typography>
      <Typography color="text.secondary">{t('kiosk.app.enroll.intro')}</Typography>
      <Stack
        component="form"
        spacing={2}
        sx={{ width: '100%', maxWidth: 420 }}
        onSubmit={(e) => {
          e.preventDefault();
          enroll.mutate();
        }}
      >
        <TextField
          label={t('kiosk.app.enroll.deviceName')}
          helperText={t('kiosk.app.enroll.deviceNameHelp')}
          value={deviceName}
          onChange={(e) => setDeviceName(e.target.value)}
          inputProps={{ maxLength: 60 }}
        />
        <TextField
          label={t('kiosk.app.enroll.login')}
          value={login}
          onChange={(e) => setLogin(e.target.value)}
          autoComplete="off"
          required
        />
        <TextField
          label={t('kiosk.app.enroll.password')}
          type="password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          autoComplete="off"
          required
        />
        {enroll.isError && (
          <Alert severity="error">{errorText(enroll.error, t('kiosk.app.enroll.notAdmin'), t('kiosk.app.error'))}</Alert>
        )}
        <Button type="submit" variant="contained" size="large" disabled={!login.trim() || !password || enroll.isPending}>
          {t('kiosk.app.enroll.submit')}
        </Button>
      </Stack>
    </Centered>
  );
}

/** Hidden behind five quick taps on the kiosk logo: an administrator takes the device out of kiosk mode. */
export function ExitKioskDialog({ open, onClose, onExited }: { open: boolean; onClose: () => void; onExited: () => void }) {
  const { t } = useTranslation();
  const [login, setLogin] = useState('');
  const [password, setPassword] = useState('');
  const exit = useMutation({
    mutationFn: () => kioskApi.unenroll(login.trim(), password),
    onSuccess: () => {
      setLogin('');
      setPassword('');
      onExited();
    },
  });
  const close = () => {
    setLogin('');
    setPassword('');
    exit.reset();
    onClose();
  };

  return (
    <Dialog open={open} onClose={close} maxWidth="xs" fullWidth>
      <form
        onSubmit={(e) => {
          e.preventDefault();
          exit.mutate();
        }}
      >
        <DialogTitle>{t('kiosk.app.exit.title')}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ pt: 1 }}>
            <Typography variant="body2" color="text.secondary">
              {t('kiosk.app.exit.body')}
            </Typography>
            <TextField
              label={t('kiosk.app.enroll.login')}
              value={login}
              onChange={(e) => setLogin(e.target.value)}
              autoComplete="off"
              autoFocus
            />
            <TextField
              label={t('kiosk.app.enroll.password')}
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              autoComplete="off"
            />
            {exit.isError && (
              <Alert severity="error">{errorText(exit.error, t('kiosk.app.enroll.notAdmin'), t('kiosk.app.error'))}</Alert>
            )}
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={close}>{t('common.actions.cancel')}</Button>
          <Button type="submit" color="error" variant="contained" disabled={!login.trim() || !password || exit.isPending}>
            {t('kiosk.app.exit.submit')}
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  );
}
