import type { TFunction } from 'i18next';
import type {
  DashboardColumnDto,
  DashboardDataDto,
  DashboardFormat,
  DashboardLayoutDto,
  DashboardWidgetDto,
  GameDto,
} from '../../api/types';

/** Grid width (matches DashboardLayoutDto.Columns on the server). */
export const DASHBOARD_COLUMNS = 60;
export const ROW_HEIGHT = 24;
export const GRID_GAP = 16;
/** Mirrors DashboardSqlRunner limits, for the editor's hint. */
export const SQL_MAX_ROWS = 1000;
export const SQL_TIMEOUT_SECONDS = 30;

export const HOLDINGS_METRICS = ['totalUnits', 'totalCost', 'totalMarket', 'unrealized'] as const;
export const SALES_METRICS = [
  'realizedProfit',
  'realizedProceeds',
  'realizedCost',
  'realizedFees',
  'totalSold',
  'shippingCost',
  'shippingCharged',
] as const;
export const BREAKDOWNS = ['game', 'category', 'salesGame'] as const;
export const SEARCH_OUTPUTS = ['summary', 'cards', 'groups'] as const;
export const CARD_SORTS = ['value', 'price', 'quantity', 'name', 'added'] as const;
export const GROUP_SORTS = ['value', 'units', 'cards', 'group'] as const;
export const GROUP_FIELDS = [
  'setCode',
  'rarity',
  'condition',
  'language',
  'isFoil',
  'game',
  'containerName',
  'listingStatus',
] as const;
export const DISPLAYS = ['stat', 'table', 'chart'] as const;
export const CHARTS = ['bar', 'horizontalBar', 'line', 'area', 'pie'] as const;
export const FORMATS: DashboardFormat[] = ['number', 'money', 'percent', 'text', 'date'];

/** Titles the classic Dashboard used, so the built-in widgets read exactly as before. */
const METRIC_TITLE_KEYS: Record<string, string> = {
  totalUnits: 'dashboard.stats.totalUnits',
  totalCost: 'dashboard.stats.cost',
  totalMarket: 'dashboard.stats.market',
  unrealized: 'dashboard.stats.unrealized',
  realizedProfit: 'dashboard.stats.realizedProfit',
};
const BREAKDOWN_TITLE_KEYS: Record<string, string> = {
  game: 'dashboard.tables.byGame',
  category: 'dashboard.tables.byCategory',
  salesGame: 'dashboard.breakdowns.salesGame',
};

/** The widget's own title, or one worked out from its settings in the reader's language. */
export function widgetTitle(w: DashboardWidgetDto, t: TFunction, games: GameDto[] = []): string {
  if (w.title) return w.title;
  return defaultTitle(w, t, games);
}

export function defaultTitle(w: DashboardWidgetDto, t: TFunction, games: GameDto[] = []): string {
  switch (w.type) {
    case 'metric':
      return t(METRIC_TITLE_KEYS[w.metric ?? ''] ?? `dashboard.metrics.${w.metric ?? 'totalMarket'}`);
    case 'breakdown':
      return t(BREAKDOWN_TITLE_KEYS[w.groupBy ?? 'game'] ?? 'dashboard.tables.byGame');
    case 'search': {
      const game = games.find((g) => g.id === w.game)?.displayName;
      const all = game ? '' : 'All';
      if (w.output === 'cards') return t(`dashboard.titles.searchCards${all}`, { game });
      if (w.output === 'groups')
        return t(`dashboard.titles.searchGroups${all}`, {
          game,
          field: t(`dashboard.groupFields.${w.groupBy ?? 'setCode'}`).toLowerCase(),
        });
      return t(`dashboard.titles.searchSummary${all}`, { game });
    }
    case 'sql':
      return t('dashboard.titles.sql');
    case 'note':
      return t('dashboard.titles.note');
    default:
      return t('dashboard.widget.untitled');
  }
}

/** The settings that decide a widget's data (not how it's drawn): its query key. */
export function dataKey(w: DashboardWidgetDto) {
  return [w.type, w.metric, w.groupBy, w.game, w.query, w.output, w.sort, w.limit, w.sql, w.database];
}

export function newWidgetId(): string {
  return 'w' + Math.random().toString(36).slice(2, 12);
}

/** The first free row below every widget, where a new widget goes. */
export function bottomRow(layout: DashboardLayoutDto): number {
  return layout.widgets.reduce((max, w) => Math.max(max, w.y + w.h), 0);
}

const NUMERIC = new Set(['number', 'money', 'percent']);

/** How a column is formatted: the widget's override, else what the server says it holds. */
export function formatOf(w: DashboardWidgetDto, c: DashboardColumnDto): DashboardFormat | 'bool' {
  return w.formats?.[c.key] ?? c.kind;
}

export function isNumericColumn(w: DashboardWidgetDto, c: DashboardColumnDto): boolean {
  return NUMERIC.has(formatOf(w, c));
}

/** The column naming each chart point: the widget's choice, else the first non-numeric column. */
export function labelColumn(w: DashboardWidgetDto, data: DashboardDataDto): number {
  const chosen = data.columns.findIndex((c) => c.key === w.labelColumn);
  if (chosen >= 0) return chosen;
  const text = data.columns.findIndex((c) => !isNumericColumn(w, c));
  return text >= 0 ? text : 0;
}

/** The columns a chart plots: the widget's choice, else one sensible measure (market value when there
 * is one). Never more than the eight palette colors. */
