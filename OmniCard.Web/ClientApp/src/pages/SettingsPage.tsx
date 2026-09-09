import { useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  Checkbox,
  Chip,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControlLabel,
  IconButton,
  LinearProgress,
  MenuItem,
  Paper,
  Slider,
  Stack,
  Tab,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Tabs,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material';
import DeleteIcon from '@mui/icons-material/Delete';
import KeyIcon from '@mui/icons-material/Key';
import Link from '@mui/material/Link';
import { api, ApiError } from '../api/client';
import type { ComponentDto, UserDto } from '../api/types';
import { LocationPickerDialog } from '../components/dialogs/LocationPickerDialog';
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

/** Self-service password change for the signed-in user — requires the current password + confirm. */
function ChangePasswordCard() {
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
        Your password
      </Typography>
      <Typography variant="body2" color="text.secondary" gutterBottom>
        Change the password for your own account. You must enter your current password.
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
          label="Current password"
          size="small"
          value={current}
          autoComplete="current-password"
          onChange={(e) => setCurrent(e.target.value)}
        />
        <TextField
          type="password"
          label="New password"
          size="small"
          value={next}
          autoComplete="new-password"
          onChange={(e) => setNext(e.target.value)}
        />
        <TextField
          type="password"
          label="Confirm new password"
          size="small"
          value={confirm}
          autoComplete="new-password"
          error={mismatch}
          helperText={mismatch ? "Passwords don't match." : ' '}
          onChange={(e) => setConfirm(e.target.value)}
        />
        {change.error instanceof ApiError && (
          <Alert severity="error">{change.error.message}</Alert>
        )}
        {change.isSuccess && <Alert severity="success">Password changed.</Alert>}
        <Box>
          <Button type="submit" variant="contained" disabled={!canSubmit}>
            {change.isPending ? 'Saving…' : 'Change password'}
          </Button>
        </Box>
      </Stack>
    </Paper>
  );
}

