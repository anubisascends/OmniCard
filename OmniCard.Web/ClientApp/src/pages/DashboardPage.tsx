import { useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import GridLayout, { WidthProvider, type Layout } from 'react-grid-layout';
import 'react-grid-layout/css/styles.css';
import 'react-resizable/css/styles.css';
import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  IconButton,
  Paper,
  Snackbar,
  Stack,
  ToggleButton,
  ToggleButtonGroup,
  Tooltip,
  Typography,
  useMediaQuery,
  useTheme,
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import RefreshIcon from '@mui/icons-material/Refresh';
import TuneIcon from '@mui/icons-material/Tune';
import RestartAltIcon from '@mui/icons-material/RestartAlt';
import { api } from '../api/client';
import type { DashboardLayoutDto, DashboardWidgetDto } from '../api/types';
import { WidgetCard, DRAG_HANDLE_CLASS } from '../components/dashboard/WidgetCard';
import { WidgetEditorDialog } from '../components/dashboard/WidgetEditorDialog';
import { AddWidgetDialog } from '../components/dashboard/AddWidgetDialog';
import {
  DASHBOARD_COLUMNS,
  GRID_GAP,
  ROW_HEIGHT,
  bottomRow,
  newWidgetId,
  widgetTitle,
  type WidgetPreset,
} from '../components/dashboard/widgetModel';

const Grid = WidthProvider(GridLayout);

type EditTarget = 'mine' | 'everyone';

/**
 * The Dashboard: a grid of widgets — the user's own layout, else everyone's default, else the built-in
 * one (the classic totals and tables). Customize edits a working copy: add, move, resize, edit,
 * duplicate and remove widgets, then Save. Administrators can edit everyone's default instead.
 * Widgets never follow the game picked in the top bar.
 */
export function DashboardPage() {
  const { t } = useTranslation();
  const theme = useTheme();
  const phone = useMediaQuery(theme.breakpoints.down('sm'));
  const queryClient = useQueryClient();
  const stateQuery = useQuery({ queryKey: ['dashboard-state'], queryFn: api.dashboardState });
  const games = useQuery({ queryKey: ['games'], queryFn: () => api.games() }).data ?? [];

  const [target, setTarget] = useState<EditTarget | null>(null);
  const [draft, setDraft] = useState<DashboardLayoutDto | null>(null);
  const [dirty, setDirty] = useState(false);
  const [editor, setEditor] = useState<{ widget: DashboardWidgetDto; isNew: boolean } | null>(null);
  const [addOpen, setAddOpen] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const state = stateQuery.data;
  const editing = target !== null && draft !== null;
  const layout = editing ? draft : state?.layout;

  const startEditing = (next: EditTarget) => {
    if (!state) return;
    setDraft(structuredClone(next === 'everyone' ? state.defaultLayout : state.layout));
    setTarget(next);
    setDirty(false);
    setError(null);
  };
  const stopEditing = () => {
    setTarget(null);
    setDraft(null);
    setDirty(false);
    setError(null);
  };
  const confirmDiscard = () => !dirty || window.confirm(t('dashboard.confirm.discard'));

  const update = (fn: (widgets: DashboardWidgetDto[]) => DashboardWidgetDto[]) => {
    setDraft((d) => (d ? { ...d, widgets: fn(d.widgets) } : d));
    setDirty(true);
  };

  const onError = (e: Error) => setError(e.message);
  const afterChange = async (message: string) => {
    await queryClient.invalidateQueries({ queryKey: ['dashboard-state'] });
    queryClient.invalidateQueries({ queryKey: ['dashboard-widget'] });
    stopEditing();
    setNotice(message);
  };
  const save = useMutation({
    mutationFn: (l: DashboardLayoutDto) =>
      target === 'everyone' ? api.saveDefaultDashboardLayout(l) : api.saveDashboardLayout(l),
    onSuccess: () => afterChange(t('dashboard.toolbar.saved')),
    onError,
  });
  const reset = useMutation({
    mutationFn: () => (target === 'everyone' ? api.resetDefaultDashboardLayout() : api.resetDashboardLayout()),
    onSuccess: () => afterChange(t('dashboard.toolbar.saved')),
    onError,
  });

  // Positions come back from the grid after every drag/resize (and its own compaction).
  const onLayoutChange = (items: Layout[]) => {
    if (!editing) return;
    setDraft((d) => {
      if (!d) return d;
      const byId = new Map(items.map((i) => [i.i, i]));
      let changed = false;
      const widgets = d.widgets.map((w) => {
        const p = byId.get(w.id);
        if (!p || (p.x === w.x && p.y === w.y && p.w === w.w && p.h === w.h)) return w;
        changed = true;
        return { ...w, x: p.x, y: p.y, w: p.w, h: p.h };
      });
      return changed ? { ...d, widgets } : d;
    });
  };

  const addPreset = (preset: WidgetPreset) => {
    setAddOpen(false);
    if (!draft) return;
    const named = preset.section === 'sql' && preset.key !== 'sql';
    setEditor({
      isNew: true,
      widget: {
        ...preset.widget,
        id: newWidgetId(),
        x: 0,
        y: bottomRow(draft),
        title: named ? t(`dashboard.add.presets.${preset.key}.name`) : null,
      },
    });
  };
  const applyEditor = (widget: DashboardWidgetDto) => {
    const isNew = editor?.isNew;
    setEditor(null);
    update((ws) => (isNew ? [...ws, widget] : ws.map((w) => (w.id === widget.id ? widget : w))));
  };
  const duplicate = (w: DashboardWidgetDto) =>
    update((ws) => [...ws, { ...structuredClone(w), id: newWidgetId(), y: bottomRow({ version: 1, widgets: ws }) }]);
  const remove = (w: DashboardWidgetDto) => {
    if (window.confirm(t('dashboard.confirm.remove'))) update((ws) => ws.filter((x) => x.id !== w.id));
  };

  const gridLayout = useMemo<Layout[]>(
    () =>
      (layout?.widgets ?? []).map((w) => ({
        i: w.id,
        x: w.x,
        y: w.y,
        w: w.w,
        h: w.h,
        minW: 4,
        minH: 2,
      })),
    [layout],
  );

  if (stateQuery.isLoading) return <CircularProgress />;
  if (stateQuery.error || !state || !layout)
    return <Alert severity="error">{(stateQuery.error as Error | null)?.message ?? t('common.states.error')}</Alert>;

  const sourceLabel =
    state.source === 'personal'
      ? t('dashboard.toolbar.sourcePersonal')
      : state.source === 'default'
        ? t('dashboard.toolbar.sourceDefault')
        : t('dashboard.toolbar.sourceBuiltIn');

  const card = (w: DashboardWidgetDto) => (
    <WidgetCard
      widget={w}
      title={widgetTitle(w, t, games)}
      editing={editing}
      draggable={!phone}
      onEdit={() => setEditor({ widget: w, isNew: false })}
      onDuplicate={() => duplicate(w)}
      onRemove={() => remove(w)}
    />
  );

  return (
    <Stack spacing={{ xs: 2, sm: 3 }}>
      <Stack direction="row" alignItems="center" spacing={1} flexWrap="wrap" useFlexGap>
        <Typography variant="h4">{t('dashboard.title')}</Typography>
        {!editing && (state.source === 'personal' || state.canEditDefault) && (
          <Chip size="small" variant="outlined" label={sourceLabel} />
        )}
        <Box sx={{ flex: 1 }} />
        {!editing ? (
          <>
            <Tooltip title={t('common.actions.refresh')}>
              <IconButton onClick={() => queryClient.invalidateQueries({ queryKey: ['dashboard-widget'] })} aria-label={t('common.actions.refresh')}>
                <RefreshIcon />
              </IconButton>
            </Tooltip>
            <Button startIcon={<TuneIcon />} variant="outlined" onClick={() => startEditing('mine')}>
              {t('dashboard.toolbar.customize')}
            </Button>
          </>
        ) : (
          <>
            {state.canEditDefault && (
              <ToggleButtonGroup
                exclusive
                size="small"
                value={target}
                onChange={(_, v: EditTarget | null) => v && v !== target && confirmDiscard() && startEditing(v)}
                aria-label={t('dashboard.toolbar.editing')}
              >
                <ToggleButton value="mine">{t('dashboard.toolbar.mine')}</ToggleButton>
                <ToggleButton value="everyone">{t('dashboard.toolbar.everyone')}</ToggleButton>
              </ToggleButtonGroup>
            )}
            <Button startIcon={<AddIcon />} onClick={() => setAddOpen(true)}>
              {t('dashboard.toolbar.addWidget')}
            </Button>
            {(target === 'mine' ? state.source === 'personal' : state.defaultSource === 'default') && (
              <Button
                startIcon={<RestartAltIcon />}
                color="inherit"
                disabled={reset.isPending}
                onClick={() =>
                  window.confirm(t(target === 'mine' ? 'dashboard.confirm.useDefault' : 'dashboard.confirm.restoreBuiltIn')) &&
                  reset.mutate()
                }
              >
                {target === 'mine' ? t('dashboard.toolbar.useDefault') : t('dashboard.toolbar.restoreBuiltIn')}
              </Button>
            )}
            <Button color="inherit" onClick={() => confirmDiscard() && stopEditing()}>
              {t('common.actions.cancel')}
            </Button>
            <Button variant="contained" disabled={save.isPending} onClick={() => draft && save.mutate(draft)}>
              {t('common.actions.save')}
            </Button>
          </>
        )}
      </Stack>

      {editing && target === 'everyone' && <Alert severity="info">{t('dashboard.toolbar.editingDefaultHint')}</Alert>}
      {editing && phone && <Alert severity="info">{t('dashboard.toolbar.phoneArrangeHint')}</Alert>}
      {error && (
        <Alert severity="error" onClose={() => setError(null)}>
          {error}
        </Alert>
      )}

      {layout.widgets.length === 0 ? (
        <Paper variant="outlined" sx={{ p: 4, textAlign: 'center' }}>
          <Typography variant="h6">{t('dashboard.empty.title')}</Typography>
          <Typography color="text.secondary">{t('dashboard.empty.body')}</Typography>
        </Paper>
      ) : phone ? (
        <PhoneGrid widgets={layout.widgets} render={card} />
      ) : (
        <Box
          sx={{
            mx: `-${GRID_GAP / 2}px`,
            '& .react-grid-item.react-grid-placeholder': {
              bgcolor: 'primary.main',
              opacity: 0.2,
              borderRadius: 1,
            },
            '& .react-grid-item > .react-resizable-handle::after': {
              borderColor: theme.palette.text.secondary,
            },
          }}
        >
          <Grid
            layout={gridLayout}
            cols={DASHBOARD_COLUMNS}
            rowHeight={ROW_HEIGHT}
            margin={[GRID_GAP, GRID_GAP]}
            containerPadding={[GRID_GAP / 2, 0]}
            isDraggable={editing}
            isResizable={editing}
            draggableHandle={`.${DRAG_HANDLE_CLASS}`}
            compactType="vertical"
            onLayoutChange={onLayoutChange}
            onDragStop={() => setDirty(true)}
            onResizeStop={() => setDirty(true)}
            useCSSTransforms
          >
            {layout.widgets.map((w) => (
              <div key={w.id}>{card(w)}</div>
            ))}
          </Grid>
        </Box>
      )}

      <AddWidgetDialog
        open={addOpen}
        canWriteSql={state.canWriteSql}
        canSearchCollection={state.canSearchCollection}
        onPick={addPreset}
        onClose={() => setAddOpen(false)}
      />
      <WidgetEditorDialog
        open={!!editor}
        widget={editor?.widget ?? null}
        isNew={!!editor?.isNew}
        games={games}
        state={state}
        sqlLocked={editor?.widget.type === 'sql' && !state.canWriteSql}
        onApply={applyEditor}
        onClose={() => setEditor(null)}
      />
      <Snackbar open={!!notice} autoHideDuration={3000} onClose={() => setNotice(null)} message={notice} />
    </Stack>
  );
}

/** Phones: widgets in reading order (top to bottom, left to right), two columns; small stats share a
 * row the way the classic totals did, everything else takes the full width. */
function PhoneGrid({ widgets, render }: { widgets: DashboardWidgetDto[]; render: (w: DashboardWidgetDto) => React.ReactNode }) {
  const ordered = [...widgets].sort((a, b) => a.y - b.y || a.x - b.x);
  return (
    <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 2 }}>
      {ordered.map((w) => {
        const small = w.display === 'stat' && w.type !== 'note' && w.w <= DASHBOARD_COLUMNS / 2;
        return (
          <Box
            key={w.id}
            sx={{
              gridColumn: small ? 'span 1' : 'span 2',
              height: small ? undefined : Math.min(420, w.h * ROW_HEIGHT + (w.h - 1) * GRID_GAP),
              minHeight: small ? 88 : undefined,
              minWidth: 0,
            }}
          >
            {render(w)}
          </Box>
        );
      })}
    </Box>
  );
}
