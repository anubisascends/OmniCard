import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useQuery } from '@tanstack/react-query';
import {
  Button,
  Checkbox,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControl,
  FormControlLabel,
  FormLabel,
  List,
  ListItemButton,
  ListItemIcon,
  ListItemText,
  ListSubheader,
  Radio,
  RadioGroup,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import { api } from '../../api/client';
import { groupLocations } from '../../lib/locationGroups';
import { ALL_GAMES_KEY } from '../../lib/savedViews';
import { headingFor } from '../LocationSelectOptions';

export interface SaveViewChoice {
  name: string;
  /** The view's game key, or null for any game. */
  game: string | null;
  shared: boolean;
  /** Shared Location-page views only: offer it on every location. */
  allLocations: boolean;
  makeDefault: boolean;
}

/**
 * "Save as new view": name, which games it's offered for (the selected game or any game), and — for
 * administrators — sharing it with everyone (on a Location page, optionally on every location).
 */
export function SaveViewDialog({
  open,
  onLocation,
  gameKey,
  isAdmin,
  busy,
  error,
  onSave,
  onClose,
}: {
  open: boolean;
  onLocation: boolean;
  /** The selected game's key ("all" for All Games). */
  gameKey: string;
  isAdmin: boolean;
  busy: boolean;
  error?: string | null;
  onSave: (choice: SaveViewChoice) => void;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const games = useQuery({ queryKey: ['games'], queryFn: api.games, enabled: open });
  const [name, setName] = useState('');
  const [anyGame, setAnyGame] = useState(false);
  const [shared, setShared] = useState(false);
  const [allLocations, setAllLocations] = useState(false);
  const [makeDefault, setMakeDefault] = useState(false);

  useEffect(() => {
    if (!open) return;
    setName('');
    setAnyGame(false);
    setShared(false);
    setAllLocations(false);
    setMakeDefault(false);
  }, [open]);

  const thisGameLabel =
    gameKey === ALL_GAMES_KEY
      ? t('collection.savedViews.save.allGamesOnly')
      : t('collection.savedViews.save.thisGame', {
          game: games.data?.find((g) => g.id === gameKey)?.displayName ?? gameKey,
        });

  const submit = () =>
    name.trim() &&
    onSave({
      name: name.trim(),
      game: anyGame ? null : gameKey,
      shared,
      allLocations: shared && onLocation && allLocations,
      makeDefault,
    });

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>{t('collection.savedViews.save.title')}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <TextField
            autoFocus
            label={t('common.labels.name')}
            value={name}
            inputProps={{ maxLength: 100 }}
            onChange={(e) => setName(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && submit()}
          />
          <FormControl>
            <FormLabel>{t('collection.savedViews.save.gamesLabel')}</FormLabel>
            <RadioGroup value={anyGame ? 'any' : 'this'} onChange={(e) => setAnyGame(e.target.value === 'any')}>
              <FormControlLabel
                value="this"
                control={<Radio size="small" />}
                label={thisGameLabel}
              />
              <FormControlLabel value="any" control={<Radio size="small" />} label={t('collection.savedViews.save.anyGame')} />
            </RadioGroup>
          </FormControl>
          {isAdmin && (
            <FormControlLabel
              control={<Checkbox checked={shared} onChange={(e) => setShared(e.target.checked)} />}
              label={t('collection.savedViews.save.share')}
            />
          )}
          {shared && onLocation && (
            <FormControl>
              <FormLabel>{t('collection.savedViews.save.locationsLabel')}</FormLabel>
              <RadioGroup
                value={allLocations ? 'all' : 'this'}
                onChange={(e) => setAllLocations(e.target.value === 'all')}
              >
                <FormControlLabel
                  value="this"
                  control={<Radio size="small" />}
                  label={t('collection.savedViews.save.thisLocation')}
                />
                <FormControlLabel
                  value="all"
                  control={<Radio size="small" />}
                  label={t('collection.savedViews.save.allLocations')}
                />
              </RadioGroup>
            </FormControl>
          )}
          <FormControlLabel
            control={<Checkbox checked={makeDefault} onChange={(e) => setMakeDefault(e.target.checked)} />}
            label={
              shared
                ? t('collection.savedViews.save.makeEveryoneDefault')
                : t('collection.savedViews.save.makeMyDefault')
            }
          />
          {error && (
            <Typography color="error" variant="body2">
              {error}
            </Typography>
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('common.actions.cancel')}</Button>
        <Button variant="contained" disabled={busy || !name.trim()} onClick={submit}>
          {t('common.actions.save')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

/** Rename a saved view. */
export function RenameViewDialog({
  open,
  currentName,
  busy,
  error,
  onRename,
  onClose,
}: {
  open: boolean;
  currentName: string;
  busy: boolean;
  error?: string | null;
  onRename: (name: string) => void;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const [name, setName] = useState(currentName);
  useEffect(() => {
    if (open) setName(currentName);
  }, [open, currentName]);
  const submit = () => name.trim() && name.trim() !== currentName && onRename(name.trim());

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>{t('collection.savedViews.rename.title')}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <TextField
            autoFocus
            label={t('common.labels.name')}
            value={name}
            inputProps={{ maxLength: 100 }}
            onChange={(e) => setName(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && submit()}
          />
          {error && (
            <Typography color="error" variant="body2">
              {error}
            </Typography>
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('common.actions.cancel')}</Button>
        <Button variant="contained" disabled={busy || !name.trim() || name.trim() === currentName} onClick={submit}>
          {t('collection.savedViews.rename.action')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

/**
 * Copy a Location view to other locations: tick the locations (grouped by type, like every location
 * picker), optionally making it the default there. A same-named view on a target is overwritten.
 */
export function CopyViewDialog({
  open,
  viewName,
  shared,
  excludeId,
  busy,
  error,
  onCopy,
  onClose,
}: {
  open: boolean;
  viewName: string;
  shared: boolean;
  excludeId?: number;
  busy: boolean;
  error?: string | null;
  onCopy: (containerIds: number[], setDefault: boolean) => void;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const locations = useQuery({ queryKey: ['locations', undefined], queryFn: () => api.locations(), enabled: open });
  const [picked, setPicked] = useState<Set<number>>(new Set());
  const [setDefault, setSetDefault] = useState(false);

  useEffect(() => {
    if (!open) return;
    setPicked(new Set());
    setSetDefault(false);
  }, [open]);

  const groups = useMemo(
    () => groupLocations((locations.data ?? []).filter((l) => l.id !== excludeId)),
    [locations.data, excludeId],
  );
  const allIds = groups.flatMap((g) => g.items.map((l) => l.id));
  const toggle = (id: number) =>
    setPicked((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  const toggleGroup = (ids: number[]) =>
    setPicked((prev) => {
      const next = new Set(prev);
      const all = ids.every((id) => next.has(id));
      for (const id of ids) {
        if (all) next.delete(id);
        else next.add(id);
      }
      return next;
    });

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>{t('collection.savedViews.copy.title', { name: viewName })}</DialogTitle>
      <DialogContent dividers>
        <Stack spacing={1}>
          <Typography variant="body2" color="text.secondary">
            {t('collection.savedViews.copy.intro')}
          </Typography>
          <Stack direction="row" spacing={1}>
            <Button size="small" onClick={() => setPicked(new Set(allIds))}>
              {t('collection.savedViews.copy.selectAll')}
            </Button>
            <Button size="small" onClick={() => setPicked(new Set())}>
              {t('collection.savedViews.copy.selectNone')}
            </Button>
          </Stack>
          <List dense sx={{ maxHeight: 360, overflowY: 'auto' }}>
            {groups.map((g) => {
              const ids = g.items.map((l) => l.id);
              return (
                <li key={g.key}>
                  <ul style={{ padding: 0 }}>
                    <ListSubheader
                      disableSticky
                      sx={{ cursor: 'pointer', lineHeight: '32px' }}
                      onClick={() => toggleGroup(ids)}
                      title={t('collection.savedViews.copy.toggleGroup')}
                    >
                      {headingFor(g.key, g.heading)}
                    </ListSubheader>
                    {g.items.map((l) => (
                      <ListItemButton key={l.id} dense onClick={() => toggle(l.id)}>
                        <ListItemIcon sx={{ minWidth: 36 }}>
                          <Checkbox edge="start" size="small" checked={picked.has(l.id)} tabIndex={-1} disableRipple />
                        </ListItemIcon>
                        <ListItemText primary={l.name} secondary={l.siteName} />
                      </ListItemButton>
                    ))}
                  </ul>
                </li>
              );
            })}
          </List>
          <FormControlLabel
            control={<Checkbox checked={setDefault} onChange={(e) => setSetDefault(e.target.checked)} />}
            label={
              shared
                ? t('collection.savedViews.copy.makeEveryoneDefault')
                : t('collection.savedViews.copy.makeMyDefault')
            }
          />
          {error && (
            <Typography color="error" variant="body2">
              {error}
            </Typography>
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('common.actions.cancel')}</Button>
        <Button
          variant="contained"
          disabled={busy || picked.size === 0}
          onClick={() => onCopy([...picked], setDefault)}
        >
          {t('collection.savedViews.copy.action', { count: picked.size })}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
