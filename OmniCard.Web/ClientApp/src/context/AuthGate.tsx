import { useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  CircularProgress,
  Container,
  Paper,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import { api, ApiError } from '../api/client';

/**
 * Renders the app only when the session is authorized. If the server requires a passphrase and this
 * session isn't unlocked, shows a login screen. When no passphrase is configured, passes straight
 * through.
 */
export function AuthGate({ children }: { children: ReactNode }) {
  const qc = useQueryClient();
  const statusQuery = useQuery({ queryKey: ['auth-status'], queryFn: api.authStatus });
  const [passphrase, setPassphrase] = useState('');

  const login = useMutation({
    mutationFn: () => api.login(passphrase),
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
          <Typography variant="h5">OmniCard</Typography>
          <Typography variant="body2" color="text.secondary">
            Enter the passphrase to continue.
          </Typography>
          <TextField
            type="password"
            label="Passphrase"
            value={passphrase}
            onChange={(e) => setPassphrase(e.target.value)}
            autoFocus
            fullWidth
          />
          {login.error instanceof ApiError && <Alert severity="error">{login.error.message}</Alert>}
          <Button type="submit" variant="contained" disabled={login.isPending || !passphrase}>
            {login.isPending ? 'Checking…' : 'Unlock'}
          </Button>
        </Stack>
      </Paper>
    </Container>
  );
}
