import { useState, type ReactNode } from 'react';
import { useMutation } from '@tanstack/react-query';
import { Alert, Box, Button, Stack, TextField, Typography } from '@mui/material';
import { useTranslation } from 'react-i18next';
import type { KioskSessionDto, KioskStatusDto } from '../api/types';
import { kioskApi, KioskApiError } from './kioskApi';
import { Centered } from './Centered';

/** The customer gives a name (unique among open pick lists) to start browsing. */
export function WelcomeScreen({
  status,
  logo,
  notice,
  onStarted,
}: {
  status: KioskStatusDto;
  logo: ReactNode;
  notice: string | null;
  onStarted: (session: KioskSessionDto) => void;
}) {
  const { t } = useTranslation();
  const [name, setName] = useState('');
  const start = useMutation({
    mutationFn: () => kioskApi.startSession(name.trim()),
    onSuccess: (session) => {
      setName('');
      onStarted(session);
    },
  });

  const error = start.error;
  const errorMessage =
    error instanceof KioskApiError && error.code === 'name-taken'
      ? t('kiosk.app.welcome.nameTaken', { name: name.trim() })
      : error instanceof Error
        ? error.message
        : null;

  return (
    <>
      <Box sx={{ position: 'fixed', top: 16, left: 16 }}>{logo}</Box>
      <Centered>
        <Typography variant="h3">{t('kiosk.app.welcome.title')}</Typography>
        <Typography variant="h6" color="text.secondary" sx={{ whiteSpace: 'pre-line' }}>
          {status.welcomeMessage || t('kiosk.app.welcome.body')}
        </Typography>
        {notice && <Alert severity="info">{notice}</Alert>}
        <Stack
          component="form"
          spacing={2}
          sx={{ width: '100%', maxWidth: 420 }}
          onSubmit={(e) => {
            e.preventDefault();
            if (name.trim()) start.mutate();
          }}
        >
          <TextField
            label={t('kiosk.app.welcome.nameLabel')}
            helperText={t('kiosk.app.welcome.nameHelp')}
            value={name}
            onChange={(e) => {
              setName(e.target.value);
              if (start.isError) start.reset();
            }}
            inputProps={{ maxLength: 40, style: { fontSize: 24 } }}
            autoComplete="off"
            autoFocus
          />
          {errorMessage && <Alert severity="warning">{errorMessage}</Alert>}
          <Button type="submit" variant="contained" size="large" disabled={!name.trim() || start.isPending} sx={{ py: 1.5 }}>
            {t('kiosk.app.welcome.start')}
          </Button>
        </Stack>
      </Centered>
    </>
  );
}
