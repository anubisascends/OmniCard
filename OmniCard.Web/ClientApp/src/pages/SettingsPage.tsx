import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
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
  Switch,
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
import EditIcon from '@mui/icons-material/Edit';
import KeyIcon from '@mui/icons-material/Key';
import Link from '@mui/material/Link';
import StarIcon from '@mui/icons-material/Star';
import { api, ApiError } from '../api/client';
import type { ComponentDto, RoleDto, UserDto } from '../api/types';
import { LocationPickerDialog } from '../components/dialogs/LocationPickerDialog';
import { DeckTypesCard } from '../components/settings/DeckTypesCard';
import { RolesCard } from '../components/settings/RolesCard';
import { PermissionChecklist } from '../components/settings/PermissionChecklist';
import { usePermissions } from '../context/usePermissions';
import { currencySymbol } from '../lib/scanBadges';
import { useFormatters } from '../i18n/format';
import {
  usePreviewScale,
  setPreviewScale,
  PREVIEW_SCALE_MIN,
  PREVIEW_SCALE_MAX,
  PREVIEW_BASE_WIDTH,
  PREVIEW_BASE_MAX_HEIGHT,
} from '../lib/previewScale';

const OPERATIONS: { key: 'prices' | 'bulk' | 'hashes' | 'images' }[] = [
  { key: 'prices' },
  { key: 'bulk' },
  { key: 'hashes' },
  { key: 'images' },
];

function CatalogCard() {
  const { t } = useTranslation();
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
        {t('settings.catalog.title')}
      </Typography>
      <Typography variant="body2" color="text.secondary" gutterBottom>
        {t('settings.catalog.description')}
      </Typography>

      <Stack spacing={2} sx={{ mt: 1 }}>
        <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap alignItems="center">
          <TextField
            select
            size="small"
            label={t('common.labels.game')}
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
              {t(`settings.catalog.operations.${o.key}`)}
            </Button>
          ))}
        </Stack>

        {refresh.error && <Alert severity="error">{(refresh.error as Error).message}</Alert>}

        {running && (
          <Alert severity="info" icon={false}>
            <Typography variant="body2" fontWeight={600}>
              {t('settings.catalog.running', { game: running.game, operation: running.operation })}
            </Typography>
            <Typography variant="caption" color="text.secondary">
              {running.message}
            </Typography>
            <LinearProgress sx={{ mt: 1 }} />
          </Alert>
        )}

        {recent.length > 0 && (
          <Stack spacing={0.5}>
            <Typography variant="subtitle2">{t('settings.catalog.recentHeading')}</Typography>
            {recent.map((j, i) => (
              <Typography key={i} variant="caption" color="text.secondary">
                {t('settings.catalog.recentItem', {
                  icon: j.state === 'succeeded' ? '✓' : '✗',
                  game: j.game,
                  operation: j.operation,
                  message: j.message,
                })}
              </Typography>
            ))}
          </Stack>
        )}
      </Stack>
    </Paper>
  );
}

