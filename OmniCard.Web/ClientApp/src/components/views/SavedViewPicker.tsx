import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation } from '@tanstack/react-query';
import {
  Alert,
  Badge,
  Button,
  Divider,
  ListItemIcon,
  ListItemText,
  ListSubheader,
  Menu,
  MenuItem,
  Snackbar,
  Tooltip,
} from '@mui/material';
import ArrowDropDownIcon from '@mui/icons-material/ArrowDropDown';
import BookmarksIcon from '@mui/icons-material/Bookmarks';
import CheckIcon from '@mui/icons-material/Check';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import DeleteIcon from '@mui/icons-material/Delete';
import DriveFileRenameOutlineIcon from '@mui/icons-material/DriveFileRenameOutline';
import GroupsIcon from '@mui/icons-material/Groups';
import RestartAltIcon from '@mui/icons-material/RestartAlt';
import SaveAsIcon from '@mui/icons-material/SaveAs';
import SaveIcon from '@mui/icons-material/Save';
import StarIcon from '@mui/icons-material/Star';
import StarBorderIcon from '@mui/icons-material/StarBorder';
import { api } from '../../api/client';
import type { SavedViewDto } from '../../api/types';
import { usePermissions } from '../../context/usePermissions';
import { CopyViewDialog, RenameViewDialog, SaveViewDialog, type SaveViewChoice } from './SavedViewDialogs';
import type { SavedViews } from './useSavedViews';

const errorText = (e: unknown) => (e instanceof Error ? e.message : String(e));

/**
 * The saved-view menu beside a Collection / Location page's search box: switch views (your own, the
 * shared ones, or the built-in layout), save changes, save as a new view, and manage the active one —
 * your default, everyone's default (administrators), rename, copy to other locations, delete. A dot on
 * the button means the layout on screen has unsaved changes.
 */