/** Dialog to set a password (create user or admin reset). Requires confirmation. */
function PasswordDialog({
  open,
  title,
  withUsername,
  submitLabel,
  onClose,
  onSubmit,
  pending,
  error,
}: {
  open: boolean;
  title: string;
  withUsername: boolean;
  submitLabel: string;
  onClose: () => void;
  onSubmit: (v: { username: string; password: string; isAdmin: boolean }) => void;
  pending: boolean;
  error?: string | null;
}) {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [isAdmin, setIsAdmin] = useState(false);

  // Reset fields whenever the dialog is (re)opened.
  const reset = () => {
    setUsername('');
    setPassword('');
    setConfirm('');
    setIsAdmin(false);
  };

  const mismatch = confirm.length > 0 && password !== confirm;
  const canSubmit =
    !!password && password === confirm && (!withUsername || !!username.trim()) && !pending;

  return (
    <Dialog
      open={open}
      onClose={onClose}
      fullWidth
      maxWidth="xs"
      TransitionProps={{ onExited: reset }}
    >
      <DialogTitle>{title}</DialogTitle>
      <DialogContent>
        <Stack
          component="form"
          spacing={2}
          sx={{ mt: 1 }}
          onSubmit={(e) => {
            e.preventDefault();
            if (canSubmit) onSubmit({ username: username.trim(), password, isAdmin });
          }}
        >
          {withUsername && (
            <TextField
              label="Username"
              size="small"
              value={username}
              autoFocus
              onChange={(e) => setUsername(e.target.value)}
            />
          )}
          <TextField
            type="password"
            label="Password"
            size="small"
            value={password}
            autoComplete="new-password"
            autoFocus={!withUsername}
            onChange={(e) => setPassword(e.target.value)}
          />
          <TextField
            type="password"
            label="Confirm password"
            size="small"
            value={confirm}
            autoComplete="new-password"
            error={mismatch}
            helperText={mismatch ? "Passwords don't match." : ' '}
            onChange={(e) => setConfirm(e.target.value)}
          />
          {withUsername && (
            <FormControlLabel
              control={
                <Checkbox checked={isAdmin} onChange={(e) => setIsAdmin(e.target.checked)} />
              }
              label="Administrator (full access)"
            />
          )}
          {error && <Alert severity="error">{error}</Alert>}
          {/* Hidden submit so Enter works. */}
          <button type="submit" style={{ display: 'none' }} />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button
          variant="contained"
          disabled={!canSubmit}
          onClick={() => onSubmit({ username: username.trim(), password, isAdmin })}
        >
          {pending ? 'Saving…' : submitLabel}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

/** Admin-only management of all accounts (create / delete / reset password). */
function ManageUsersCard() {
  const qc = useQueryClient();
  const usersQuery = useQuery({ queryKey: ['users'], queryFn: api.users });
  const [createOpen, setCreateOpen] = useState(false);
  const [resetFor, setResetFor] = useState<UserDto | null>(null);

  const invalidate = () => qc.invalidateQueries({ queryKey: ['users'] });

  const create = useMutation({
    mutationFn: (v: { username: string; password: string; isAdmin: boolean }) =>
      api.userCreate(v),
    onSuccess: () => {
      setCreateOpen(false);
      invalidate();
    },
  });
  const del = useMutation({
    mutationFn: (id: number) => api.userDelete(id),
    onSuccess: invalidate,
  });
  const reset = useMutation({
    mutationFn: (v: { id: number; password: string }) => api.userResetPassword(v.id, v.password),
    onSuccess: () => setResetFor(null),
  });

  return (
    <Paper variant="outlined" sx={{ p: 2, maxWidth: 640 }}>
      <Stack direction="row" alignItems="center" justifyContent="space-between">
        <Typography variant="h6">Users</Typography>
        <Button variant="contained" size="small" onClick={() => setCreateOpen(true)}>
          Add user
        </Button>
      </Stack>
      <Typography variant="body2" color="text.secondary" gutterBottom>
        Accounts that can sign in. The built-in Admin account can't be deleted.
      </Typography>

      {del.error instanceof ApiError && (
        <Alert severity="error" sx={{ mt: 1 }}>
          {del.error.message}
        </Alert>
      )}

      {usersQuery.isLoading ? (
        <CircularProgress size={24} sx={{ mt: 1 }} />
      ) : (
        <Table size="small" sx={{ mt: 1 }}>
          <TableHead>
            <TableRow>
              <TableCell>Username</TableCell>
              <TableCell>Role</TableCell>
              <TableCell align="right">Actions</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {usersQuery.data?.map((u) => (
              <TableRow key={u.id}>
                <TableCell>
                  {u.username}
                  {u.isSystem && (
                    <Chip label="system" size="small" sx={{ ml: 1 }} variant="outlined" />
                  )}
                </TableCell>
                <TableCell>{u.isAdmin ? 'Administrator' : 'User'}</TableCell>
                <TableCell align="right">
                  <Tooltip title="Reset password">
                    <IconButton size="small" onClick={() => setResetFor(u)}>
                      <KeyIcon fontSize="small" />
                    </IconButton>
                  </Tooltip>
                  <Tooltip title={u.isSystem ? "The system account can't be deleted" : 'Delete user'}>
                    <span>
                      <IconButton
                        size="small"
                        color="error"
                        disabled={u.isSystem || del.isPending}
                        onClick={() => {
                          if (confirm(`Delete user "${u.username}"?`)) del.mutate(u.id);
                        }}
                      >
                        <DeleteIcon fontSize="small" />
                      </IconButton>
                    </span>
                  </Tooltip>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}

      <PasswordDialog
        open={createOpen}
        title="Add user"
        withUsername
        submitLabel="Create"
        pending={create.isPending}
        error={create.error instanceof ApiError ? create.error.message : null}
        onClose={() => setCreateOpen(false)}
        onSubmit={(v) => create.mutate(v)}
      />
      <PasswordDialog
        open={!!resetFor}
        title={resetFor ? `Reset password — ${resetFor.username}` : 'Reset password'}
        withUsername={false}
        submitLabel="Reset"
        pending={reset.isPending}
        error={reset.error instanceof ApiError ? reset.error.message : null}
        onClose={() => setResetFor(null)}
        onSubmit={(v) => resetFor && reset.mutate({ id: resetFor.id, password: v.password })}
      />
    </Paper>
  );
}

function UsersTab() {
  const authQuery = useQuery({ queryKey: ['auth-status'], queryFn: api.authStatus });
  return (
    <Stack spacing={3}>
      <ChangePasswordCard />
      {authQuery.data?.isAdmin && <ManageUsersCard />}
    </Stack>
  );
}

/** Read-only inventory of the software/components OmniCard ships or runs on, with versions + links. */
function ComponentsCard() {
  const components = useQuery({ queryKey: ['components'], queryFn: api.components });

  // Preserve server order but split into visual groups by category.
  const groups: { category: string; items: ComponentDto[] }[] = [];
  for (const c of components.data ?? []) {
    let g = groups.find((x) => x.category === c.category);
    if (!g) {
      g = { category: c.category, items: [] };
      groups.push(g);
    }
    g.items.push(c);
  }

  return (
    <Paper variant="outlined" sx={{ p: 2, maxWidth: 880 }}>
      <Typography variant="h6" gutterBottom>
        Components
      </Typography>
      <Typography variant="body2" color="text.secondary" gutterBottom>
        Software and third-party components that OmniCard ships or runs on, with their versions and
        links to each project's website and license.
      </Typography>

      {components.isLoading ? (
        <CircularProgress size={24} sx={{ mt: 1 }} />
      ) : components.error ? (
        <Alert severity="error" sx={{ mt: 1 }}>
          {(components.error as Error).message}
        </Alert>
      ) : (
        <Stack spacing={2} sx={{ mt: 1 }}>
          {groups.map((g) => (
            <Box key={g.category}>
              <Typography variant="subtitle2" color="text.secondary" sx={{ mb: 0.5 }}>
                {g.category}
              </Typography>
              <Table size="small">
                <TableHead>
                  <TableRow>
                    <TableCell>Component</TableCell>
                    <TableCell>Version</TableCell>
                    <TableCell>License</TableCell>
                    <TableCell align="right">Links</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {g.items.map((c) => (
                    <TableRow key={`${c.category}:${c.name}`}>
                      <TableCell>{c.name}</TableCell>
                      <TableCell>{c.version}</TableCell>
                      <TableCell>{c.license}</TableCell>
                      <TableCell align="right">
                        <Stack direction="row" spacing={1.5} justifyContent="flex-end">
                          {c.homepageUrl && (
                            <Link href={c.homepageUrl} target="_blank" rel="noopener noreferrer">
                              Website
                            </Link>
                          )}
                          {c.licenseUrl && (
                            <Link href={c.licenseUrl} target="_blank" rel="noopener noreferrer">
                              License
                            </Link>
                          )}
                        </Stack>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </Box>
          ))}
        </Stack>
      )}
    </Paper>
  );
}

const TABS = [
  { key: 'sales', label: 'Sales', render: () => <SalesCard /> },
  { key: 'appearance', label: 'Appearance', render: () => <AppearanceCard /> },
  { key: 'catalog', label: 'Catalog Data', render: () => <CatalogCard /> },
  { key: 'ebay', label: 'eBay', render: () => <EbayCard /> },
  { key: 'users', label: 'Users', render: () => <UsersTab /> },
  { key: 'components', label: 'Components', render: () => <ComponentsCard /> },
] as const;

export function SettingsPage() {
  const [params, setParams] = useSearchParams();
  const requested = params.get('tab');
  const active = Math.max(
    0,
    TABS.findIndex((t) => t.key === requested),
  );

  return (
    <Stack spacing={3}>
      <Typography variant="h4">Administration</Typography>
      <Box sx={{ borderBottom: 1, borderColor: 'divider' }}>
        <Tabs
          value={active}
          onChange={(_, v) => {
            const nextParams = new URLSearchParams(params);
            nextParams.set('tab', TABS[v].key);
            setParams(nextParams, { replace: true });
          }}
          variant="scrollable"
          scrollButtons="auto"
        >
          {TABS.map((t) => (
            <Tab key={t.key} label={t.label} />
          ))}
        </Tabs>
      </Box>
      <Box>{TABS[active].render()}</Box>
    </Stack>
  );
}