function EbayCard() {
  const { t } = useTranslation();
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
        {t('settings.ebay.title')}
      </Typography>

      {ebayParam === 'connected' && (
        <Alert severity="success" onClose={clearParam} sx={{ mb: 2 }}>
          {t('settings.ebay.connected')}
        </Alert>
      )}
      {ebayParam === 'failed' && (
        <Alert severity="error" onClose={clearParam} sx={{ mb: 2 }}>
          {t('settings.ebay.failed')}
        </Alert>
      )}
      {ebayParam === 'misconfigured' && (
        <Alert severity="warning" onClose={clearParam} sx={{ mb: 2 }}>
          {t('settings.ebay.misconfigured')}
        </Alert>
      )}

      {status.isLoading || !status.data ? (
        <CircularProgress size={24} />
      ) : (
        <Stack spacing={2}>
          <Stack direction="row" spacing={1} alignItems="center">
            <Typography variant="body2">{t('settings.ebay.statusLabel')}</Typography>
            {status.data.connected ? (
              <Chip color="success" size="small" label={t('settings.ebay.connectedChip')} />
            ) : status.data.configured ? (
              <Chip color="default" size="small" label={t('settings.ebay.notConnectedChip')} />
            ) : (
              <Chip color="warning" size="small" label={t('settings.ebay.notConfiguredChip')} />
            )}
          </Stack>

          {!status.data.configured && (
            <Alert severity="info">
              {t('settings.ebay.missingCredentialsBefore', {
                missing: status.data.missingConfig.join(', '),
              })}
              <code>eBay</code>
              {t('settings.ebay.missingCredentialsAfter')}
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
                  {setup.isPending ? t('settings.ebay.runningSetup') : t('settings.ebay.runSellerSetup')}
                </Button>
                <Button
                  color="error"
                  variant="outlined"
                  disabled={disconnect.isPending}
                  onClick={() => disconnect.mutate()}
                >
                  {t('settings.ebay.disconnect')}
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
                {t('settings.ebay.connect')}
              </Button>
            )}
          </Stack>

          {setup.data && (
            <Alert severity={setup.data.success ? 'success' : 'error'}>
              {setup.data.success ? t('settings.ebay.setupComplete') : t('settings.ebay.setupFailed')}
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
  const { t } = useTranslation();
  const qc = useQueryClient();
  const settings = useQuery({ queryKey: ['settings'], queryFn: api.settings });
  const locations = useQuery({ queryKey: ['locations', undefined], queryFn: () => api.locations() });
  const [pickOpen, setPickOpen] = useState(false);

  const save = useMutation({
    mutationFn: (body: { forSaleLocationId: number | null; movePickedToForSaleLocation: boolean }) =>
      api.settingsUpdate(body),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['settings'] }),
  });

  const currentId = settings.data?.forSaleLocationId ?? null;
  const currentName = locations.data?.find((l) => l.id === currentId)?.name;
  const moveEnabled = settings.data?.movePickedToForSaleLocation ?? true;

  return (
    <Paper variant="outlined" sx={{ p: 2, maxWidth: 640 }}>
      <Typography variant="h6" gutterBottom>
        {t('settings.sales.title')}
      </Typography>
      <Typography variant="body2" color="text.secondary" gutterBottom>
        {t('settings.sales.description')}
      </Typography>

      {settings.isLoading ? (
        <CircularProgress size={24} sx={{ mt: 1 }} />
      ) : (
        <Stack spacing={1} sx={{ mt: 1 }}>
          <Stack direction="row" spacing={1} alignItems="center">
            <Box sx={{ flexGrow: 1 }}>
              <Typography variant="caption" color="text.secondary">
                {t('settings.sales.forSaleLocation')}
              </Typography>
              <Typography variant="body2">
                {currentName ?? (currentId != null ? `#${currentId}` : t('settings.sales.none'))}
              </Typography>
            </Box>
            <Button size="small" disabled={!moveEnabled} onClick={() => setPickOpen(true)}>
              {t('common.actions.change')}
            </Button>
            {currentId != null && (
              <Button
                size="small"
                color="error"
                disabled={save.isPending || !moveEnabled}
                onClick={() => save.mutate({ forSaleLocationId: null, movePickedToForSaleLocation: moveEnabled })}
              >
                {t('common.actions.clear')}
              </Button>
            )}
          </Stack>
          <FormControlLabel
            control={
              <Switch
                checked={moveEnabled}
                disabled={save.isPending}
                onChange={(e) =>
                  save.mutate({ forSaleLocationId: currentId, movePickedToForSaleLocation: e.target.checked })
                }
              />
            }
            label={t('settings.sales.moveSwitch')}
          />
          <Typography variant="caption" color="text.secondary">
            {moveEnabled ? t('settings.sales.moveOn') : t('settings.sales.moveOff')}
          </Typography>
          {save.error && <Alert severity="error">{(save.error as Error).message}</Alert>}
        </Stack>
      )}

      <LocationPickerDialog
        open={pickOpen}
        title={t('settings.sales.forSaleLocation')}
        onPick={(id) => {
          setPickOpen(false);
          save.mutate({ forSaleLocationId: id, movePickedToForSaleLocation: moveEnabled });
        }}
        onClose={() => setPickOpen(false)}
      />
    </Paper>
  );
}

function AppearanceCard() {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const scale = usePreviewScale();

  return (
    <Paper variant="outlined" sx={{ p: 2, maxWidth: 640 }}>
      <Typography variant="h6" gutterBottom>
        {t('settings.appearance.title')}
      </Typography>
      <Typography variant="body2" color="text.secondary" gutterBottom>
        {t('settings.appearance.description', { max: PREVIEW_SCALE_MAX })}
      </Typography>

      <Stack direction="row" spacing={3} alignItems="center" sx={{ mt: 1 }}>
        <Box sx={{ flex: 1 }}>
          <Slider
            value={scale}
            min={PREVIEW_SCALE_MIN}
            max={PREVIEW_SCALE_MAX}
            step={10}
            marks={[
              { value: 100, label: fmt.percent(1, 0) },
              { value: 200, label: fmt.percent(2, 0) },
              { value: 300, label: fmt.percent(3, 0) },
            ]}
            valueLabelDisplay="auto"
            valueLabelFormat={(v) => fmt.percent(v / 100, 0)}
            onChange={(_, v) => setPreviewScale(v as number)}
          />
          <Typography variant="caption" color="text.secondary">
            {t('settings.appearance.popup', {
              width: fmt.number(Math.round((PREVIEW_BASE_WIDTH * scale) / 100)),
              height: fmt.number(Math.round((PREVIEW_BASE_MAX_HEIGHT * scale) / 100)),
            })}
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
            {fmt.percent(scale / 100, 0)}
          </Typography>
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
        {t('settings.password.title')}
      </Typography>
      <Typography variant="body2" color="text.secondary" gutterBottom>
        {t('settings.password.description')}
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
          label={t('settings.password.current')}
          size="small"
          value={current}
          autoComplete="current-password"
          onChange={(e) => setCurrent(e.target.value)}
        />
        <TextField
          type="password"
          label={t('settings.password.new')}
          size="small"
          value={next}
          autoComplete="new-password"
          onChange={(e) => setNext(e.target.value)}
        />
        <TextField
          type="password"
          label={t('settings.password.confirm')}
          size="small"
          value={confirm}
          autoComplete="new-password"
          error={mismatch}
          helperText={mismatch ? t('settings.password.mismatch') : ' '}
          onChange={(e) => setConfirm(e.target.value)}
        />
        {change.error instanceof ApiError && (
          <Alert severity="error">{change.error.message}</Alert>
        )}
        {change.isSuccess && <Alert severity="success">{t('settings.password.changed')}</Alert>}
        <Box>
          <Button type="submit" variant="contained" disabled={!canSubmit}>
            {change.isPending ? t('common.states.saving') : t('settings.password.changeButton')}
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
  roles,
}: {
  open: boolean;
  title: string;
  withUsername: boolean;
  submitLabel: string;
  onClose: () => void;
  onSubmit: (v: { username: string; password: string; isAdmin: boolean; roleId: number | null }) => void;
  pending: boolean;
  error?: string | null;
  roles?: RoleDto[];
}) {
  const { t } = useTranslation();
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [isAdmin, setIsAdmin] = useState(false);
  const [roleId, setRoleId] = useState<number | ''>('');

  // Reset fields whenever the dialog is (re)opened.
  const reset = () => {
    setUsername('');
    setPassword('');
    setConfirm('');
    setIsAdmin(false);
    setRoleId('');
  };

  const submit = () =>
    onSubmit({ username: username.trim(), password, isAdmin, roleId: roleId === '' ? null : roleId });

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
            if (canSubmit) submit();
          }}
        >
          {withUsername && (
            <TextField
              label={t('settings.users.username')}
              size="small"
              value={username}
              autoFocus
              onChange={(e) => setUsername(e.target.value)}
            />
          )}
          <TextField
            type="password"
            label={t('settings.users.password')}
            size="small"
            value={password}
            autoComplete="new-password"
            autoFocus={!withUsername}
            onChange={(e) => setPassword(e.target.value)}
          />
          <TextField
            type="password"
            label={t('settings.users.confirmPassword')}
            size="small"
            value={confirm}
            autoComplete="new-password"
            error={mismatch}
            helperText={mismatch ? t('settings.users.passwordMismatch') : ' '}
            onChange={(e) => setConfirm(e.target.value)}
          />
          {withUsername && roles && !isAdmin && (
            <TextField
              select
              label={t('settings.users.role')}
              size="small"
              value={roleId}
              onChange={(e) => setRoleId(e.target.value === '' ? '' : Number(e.target.value))}
              helperText={t('settings.users.roleHelper')}
            >
              <MenuItem value="">{t('settings.users.noRole')}</MenuItem>
              {roles.map((r) => (
                <MenuItem key={r.id} value={r.id}>
                  {r.name}
                </MenuItem>
              ))}
            </TextField>
          )}
          {withUsername && (
            <FormControlLabel
              control={
                <Checkbox checked={isAdmin} onChange={(e) => setIsAdmin(e.target.checked)} />
              }
              label={t('settings.users.administratorFullAccess')}
            />
          )}
          {error && <Alert severity="error">{error}</Alert>}
          {/* Hidden submit so Enter works. */}
          <button type="submit" style={{ display: 'none' }} />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('common.actions.cancel')}</Button>
        <Button variant="contained" disabled={!canSubmit} onClick={submit}>
          {pending ? t('common.states.saving') : submitLabel}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

