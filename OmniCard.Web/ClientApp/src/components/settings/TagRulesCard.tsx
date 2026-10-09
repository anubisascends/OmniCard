import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Autocomplete,
  Box,
  Button,
  Chip,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  FormControlLabel,
  IconButton,
  List,
  ListItem,
  ListItemText,
  MenuItem,
  Paper,
  Stack,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import EditIcon from '@mui/icons-material/Edit';
import DeleteIcon from '@mui/icons-material/DeleteOutline';
import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import { api } from '../../api/client';
import type { TagRuleDto, TagRuleInput, TagRulePreviewDto } from '../../api/types';
import { useFormatters } from '../../i18n/format';
import { SearchBox } from '../SearchBox';

const emptyRule = (game: string): TagRuleInput => ({ name: '', game, query: '', tags: [], enabled: true });

/** `value`, settled for `delay` ms — keeps the live preview from querying on every keystroke. */
function useSettled<T>(value: T, delay = 400): T {
  const [settled, setSettled] = useState(value);
  useEffect(() => {
    const id = setTimeout(() => setSettled(value), delay);
    return () => clearTimeout(id);
  }, [value, delay]);
  return settled;
}

/** Match count + sample of the owned cards a rule would change. */
function PreviewSummary({ preview }: { preview: TagRulePreviewDto }) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  return (
    <Box>
      <Typography variant="body2">
        {t('settings.tagRules.previewCounts', {
          matches: fmt.number(preview.matchCount),
          changes: fmt.number(preview.changeCount),
        })}
      </Typography>
      {preview.sample.length > 0 && (
        <List dense disablePadding sx={{ maxHeight: 220, overflow: 'auto', mt: 0.5 }}>
          {preview.sample.map((c) => (
            <ListItem key={c.id} disableGutters>
              <ListItemText
                primary={`${c.name} · ${c.setCode.toUpperCase()} #${c.collectorNumber}${c.isFoil ? ` · ${t('common.labels.foil')}` : ''}`}
                secondary={t('settings.tagRules.previewCardDetail', {
                  location: c.location ?? t('settings.tagRules.noLocation'),
                  tags: c.missingTags.join(', '),
                })}
              />
            </ListItem>
          ))}
        </List>
      )}
      {preview.changeCount > preview.sample.length && (
        <Typography variant="caption" color="text.secondary">
          {t('settings.tagRules.previewMore', {
            count: preview.changeCount - preview.sample.length,
            formatted: fmt.number(preview.changeCount - preview.sample.length),
          })}
        </Typography>
      )}
    </Box>
  );
}

