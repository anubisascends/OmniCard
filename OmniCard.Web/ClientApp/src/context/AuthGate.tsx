import { useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  Checkbox,
  CircularProgress,
  Container,
  FormControlLabel,
  Paper,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import { useTranslation } from 'react-i18next';
import { api, ApiError } from '../api/client';

/**
 * Renders the app only when the session is authorized. If the server requires a passphrase and this
 * session isn't unlocked, shows a login screen. When no passphrase is configured, passes straight
 * through.
 */
export function AuthGate({ children }: { children: ReactNode }) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const statusQuery = useQuery({ queryKey: ['auth-status'], queryFn: api.authStatus });
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [rememberMe, setRememberMe] = useState(false);

  const login = useMutation({
    mutationFn: () => api.login(username, password, rememberMe),
    onSuccess: (status) => qc.setQueryData(['auth-status'], status),
  });

  if (statusQuery.isLoading) {
    return (
      <Box sx={{ display: 'grid', placeItems: 'center', height: '100vh' }}>
        <CircularProgress />
      </Box>
    );
  }

  const status = statusQuery.data;
  const authed = status ? !status.authRequired || status.authenticated : false;
  if (authed) return <>{children}</>;

  return (
    <Container maxWidth="xs" sx={{ display: 'grid', placeItems: 'center', minHeight: '100vh' }}>
      <Paper sx={{ p: 4, width: '100%' }}>
        <Stack
          component="form"
          spacing={2}
          onSubmit={(e) => {
            e.preventDefault();
            login.mutate();
          }}
        >
          <Typography variant="h5">{t('common.app.name')}</Typography>
          <Typography variant="body2" color="text.secondary">
            {t('auth.signInToContinue')}
          </Typography>
          <TextField
            label={t('auth.username')}
            value={username}
            onChange={(e) => setUsername(e.target.value)}
            autoFocus
            autoComplete="username"
            fullWidth
          />
          <TextField
            type="password"
            label={t('auth.password')}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            autoComplete="current-password"
            fullWidth
          />
          <FormControlLabel
            control={
              <Checkbox checked={rememberMe} onChange={(e) => setRememberMe(e.target.checked)} />
            }
            label={t('auth.rememberMe')}
          />
          {login.error instanceof ApiError && <Alert severity="error">{login.error.message}</Alert>}
          <Button
            type="submit"
            variant="contained"
            disabled={login.isPending || !username || !password}
          >
            {login.isPending ? t('auth.signingIn') : t('auth.signIn')}
          </Button>
        </Stack>
      </Paper>
    </Container>
  );
}