/** Admin edit of a user's access: assigned role, per-user grant/deny overrides, and the admin flag.
 * Admins bypass roles/overrides, so those controls disable when "Administrator" is ticked. */
function UserEditDialog({
  user,
  roles,
  onClose,
  onSaved,
}: {
  user: UserDto;
  roles: RoleDto[];
  onClose: () => void;
  onSaved: () => void;
}) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const catalog = useQuery({ queryKey: ['permission-catalog'], queryFn: api.permissionCatalog });
  const [roleId, setRoleId] = useState<number | ''>(user.roleId ?? '');
  const [grant, setGrant] = useState<string[]>(user.grant ?? []);
  const [deny, setDeny] = useState<string[]>(user.deny ?? []);
  const [isAdmin, setIsAdmin] = useState(user.isAdmin);

  const save = useMutation({
    mutationFn: () =>
      api.userUpdate(user.id, {
        roleId: roleId === '' ? null : roleId,
        grant,
        deny,
        isAdmin,
      }),
    onSuccess: () => {
      // Effective permissions changed — refresh the current session's own status too.
      qc.invalidateQueries({ queryKey: ['auth-status'] });
      onSaved();
    },
  });

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="md">
      <DialogTitle>{t('settings.users.editAccessTitle', { username: user.username })}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <FormControlLabel
            control={<Checkbox checked={isAdmin} onChange={(e) => setIsAdmin(e.target.checked)} />}
            label={t('settings.users.administratorFullAccess')}
          />
          {!isAdmin && (
            <>
              <TextField
                select
                label={t('settings.users.role')}
                size="small"
                value={roleId}
                onChange={(e) => setRoleId(e.target.value === '' ? '' : Number(e.target.value))}
                sx={{ maxWidth: 320 }}
              >
                <MenuItem value="">{t('settings.users.noRole')}</MenuItem>
                {roles.map((r) => (
                  <MenuItem key={r.id} value={r.id}>
                    {r.name}
                  </MenuItem>
                ))}
              </TextField>
              <Box>
                <Typography variant="subtitle2" gutterBottom>
                  {t('settings.users.grantHeading')}
                </Typography>
                <Typography variant="caption" color="text.secondary">
                  {t('settings.users.grantHelper')}
                </Typography>
                <Box sx={{ mt: 1 }}>
                  <PermissionChecklist catalog={catalog.data} selected={grant} onChange={setGrant} />
                </Box>
              </Box>
              <Box>
                <Typography variant="subtitle2" gutterBottom>
                  {t('settings.users.denyHeading')}
                </Typography>
                <Typography variant="caption" color="text.secondary">
                  {t('settings.users.denyHelper')}
                </Typography>
                <Box sx={{ mt: 1 }}>
                  <PermissionChecklist catalog={catalog.data} selected={deny} onChange={setDeny} />
                </Box>
              </Box>
            </>
          )}
          {save.error instanceof ApiError && <Alert severity="error">{save.error.message}</Alert>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('common.actions.cancel')}</Button>
        <Button variant="contained" disabled={save.isPending} onClick={() => save.mutate()}>
          {save.isPending ? t('common.states.saving') : t('common.actions.save')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

/** Admin-only management of all accounts (create / delete / reset password). */
function ManageUsersCard() {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const usersQuery = useQuery({ queryKey: ['users'], queryFn: api.users });
  const rolesQuery = useQuery({ queryKey: ['roles'], queryFn: api.roles });
  const [createOpen, setCreateOpen] = useState(false);
  const [resetFor, setResetFor] = useState<UserDto | null>(null);
  const [editFor, setEditFor] = useState<UserDto | null>(null);

  const roleName = (id: number | null | undefined) =>
    id == null ? null : rolesQuery.data?.find((r) => r.id === id)?.name ?? `#${id}`;
  const invalidate = () => qc.invalidateQueries({ queryKey: ['users'] });

  const create = useMutation({
    mutationFn: (v: { username: string; password: string; isAdmin: boolean; roleId: number | null }) =>
      api.userCreate({ username: v.username, password: v.password, isAdmin: v.isAdmin, roleId: v.roleId }),
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
        <Typography variant="h6">{t('settings.tabs.users')}</Typography>
        <Button variant="contained" size="small" onClick={() => setCreateOpen(true)}>
          {t('settings.users.addUser')}
        </Button>
      </Stack>
      <Typography variant="body2" color="text.secondary" gutterBottom>
        {t('settings.users.manageDescription')}
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
              <TableCell>{t('settings.users.colUsername')}</TableCell>
              <TableCell>{t('settings.users.colRole')}</TableCell>
              <TableCell align="right">{t('settings.users.colActions')}</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {usersQuery.data?.map((u) => (
              <TableRow key={u.id}>
                <TableCell>
                  {u.username}
                  {u.isSystem && (
                    <Chip label={t('settings.users.systemChip')} size="small" sx={{ ml: 1 }} variant="outlined" />
                  )}
                </TableCell>
                <TableCell>
                  {u.isAdmin
                    ? t('settings.users.roleAdmin')
                    : roleName(u.roleId) ?? t('settings.users.noRole')}
                  {!u.isAdmin && ((u.grant?.length ?? 0) > 0 || (u.deny?.length ?? 0) > 0) && (
                    <Chip label={t('settings.users.overridesChip')} size="small" sx={{ ml: 1 }} variant="outlined" />
                  )}
                </TableCell>
                <TableCell align="right">
                  <Tooltip title={t('settings.users.editAccessTooltip')}>
                    <span>
                      <IconButton size="small" disabled={u.isSystem} onClick={() => setEditFor(u)}>
                        <EditIcon fontSize="small" />
                      </IconButton>
                    </span>
                  </Tooltip>
                  <Tooltip title={t('settings.users.resetPasswordTooltip')}>
                    <IconButton size="small" onClick={() => setResetFor(u)}>
                      <KeyIcon fontSize="small" />
                    </IconButton>
                  </Tooltip>
                  <Tooltip title={u.isSystem ? t('settings.users.systemDeleteTooltip') : t('settings.users.deleteTooltip')}>
                    <span>
                      <IconButton
                        size="small"
                        color="error"
                        disabled={u.isSystem || del.isPending}
                        onClick={() => {
                          if (confirm(t('settings.users.confirmDelete', { username: u.username }))) del.mutate(u.id);
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
        title={t('settings.users.addUser')}
        withUsername
        roles={rolesQuery.data}
        submitLabel={t('common.actions.create')}
        pending={create.isPending}
        error={create.error instanceof ApiError ? create.error.message : null}
        onClose={() => setCreateOpen(false)}
        onSubmit={(v) => create.mutate(v)}
      />
      {editFor && (
        <UserEditDialog
          user={editFor}
          roles={rolesQuery.data ?? []}
          onClose={() => setEditFor(null)}
          onSaved={() => {
            setEditFor(null);
            invalidate();
          }}
        />
      )}
      <PasswordDialog
        open={!!resetFor}
        title={resetFor ? t('settings.users.resetTitleFor', { username: resetFor.username }) : t('settings.users.resetTitle')}
        withUsername={false}
        submitLabel={t('common.actions.reset')}
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
  const { t } = useTranslation();
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
        {t('settings.components.title')}
      </Typography>
      <Typography variant="body2" color="text.secondary" gutterBottom>
        {t('settings.components.description')}
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
                    <TableCell>{t('settings.components.colComponent')}</TableCell>
                    <TableCell>{t('settings.components.colVersion')}</TableCell>
                    <TableCell>{t('settings.components.colLicense')}</TableCell>
                    <TableCell align="right">{t('settings.components.colLinks')}</TableCell>
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
                              {t('settings.components.website')}
                            </Link>
                          )}
                          {c.licenseUrl && (
                            <Link href={c.licenseUrl} target="_blank" rel="noopener noreferrer">
                              {t('settings.components.license')}
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

/** Admin config for the scan page's value-tier badges: the currency code and the four ascending price
 * ceilings that split cards into five tiers (one to five currency signs). Editing is admin-only. */
function ScanBadgesCard() {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const qc = useQueryClient();
  const authQuery = useQuery({ queryKey: ['auth-status'], queryFn: api.authStatus });
  const settings = useQuery({ queryKey: ['scan-badge-settings'], queryFn: api.scanBadgeSettings });
  const isAdmin = !!authQuery.data?.isAdmin;

  const [currency, setCurrency] = useState('USD');
  const [thresholds, setThresholds] = useState<string[]>(['1', '5', '20', '50']);

  // Seed the form from the server once it loads (and after a save re-fetches the canonical values).
  useEffect(() => {
    if (settings.data) {
      setCurrency(settings.data.currencyCode);
      setThresholds(settings.data.thresholds.map((t) => String(t)));
    }
  }, [settings.data]);

  const save = useMutation({
    mutationFn: () =>
      api.scanBadgeSettingsUpdate({
        currencyCode: currency.trim().toUpperCase() || 'USD',
        thresholds: thresholds.map((t) => Number(t) || 0),
      }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['scan-badge-settings'] }),
  });

  const nums = thresholds.map((t) => Number(t));
  const validNumbers = nums.every((n) => Number.isFinite(n) && n > 0);
  const ascending = nums.every((n, i) => i === 0 || n > nums[i - 1]);
  const symbol = currencySymbol(currency || 'USD');
  const money = (n: number) => fmt.money(n, currency || 'USD');

  // The five tiers, described for the live preview (tier 5 is open-ended above the last threshold).
  const tierRows = [1, 2, 3, 4, 5].map((tier) => {
    let range: string;
    if (!validNumbers) range = t('settings.scanBadges.rangeUnknown');
    else if (tier === 1) range = t('settings.scanBadges.rangeAtMost', { max: money(nums[0]) });
    else if (tier === 5) range = t('settings.scanBadges.rangeAbove', { min: money(nums[3]) });
    else
      range = t('settings.scanBadges.rangeBetween', {
        min: money(nums[tier - 2]),
        max: money(nums[tier - 1]),
      });
    return { tier, range };
  });

  return (
    <Paper variant="outlined" sx={{ p: 2, maxWidth: 640 }}>
      <Typography variant="h6" gutterBottom>
        {t('settings.scanBadges.title')}
      </Typography>
      <Typography variant="body2" color="text.secondary" gutterBottom>
        {t('settings.scanBadges.descriptionBefore')}
        <StarIcon sx={{ fontSize: 16, color: '#f5b301', verticalAlign: 'text-bottom' }} />
        {t('settings.scanBadges.descriptionAfter')}
      </Typography>

      {settings.isLoading ? (
        <CircularProgress size={24} sx={{ mt: 1 }} />
      ) : (
        <Stack spacing={2} sx={{ mt: 1 }}>
          <TextField
            size="small"
            label={t('settings.scanBadges.currencyCode')}
            value={currency}
            disabled={!isAdmin}
            onChange={(e) => setCurrency(e.target.value.toUpperCase().slice(0, 3))}
            helperText={t('settings.scanBadges.currencyHelper', { symbol })}
            sx={{ maxWidth: 260 }}
          />

          <Stack direction="row" spacing={2} flexWrap="wrap" useFlexGap>
            {thresholds.map((threshold, i) => (
              <TextField
                key={i}
                size="small"
                type="number"
                label={t('settings.scanBadges.tierMax', { tier: i + 1, signs: symbol.repeat(i + 1) })}
                value={threshold}
                disabled={!isAdmin}
                onChange={(e) =>
                  setThresholds((prev) => prev.map((v, j) => (j === i ? e.target.value : v)))
                }
                inputProps={{ min: 0, step: '0.01' }}
                sx={{ width: 150 }}
              />
            ))}
          </Stack>

          {!ascending && validNumbers && (
            <Alert severity="warning">{t('settings.scanBadges.ascendingWarning')}</Alert>
          )}

          {/* Live preview of the five tiers. */}
          <Box>
            <Typography variant="subtitle2" gutterBottom>
              {t('settings.scanBadges.preview')}
            </Typography>
            <Stack spacing={0.5}>
              {tierRows.map(({ tier, range }) => (
                <Stack key={tier} direction="row" spacing={1.5} alignItems="center">
                  <Box
                    component="span"
                    sx={{ fontWeight: 700, letterSpacing: '-0.05em', minWidth: 72, color: 'success.main' }}
                  >
                    {symbol.repeat(tier)}
                  </Box>
                  <Typography variant="body2" color="text.secondary">
                    {range}
                  </Typography>
                </Stack>
              ))}
            </Stack>
          </Box>

          {save.error && <Alert severity="error">{(save.error as Error).message}</Alert>}
          {save.isSuccess && <Alert severity="success">{t('settings.scanBadges.saved')}</Alert>}

          {isAdmin ? (
            <Box>
              <Button
                variant="contained"
                disabled={!validNumbers || save.isPending}
                onClick={() => save.mutate()}
              >
                {save.isPending ? t('common.states.saving') : t('common.actions.save')}
              </Button>
            </Box>
          ) : (
            <Alert severity="info">{t('settings.scanBadges.adminOnly')}</Alert>
          )}
        </Stack>
      )}
    </Paper>
  );
}

/** Admin config for printed sales receipts: the store/company identity + logo shown at the top, and the
 * thermal-printer layout (roll width, margins, font size, whether prices show, footer text). The receipt
 * PDF (printed from an order's detail drawer) is generated at exactly the configured width. Editing is
 * admin-only; the width drives a live to-scale preview strip. */
function ReceiptSettingsCard() {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const authQuery = useQuery({ queryKey: ['auth-status'], queryFn: api.authStatus });
  const config = useQuery({ queryKey: ['receipt-config'], queryFn: api.receiptConfig });
  const isAdmin = !!authQuery.data?.isAdmin;

  const [company, setCompany] = useState({
    name: '', addressLine1: '', addressLine2: '', city: '', state: '', postalCode: '', country: '',
    email: '', phone: '',
  });
  const [widthMm, setWidthMm] = useState('80');
  const [marginMm, setMarginMm] = useState('4');
  const [fontPointSize, setFontPointSize] = useState('9');
  const [showPrices, setShowPrices] = useState(true);
  const [footerText, setFooterText] = useState('');

  // Seed the form from the server once it loads (and after a save re-fetches the canonical values).
  useEffect(() => {
    if (!config.data) return;
    const c = config.data.company;
    setCompany({
      name: c.name ?? '', addressLine1: c.addressLine1 ?? '', addressLine2: c.addressLine2 ?? '',
      city: c.city ?? '', state: c.state ?? '', postalCode: c.postalCode ?? '', country: c.country ?? '',
      email: c.email ?? '', phone: c.phone ?? '',
    });
    const r = config.data.receipt;
    setWidthMm(String(r.widthMm));
    setMarginMm(String(r.marginMm));
    setFontPointSize(String(r.fontPointSize));
    setShowPrices(r.showPrices);
    setFooterText(r.footerText ?? '');
  }, [config.data]);

  const save = useMutation({
    mutationFn: () =>
      api.receiptConfigUpdate({
        company,
        receipt: {
          widthMm: Number(widthMm) || 80,
          marginMm: Number(marginMm) || 0,
          fontPointSize: Number(fontPointSize) || 9,
          showPrices,
          footerText,
        },
      }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['receipt-config'] }),
  });

  const uploadLogo = useMutation({
    mutationFn: (file: File) => api.receiptLogoUpload(file),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['receipt-config'] }),
  });
  const removeLogo = useMutation({
    mutationFn: () => api.receiptLogoDelete(),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['receipt-config'] }),
  });

  const logoUrl = config.data?.company.logoUrl ?? null;
  const widthNum = Number(widthMm) || 80;
  const field = (label: string, key: keyof typeof company, width = 260) => (
    <TextField
      size="small"
      label={label}
      value={company[key]}
      disabled={!isAdmin}
      onChange={(e) => setCompany((prev) => ({ ...prev, [key]: e.target.value }))}
      sx={{ width }}
    />
  );

  return (
    <Paper variant="outlined" sx={{ p: 2, maxWidth: 720 }}>
      <Typography variant="h6" gutterBottom>
        {t('settings.receipt.title')}
      </Typography>
      <Typography variant="body2" color="text.secondary" gutterBottom>
        {t('settings.receipt.description')}
      </Typography>

      {config.isLoading ? (
        <CircularProgress size={24} sx={{ mt: 1 }} />
      ) : (
        <Stack spacing={3} sx={{ mt: 1 }}>
          {/* Company identity */}
          <Box>
            <Typography variant="subtitle2" gutterBottom>
              {t('settings.receipt.companySection')}
            </Typography>
            <Stack direction="row" spacing={2} flexWrap="wrap" useFlexGap>
              {field(t('settings.receipt.companyName'), 'name', 320)}
              {field(t('settings.receipt.email'), 'email')}
              {field(t('settings.receipt.phone'), 'phone', 180)}
              {field(t('settings.receipt.addressLine1'), 'addressLine1', 320)}
              {field(t('settings.receipt.addressLine2'), 'addressLine2', 320)}
              {field(t('settings.receipt.city'), 'city', 200)}
              {field(t('settings.receipt.state'), 'state', 120)}
              {field(t('settings.receipt.postalCode'), 'postalCode', 140)}
              {field(t('settings.receipt.country'), 'country', 160)}
            </Stack>
          </Box>

          {/* Logo */}
          <Box>
            <Typography variant="subtitle2" gutterBottom>
              {t('settings.receipt.logoSection')}
            </Typography>
            <Stack direction="row" spacing={2} alignItems="center" flexWrap="wrap" useFlexGap>
              <Box
                sx={{
                  width: 160, height: 80, border: 1, borderColor: 'divider', borderRadius: 1,
                  display: 'flex', alignItems: 'center', justifyContent: 'center', bgcolor: 'background.default',
                  overflow: 'hidden',
                }}
              >
                {logoUrl ? (
                  <Box component="img" src={logoUrl} alt="" sx={{ maxWidth: '100%', maxHeight: '100%' }} />
                ) : (
                  <Typography variant="caption" color="text.secondary">
                    {t('settings.receipt.noLogo')}
                  </Typography>
                )}
              </Box>
              {isAdmin && (
                <Stack spacing={1}>
                  <Button variant="outlined" component="label" disabled={uploadLogo.isPending}>
                    {uploadLogo.isPending ? t('common.states.saving') : t('settings.receipt.uploadLogo')}
                    <input
                      type="file"
                      hidden
                      accept="image/png,image/jpeg,image/gif,image/webp,image/bmp"
                      onChange={(e) => {
                        const f = e.target.files?.[0];
                        if (f) uploadLogo.mutate(f);
                        e.target.value = '';
                      }}
                    />
                  </Button>
                  {logoUrl && (
                    <Button
                      color="error"
                      size="small"
                      startIcon={<DeleteIcon />}
                      disabled={removeLogo.isPending}
                      onClick={() => removeLogo.mutate()}
                    >
                      {t('settings.receipt.removeLogo')}
                    </Button>
                  )}
                </Stack>
              )}
            </Stack>
            {uploadLogo.error && <Alert severity="error" sx={{ mt: 1 }}>{(uploadLogo.error as Error).message}</Alert>}
          </Box>

          {/* Printer layout */}
          <Box>
            <Typography variant="subtitle2" gutterBottom>
              {t('settings.receipt.layoutSection')}
            </Typography>
            <Stack direction="row" spacing={2} alignItems="flex-start" flexWrap="wrap" useFlexGap>
              <Box>
                <TextField
                  size="small"
                  type="number"
                  label={t('settings.receipt.widthMm')}
                  value={widthMm}
                  disabled={!isAdmin}
                  onChange={(e) => setWidthMm(e.target.value)}
                  inputProps={{ min: 20, max: 210, step: 1 }}
                  sx={{ width: 150 }}
                />
                <Stack direction="row" spacing={1} sx={{ mt: 1 }}>
                  {['58', '80'].map((w) => (
                    <Chip
                      key={w}
                      label={t('settings.receipt.widthPreset', { mm: w })}
                      size="small"
                      variant={widthMm === w ? 'filled' : 'outlined'}
                      color={widthMm === w ? 'primary' : 'default'}
                      onClick={isAdmin ? () => setWidthMm(w) : undefined}
                    />
                  ))}
                </Stack>
              </Box>
              <TextField
                size="small"
                type="number"
                label={t('settings.receipt.marginMm')}
                value={marginMm}
                disabled={!isAdmin}
                onChange={(e) => setMarginMm(e.target.value)}
                inputProps={{ min: 0, max: 25, step: 0.5 }}
                sx={{ width: 130 }}
              />
              <TextField
                size="small"
                type="number"
                label={t('settings.receipt.fontPointSize')}
                value={fontPointSize}
                disabled={!isAdmin}
                onChange={(e) => setFontPointSize(e.target.value)}
                inputProps={{ min: 5, max: 24, step: 0.5 }}
                sx={{ width: 130 }}
              />
              <FormControlLabel
                control={
                  <Switch
                    checked={showPrices}
                    disabled={!isAdmin}
                    onChange={(e) => setShowPrices(e.target.checked)}
                  />
                }
                label={t('settings.receipt.showPrices')}
              />
            </Stack>
            <TextField
              size="small"
              label={t('settings.receipt.footerText')}
              value={footerText}
              disabled={!isAdmin}
              onChange={(e) => setFooterText(e.target.value)}
              fullWidth
              multiline
              minRows={2}
              sx={{ mt: 2, maxWidth: 480 }}
              placeholder={t('settings.receipt.footerPlaceholder')}
            />

            {/* To-scale width preview so the admin can gauge the roll width. */}
            <Box sx={{ mt: 2 }}>
              <Typography variant="caption" color="text.secondary">
                {t('settings.receipt.previewWidth', { mm: widthNum })}
              </Typography>
              <Box
                sx={{
                  mt: 0.5,
                  width: `${widthNum}mm`,
                  maxWidth: '100%',
                  border: '1px dashed',
                  borderColor: 'divider',
                  borderRadius: 1,
                  p: 1,
                  bgcolor: 'background.default',
                }}
              >
                {logoUrl && (
                  <Box component="img" src={logoUrl} alt="" sx={{ display: 'block', mx: 'auto', maxHeight: 48, maxWidth: '80%', mb: 0.5 }} />
                )}
                <Typography variant="caption" sx={{ display: 'block', textAlign: 'center', fontWeight: 600 }}>
                  {company.name || t('settings.receipt.companyNamePlaceholder')}
                </Typography>
                <Typography variant="caption" sx={{ display: 'block', textAlign: 'center', color: 'text.secondary' }}>
                  {t('settings.receipt.previewSampleLine')}
                </Typography>
              </Box>
            </Box>
          </Box>

          {save.error && <Alert severity="error">{(save.error as Error).message}</Alert>}
          {save.isSuccess && <Alert severity="success">{t('settings.receipt.saved')}</Alert>}

          {isAdmin ? (
            <Box>
              <Button variant="contained" disabled={save.isPending} onClick={() => save.mutate()}>
                {save.isPending ? t('common.states.saving') : t('common.actions.save')}
              </Button>
            </Box>
          ) : (
            <Alert severity="info">{t('settings.receipt.adminOnly')}</Alert>
          )}
        </Stack>
      )}
    </Paper>
  );
}

// `show` decides tab visibility from the signed-in user's access. The Administration hub itself is
// always reachable so everyone can change their password (Users tab) and read Components.
type TabGate = { isAdmin: boolean; can: (perm: string) => boolean };
const TABS: {
  key: string;
  labelKey: string;
  render: () => JSX.Element;
  show: (g: TabGate) => boolean;
}[] = [
  { key: 'sales', labelKey: 'settings.tabs.sales', render: () => <SalesCard />, show: (g) => g.can('settings.view') },
  { key: 'receipt', labelKey: 'settings.tabs.receipt', render: () => <ReceiptSettingsCard />, show: (g) => g.can('settings.view') },
  { key: 'scan', labelKey: 'settings.tabs.scan', render: () => <ScanBadgesCard />, show: (g) => g.can('settings.view') },
  { key: 'deck-types', labelKey: 'settings.tabs.deckTypes', render: () => <DeckTypesCard />, show: (g) => g.can('decktypes.view') },
  { key: 'appearance', labelKey: 'settings.tabs.appearance', render: () => <AppearanceCard />, show: () => true },
  { key: 'catalog', labelKey: 'settings.tabs.catalog', render: () => <CatalogCard />, show: (g) => g.can('catalog.view') },
  { key: 'ebay', labelKey: 'settings.tabs.ebay', render: () => <EbayCard />, show: (g) => g.can('ebay.view') },
  { key: 'roles', labelKey: 'settings.tabs.roles', render: () => <RolesCard />, show: (g) => g.isAdmin },
  { key: 'users', labelKey: 'settings.tabs.users', render: () => <UsersTab />, show: () => true },
  { key: 'components', labelKey: 'settings.tabs.components', render: () => <ComponentsCard />, show: () => true },
];

export function SettingsPage() {
  const { t } = useTranslation();
  const { isAdmin, can } = usePermissions();
  const [params, setParams] = useSearchParams();
  const requested = params.get('tab');
  const tabs = TABS.filter((tab) => tab.show({ isAdmin, can }));
  const active = Math.max(
    0,
    tabs.findIndex((tab) => tab.key === requested),
  );

  return (
    <Stack spacing={3}>
      <Typography variant="h4">{t('settings.title')}</Typography>
      <Box sx={{ borderBottom: 1, borderColor: 'divider' }}>
        <Tabs
          value={active}
          onChange={(_, v) => {
            const nextParams = new URLSearchParams(params);
            nextParams.set('tab', tabs[v].key);
            setParams(nextParams, { replace: true });
          }}
          variant="scrollable"
          scrollButtons="auto"
        >
          {tabs.map((tab) => (
            <Tab key={tab.key} label={t(tab.labelKey)} />
          ))}
        </Tabs>
      </Box>
      <Box>{tabs[active]?.render()}</Box>
    </Stack>
  );
}
