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
import type { SignInStepDto } from '../api/types';

type Step = 'identify' | SignInStepDto['step'];

/**
 * Renders the app only when the session is signed in; otherwise shows the two-step sign-in screen.
 * Step 1 takes a username or email; the server then says whether to ask for the password, or (for a
 * new account / admin-required reset) the setup key the admin handed out plus a new password.
 */
export function AuthGate({ children }: { children: ReactNode }) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const statusQuery = useQuery({ queryKey: ['auth-status'], queryFn: api.authStatus });
  const [step, setStep] = useState<Step>('identify');
  const [login, setLogin] = useState('');
  const [password, setPassword] = useState('');
  const [setupKey, setSetupKey] = useState('');
  const [confirm, setConfirm] = useState('');
  const [rememberMe, setRememberMe] = useState(false);

  const identify = useMutation({
    mutationFn: () => api.signInStep(login.trim()),
    onSuccess: (res) => setStep(res.step),
  });
  const signIn = useMutation({
    mutationFn: () => api.login(login.trim(), password, rememberMe),
    onSuccess: (status) => qc.setQueryData(['auth-status'], status),
  });
  const completeSetup = useMutation({
    mutationFn: () => api.completeSetup(login.trim(), setupKey, password, rememberMe),
    onSuccess: (status) => qc.setQueryData(['auth-status'], status),
    onError: (err) => {
      // Too many wrong keys voids it; switch to the "ask your administrator" screen.
      if (err instanceof ApiError && (err.body as { locked?: boolean } | undefined)?.locked) setStep('locked');
    },
  });

  const back = () => {
    setStep('identify');
    setPassword('');
    setSetupKey('');
    setConfirm('');
    signIn.reset();
    completeSetup.reset();
  };

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

  const mismatch = step === 'setupKey' && confirm.length > 0 && password !== confirm;
  const error = (step === 'identify' ? identify : step === 'setupKey' ? completeSetup : signIn).error;

  const canSubmit =
    step === 'identify'
      ? !!login.trim() && !identify.isPending
      : step === 'password'
        ? !!password && !signIn.isPending
        : step === 'setupKey'
          ? !!setupKey.trim() && !!password && password === confirm && !completeSetup.isPending
          : false;

  const submit = () => {
    if (!canSubmit) return;
    if (step === 'identify') identify.mutate();
    else if (step === 'password') signIn.mutate();
    else if (step === 'setupKey') completeSetup.mutate();
  };

  const rememberMeBox = (
    <FormControlLabel
      control={<Checkbox checked={rememberMe} onChange={(e) => setRememberMe(e.target.checked)} />}
      label={t('auth.rememberMe')}
    />
  );

  return (
    <Container maxWidth="xs" sx={{ display: 'grid', placeItems: 'center', minHeight: '100vh' }}>
      <Paper sx={{ p: 4, width: '100%' }}>
        <Stack
          component="form"
          spacing={2}
          onSubmit={(e) => {
            e.preventDefault();
            submit();
          }}
        >
          <Typography variant="h5">{t('common.app.name')}</Typography>

          {step === 'identify' ? (
            <>
              <Typography variant="body2" color="text.secondary">
                {t('auth.signInToContinue')}
              </Typography>
              <TextField
                label={t('auth.usernameOrEmail')}
                value={login}
                onChange={(e) => setLogin(e.target.value)}
                autoFocus
                autoComplete="username"
                fullWidth
              />
            </>
          ) : (
            // Who's signing in, with a way back to fix a typo. Also keeps the username in the form for
            // password managers.
            <Stack direction="row" alignItems="center" justifyContent="space-between" spacing={1}>
              <Typography variant="body1" sx={{ fontWeight: 500, overflowWrap: 'anywhere' }}>
                {login.trim()}
              </Typography>
              <Button size="small" onClick={back}>
                {t('auth.changeAccount')}
              </Button>
              <input type="hidden" name="username" autoComplete="username" value={login.trim()} readOnly />
            </Stack>
          )}

          {step === 'password' && (
            <>
              <TextField
                type="password"
                label={t('auth.password')}
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                autoFocus
                autoComplete="current-password"
                fullWidth
              />
              {rememberMeBox}
            </>
          )}

          {step === 'setupKey' && (
            <>
              <Alert severity="info">{t('auth.setupKeyIntro')}</Alert>
              <TextField
                label={t('auth.setupKey')}
                value={setupKey}
                onChange={(e) => setSetupKey(e.target.value)}
                autoFocus
                autoComplete="one-time-code"
                inputProps={{ autoCapitalize: 'characters', spellCheck: false }}
                fullWidth
              />
              <TextField
                type="password"
                label={t('auth.newPassword')}
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                autoComplete="new-password"
                fullWidth
              />
              <TextField
                type="password"
                label={t('auth.confirmNewPassword')}
                value={confirm}
                onChange={(e) => setConfirm(e.target.value)}
                autoComplete="new-password"
                error={mismatch}
                helperText={mismatch ? t('auth.passwordMismatch') : ' '}
                fullWidth
              />
              {rememberMeBox}
            </>
          )}

          {step === 'locked' && <Alert severity="warning">{t('auth.lockedMessage')}</Alert>}

          {error instanceof ApiError && step !== 'locked' && <Alert severity="error">{error.message}</Alert>}

          {step !== 'locked' && (
            <Button type="submit" variant="contained" disabled={!canSubmit}>
              {step === 'identify'
                ? identify.isPending
                  ? t('auth.checking')
                  : t('auth.continue')
                : step === 'setupKey'
                  ? completeSetup.isPending
                    ? t('auth.signingIn')
                    : t('auth.setPasswordAndSignIn')
                  : signIn.isPending
                    ? t('auth.signingIn')
                    : t('auth.signIn')}
            </Button>
          )}
          {step === 'locked' && (
            <Button variant="outlined" onClick={back}>
              {t('auth.backToSignIn')}
            </Button>
          )}
        </Stack>
      </Paper>
    </Container>
  );
}
