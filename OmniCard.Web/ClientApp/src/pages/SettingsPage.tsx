import { useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  LinearProgress,
  MenuItem,
  Paper,
  Slider,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import { api } from '../api/client';
import { LocationPickerDialog } from '../components/LocationPickerDialog';
import {
  usePreviewScale,
  setPreviewScale,
  PREVIEW_SCALE_MIN,
  PREVIEW_SCALE_MAX,
  PREVIEW_BASE_WIDTH,
  PREVIEW_BASE_MAX_HEIGHT,
} from '../lib/previewScale';

const OPERATIONS: { key: 'prices' | 'bulk' | 'hashes' | 'images'; label: string }[] = [
  { key: 'prices', label: 'Update prices' },
  { key: 'bulk', label: 'Download catalog' },
  { key: 'hashes', label: 'Recompute hashes' },
  { key: 'images', label: 'Download artwork' },
];

function CatalogCard() {
  const qc = useQueryClient();
  const games = useQuery({ queryKey: ['games'], queryFn: api.games });
  const [game, setGame] = useState('Mtg');

  const status = useQuery({
    queryKey: ['catalog-status'],
    queryFn: api.catalogStatus,
    // Poll quickly while a job is running so progress updates live; idle otherwise.
    refetchInterval: (q) => (q.state.data?.running ? 1500 : false),
  });

  const refresh = useMutation({
    mutationFn: ({ op }: { op: 'prices' | 'bulk' | 'hashes' | 'images' }) => api.catalogRefresh(game, op),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['catalog-status'] }),
  });

  const running = status.data?.running;
  const recent = status.data?.recent ?? [];

  return (
    <Paper variant="outlined" sx={{ p: 2, maxWidth: 640 }}>
      <Typography variant="h6" gutterBottom>
        Catalog data
      </Typography>
      <Typography variant="body2" color="text.secondary" gutterBottom>
        Refresh the per-game card catalogs, prices, and image hashes on the server (no desktop app
        needed). One job runs at a time.
      </Typography>

      <Stack spacing={2} sx={{ mt: 1 }}>
        <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap alignItems="center">
          <TextField
            select
            size="small"
            label="Game"
            value={game}
            onChange={(e) => setGame(e.target.value)}
            sx={{ minWidth: 180 }}
            disabled={!!running}
          >
            {games.data?.map((g) => (
              <MenuItem key={g.id} value={g.id}>
                {g.displayName}
              </MenuItem>
            ))}
          </TextField>
          {OPERATIONS.map((o) => (
            <Button
              key={o.key}
              variant="outlined"
              disabled={!!running || refresh.isPending}
              onClick={() => refresh.mutate({ op: o.key })}
            >
              {o.label}
            </Button>
          ))}
        </Stack>

        {refresh.error && <Alert severity="error">{(refresh.error as Error).message}</Alert>}

        {running && (
          <Alert severity="info" icon={false}>
            <Typography variant="body2" fontWeight={600}>
              {running.game} · {running.operation} — running
            </Typography>
            <Typography variant="caption" color="text.secondary">
              {running.message}
            </Typography>
            <LinearProgress sx={{ mt: 1 }} />
          </Alert>
        )}

        {recent.length > 0 && (
          <Stack spacing={0.5}>
            <Typography variant="subtitle2">Recent</Typography>
            {recent.map((j, i) => (
              <Typography key={i} variant="caption" color="text.secondary">
                {j.state === 'succeeded' ? '✓' : '✗'} {j.game} · {j.operation} — {j.message}
              </Typography>
            ))}
          </Stack>
        )}
      </Stack>
    </Paper>
  );
}

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