export function chartColumns(w: DashboardWidgetDto, data: DashboardDataDto): number[] {
  const picked = (w.valueColumns ?? [])
    .map((k) => data.columns.findIndex((c) => c.key === k))
    .filter((i) => i >= 0 && isNumericColumn(w, data.columns[i]));
  if (picked.length) return picked.slice(0, 8);
  for (const preferred of ['market', 'value', 'profit']) {
    const i = data.columns.findIndex((c) => c.key === preferred);
    if (i >= 0) return [i];
  }
  const label = labelColumn(w, data);
  const first = data.columns.findIndex((c, i) => i !== label && isNumericColumn(w, c));
  return first >= 0 ? [first] : [];
}

/** The columns a table shows, in order: the widget's choice, else every column. */
export function tableColumns(w: DashboardWidgetDto, data: DashboardDataDto): number[] {
  const picked = (w.valueColumns ?? [])
    .map((k) => data.columns.findIndex((c) => c.key === k))
    .filter((i) => i >= 0);
  return picked.length ? picked : data.columns.map((_, i) => i);
}

/** The column a stat shows (from the first row): the widget's choice, else the first numeric one. */
export function statColumn(w: DashboardWidgetDto, data: DashboardDataDto): number {
  const chosen = data.columns.findIndex((c) => c.key === w.statColumn);
  if (chosen >= 0) return chosen;
  const numeric = data.columns.findIndex((c) => isNumericColumn(w, c));
  return numeric >= 0 ? numeric : 0;
}

/** A column's header: built-in keys are translated, a SQL column keeps its own name. */
export function columnLabel(key: string, t: TFunction): string {
  return t(`dashboard.data.columns.${key}`, { defaultValue: key });
}

/** The display choices that make sense for a widget. */
export function displaysFor(w: DashboardWidgetDto): readonly DashboardWidgetDto['display'][] {
  if (w.type === 'note') return [];
  if (w.type === 'metric') return ['stat'];
  if (w.type === 'search' && w.output === 'summary') return ['stat', 'table'];
  if (w.type === 'search' || w.type === 'breakdown') return ['table', 'chart'];
  return DISPLAYS;
}

const ORDERS_PER_MONTH = `SELECT FORMAT(OrderDate, 'yyyy-MM') AS Month,
       COUNT(*) AS Orders
FROM Orders
WHERE Status <> 'Cancelled'
GROUP BY FORMAT(OrderDate, 'yyyy-MM')
ORDER BY Month`;

const CARDS_ADDED_PER_MONTH = `SELECT FORMAT(l.AcquisitionDate, 'yyyy-MM') AS Month,
       SUM(l.Quantity) AS Cards
FROM Lots l
JOIN Products p ON p.Id = l.ProductId
WHERE p.Category = 'Single'
GROUP BY FORMAT(l.AcquisitionDate, 'yyyy-MM')
ORDER BY Month`;

export type PresetSection = 'totals' | 'breakdowns' | 'search' | 'sql' | 'other';

export interface WidgetPreset {
  key: string;
  section: PresetSection;
  /** Which permission the preset needs beyond viewing the Dashboard. */
  needs?: 'sql' | 'search';
  widget: Omit<DashboardWidgetDto, 'id' | 'x' | 'y'>;
}

/** The Add widget gallery. */
export const PRESETS: WidgetPreset[] = [
  { key: 'metric', section: 'totals', widget: { type: 'metric', metric: 'totalMarket', display: 'stat', w: 12, h: 3 } },
  {
    key: 'breakdownTable',
    section: 'breakdowns',
    widget: { type: 'breakdown', groupBy: 'game', display: 'table', w: 30, h: 10 },
  },
  {
    key: 'breakdownChart',
    section: 'breakdowns',
    widget: { type: 'breakdown', groupBy: 'game', display: 'chart', chart: 'pie', valueColumns: ['market'], w: 30, h: 10 },
  },
  {
    key: 'searchSummary',
    section: 'search',
    needs: 'search',
    widget: { type: 'search', output: 'summary', display: 'stat', statColumn: 'market', w: 15, h: 3 },
  },
  {
    key: 'searchCards',
    section: 'search',
    needs: 'search',
    widget: { type: 'search', output: 'cards', sort: 'value', limit: 10, display: 'table', w: 30, h: 12 },
  },
  {
    key: 'searchGroups',
    section: 'search',
    needs: 'search',
    widget: {
      type: 'search',
      output: 'groups',
      groupBy: 'setCode',
      sort: 'value',
      limit: 10,
      display: 'chart',
      chart: 'horizontalBar',
      w: 30,
      h: 12,
    },
  },
  {
    key: 'sql',
    section: 'sql',
    needs: 'sql',
    widget: { type: 'sql', database: 'main', sql: 'SELECT COUNT(*) AS Cards\nFROM Lots', display: 'table', w: 30, h: 10 },
  },
  {
    key: 'sqlOrders',
    section: 'sql',
    needs: 'sql',
    widget: { type: 'sql', database: 'main', sql: ORDERS_PER_MONTH, display: 'chart', chart: 'line', w: 30, h: 10 },
  },
  {
    key: 'sqlAdded',
    section: 'sql',
    needs: 'sql',
    widget: { type: 'sql', database: 'main', sql: CARDS_ADDED_PER_MONTH, display: 'chart', chart: 'bar', w: 30, h: 10 },
  },
  { key: 'note', section: 'other', widget: { type: 'note', text: '## ', w: 30, h: 4 } },
];
