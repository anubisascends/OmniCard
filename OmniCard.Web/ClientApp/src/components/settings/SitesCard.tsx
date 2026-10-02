import { useEffect, useMemo, useState } from 'react';
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
  MenuItem,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  TextField,
  ToggleButton,
  ToggleButtonGroup,
  Tooltip,
  Typography,
} from '@mui/material';
import DeleteIcon from '@mui/icons-material/Delete';
import EditIcon from '@mui/icons-material/Edit';
import GroupIcon from '@mui/icons-material/Group';
import { api, ApiError } from '../../api/client';
import type { SiteDto, SiteGrantDto } from '../../api/types';
import { useFormatters } from '../../i18n/format';

type Level = SiteGrantDto['level'];

/**
 * Admin-only management of sites — MAJOR physical locations (a home, a shop) that each hold many
 * storage locations. Create/rename/delete sites and choose which users and roles may view (Read) or
 * change (Write) each one. The default site is always visible to everyone, so it has no access list
 * and can't be deleted (it can be renamed).
 */
export function SitesCard() {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const qc = useQueryClient();
  const sitesQuery = useQuery({ queryKey: ['sites'], queryFn: api.sites });
  const [editing, setEditing] = useState<SiteDto | 'new' | null>(null);
  const [access, setAccess] = useState<SiteDto | null>(null);
  const [deleting, setDeleting] = useState<SiteDto | null>(null);

  const invalidate = () => {
    qc.invalidateQueries({ queryKey: ['sites'] });
    qc.invalidateQueries({ queryKey: ['locations'] });
  };

  return (
    <Paper variant="outlined" sx={{ p: 2, maxWidth: 820 }}>
      <Stack direction="row" alignItems="center" justifyContent="space-between">
        <Typography variant="h6">{t('settings.sites.title')}</Typography>
        <Button variant="contained" size="small" onClick={() => setEditing('new')}>
          {t('settings.sites.addSite')}
        </Button>
      </Stack>
      <Typography variant="body2" color="text.secondary" gutterBottom>
        {t('settings.sites.description')}
      </Typography>

      {sitesQuery.isLoading ? (
        <CircularProgress size={24} sx={{ mt: 1 }} />
      ) : (
        <Table size="small" sx={{ mt: 1 }}>
          <TableHead>
            <TableRow>
              <TableCell>{t('settings.sites.colName')}</TableCell>
              <TableCell>{t('settings.sites.colDescription')}</TableCell>
              <TableCell align="right">{t('settings.sites.colLocations')}</TableCell>
              <TableCell align="right">{t('settings.users.colActions')}</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {sitesQuery.data?.map((s) => (
              <TableRow key={s.id}>
                <TableCell>
                  {s.name}
                  {s.isDefault && (
                    <Chip label={t('settings.sites.defaultChip')} size="small" sx={{ ml: 1 }} variant="outlined" />
                  )}
                </TableCell>
                <TableCell>
                  <Typography variant="body2" color="text.secondary">
                    {s.description}
                  </Typography>
                </TableCell>
                <TableCell align="right">{fmt.number(s.locationCount)}</TableCell>
                <TableCell align="right" sx={{ whiteSpace: 'nowrap' }}>
                  <Tooltip title={t('common.actions.edit')}>
                    <IconButton size="small" onClick={() => setEditing(s)}>
                      <EditIcon fontSize="small" />
                    </IconButton>
                  </Tooltip>
                  <Tooltip title={s.isDefault ? t('settings.sites.defaultAccessTooltip') : t('settings.sites.access')}>
                    <span>
                      <IconButton size="small" disabled={s.isDefault} onClick={() => setAccess(s)}>
                        <GroupIcon fontSize="small" />
                      </IconButton>
                    </span>
                  </Tooltip>
                  <Tooltip title={s.isDefault ? t('settings.sites.defaultDeleteTooltip') : t('common.actions.delete')}>
                    <span>
                      <IconButton size="small" color="error" disabled={s.isDefault} onClick={() => setDeleting(s)}>
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
        <SiteDialog
          site={editing === 'new' ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            invalidate();
          }}
        />
      )}
      {access && <SiteAccessDialog site={access} onClose={() => setAccess(null)} />}
      {deleting && (
        <DeleteSiteDialog
          site={deleting}
          sites={sitesQuery.data ?? []}
          onClose={() => setDeleting(null)}
          onDeleted={() => {
            setDeleting(null);
            invalidate();
          }}
        />
      )}
    </Paper>
  );
}

function SiteDialog({ site, onClose, onSaved }: { site: SiteDto | null; onClose: () => void; onSaved: () => void }) {
  const { t } = useTranslation();
  const [name, setName] = useState(site?.name ?? '');
  const [description, setDescription] = useState(site?.description ?? '');
  const save = useMutation({
    mutationFn: () => {
      const body = { name: name.trim(), description: description.trim() || null };
      return site ? api.siteUpdate(site.id, body) : api.siteCreate(body);
    },
    onSuccess: onSaved,
  });
  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>{site ? t('settings.sites.editTitle', { name: site.name }) : t('settings.sites.addSite')}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <Typography variant="body2" color="text.secondary">
            {t('settings.sites.whatIsASite')}
          </Typography>
          <TextField
            label={t('settings.sites.colName')}
            size="small"
            autoFocus
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder={t('settings.sites.namePlaceholder')}
          />
          <TextField
            label={t('settings.sites.colDescription')}
            size="small"
            multiline
            minRows={2}
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            placeholder={t('settings.sites.descriptionPlaceholder')}
          />
          {save.error instanceof ApiError && <Alert severity="error">{save.error.message}</Alert>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('common.actions.cancel')}</Button>
        <Button variant="contained" disabled={!name.trim() || save.isPending} onClick={() => save.mutate()}>
          {save.isPending ? t('common.states.saving') : t('common.actions.save')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

function DeleteSiteDialog({
  site,
  sites,
  onClose,
  onDeleted,
}: {
  site: SiteDto;
  sites: SiteDto[];
  onClose: () => void;
  onDeleted: () => void;
}) {
  const { t } = useTranslation();
  const targets = sites.filter((s) => s.id !== site.id);
  const [target, setTarget] = useState<number>(targets.find((s) => s.isDefault)?.id ?? targets[0]?.id);
  const del = useMutation({ mutationFn: () => api.siteDelete(site.id, target), onSuccess: onDeleted });
  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>{t('settings.sites.deleteTitle', { name: site.name })}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <Typography variant="body2">
            {t('settings.sites.deleteHelp', { count: site.locationCount })}
          </Typography>
          {site.locationCount > 0 && (
            <TextField
              select
              size="small"
              label={t('settings.sites.moveLocationsTo')}
              value={target}
              onChange={(e) => setTarget(Number(e.target.value))}
            >
              {targets.map((s) => (
                <MenuItem key={s.id} value={s.id}>
                  {s.name}
                </MenuItem>
              ))}
            </TextField>
          )}
          {del.error instanceof ApiError && <Alert severity="error">{del.error.message}</Alert>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('common.actions.cancel')}</Button>
        <Button color="error" variant="contained" disabled={del.isPending} onClick={() => del.mutate()}>
          {t('common.actions.delete')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

const keyOf = (type: SiteGrantDto['principalType'], id: number) => `${type}:${id}`;

/** Who may view (Read) or change (Write) one site — per role and per user. A user's effective level is
 * the higher of their own and their role's; administrators always see every site. */
function SiteAccessDialog({ site, onClose }: { site: SiteDto; onClose: () => void }) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const grantsQuery = useQuery({ queryKey: ['site-grants', site.id], queryFn: () => api.siteGrants(site.id) });
  const roles = useQuery({ queryKey: ['roles'], queryFn: api.roles });
  const users = useQuery({ queryKey: ['users'], queryFn: api.users });
  const [levels, setLevels] = useState<Map<string, Level>>(new Map());

  useEffect(() => {
    if (grantsQuery.data)
      setLevels(new Map(grantsQuery.data.map((g) => [keyOf(g.principalType, g.principalId), g.level])));
  }, [grantsQuery.data]);

  const levelOf = (type: SiteGrantDto['principalType'], id: number): Level => levels.get(keyOf(type, id)) ?? 'None';
  const setLevel = (type: SiteGrantDto['principalType'], id: number, level: Level) =>
    setLevels((prev) => new Map(prev).set(keyOf(type, id), level));

  const save = useMutation({
    mutationFn: () => {
      const grants: SiteGrantDto[] = [...levels.entries()]
        .filter(([, level]) => level !== 'None')
        .map(([key, level]) => {
          const [principalType, id] = key.split(':');
          return { principalType: principalType as SiteGrantDto['principalType'], principalId: Number(id), level };
        });
      return api.siteSetGrants(site.id, grants);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['site-grants', site.id] });
      qc.invalidateQueries({ queryKey: ['sites'] });
      onClose();
    },
  });

  const roleName = useMemo(() => new Map((roles.data ?? []).map((r) => [r.id, r.name])), [roles.data]);
  const loading = grantsQuery.isLoading || roles.isLoading || users.isLoading;

  const LevelToggle = ({ type, id }: { type: SiteGrantDto['principalType']; id: number }) => (
    <ToggleButtonGroup
      size="small"
      exclusive
      value={levelOf(type, id)}
      onChange={(_, v: Level | null) => v && setLevel(type, id, v)}
    >
      <ToggleButton value="None">{t('settings.sites.levelNone')}</ToggleButton>
      <ToggleButton value="Read">{t('settings.sites.levelRead')}</ToggleButton>
      <ToggleButton value="Write">{t('settings.sites.levelWrite')}</ToggleButton>
    </ToggleButtonGroup>
  );

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>{t('settings.sites.accessTitle', { name: site.name })}</DialogTitle>
      <DialogContent>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          {t('settings.sites.accessHelp')}
        </Typography>
        {loading ? (
          <CircularProgress size={24} />
        ) : (
          <Stack spacing={2}>
            <Typography variant="subtitle2">{t('settings.sites.roles')}</Typography>
            <Table size="small">
              <TableBody>
                {roles.data?.map((r) => (
                  <TableRow key={r.id}>
                    <TableCell>{r.name}</TableCell>
                    <TableCell align="right">
                      <LevelToggle type="Role" id={r.id} />
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
            <Typography variant="subtitle2">{t('settings.sites.users')}</Typography>
            <Table size="small">
              <TableBody>
                {users.data?.map((u) => (
                  <TableRow key={u.id}>
                    <TableCell>
                      {u.username}
                      {!u.isAdmin && u.roleId != null && roleName.get(u.roleId) && (
                        <Typography variant="caption" color="text.secondary" sx={{ ml: 1 }}>
                          {roleName.get(u.roleId)}
                        </Typography>
                      )}
                    </TableCell>
                    <TableCell align="right">
                      {u.isAdmin ? (
                        <Typography variant="body2" color="text.secondary">
                          {t('settings.sites.adminAllSites')}
                        </Typography>
                      ) : (
                        <LevelToggle type="User" id={u.id} />
                      )}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
            {save.error instanceof ApiError && <Alert severity="error">{save.error.message}</Alert>}
          </Stack>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('common.actions.cancel')}</Button>
        <Button variant="contained" disabled={loading || save.isPending} onClick={() => save.mutate()}>
          {save.isPending ? t('common.states.saving') : t('common.actions.save')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