function SalesCard() {
  const qc = useQueryClient();
  const settings = useQuery({ queryKey: ['settings'], queryFn: api.settings });
  const locations = useQuery({ queryKey: ['locations', undefined], queryFn: () => api.locations() });
  const [pickOpen, setPickOpen] = useState(false);

  const save = useMutation({
    mutationFn: (forSaleLocationId: number | null) => api.settingsUpdate({ forSaleLocationId }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['settings'] }),
  });

  const currentId = settings.data?.forSaleLocationId ?? null;
  const currentName = locations.data?.find((l) => l.id === currentId)?.name;

  return (
    <Paper variant="outlined" sx={{ p: 2, maxWidth: 640 }}>
      <Typography variant="h6" gutterBottom>
        Sales
      </Typography>
      <Typography variant="body2" color="text.secondary" gutterBottom>
        When you mark a listing as picked, its card is automatically moved to this location. Leave it
        unset to disable picking.
      </Typography>

      {settings.isLoading ? (
        <CircularProgress size={24} sx={{ mt: 1 }} />
      ) : (
        <Stack spacing={1} sx={{ mt: 1 }}>
          <Stack direction="row" spacing={1} alignItems="center">
            <Box sx={{ flexGrow: 1 }}>
              <Typography variant="caption" color="text.secondary">
                For-sale location
              </Typography>
              <Typography variant="body2">
                {currentName ?? (currentId != null ? `#${currentId}` : '— none —')}
              </Typography>
            </Box>
            <Button size="small" onClick={() => setPickOpen(true)}>
              Change
            </Button>
            {currentId != null && (
              <Button size="small" color="error" disabled={save.isPending} onClick={() => save.mutate(null)}>
                Clear
              </Button>
            )}
          </Stack>
          {save.error && <Alert severity="error">{(save.error as Error).message}</Alert>}
        </Stack>
      )}

      <LocationPickerDialog
        open={pickOpen}
        title="For-sale location"
        onPick={(id) => {
          setPickOpen(false);
          save.mutate(id);
        }}
        onClose={() => setPickOpen(false)}
      />
    </Paper>
  );
}

function AppearanceCard() {
  const scale = usePreviewScale();

  return (
    <Paper variant="outlined" sx={{ p: 2, maxWidth: 640 }}>
      <Typography variant="h6" gutterBottom>
        Appearance
      </Typography>
      <Typography variant="body2" color="text.secondary" gutterBottom>
        Card preview size — how large the artwork popup grows when you hover a card in a list. 100% is
        the default; drag up to {PREVIEW_SCALE_MAX}%.
      </Typography>

      <Stack direction="row" spacing={3} alignItems="center" sx={{ mt: 1 }}>
        <Box sx={{ flex: 1 }}>
          <Slider
            value={scale}
            min={PREVIEW_SCALE_MIN}
            max={PREVIEW_SCALE_MAX}
            step={10}
            marks={[
              { value: 100, label: '100%' },
              { value: 200, label: '200%' },
              { value: 300, label: '300%' },
            ]}
            valueLabelDisplay="auto"
            valueLabelFormat={(v) => `${v}%`}
            onChange={(_, v) => setPreviewScale(v as number)}
          />
          <Typography variant="caption" color="text.secondary">
            Popup: {Math.round((PREVIEW_BASE_WIDTH * scale) / 100)} ×{' '}
            {Math.round((PREVIEW_BASE_MAX_HEIGHT * scale) / 100)} px
          </Typography>
        </Box>
        {/* Compact proportional swatch — capped to a display size so the panel never blows out. */}
        <Box
          sx={{
            width: (80 * scale) / 100,
            height: (112 * scale) / 100,
            flexShrink: 0,
            borderRadius: 1,
            border: '1px dashed',
            borderColor: 'divider',
            bgcolor: 'action.hover',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            transition: 'width 0.1s, height 0.1s',
          }}
        >
          <Typography variant="caption" color="text.secondary">
            {scale}%
          </Typography>
        </Box>
      </Stack>
    </Paper>
  );
}

export function SettingsPage() {
  return (
    <Stack spacing={3}>
      <Typography variant="h4">Settings</Typography>
      <SalesCard />
      <AppearanceCard />
      <CatalogCard />
      <EbayCard />
    </Stack>
  );
}
