import { useSearchParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Button,
  Chip,
  CircularProgress,
  Paper,
  Stack,
  Typography,
} from '@mui/material';
import { api } from '../api/client';

function EbayCard() {
  const qc = useQueryClient();
  const [params, setParams] = useSearchParams();
  const status = useQuery({ queryKey: ['ebay-status'], queryFn: api.ebayStatus });

  const disconnect = useMutation({
    mutationFn: () => api.ebayDisconnect(),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['ebay-status'] }),
  });
  const setup = useMutation({ mutationFn: () => api.ebaySetup() });

  // One-time feedback from the OAuth callback redirect (?ebay=connected|failed|misconfigured).
  const ebayParam = params.get('ebay');
  const clearParam = () => {
    params.delete('ebay');
    setParams(params, { replace: true });
  };

  return (
    <Paper variant="outlined" sx={{ p: 2, maxWidth: 640 }}>
      <Typography variant="h6" gutterBottom>
        eBay
      </Typography>

      {ebayParam === 'connected' && (
        <Alert severity="success" onClose={clearParam} sx={{ mb: 2 }}>
          Connected to eBay.
        </Alert>
      )}
      {ebayParam === 'failed' && (
        <Alert severity="error" onClose={clearParam} sx={{ mb: 2 }}>
          eBay connection failed. Please try again.
        </Alert>
      )}
      {ebayParam === 'misconfigured' && (
        <Alert severity="warning" onClose={clearParam} sx={{ mb: 2 }}>
          eBay isn't configured on the server yet (AppId/CertId/RuName/AcceptUrl).
        </Alert>
      )}

      {status.isLoading || !status.data ? (
        <CircularProgress size={24} />
      ) : (
        <Stack spacing={2}>
          <Stack direction="row" spacing={1} alignItems="center">
            <Typography variant="body2">Status:</Typography>
            {status.data.connected ? (
              <Chip color="success" size="small" label="Connected" />
            ) : status.data.configured ? (
              <Chip color="default" size="small" label="Not connected" />
            ) : (
              <Chip color="warning" size="small" label="Not configured" />
            )}
          </Stack>

          {!status.data.configured && (
            <Alert severity="info">
              The server is missing eBay app credentials:{' '}
              {status.data.missingConfig.join(', ')}. Set the <code>eBay</code> section in the
              server's appsettings, then reload.
            </Alert>
          )}

          <Stack direction="row" spacing={1}>
            {status.data.connected ? (
              <>
                <Button
                  variant="outlined"
                  disabled={setup.isPending}
                  onClick={() => setup.mutate()}
                >
                  {setup.isPending ? 'Running setup…' : 'Run seller setup'}
                </Button>
                <Button
                  color="error"
                  variant="outlined"
                  disabled={disconnect.isPending}
                  onClick={() => disconnect.mutate()}
                >
                  Disconnect
                </Button>
              </>
            ) : (
              <Button
                variant="contained"
                disabled={!status.data.configured}
                onClick={() => {
                  window.location.href = api.ebayConnectUrl;
                }}
              >
                Connect to eBay
              </Button>
            )}
          </Stack>

          {setup.data && (
            <Alert severity={setup.data.success ? 'success' : 'error'}>
              {setup.data.success ? 'Seller setup complete.' : 'Seller setup failed.'}
              {setup.data.message ? ` ${setup.data.message}` : ''}
            </Alert>
          )}
          {setup.error && <Alert severity="error">{(setup.error as Error).message}</Alert>}
        </Stack>
      )}
    </Paper>
  );
}

export function SettingsPage() {
  return (
    <Stack spacing={3}>
      <Typography variant="h4">Settings</Typography>
      <EbayCard />
    </Stack>
  );
}
