import { useEffect, useMemo, useRef, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import {
  Accordion,
  AccordionDetails,
  AccordionSummary,
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControlLabel,
  Grid2 as Grid,
  ListSubheader,
  MenuItem,
  Paper,
  Stack,
  Switch,
  TextField,
  ToggleButton,
  ToggleButtonGroup,
  Typography,
} from '@mui/material';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import TableChartIcon from '@mui/icons-material/TableChart';
import { api } from '../../api/client';
import type {
  DashboardDataDto,
  DashboardFormat,
  DashboardStateDto,
  DashboardWidgetDto,
  GameDto,
} from '../../api/types';
import { SearchBox } from '../SearchBox';
import { DataView, NoteView } from './WidgetBody';
import {
  BREAKDOWNS,
  CARD_SORTS,
  CHARTS,
  FORMATS,
  GROUP_FIELDS,
  GROUP_SORTS,
  HOLDINGS_METRICS,
  SALES_METRICS,
  SEARCH_OUTPUTS,
  SQL_MAX_ROWS,
  SQL_TIMEOUT_SECONDS,
  columnLabel,
  dataKey,
  defaultTitle,
  displaysFor,
} from './widgetModel';

import { SelectField } from '../SelectField';
interface WidgetEditorDialogProps {
  open: boolean;
  widget: DashboardWidgetDto | null;
  isNew: boolean;
  games: GameDto[];
  state: DashboardStateDto;
  /** True when the user may not change this widget's SQL (it came from the default dashboard). */
  sqlLocked: boolean;
  onApply: (widget: DashboardWidgetDto) => void;
  onClose: () => void;
}

/** Edit one widget's data source and display, with a live preview. */
export function WidgetEditorDialog(props: WidgetEditorDialogProps) {
  // Remount per widget so the draft starts from it.
  if (!props.open || !props.widget) return null;
  return <EditorBody key={props.widget.id} {...props} widget={props.widget} />;
}

function EditorBody({
  widget,
  isNew,
  games,
  state,
  sqlLocked,
  onApply,
  onClose,
}: WidgetEditorDialogProps & { widget: DashboardWidgetDto }) {
  const { t } = useTranslation();
  const [draft, setDraft] = useState<DashboardWidgetDto>(widget);
  const set = (patch: Partial<DashboardWidgetDto>) => setDraft((d) => ({ ...d, ...patch }));

  // The preview follows the draft after a pause; a SQL query only runs when asked (Run).
  const [preview, setPreview] = useState<DashboardWidgetDto>(widget);
  useEffect(() => {
    if (draft.type === 'sql') {
      setPreview((p) => ({ ...draft, sql: p.sql, database: p.database }));
      return;
    }
    const timer = window.setTimeout(() => setPreview(draft), 500);
    return () => window.clearTimeout(timer);
  }, [draft]);
  const runSql = () => setPreview(draft);

  const previewQuery = useQuery({
    queryKey: ['dashboard-widget', ...dataKey(preview)],
    queryFn: ({ signal }) => api.dashboardWidgetData(preview, signal),
    enabled: preview.type !== 'note' && !(preview.type === 'sql' && !preview.sql?.trim()),
    staleTime: 30_000,
    retry: false,
  });
  const columns = previewQuery.data?.columns ?? [];

  const displays = displaysFor(draft);
  useEffect(() => {
    // Keep the display valid when the source changes (e.g. totals → top cards).
    if (displays.length && !displays.includes(draft.display ?? null)) set({ display: displays[0] });
  }, [displays, draft.display]);

  const autoTitle = defaultTitle(draft, t, games);

  return (
    <Dialog open onClose={onClose} maxWidth="lg" fullWidth>
      <DialogTitle>{isNew ? t('dashboard.editor.addTitle') : t('dashboard.editor.title')}</DialogTitle>
      <DialogContent dividers>
        <Grid container spacing={3}>
          <Grid size={{ xs: 12, md: 6 }}>
            <Stack spacing={2}>
              <TextField
                label={t('dashboard.editor.widgetTitle')}
                value={draft.title ?? ''}
                onChange={(e) => set({ title: e.target.value || null })}
                placeholder={t('dashboard.editor.titlePlaceholder', { title: autoTitle })}
                size="small"
                fullWidth
                slotProps={{ inputLabel: { shrink: true } }}
              />
              <Typography variant="subtitle2" color="text.secondary">
                {t('dashboard.editor.source')} · {t(`dashboard.types.${draft.type}`)}
              </Typography>
              <SourceFields draft={draft} set={set} games={games} state={state} sqlLocked={sqlLocked} onRunSql={runSql} />
              {draft.type !== 'note' && (
                <DisplayFields draft={draft} set={set} displays={displays} columns={columns} />
              )}
            </Stack>
          </Grid>
          <Grid size={{ xs: 12, md: 6 }}>
            <Typography variant="subtitle2" color="text.secondary" gutterBottom>
              {t('dashboard.editor.preview')}
            </Typography>
            <Preview widget={draft} data={previewQuery.data} loading={previewQuery.isFetching} error={previewQuery.error} title={draft.title || autoTitle} />
          </Grid>
        </Grid>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('common.actions.cancel')}</Button>
        <Button variant="contained" onClick={() => onApply(draft)}>
          {isNew ? t('common.actions.add') : t('dashboard.editor.apply')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

type Setter = (patch: Partial<DashboardWidgetDto>) => void;

function SourceFields({
  draft,
  set,
  games,
  state,
  sqlLocked,
  onRunSql,
}: {
  draft: DashboardWidgetDto;
  set: Setter;
  games: GameDto[];
  state: DashboardStateDto;
  sqlLocked: boolean;
  onRunSql: () => void;
}) {
  const { t } = useTranslation();
  switch (draft.type) {
    case 'metric':
      return (
        <>
          <SelectField
            label={t('dashboard.editor.metric')}
            value={draft.metric ?? 'totalMarket'}
            onChange={(e) => set({ metric: e.target.value })}
            size="small"
            fullWidth
          >
            <ListSubheader>{t('dashboard.metrics.holdings')}</ListSubheader>
            {HOLDINGS_METRICS.map((m) => (
              <MenuItem key={m} value={m}>
                {t(`dashboard.metrics.${m}`)}
              </MenuItem>
            ))}
            <ListSubheader>{t('dashboard.metrics.sales')}</ListSubheader>
            {SALES_METRICS.map((m) => (
              <MenuItem key={m} value={m}>
                {t(`dashboard.metrics.${m}`)}
              </MenuItem>
            ))}
          </SelectField>
          <FormControlLabel
            control={<Switch checked={!!draft.colorBySign} onChange={(e) => set({ colorBySign: e.target.checked })} />}
            label={t('dashboard.editor.colorBySign')}
          />
        </>
      );
    case 'breakdown':
      return (
        <SelectField
          label={t('dashboard.editor.breakdown')}
          value={draft.groupBy ?? 'game'}
          onChange={(e) => set({ groupBy: e.target.value, valueColumns: null, labelColumn: null })}
          size="small"
          fullWidth
        >
          {BREAKDOWNS.map((b) => (
            <MenuItem key={b} value={b}>
              {t(`dashboard.breakdowns.${b}`)}
            </MenuItem>
          ))}
        </SelectField>
      );
    case 'search':
      return <SearchFields draft={draft} set={set} games={games} />;
    case 'sql':
      return <SqlFields draft={draft} set={set} state={state} locked={sqlLocked} onRun={onRunSql} />;
    case 'note':
      return (
        <TextField
          label={t('dashboard.editor.text')}
          value={draft.text ?? ''}
          onChange={(e) => set({ text: e.target.value })}
          helperText={t('dashboard.editor.textHint')}
          multiline
          minRows={6}
          fullWidth
        />
      );
    default:
      return null;
  }
}

function SearchFields({ draft, set, games }: { draft: DashboardWidgetDto; set: Setter; games: GameDto[] }) {
  const { t } = useTranslation();
  const [query, setQuery] = useState(draft.query ?? '');
  useEffect(() => {
    const timer = window.setTimeout(() => set({ query: query || null }), 300);
    return () => window.clearTimeout(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [query]);
  const output = draft.output ?? 'summary';
  return (
    <>
      <SelectField
        label={t('dashboard.editor.game')}
        value={draft.game ?? ''}
        onChange={(e) => set({ game: e.target.value || null })}
        helperText={t('dashboard.editor.gameHint')}
        size="small"
        fullWidth
        slotProps={{ inputLabel: { shrink: true }, select: { displayEmpty: true } }}
      >
        <MenuItem value="">{t('dashboard.editor.allGames')}</MenuItem>
        {games.map((g) => (
          <MenuItem key={g.id} value={g.id}>
            {g.displayName}
          </MenuItem>
        ))}
      </SelectField>
      <Box>
        <SearchBox value={query} onChange={setQuery} game={draft.game ?? ''} />
        <Typography variant="caption" color="text.secondary">
          {t('dashboard.editor.queryHint')}
        </Typography>
      </Box>
      <SelectField
        label={t('dashboard.editor.output')}
        value={output}
        onChange={(e) =>
          set({
            output: e.target.value as DashboardWidgetDto['output'],
            sort: null,
            valueColumns: null,
            labelColumn: null,
            statColumn: null,
          })
        }
        size="small"
        fullWidth
      >
        {SEARCH_OUTPUTS.map((o) => (
          <MenuItem key={o} value={o}>
            {t(`dashboard.outputs.${o}`)}
          </MenuItem>
        ))}
      </SelectField>
      {output === 'groups' && (
        <SelectField
          label={t('dashboard.editor.groupBy')}
          value={draft.groupBy ?? 'setCode'}
          onChange={(e) => set({ groupBy: e.target.value })}
          size="small"
          fullWidth
        >
          {GROUP_FIELDS.map((f) => (
            <MenuItem key={f} value={f}>
              {t(`dashboard.groupFields.${f}`)}
            </MenuItem>
          ))}
        </SelectField>
      )}
      {output !== 'summary' && (
        <Stack direction="row" spacing={2}>
          <SelectField
            label={t('dashboard.editor.sort')}
            value={draft.sort ?? 'value'}
            onChange={(e) => set({ sort: e.target.value })}
            size="small"
            sx={{ flex: 1 }}
          >
            {(output === 'cards' ? CARD_SORTS : GROUP_SORTS).map((s) => (
              <MenuItem key={s} value={s}>
                {t(`dashboard.sorts.${s}`)}
              </MenuItem>
            ))}
          </SelectField>
          <TextField
            type="number"
            label={t('dashboard.editor.limit')}
            value={draft.limit ?? 10}
            onChange={(e) => set({ limit: Math.max(1, Math.min(100, Number(e.target.value) || 1)) })}
            size="small"
            sx={{ width: 110 }}
            slotProps={{ htmlInput: { min: 1, max: 100 } }}
          />
        </Stack>
      )}
    </>
  );
}

function SqlFields({
  draft,
  set,
  state,
  locked,
  onRun,
}: {
  draft: DashboardWidgetDto;
  set: Setter;
  state: DashboardStateDto;
  locked: boolean;
  onRun: () => void;
}) {
  const { t } = useTranslation();
  const inputRef = useRef<HTMLTextAreaElement | null>(null);
  const [showSchema, setShowSchema] = useState(false);
  const database = draft.database ?? 'main';
  const schema = useQuery({
    queryKey: ['dashboard-sql-schema', database],
    queryFn: () => api.dashboardSqlSchema(database),
    enabled: showSchema && state.canWriteSql,
    staleTime: 5 * 60_000,
  });

  const insert = (text: string) => {
    const el = inputRef.current;
    const sql = draft.sql ?? '';
    const start = el?.selectionStart ?? sql.length;
    const end = el?.selectionEnd ?? sql.length;
    set({ sql: sql.slice(0, start) + text + sql.slice(end) });
    requestAnimationFrame(() => {
      el?.focus();
      el?.setSelectionRange(start + text.length, start + text.length);
    });
  };

  return (
    <>
      {locked && <Alert severity="info">{t('dashboard.editor.sqlReadOnly')}</Alert>}
      <SelectField
        label={t('dashboard.editor.database')}
        value={database}
        onChange={(e) => set({ database: e.target.value })}
        size="small"
        fullWidth
        disabled={locked}
      >
        {state.sqlDatabases.map((d) => (
          <MenuItem key={d.key} value={d.key}>
            {d.label}
          </MenuItem>
        ))}
      </SelectField>
      <TextField
        label={t('dashboard.editor.sql')}
        value={draft.sql ?? ''}
        onChange={(e) => set({ sql: e.target.value })}
        onKeyDown={(e) => {
          if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
            e.preventDefault();
            onRun();
          }
        }}
        inputRef={inputRef}
        helperText={t('dashboard.editor.sqlHint', { rows: SQL_MAX_ROWS, seconds: SQL_TIMEOUT_SECONDS })}
        multiline
        minRows={8}
        maxRows={20}
        fullWidth
        slotProps={{
          input: { readOnly: locked, sx: { fontFamily: 'ui-monospace, SFMono-Regular, Consolas, monospace', fontSize: 13 } },
          htmlInput: { spellCheck: false },
        }}
      />
      <Stack direction="row" spacing={1}>
        <Button variant="outlined" startIcon={<PlayArrowIcon />} onClick={onRun}>
          {t('dashboard.editor.run')}
        </Button>
        {state.canWriteSql && !locked && (
          <Button startIcon={<TableChartIcon />} onClick={() => setShowSchema((s) => !s)}>
            {t('dashboard.editor.schema')}
          </Button>
        )}
      </Stack>
      {showSchema && !locked && (
        <Paper variant="outlined" sx={{ maxHeight: 260, overflow: 'auto', p: 1 }}>
          <Typography variant="caption" color="text.secondary">
            {t('dashboard.editor.schemaHint')}
          </Typography>
          {schema.isLoading && <CircularProgress size={20} sx={{ display: 'block', m: 1 }} />}
          {schema.error && <Alert severity="error">{(schema.error as Error).message}</Alert>}
          {schema.data?.tables.map((table) => (
            <Accordion key={table.name} disableGutters elevation={0} sx={{ '&:before': { display: 'none' } }}>
              <AccordionSummary expandIcon={<ExpandMoreIcon />} sx={{ minHeight: 32, '& .MuiAccordionSummary-content': { my: 0.5 } }}>
                <Typography
                  variant="body2"
                  component="span"
                  sx={{ fontFamily: 'monospace', cursor: 'copy', '&:hover': { textDecoration: 'underline' } }}
                  onClick={(e) => {
                    e.stopPropagation();
                    insert(table.name);
                  }}
                >
                  {table.name}
                </Typography>
              </AccordionSummary>
              <AccordionDetails sx={{ pt: 0, display: 'flex', flexWrap: 'wrap', gap: 0.5 }}>
                {table.columns.map((c) => (
                  <Chip
                    key={c.name}
                    size="small"
                    variant="outlined"
                    label={`${c.name} · ${c.type}`}
                    onClick={() => insert(c.name)}
                    sx={{ fontFamily: 'monospace' }}
                  />
                ))}
              </AccordionDetails>
            </Accordion>
          ))}
        </Paper>
      )}
    </>
  );
}

function DisplayFields({
  draft,
  set,
  displays,
  columns,
}: {
  draft: DashboardWidgetDto;
  set: Setter;
  displays: readonly DashboardWidgetDto['display'][];
  columns: DashboardDataDto['columns'];
}) {
  const { t } = useTranslation();
  const display = draft.display ?? displays[0];
  const label = (key: string) => columnLabel(key, t);
  return (
    <>
      <Typography variant="subtitle2" color="text.secondary">
        {t('dashboard.editor.display')}
      </Typography>
      {displays.length > 1 && (
        <ToggleButtonGroup
          exclusive
          size="small"
          value={display}
          onChange={(_, v) => v && set({ display: v, valueColumns: null })}
        >
          {displays.map((d) => (
            <ToggleButton key={d} value={d!}>
              {t(`dashboard.displays.${d}`)}
            </ToggleButton>
          ))}
        </ToggleButtonGroup>
      )}
      {display === 'chart' && (
        <SelectField
          label={t('dashboard.editor.chart')}
          value={draft.chart ?? 'bar'}
          onChange={(e) => set({ chart: e.target.value as DashboardWidgetDto['chart'] })}
          size="small"
          fullWidth
        >
          {CHARTS.map((c) => (
            <MenuItem key={c} value={c}>
              {t(`dashboard.charts.${c}`)}
            </MenuItem>
          ))}
        </SelectField>
      )}
      {columns.length > 0 && display === 'stat' && (
        <SelectField
          label={t('dashboard.editor.statColumn')}
          value={draft.statColumn ?? ''}
          onChange={(e) => set({ statColumn: e.target.value || null })}
          size="small"
          fullWidth
          slotProps={{ inputLabel: { shrink: true }, select: { displayEmpty: true } }}
        >
          <MenuItem value="">{t('dashboard.formats.auto')}</MenuItem>
          {columns.map((c) => (
            <MenuItem key={c.key} value={c.key}>
              {label(c.key)}
            </MenuItem>
          ))}
        </SelectField>
      )}
      {columns.length > 0 && display === 'chart' && (
        <SelectField
          label={t('dashboard.editor.labelColumn')}
          value={draft.labelColumn ?? ''}
          onChange={(e) => set({ labelColumn: e.target.value || null })}
          size="small"
          fullWidth
          slotProps={{ inputLabel: { shrink: true }, select: { displayEmpty: true } }}
        >
          <MenuItem value="">{t('dashboard.formats.auto')}</MenuItem>
          {columns.map((c) => (
            <MenuItem key={c.key} value={c.key}>
              {label(c.key)}
            </MenuItem>
          ))}
        </SelectField>
      )}
      {columns.length > 0 && display !== 'stat' && (
        <SelectField
          label={display === 'chart' ? t('dashboard.editor.valueColumns') : t('dashboard.editor.tableColumns')}
          value={draft.valueColumns ?? []}
          onChange={(e) => {
            const v = e.target.value as unknown as string[];
            set({ valueColumns: v.length ? v : null });
          }}
          size="small"
          fullWidth
          slotProps={{
            inputLabel: { shrink: true },
            select: {
              multiple: true,
              displayEmpty: true,
              renderValue: (selected) => {
                const keys = selected as string[];
                if (!keys.length) return t(display === 'chart' ? 'dashboard.formats.auto' : 'dashboard.editor.allColumns');
                return (
                  <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.5 }}>
                    {keys.map((k) => (
                      <Chip key={k} size="small" label={label(k)} />
                    ))}
                  </Box>
                );
              },
            },
          }}
        >
          {columns.map((c) => (
            <MenuItem key={c.key} value={c.key}>
              {label(c.key)}
            </MenuItem>
          ))}
        </SelectField>
      )}
      {display === 'stat' && draft.type !== 'metric' && (
        <FormControlLabel
          control={<Switch checked={!!draft.colorBySign} onChange={(e) => set({ colorBySign: e.target.checked })} />}
          label={t('dashboard.editor.colorBySign')}
        />
      )}
      {columns.length > 0 && (
        <Accordion disableGutters variant="outlined">
          <AccordionSummary expandIcon={<ExpandMoreIcon />}>
            <Typography variant="body2">{t('dashboard.editor.formats')}</Typography>
          </AccordionSummary>
          <AccordionDetails>
            <Stack spacing={1}>
              {columns.map((c) => (
                <SelectField
                  key={c.key}
                  label={label(c.key)}
                  value={draft.formats?.[c.key] ?? ''}
                  onChange={(e) => {
                    const next = { ...(draft.formats ?? {}) };
                    if (e.target.value) next[c.key] = e.target.value as DashboardFormat;
                    else delete next[c.key];
                    set({ formats: Object.keys(next).length ? next : null });
                  }}
                  size="small"
                  slotProps={{ inputLabel: { shrink: true }, select: { displayEmpty: true } }}
                >
                  <MenuItem value="">{t('dashboard.formats.auto')}</MenuItem>
                  {FORMATS.map((f) => (
                    <MenuItem key={f} value={f}>
                      {t(`dashboard.formats.${f}`)}
                    </MenuItem>
                  ))}
                </SelectField>
              ))}
            </Stack>
          </AccordionDetails>
        </Accordion>
      )}
    </>
  );
}

function Preview({
  widget,
  data,
  loading,
  error,
  title,
}: {
  widget: DashboardWidgetDto;
  data: DashboardDataDto | undefined;
  loading: boolean;
  error: unknown;
  title: string;
}) {
  const { t } = useTranslation();
  const isStat = widget.display === 'stat' && widget.type !== 'note';
  const body = useMemo(() => {
    if (widget.type === 'note') return <NoteView text={widget.text ?? ''} />;
    if (error) return <Alert severity="error">{t('dashboard.widget.error', { message: (error as Error).message })}</Alert>;
    if (!data)
      return loading ? (
        <CircularProgress size={24} />
      ) : (
        <Typography variant="body2" color="text.secondary">
          {t('dashboard.editor.previewHint')}
        </Typography>
      );
    return <DataView widget={widget} data={data} title={title} />;
  }, [widget, data, loading, error, title, t]);
  return (
    <Paper
      variant="outlined"
      sx={{
        p: 2,
        height: isStat ? 120 : 360,
        display: 'flex',
        flexDirection: 'column',
        overflow: 'auto',
        opacity: loading && data ? 0.6 : 1,
        transition: 'opacity 120ms',
      }}
    >
      {!isStat && (
        <Typography variant="h6" noWrap gutterBottom>
          {title}
        </Typography>
      )}
      {body}
    </Paper>
  );
}
