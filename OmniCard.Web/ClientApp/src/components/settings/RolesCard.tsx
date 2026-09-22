import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Button,
  Chip,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  IconButton,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material';
import DeleteIcon from '@mui/icons-material/Delete';
import EditIcon from '@mui/icons-material/Edit';
import { api, ApiError } from '../../api/client';
import type { PermissionCatalogDto, RoleDto } from '../../api/types';
import { PermissionChecklist } from './PermissionChecklist';

/** Admin-only management of roles (reusable permission bundles). System roles can be re-permissioned
 * but not renamed or deleted. */
export function RolesCard() {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const rolesQuery = useQuery({ queryKey: ['roles'], queryFn: api.roles });
  const catalog = useQuery({ queryKey: ['permission-catalog'], queryFn: api.permissionCatalog });
  const [editing, setEditing] = useState<RoleDto | 'new' | null>(null);

  const invalidate = () => {
    qc.invalidateQueries({ queryKey: ['roles'] });
    // Effective permissions may have changed for many users — refresh our own too.
    qc.invalidateQueries({ queryKey: ['auth-status'] });
  };

  const del = useMutation({
    mutationFn: (id: number) => api.roleDelete(id),
    onSuccess: invalidate,
  });

  return (
    <Paper variant="outlined" sx={{ p: 2, maxWidth: 720 }}>
      <Stack direction="row" alignItems="center" justifyContent="space-between">
        <Typography variant="h6">{t('settings.roles.title')}</Typography>
        <Button variant="contained" size="small" onClick={() => setEditing('new')}>
          {t('settings.roles.addRole')}
        </Button>
      </Stack>
      <Typography variant="body2" color="text.secondary" gutterBottom>
        {t('settings.roles.description')}
      </Typography>

      {del.error instanceof ApiError && (
        <Alert severity="error" sx={{ mt: 1 }}>
          {del.error.message}
        </Alert>
      )}

      {rolesQuery.isLoading ? (
        <CircularProgress size={24} sx={{ mt: 1 }} />
      ) : (
        <Table size="small" sx={{ mt: 1 }}>
          <TableHead>
            <TableRow>
              <TableCell>{t('settings.roles.colName')}</TableCell>
              <TableCell>{t('settings.roles.colPermissions')}</TableCell>
              <TableCell align="right">{t('settings.users.colActions')}</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {rolesQuery.data?.map((r) => (
              <TableRow key={r.id}>
                <TableCell>
                  {r.name}
                  {r.isSystem && (
                    <Chip label={t('settings.users.systemChip')} size="small" sx={{ ml: 1 }} variant="outlined" />
                  )}
                </TableCell>
                <TableCell>
                  <Typography variant="body2" color="text.secondary">
                    {t('settings.roles.permissionCount', { count: r.permissions.length })}
                  </Typography>
                </TableCell>
                <TableCell align="right">
                  <Tooltip title={t('common.actions.edit')}>
                    <IconButton size="small" onClick={() => setEditing(r)}>
                      <EditIcon fontSize="small" />
                    </IconButton>
                  </Tooltip>
                  <Tooltip title={r.isSystem ? t('settings.roles.systemDeleteTooltip') : t('common.actions.delete')}>
                    <span>
                      <IconButton
                        size="small"
                        color="error"
                        disabled={r.isSystem || del.isPending}
                        onClick={() => {
                          if (confirm(t('settings.roles.confirmDelete', { name: r.name }))) del.mutate(r.id);
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

      {editing && (
        <RoleDialog
          role={editing === 'new' ? null : editing}
          catalog={catalog.data}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            invalidate();
          }}
        />
      )}
    </Paper>
  );
}

function RoleDialog({
  role,
  catalog,
  onClose,
  onSaved,
}: {
  role: RoleDto | null;
  catalog: PermissionCatalogDto | undefined;
  onClose: () => void;
  onSaved: () => void;
}) {
  const { t } = useTranslation();
  const isSystem = role?.isSystem ?? false;
  const [name, setName] = useState(role?.name ?? '');
  const [perms, setPerms] = useState<string[]>(role?.permissions ?? []);

  useEffect(() => {
    setName(role?.name ?? '');
    setPerms(role?.permissions ?? []);
  }, [role]);

  const save = useMutation({
    mutationFn: () =>
      role
        ? api.roleUpdate(role.id, { name: name.trim(), permissions: perms })
        : api.roleCreate({ name: name.trim(), permissions: perms }),
    onSuccess: onSaved,
  });

  const canSubmit = (isSystem || !!name.trim()) && !save.isPending;

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="md">
      <DialogTitle>{role ? t('settings.roles.editTitle', { name: role.name }) : t('settings.roles.addRole')}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <TextField
            label={t('settings.roles.colName')}
            size="small"
            value={name}
            disabled={isSystem}
            onChange={(e) => setName(e.target.value)}
            helperText={isSystem ? t('settings.roles.systemNameLocked') : undefined}
            sx={{ maxWidth: 320 }}
          />
          <PermissionChecklist catalog={catalog} selected={perms} onChange={setPerms} />
          {save.error instanceof ApiError && <Alert severity="error">{save.error.message}</Alert>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('common.actions.cancel')}</Button>
        <Button variant="contained" disabled={!canSubmit} onClick={() => save.mutate()}>
          {save.isPending ? t('common.states.saving') : t('common.actions.save')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
