import { useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import {
  Alert,
  Box,
  CircularProgress,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Typography,
  useTheme,
} from '@mui/material';
import { BarChart, LineChart, PieChart } from '@mui/x-charts';
import { api } from '../../api/client';
import type { DashboardDataDto, DashboardFormat, DashboardWidgetDto } from '../../api/types';
import { useFormatters } from '../../i18n/format';
import { HelpMarkdown, parseBlocks } from '../../help/HelpMarkdown';
import {
  chartColumns,
  columnLabel,
  dataKey,
  formatOf,
  isNumericColumn,
  labelColumn,
  statColumn,
  tableColumns,
} from './widgetModel';

// Categorical series colors, used in this fixed order (never cycled): a CVD-validated palette with
// its own steps for each surface.
const SERIES_LIGHT = ['#2a78d6', '#eb6834', '#1baf7a', '#eda100', '#e87ba4', '#008300', '#6250d6', '#e34948'];
const SERIES_DARK = ['#3987e5', '#d95926', '#199e70', '#c98500', '#d55181', '#008300', '#9085e9', '#e66767'];
const MAX_PIE_SLICES = 8;
const MAX_TICK_LABEL = 24;

const shorten = (label: string) => (label.length > MAX_TICK_LABEL ? label.slice(0, MAX_TICK_LABEL - 1) + '…' : label);

type Cell = string | number | boolean | null;

/** Formats one cell for display. */
function useCellFormatter() {
  const { t } = useTranslation();
  const fmt = useFormatters();
  return (value: Cell, format: DashboardFormat | 'bool'): string => {
    if (value == null) return '';
    switch (format) {
      case 'money':
        return typeof value === 'number' ? fmt.money(value) : String(value);
      case 'number':
        return typeof value === 'number' ? fmt.number(value) : String(value);
      case 'percent':
        return typeof value === 'number' ? fmt.percent(value) : String(value);
      case 'date':
        return typeof value === 'string' || typeof value === 'number' ? fmt.date(value) || String(value) : String(value);
      case 'bool':
        return value === true || value === 1 ? t('common.actions.yes') : t('common.actions.no');
      default:
        return String(value);
    }
  };
}

/** Fetches a widget's data and draws it the way the widget says. Stat widgets draw `title` inside the
 * tile, the way the classic Dashboard did. */
export function WidgetBody({ widget, title }: { widget: DashboardWidgetDto; title: string }) {
  const { t } = useTranslation();
  const isNote = widget.type === 'note';
  const query = useQuery({
    queryKey: ['dashboard-widget', ...dataKey(widget)],
    queryFn: ({ signal }) => api.dashboardWidgetData(widget, signal),
    enabled: !isNote,
    staleTime: 30_000,
    retry: false,
  });

  if (isNote) return <NoteView text={widget.text ?? ''} />;
  if (query.isLoading)
    return (
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'center', flex: 1, minHeight: 40 }}>
        <CircularProgress size={24} />
      </Box>
    );
  if (query.error)
    return (
      <Alert severity="error" sx={{ overflow: 'auto' }}>
        {t('dashboard.widget.error', { message: (query.error as Error).message })}
      </Alert>
    );
  const data = query.data;
  if (!data) return null;
  return <DataView widget={widget} data={data} title={title} />;
}

export function DataView({ widget, data, title }: { widget: DashboardWidgetDto; data: DashboardDataDto; title: string }) {
  const { t } = useTranslation();
  if (widget.display !== 'stat' && data.rows.length === 0)
    return (
      <Typography color="text.secondary" variant="body2">
        {t('dashboard.widget.noData')}
      </Typography>
    );
  if (widget.display === 'stat') return <StatView widget={widget} data={data} title={title} />;
  return (
    <>
      <Box sx={widget.display === 'chart' ? { flex: 1, minHeight: 120 } : { flexShrink: 0 }}>
        {widget.display === 'chart' ? <ChartView widget={widget} data={data} /> : <TableView widget={widget} data={data} />}
      </Box>
      {data.truncated && (
        <Typography variant="caption" color="text.secondary" sx={{ mt: 0.5 }}>
          {t('dashboard.widget.truncated', { count: data.rows.length })}
        </Typography>
      )}
    </>
  );
}