export function SavedViewPicker({ sv }: { sv: SavedViews }) {
  const { t } = useTranslation();
  const { isAdmin } = usePermissions();
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const [saveOpen, setSaveOpen] = useState(false);
  const [renameOpen, setRenameOpen] = useState(false);
  const [copyOpen, setCopyOpen] = useState(false);
  const [notice, setNotice] = useState<{ text: string; error?: boolean } | null>(null);
  const onLocation = sv.page === 'Location';
  const active = sv.activeView;
  const mine = sv.views.filter((v) => !v.shared);
  const shared = sv.views.filter((v) => v.shared);
  const close = () => setAnchor(null);
  const fail = (e: unknown) => setNotice({ text: errorText(e), error: true });

  const setDefault = async (view: SavedViewDto, everyone: boolean, on: boolean) => {
    await api.savedViewSetDefault(view.id, sv.page, sv.containerId, sv.game, everyone, on);
    await sv.refresh();
  };

  const saveAs = useMutation({
    mutationFn: async (c: SaveViewChoice) => {
      const view = await api.savedViewCreate({
        name: c.name,
        page: c.allLocations ? 'AllLocations' : sv.page,
        containerId: c.allLocations ? null : sv.containerId,
        game: c.game,
        shared: c.shared,
        state: sv.state,
      });
      if (c.makeDefault) await api.savedViewSetDefault(view.id, sv.page, sv.containerId, sv.game, c.shared, true);
      await sv.refresh();
      return view;
    },
    onSuccess: (view) => {
      sv.markSaved(view);
      setSaveOpen(false);
    },
  });
  const save = useMutation({
    mutationFn: (view: SavedViewDto) => api.savedViewUpdate(view.id, { state: sv.state }),
    onSuccess: async (view) => {
      await sv.refresh();
      sv.markSaved(view);
      setNotice({ text: t('collection.savedViews.saved', { name: view.name }) });
    },
    onError: fail,
  });
  const rename = useMutation({
    mutationFn: ({ view, name }: { view: SavedViewDto; name: string }) => api.savedViewUpdate(view.id, { name }),
    onSuccess: async () => {
      await sv.refresh();
      setRenameOpen(false);
    },
  });
  const remove = useMutation({
    mutationFn: (view: SavedViewDto) => api.savedViewDelete(view.id),
    // Refetch first so the page falls back to the next default, not the deleted view.
    onSuccess: async () => {
      await sv.refresh();
      sv.reloadDefault();
    },
    onError: fail,
  });
  const copy = useMutation({
    mutationFn: ({ ids, setDefault: makeDefault }: { ids: number[]; setDefault: boolean }) =>
      api.savedViewCopy(active!.id, ids, makeDefault),
    onSuccess: (r) => {
      setCopyOpen(false);
      setNotice({ text: t('collection.savedViews.copy.done', { count: r.copied }) });
    },
  });

  const run = (action: () => Promise<unknown> | void) => {
    close();
    Promise.resolve(action()).catch(fail);
  };

  const viewItem = (v: SavedViewDto) => (
    <MenuItem
      key={v.id}
      selected={v.id === active?.id}
      onClick={() => {
        close();
        sv.select(v);
      }}
    >
      <ListItemIcon>{v.id === active?.id && <CheckIcon fontSize="small" />}</ListItemIcon>
      <ListItemText
        primary={v.name}
        secondary={[
          v.game ? null : t('collection.savedViews.anyGame'),
          v.page === 'AllLocations' ? t('collection.savedViews.allLocations') : null,
        ]
          .filter(Boolean)
          .join(' · ') || undefined}
      />
      {v.isMyDefault && (
        <Tooltip title={t('collection.savedViews.myDefault')}>
          <StarIcon fontSize="small" color="warning" sx={{ ml: 1 }} />
        </Tooltip>
      )}
      {v.isEveryoneDefault && (
        <Tooltip title={t('collection.savedViews.everyoneDefault')}>
          <GroupsIcon fontSize="small" color="action" sx={{ ml: 1 }} />
        </Tooltip>
      )}
    </MenuItem>
  );

  const canCopy = onLocation && active?.page === 'Location' && active.canEdit;

  return (
    <>
      <Tooltip title={sv.isModified ? t('collection.savedViews.modified') : t('collection.savedViews.tooltip')}>
        <Badge color="secondary" variant="dot" invisible={!sv.isModified} overlap="rectangular">
          <Button
            variant="outlined"
            startIcon={<BookmarksIcon />}
            endIcon={<ArrowDropDownIcon />}
            onClick={(e) => setAnchor(e.currentTarget)}
            sx={{ maxWidth: 260, whiteSpace: 'nowrap' }}
          >
            <span style={{ overflow: 'hidden', textOverflow: 'ellipsis' }}>
              {active?.name ?? t('collection.savedViews.builtIn')}
            </span>
          </Button>
        </Badge>
      </Tooltip>
      <Menu anchorEl={anchor} open={!!anchor} onClose={close} slotProps={{ paper: { sx: { minWidth: 280 } } }}>
        <MenuItem selected={!active} onClick={() => run(() => sv.select(null))}>
          <ListItemIcon>{!active && <CheckIcon fontSize="small" />}</ListItemIcon>
          <ListItemText primary={t('collection.savedViews.builtIn')} secondary={t('collection.savedViews.builtInHint')} />
        </MenuItem>
        {mine.length > 0 && <ListSubheader>{t('collection.savedViews.myViews')}</ListSubheader>}
        {mine.map(viewItem)}
        {shared.length > 0 && <ListSubheader>{t('collection.savedViews.sharedViews')}</ListSubheader>}
        {shared.map(viewItem)}
        <Divider />
        <MenuItem disabled={!active?.canEdit || !sv.isModified || save.isPending} onClick={() => run(() => save.mutate(active!))}>
          <ListItemIcon>
            <SaveIcon fontSize="small" />
          </ListItemIcon>
          <ListItemText primary={t('collection.savedViews.save.changes')} />
        </MenuItem>
        <MenuItem onClick={() => run(() => setSaveOpen(true))}>
          <ListItemIcon>
            <SaveAsIcon fontSize="small" />
          </ListItemIcon>
          <ListItemText primary={t('collection.savedViews.save.asNew')} />
        </MenuItem>
        <MenuItem disabled={!sv.isModified} onClick={() => run(sv.reset)}>
          <ListItemIcon>
            <RestartAltIcon fontSize="small" />
          </ListItemIcon>
          <ListItemText primary={t('collection.savedViews.discard')} />
        </MenuItem>
        {active && <Divider />}
        {active && (
          <MenuItem onClick={() => run(() => setDefault(active, false, !active.isMyDefault))}>
            <ListItemIcon>
              {active.isMyDefault ? <StarBorderIcon fontSize="small" /> : <StarIcon fontSize="small" />}
            </ListItemIcon>
            <ListItemText
              primary={
                active.isMyDefault ? t('collection.savedViews.clearMyDefault') : t('collection.savedViews.setMyDefault')
              }
            />
          </MenuItem>
        )}
        {active && isAdmin && active.shared && (
          <MenuItem onClick={() => run(() => setDefault(active, true, !active.isEveryoneDefault))}>
            <ListItemIcon>
              <GroupsIcon fontSize="small" />
            </ListItemIcon>
            <ListItemText
              primary={
                active.isEveryoneDefault
                  ? t('collection.savedViews.clearEveryoneDefault')
                  : t('collection.savedViews.setEveryoneDefault')
              }
              secondary={active.page === 'AllLocations' ? t('collection.savedViews.everyLocationHint') : undefined}
            />
          </MenuItem>
        )}
        {active?.canEdit && (
          <MenuItem onClick={() => run(() => setRenameOpen(true))}>
            <ListItemIcon>
              <DriveFileRenameOutlineIcon fontSize="small" />
            </ListItemIcon>
            <ListItemText primary={t('collection.savedViews.rename.menu')} />
          </MenuItem>
        )}
        {canCopy && (
          <MenuItem onClick={() => run(() => setCopyOpen(true))}>
            <ListItemIcon>
              <ContentCopyIcon fontSize="small" />
            </ListItemIcon>
            <ListItemText primary={t('collection.savedViews.copy.menu')} />
          </MenuItem>
        )}
        {active?.canEdit && (
          <MenuItem
            onClick={() =>
              run(() => {
                if (confirm(t('collection.savedViews.confirmDelete', { name: active.name }))) remove.mutate(active);
              })
            }
          >
            <ListItemIcon>
              <DeleteIcon fontSize="small" color="error" />
            </ListItemIcon>
            <ListItemText primary={t('common.actions.delete')} />
          </MenuItem>
        )}
      </Menu>

      <SaveViewDialog
        open={saveOpen}
        onLocation={onLocation}
        gameKey={sv.gameKey}
        isAdmin={isAdmin}
        busy={saveAs.isPending}
        error={saveAs.error ? errorText(saveAs.error) : null}
        onSave={(c) => saveAs.mutate(c)}
        onClose={() => {
          setSaveOpen(false);
          saveAs.reset();
        }}
      />
      <RenameViewDialog
        open={renameOpen}
        currentName={active?.name ?? ''}
        busy={rename.isPending}
        error={rename.error ? errorText(rename.error) : null}
        onRename={(name) => active && rename.mutate({ view: active, name })}
        onClose={() => {
          setRenameOpen(false);
          rename.reset();
        }}
      />
      <CopyViewDialog
        open={copyOpen}
        viewName={active?.name ?? ''}
        shared={!!active?.shared}
        excludeId={sv.containerId}
        busy={copy.isPending}
        error={copy.error ? errorText(copy.error) : null}
        onCopy={(ids, makeDefault) => copy.mutate({ ids, setDefault: makeDefault })}
        onClose={() => {
          setCopyOpen(false);
          copy.reset();
        }}
      />
      <Snackbar
        open={!!notice}
        autoHideDuration={4000}
        onClose={() => setNotice(null)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
      >
        <Alert severity={notice?.error ? 'error' : 'success'} onClose={() => setNotice(null)} variant="filled">
          {notice?.text}
        </Alert>
      </Snackbar>
    </>
  );
}