/** Add/edit a rule, with a live preview against the existing collection. */
function TagRuleDialog({
  open,
  game,
  existing,
  onClose,
  onSaved,
}: {
  open: boolean;
  game: string;
  existing: TagRuleDto | null;
  onClose: () => void;
  onSaved: () => void;
}) {
  const { t } = useTranslation();
  const [form, setForm] = useState<TagRuleInput>(emptyRule(game));
  const tagsQuery = useQuery({ queryKey: ['tags'], queryFn: api.tags, enabled: open });

  // Seed the form when the dialog opens (edit → prefill; add → blank for the current game).
  const [seededFor, setSeededFor] = useState<number | 'new' | null>(null);
  const key = existing?.id ?? 'new';
  if (open && seededFor !== key) {
    setSeededFor(key);
    setForm(
      existing
        ? { name: existing.name, game: existing.game, query: existing.query, tags: existing.tags, enabled: existing.enabled }
        : emptyRule(game),
    );
  }
  if (!open && seededFor !== null) setSeededFor(null);

  const query = useSettled(form.query.trim());
  const tags = useSettled(form.tags);
  const preview = useQuery({
    queryKey: ['tag-rule-preview', form.game, query, tags],
    queryFn: () => api.tagRulePreview(form.game, query, tags),
    enabled: open && query.length > 0,
  });
  const errors = query.length > 0 ? preview.data?.errors ?? [] : [];

  const save = useMutation({
    mutationFn: async () => {
      if (existing) await api.tagRuleUpdate(existing.id, form);
      else await api.tagRuleCreate(form);
    },
    onSuccess: () => {
      onSaved();
      onClose();
    },
  });

  const set = (patch: Partial<TagRuleInput>) => setForm((f) => ({ ...f, ...patch }));
  const canSave =
    form.name.trim().length > 0 && form.query.trim().length > 0 && form.tags.length > 0 && errors.length === 0;

  return (
    <Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth>
      <DialogTitle>{existing ? t('settings.tagRules.editTitle') : t('settings.tagRules.newTitle')}</DialogTitle>
      <DialogContent>
        <Stack spacing={1.5} sx={{ mt: 1 }}>
          <TextField
            label={t('common.labels.name')}
            size="small"
            value={form.name}
            onChange={(e) => set({ name: e.target.value })}
            autoFocus
          />
          <Box>
            <Typography variant="caption" color="text.secondary">
              {t('settings.tagRules.queryLabel')}
            </Typography>
            <SearchBox value={form.query} onChange={(v) => set({ query: v })} game={form.game} />
            <Typography variant="caption" color="text.secondary" display="block" sx={{ mt: 0.5 }}>
              {t('settings.tagRules.queryHelp')}
            </Typography>
          </Box>
          {errors.map((e) => (
            <Alert key={e} severity="error" sx={{ py: 0 }}>
              {e}
            </Alert>
          ))}
          <Autocomplete
            multiple
            freeSolo
            size="small"
            options={(tagsQuery.data ?? []).map((x) => x.name)}
            value={form.tags}
            onChange={(_, v) => set({ tags: v.map((x) => x.trim()).filter((x) => x.length > 0) })}
            renderInput={(p) => (
              <TextField {...p} label={t('settings.tagRules.tagsLabel')} helperText={t('settings.tagRules.tagsHelp')} />
            )}
          />
          <FormControlLabel
            control={<Switch checked={form.enabled} onChange={(e) => set({ enabled: e.target.checked })} />}
            label={t('settings.tagRules.enabledSwitch')}
          />
          <Paper variant="outlined" sx={{ p: 1.5 }}>
            <Typography variant="subtitle2" gutterBottom>
              {t('settings.tagRules.previewTitle')}
            </Typography>
            {query.length === 0 ? (
              <Typography variant="body2" color="text.secondary">
                {t('settings.tagRules.previewEmpty')}
              </Typography>
            ) : preview.isFetching && !preview.data ? (
              <CircularProgress size={20} />
            ) : preview.data && errors.length === 0 ? (
              <PreviewSummary preview={preview.data} />
            ) : null}
          </Paper>
          {save.isError && (
            <Typography variant="caption" color="error">
              {(save.error as Error).message}
            </Typography>
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('common.actions.cancel')}</Button>
        <Button variant="contained" disabled={!canSave || save.isPending} onClick={() => save.mutate()}>
          {t('common.actions.save')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

/** "Run now": preview what the rule would change, then apply it on confirm. */
function RunRuleDialog({ rule, onClose }: { rule: TagRuleDto | null; onClose: () => void }) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const qc = useQueryClient();
  const preview = useQuery({
    queryKey: ['tag-rule-run-preview', rule?.id, rule?.updatedAt],
    queryFn: () => api.tagRulePreview(rule!.game, rule!.query, rule!.tags),
    enabled: rule !== null,
  });
  const run = useMutation({
    mutationFn: () => api.tagRuleRun(rule!.id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['tags'] });
      qc.invalidateQueries({ queryKey: ['tag-rule-preview'] });
    },
  });

  const close = () => {
    run.reset();
    onClose();
  };
  const changeCount = preview.data?.changeCount ?? 0;

  return (
    <Dialog open={rule !== null} onClose={close} maxWidth="sm" fullWidth>
      <DialogTitle>{t('settings.tagRules.runTitle', { name: rule?.name ?? '' })}</DialogTitle>
      <DialogContent>
        {run.isSuccess ? (
          <Alert severity="success">
            {t('settings.tagRules.runDone', { count: run.data.cardsTagged, formatted: fmt.number(run.data.cardsTagged) })}
          </Alert>
        ) : preview.isLoading ? (
          <CircularProgress size={24} />
        ) : preview.data?.errors.length ? (
          preview.data.errors.map((e) => (
            <Alert key={e} severity="error" sx={{ mb: 1 }}>
              {e}
            </Alert>
          ))
        ) : preview.data ? (
          <Stack spacing={1.5}>
            <DialogContentText>
              {changeCount === 0
                ? t('settings.tagRules.runNothing')
                : t('settings.tagRules.runConfirm', {
                    count: changeCount,
                    formatted: fmt.number(changeCount),
                    tags: rule?.tags.join(', ') ?? '',
                  })}
            </DialogContentText>
            <PreviewSummary preview={preview.data} />
          </Stack>
        ) : null}
        {run.isError && (
          <Typography variant="caption" color="error">
            {(run.error as Error).message}
          </Typography>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={close}>{run.isSuccess ? t('common.actions.close') : t('common.actions.cancel')}</Button>
        {!run.isSuccess && (
          <Button
            variant="contained"
            disabled={changeCount === 0 || run.isPending || preview.isFetching}
            onClick={() => run.mutate()}
          >
            {t('settings.tagRules.runAction')}
          </Button>
        )}
      </DialogActions>
    </Dialog>
  );
}

/** Settings tab (admin only): per-game rules that tag cards matching a search as they're scanned or
 * imported, and on demand across the existing collection. */
export function TagRulesCard() {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const games = useQuery({ queryKey: ['games'], queryFn: api.games });
  const [game, setGame] = useState('Mtg');
  const [dialog, setDialog] = useState<{ open: boolean; existing: TagRuleDto | null }>({ open: false, existing: null });
  const [running, setRunning] = useState<TagRuleDto | null>(null);

  const rules = useQuery({ queryKey: ['tag-rules', game], queryFn: () => api.tagRules(game) });
  const refresh = () => qc.invalidateQueries({ queryKey: ['tag-rules'] });
  const remove = useMutation({ mutationFn: (id: number) => api.tagRuleDelete(id), onSuccess: refresh });
  const toggle = useMutation({
    mutationFn: (r: TagRuleDto) =>
      api.tagRuleUpdate(r.id, { name: r.name, game: r.game, query: r.query, tags: r.tags, enabled: !r.enabled }),
    onSuccess: refresh,
  });

  const rows = rules.data ?? [];

  return (
    <Paper variant="outlined" sx={{ p: 2, maxWidth: 960 }}>
      <Typography variant="h6" gutterBottom>
        {t('settings.tagRules.title')}
      </Typography>
      <Typography variant="body2" color="text.secondary" gutterBottom>
        {t('settings.tagRules.description')}
      </Typography>

      <Stack direction="row" spacing={1} alignItems="center" sx={{ mt: 1, mb: 1 }}>
        <TextField
          select
          size="small"
          label={t('common.labels.game')}
          value={game}
          onChange={(e) => setGame(e.target.value)}
          sx={{ minWidth: 200 }}
        >
          {games.data?.map((g) => (
            <MenuItem key={g.id} value={g.id}>
              {g.displayName}
            </MenuItem>
          ))}
        </TextField>
        <Button variant="outlined" startIcon={<AddIcon />} onClick={() => setDialog({ open: true, existing: null })}>
          {t('settings.tagRules.addRule')}
        </Button>
      </Stack>

      {(toggle.isError || remove.isError) && (
        <Alert severity="error" sx={{ mb: 1 }}>
          {((toggle.error ?? remove.error) as Error).message}
        </Alert>
      )}

      <Table size="small">
        <TableHead>
          <TableRow>
            <TableCell>{t('settings.tagRules.colName')}</TableCell>
            <TableCell>{t('settings.tagRules.colQuery')}</TableCell>
            <TableCell>{t('settings.tagRules.colTags')}</TableCell>
            <TableCell>{t('settings.tagRules.colEnabled')}</TableCell>
            <TableCell align="right">{t('settings.tagRules.colActions')}</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {rows.map((r) => (
            <TableRow key={r.id}>
              <TableCell>{r.name}</TableCell>
              <TableCell sx={{ fontFamily: 'monospace', wordBreak: 'break-word' }}>{r.query}</TableCell>
              <TableCell>
                <Stack direction="row" spacing={0.5} flexWrap="wrap" useFlexGap>
                  {r.tags.map((tag) => (
                    <Chip key={tag} label={tag} size="small" />
                  ))}
                </Stack>
              </TableCell>
              <TableCell>
                <Switch
                  size="small"
                  checked={r.enabled}
                  disabled={toggle.isPending}
                  onChange={() => toggle.mutate(r)}
                  inputProps={{ 'aria-label': t('settings.tagRules.colEnabled') }}
                />
              </TableCell>
              <TableCell align="right" sx={{ whiteSpace: 'nowrap' }}>
                <Tooltip title={t('settings.tagRules.runAction')}>
                  <IconButton size="small" onClick={() => setRunning(r)} aria-label={t('settings.tagRules.runAction')}>
                    <PlayArrowIcon fontSize="small" />
                  </IconButton>
                </Tooltip>
                <Tooltip title={t('common.actions.edit')}>
                  <IconButton
                    size="small"
                    onClick={() => setDialog({ open: true, existing: r })}
                    aria-label={t('common.actions.edit')}
                  >
                    <EditIcon fontSize="small" />
                  </IconButton>
                </Tooltip>
                <Tooltip title={t('common.actions.delete')}>
                  <IconButton
                    size="small"
                    aria-label={t('common.actions.delete')}
                    onClick={() => {
                      if (confirm(t('settings.tagRules.confirmDelete', { name: r.name }))) remove.mutate(r.id);
                    }}
                  >
                    <DeleteIcon fontSize="small" />
                  </IconButton>
                </Tooltip>
              </TableCell>
            </TableRow>
          ))}
          {rows.length === 0 && !rules.isLoading && (
            <TableRow>
              <TableCell colSpan={5}>
                <Typography variant="body2" color="text.secondary">
                  {t('settings.tagRules.emptyState')}
                </Typography>
              </TableCell>
            </TableRow>
          )}
        </TableBody>
      </Table>

      <TagRuleDialog
        open={dialog.open}
        game={game}
        existing={dialog.existing}
        onClose={() => setDialog({ open: false, existing: null })}
        onSaved={refresh}
      />
      <RunRuleDialog rule={running} onClose={() => setRunning(null)} />
    </Paper>
  );
}
