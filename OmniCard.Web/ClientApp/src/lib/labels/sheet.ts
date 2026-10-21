import type { LabelTemplateInput } from '../../api/types';

// Label sheet geometry. Everything is millimetres; the editor can show inches. A sheet is a grid of
// equally sized labels: the page minus its margins, shared by `columns` × `rows` labels with the
// column/row spacing between neighbours — so the label size is always derived, never typed in.

export const MM_PER_INCH = 25.4;

export type SheetLayout = Omit<LabelTemplateInput, 'name' | 'design'>;

export type LengthUnit = 'mm' | 'in';

export function labelSize(s: SheetLayout): { width: number; height: number } {
  const width = s.columns < 1 ? 0 : (s.pageWidthMm - s.marginLeftMm - s.marginRightMm - (s.columns - 1) * s.columnGapMm) / s.columns;
  const height = s.rows < 1 ? 0 : (s.pageHeightMm - s.marginTopMm - s.marginBottomMm - (s.rows - 1) * s.rowGapMm) / s.rows;
  return { width, height };
}

export const labelsPerSheet = (s: SheetLayout) => Math.max(1, s.columns) * Math.max(1, s.rows);

/** Top-left corner (mm from the page's top-left) of the label at `cell` — counted left to right, then
 * top to bottom. */
export function cellOrigin(s: SheetLayout, cell: number): { x: number; y: number } {
  const { width, height } = labelSize(s);
  const col = cell % s.columns;
  const row = Math.floor(cell / s.columns);
  return { x: s.marginLeftMm + col * (width + s.columnGapMm), y: s.marginTopMm + row * (height + s.rowGapMm) };
}

/** Pages the layout's page size can be picked from (`custom` = typed in). */
export const PAGE_SIZES = [
  { key: 'letter', width: 215.9, height: 279.4 },
  { key: 'legal', width: 215.9, height: 355.6 },
  { key: 'a4', width: 210, height: 297 },
  { key: 'a5', width: 148, height: 210 },
] as const;

export function pageSizeKey(s: SheetLayout): string {
  const near = (a: number, b: number) => Math.abs(a - b) < 0.05;
  return PAGE_SIZES.find((p) => near(p.width, s.pageWidthMm) && near(p.height, s.pageHeightMm))?.key ?? 'custom';
}

const inch = (v: number) => v * MM_PER_INCH;

/** Common label stock to start a template from (the user can change every value afterwards). */
export const SHEET_PRESETS: { key: string; sheet: SheetLayout }[] = [
  {
    // 30 per US-Letter sheet, 2⅝″ × 1″ — the usual address-label stock.
    key: 'letter30',
    sheet: {
      pageWidthMm: 215.9, pageHeightMm: 279.4,
      marginTopMm: inch(0.5), marginBottomMm: inch(0.5), marginLeftMm: inch(0.1875), marginRightMm: inch(0.1875),
      columns: 3, rows: 10, columnGapMm: inch(0.125), rowGapMm: 0,
    },
  },
  {
    // 80 per US-Letter sheet, 1¾″ × ½″ — return-address labels.
    key: 'letter80',
    sheet: {
      pageWidthMm: 215.9, pageHeightMm: 279.4,
      marginTopMm: inch(0.5), marginBottomMm: inch(0.5), marginLeftMm: inch(0.3), marginRightMm: inch(0.3),
      columns: 4, rows: 20, columnGapMm: inch(0.3), rowGapMm: 0,
    },
  },
  {
    // 65 per A4 sheet, 38.1 × 21.2 mm.
    key: 'a4_65',
    sheet: {
      pageWidthMm: 210, pageHeightMm: 297,
      marginTopMm: 10.7, marginBottomMm: 10.7, marginLeftMm: 4.75, marginRightMm: 4.75,
      columns: 5, rows: 13, columnGapMm: 2.5, rowGapMm: 0,
    },
  },
  {
    // A thermal / roll label printer: one 2¼″ × 1¼″ label per page.
    key: 'thermal',
    sheet: {
      pageWidthMm: inch(2.25), pageHeightMm: inch(1.25),
      marginTopMm: 0, marginBottomMm: 0, marginLeftMm: 0, marginRightMm: 0,
      columns: 1, rows: 1, columnGapMm: 0, rowGapMm: 0,
    },
  },
];
