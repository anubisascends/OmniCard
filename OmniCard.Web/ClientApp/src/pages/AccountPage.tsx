import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  Paper,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import { api, ApiError } from '../api/client';
import type { AccountDto } from '../api/types';
import { useFormatters } from '../i18n/format';

/**
 * The signed-in user's own account: profile summary, email, and password. Every signed-in user can
 * open it (no permission needed). Managing OTHER accounts stays on Administration ▸ Users (admins).
 * New self-service sections go here as another card in the stack.
 */
export function AccountPage() {
  const { t } = useTranslation();
  const accountQuery = useQuery({ queryKey: ['account'], queryFn: api.account });

  return (
    <Stack spacing={3}>
      <Typography variant="h4">{t('account.title')}</Typography>
      {accountQuery.isLoading ? (
        <CircularProgress size={24} />
      ) : accountQuery.data ? (
        <>
          <ProfileCard account={accountQuery.data} />
          <EmailCard account={accountQuery.data} />
          <ChangePasswordCard />
        </>
      ) : (
        accountQuery.error instanceof ApiError && <Alert severity="error">{accountQuery.error.message}</Alert>
      )}
    </Stack>
  );
}

/** Who you're signed in as. */
function ProfileCard({ account }: { account: AccountDto }) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  return (
    <Paper variant="outlined" sx={{ p: 2, maxWidth: 640 }}>
      <Typography variant="h6" gutterBottom>
        {t('account.profile.title')}
      </Typography>
      <Stack spacing={1}>
        <Stack direction="row" spacing={1} alignItems="center">
          <Typography variant="body2" color="text.secondary" sx={{ minWidth: 120 }}>
            {t('account.profile.username')}
          </Typography>
          <Typography variant="body1" sx={{ fontWeight: 500 }}>
            {account.username}
          </Typography>
          {account.isAdmin && <Chip label={t('account.profile.adminChip')} size="small" variant="outlined" />}
        </Stack>
        <Stack direction="row" spacing={1}>
          <Typography variant="body2" color="text.secondary" sx={{ minWidth: 120 }}>
            {t('account.profile.memberSince')}
          </Typography>
          <Typography variant="body2">{fmt.date(account.createdAt)}</Typography>
        </Stack>
      </Stack>
    </Paper>
  );
}

/** Change or remove your email. It's a sign-in name, so the current password is required. */
function EmailCard({ account }: { account: AccountDto }) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const [email, setEmail] = useState(account.email ?? '');
  const [password, setPassword] = useState('');

  // Follow the saved value when it changes (e.g. after a save).
  useEffect(() => setEmail(account.email ?? ''), [account.email]);

  const save = useMutation({
    mutationFn: () => api.accountChangeEmail(email.trim() || null, password),
    onSuccess: (updated) => {
      qc.setQueryData(['account'], updated);
      setPassword('');
    },
  });

  const changed = email.trim().toLowerCase() !== (account.email ?? '');
  const canSubmit = changed && !!password && !save.isPending;

  return (
    <Paper variant="outlined" sx={{ p: 2, maxWidth: 640 }}>
      <Typography variant="h6" gutterBottom>
        {t('account.email.title')}
      </Typography>
      <Typography variant="body2" color="text.secondary" gutterBottom>
        {t('account.email.description')}
      </Typography>
      <Stack
        component="form"
        spacing={2}
        sx={{ mt: 1, maxWidth: 360 }}
        onSubmit={(e) => {
          e.preventDefault();
          if (canSubmit) save.mutate();
        }}
      >
        <TextField
          type="email"
          label={t('account.email.email')}
          size="small"
          value={email}
          autoComplete="email"
          onChange={(e) => {
            setEmail(e.target.value);
            save.reset();
          }}
          helperText={t('account.email.emailHelper')}
        />
        <TextField
          type="password"
          label={t('account.email.currentPassword')}
          size="small"
          value={password}
          autoComplete="current-password"
          onChange={(e) => setPassword(e.target.value)}
        />
        {save.error instanceof ApiError && <Alert severity="error">{save.error.message}</Alert>}
        {save.isSuccess && (
          <Alert severity="success">{account.email ? t('account.email.saved') : t('account.email.removed')}</Alert>
        )}
        <Box>
          <Button type="submit" variant="contained" disabled={!canSubmit}>
            {save.isPending ? t('common.states.saving') : t('account.email.saveButton')}
          </Button>
        </Box>
      </Stack>
    </Paper>
  );
}

/** Self-service password change for the signed-in user — requires the current password + confirm. */
function ChangePasswordCard() {
  const { t } = useTranslation();
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [confirm, setConfirm] = useState('');

  const change = useMutation({
    mutationFn: () => api.changePassword(current, next),
    onSuccess: () => {
      setCurrent('');
      setNext('');
      setConfirm('');
    },
  });

  const mismatch = confirm.length > 0 && next !== confirm;
  const canSubmit = !!current && !!next && next === confirm && !change.isPending;

  return (
    <Paper variant="outlined" sx={{ p: 2, maxWidth: 640 }}>
      <Typography variant="h6" gutterBottom>
        {t('account.password.title')}
      </Typography>
      <Typography variant="body2" color="text.secondary" gutterBottom>
        {t('account.password.description')}
      </Typography>

      <Stack
        component="form"
        spacing={2}
        sx={{ mt: 1, maxWidth: 360 }}
        onSubmit={(e) => {
          e.preventDefault();
          if (canSubmit) change.mutate();
        }}
      >
        <TextField
          type="password"
          label={t('account.password.current')}
          size="small"
          value={current}
          autoComplete="current-password"
          onChange={(e) => setCurrent(e.target.value)}
        />
        <TextField
          type="password"
          label={t('account.password.new')}
          size="small"
          value={next}
          autoComplete="new-password"
          onChange={(e) => setNext(e.target.value)}
        />
        <TextField
          type="password"
          label={t('account.password.confirm')}
          size="small"
          value={confirm}
          autoComplete="new-password"
          error={mismatch}
          helperText={mismatch ? t('account.password.mismatch') : ' '}
          onChange={(e) => setConfirm(e.target.value)}
        />
        {change.error instanceof ApiError && (
          <Alert severity="error">{change.error.message}</Alert>
        )}
        {change.isSuccess && <Alert severity="success">{t('account.password.changed')}</Alert>}
        <Box>
          <Button type="submit" variant="contained" disabled={!canSubmit}>
            {change.isPending ? t('common.states.saving') : t('account.password.changeButton')}
          </Button>
        </Box>
      </Stack>
    </Paper>
  );
}