function StatView({ widget, data, title }: { widget: DashboardWidgetDto; data: DashboardDataDto; title: string }) {
  const format = useCellFormatter();
  const col = statColumn(widget, data);
  const column = data.columns[col];
  const value = data.rows[0]?.[col] ?? null;
  const color =
    widget.colorBySign && typeof value === 'number' ? (value >= 0 ? 'success.main' : 'error.main') : undefined;
  return (
    <>
      <Typography variant="overline" color="text.secondary" noWrap title={title} sx={{ display: 'block' }}>
        {title}
      </Typography>
      <Typography variant="h5" sx={{ color }} noWrap>
        {column ? format(value, formatOf(widget, column)) || '—' : '—'}
      </Typography>
    </>
  );
}

function TableView({ widget, data }: { widget: DashboardWidgetDto; data: DashboardDataDto }) {
  const { t } = useTranslation();
  const format = useCellFormatter();
  const shown = tableColumns(widget, data);
  return (
    <Table size="small" stickyHeader>
      <TableHead>
        <TableRow>
          {shown.map((i) => (
            <TableCell key={data.columns[i].key} align={isNumericColumn(widget, data.columns[i]) ? 'right' : 'left'}>
              {columnLabel(data.columns[i].key, t)}
            </TableCell>
          ))}
        </TableRow>
      </TableHead>
      <TableBody>
        {data.rows.map((row, r) => (
          <TableRow key={r}>
            {shown.map((i) => {
              const column = data.columns[i];
              const value = row[i];
              const text = value == null && column.key === 'group' ? t('dashboard.data.none') : format(value, formatOf(widget, column));
              return (
                <TableCell key={column.key} align={isNumericColumn(widget, column) ? 'right' : 'left'}>
                  {text}
                </TableCell>
              );
            })}
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

function ChartView({ widget, data }: { widget: DashboardWidgetDto; data: DashboardDataDto }) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const format = useCellFormatter();
  const theme = useTheme();
  const palette = theme.palette.mode === 'dark' ? SERIES_DARK : SERIES_LIGHT;

  const labelIndex = labelColumn(widget, data);
  const valueIndexes = chartColumns(widget, data);
  const labels = useMemo(
    () =>
      data.rows.map((row) => {
        const v = row[labelIndex];
        if (v == null) return t('dashboard.data.none');
        return format(v, formatOf(widget, data.columns[labelIndex]));
      }),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [data, labelIndex, widget.formats],
  );

  if (valueIndexes.length === 0)
    return (
      <Typography color="text.secondary" variant="body2">
        {t('dashboard.widget.noData')}
      </Typography>
    );

  const numbers = (i: number) => data.rows.map((row) => (typeof row[i] === 'number' ? (row[i] as number) : null));
  const valueFormat = (i: number) => (v: number | null) => format(v, formatOf(widget, data.columns[i]));
  // Axis ticks: compact, and money stays money ($1.2K).
  const firstFormat = formatOf(widget, data.columns[valueIndexes[0]]);
  const tickFormat = (v: number) =>
    firstFormat === 'money'
      ? fmt.number(v, { style: 'currency', currency: 'USD', notation: 'compact' })
      : firstFormat === 'percent'
        ? fmt.percent(v)
        : fmt.number(v, { notation: 'compact' });
  const series = valueIndexes.map((i, n) => ({
    id: data.columns[i].key,
    label: columnLabel(data.columns[i].key, t),
    data: numbers(i),
    color: palette[n],
    valueFormatter: valueFormat(i),
  }));
  const hideLegend = { legend: { hidden: series.length < 2 } };
  const axisText = { fill: theme.palette.text.secondary };
  const sx = {
    '& .MuiChartsAxis-tickLabel': axisText,
    '& .MuiChartsAxis-line, & .MuiChartsAxis-tick': { stroke: theme.palette.divider },
  };

  if (widget.chart === 'pie') {
    // Identity per slice: past the palette, the smallest slices fold into "Other".
    const column = valueIndexes[0];
    const slices = data.rows
      .map((row, r) => ({ label: labels[r], value: typeof row[column] === 'number' ? (row[column] as number) : 0 }))
      .filter((s) => s.value > 0)
      .sort((a, b) => b.value - a.value);
    const kept = slices.length > MAX_PIE_SLICES ? slices.slice(0, MAX_PIE_SLICES - 1) : slices;
    const rest = slices.slice(kept.length).reduce((sum, s) => sum + s.value, 0);
    const pieData = [
      ...kept.map((s, i) => ({ id: i, value: s.value, label: s.label, color: palette[i] })),
      ...(rest > 0 ? [{ id: 'other', value: rest, label: t('dashboard.data.other'), color: theme.palette.grey[500] }] : []),
    ];
    return (
      <PieChart
        series={[
          {
            data: pieData,
            innerRadius: '45%',
            paddingAngle: 1,
            cornerRadius: 4,
            valueFormatter: (item) => format(item.value, formatOf(widget, data.columns[column])),
            highlightScope: { fade: 'global', highlight: 'item' },
          },
        ]}
        margin={{ top: 8, bottom: 8, left: 8, right: 160 }}
        slotProps={{ legend: { labelStyle: { fill: theme.palette.text.primary, fontSize: 12 }, itemMarkWidth: 10, itemMarkHeight: 10 } }}
      />
    );
  }

  if (widget.chart === 'line' || widget.chart === 'area') {
    return (
      <LineChart
        xAxis={[{ scaleType: 'point', data: labels, valueFormatter: (v: string, c: { location: string }) => (c.location === 'tick' ? shorten(v) : v) }]}
        yAxis={[{ valueFormatter: tickFormat }]}
        series={series.map((s) => ({ ...s, area: widget.chart === 'area', showMark: data.rows.length <= 40, curve: 'linear' }))}
        grid={{ horizontal: true }}
        margin={{ top: series.length > 1 ? 40 : 12, right: 16, left: 64, bottom: 28 }}
        slotProps={hideLegend}
        sx={{ ...sx, '& .MuiLineElement-root': { strokeWidth: 2 }, '& .MuiChartsGrid-line': { stroke: theme.palette.divider } }}
      />
    );
  }

  const horizontal = widget.chart === 'horizontalBar';
  const longest = Math.min(MAX_TICK_LABEL, labels.reduce((m, l) => Math.max(m, l.length), 0));
  // Long category names are cut on the axis; the tooltip keeps them whole.
  const bandFormat = (v: string, context: { location: string }) => (context.location === 'tick' ? shorten(v) : v);
  return (
    <BarChart
      layout={horizontal ? 'horizontal' : 'vertical'}
      xAxis={horizontal ? [{ valueFormatter: tickFormat }] : [{ scaleType: 'band', data: labels, valueFormatter: bandFormat }]}
      yAxis={horizontal ? [{ scaleType: 'band', data: labels, valueFormatter: bandFormat }] : [{ valueFormatter: tickFormat }]}
      series={series}
      borderRadius={4}
      grid={horizontal ? { vertical: true } : { horizontal: true }}
      margin={{
        top: series.length > 1 ? 40 : 12,
        right: 16,
        left: horizontal ? Math.max(48, longest * 6.5 + 12) : 64,
        bottom: 28,
      }}
      slotProps={hideLegend}
      sx={{ ...sx, '& .MuiChartsGrid-line': { stroke: theme.palette.divider } }}
    />
  );
}

export function NoteView({ text }: { text: string }) {
  const blocks = useMemo(() => parseBlocks(text), [text]);
  return <HelpMarkdown blocks={blocks} />;
}
