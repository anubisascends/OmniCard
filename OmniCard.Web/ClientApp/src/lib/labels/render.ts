import { generate } from '@pdfme/generator';
import { PDFDocument, rgb } from '@pdfme/pdf-lib';
import type { Template } from '@pdfme/common';
import { buildInput, type LabelValues } from './fields';
import { labelElements, parseDesign, withLabelSize } from './design';
import { labelPlugins, type Translate } from './plugins';
import { cellOrigin, labelSize, labelsPerSheet, type SheetLayout } from './sheet';

// Renders a print run in the browser: pdfme fills one page per distinct label from the design, then
// each label is placed in its cell on the sheet (pdf-lib), skipping the cells already used on a
// partly-used first sheet. Loaded on demand — it carries pdfme and the barcode engine.

const PT_PER_MM = 72 / 25.4;

export interface RenderLabelsOptions {
  sheet: SheetLayout;
  /** The template's design JSON (one label). */
  design: string;
  /** One entry per label to print, in sheet order; identical entries are rendered once. */
  labels: LabelValues[];
  /** Cells to leave empty at the start of the first sheet. */
  skip?: number;
  /** Draw a thin outline around every cell (to check alignment, or as cutting guides). */
  outlines?: boolean;
  t: Translate;
}

export async function renderLabelSheets(opts: RenderLabelsOptions): Promise<Uint8Array> {
  const { sheet, labels } = opts;
  const { width, height } = labelSize(sheet);
  const template: Template = withLabelSize(parseDesign(opts.design) ?? { basePdf: { width, height, padding: [0, 0, 0, 0] }, schemas: [[]] }, width, height);
  // A label is one page — only the first page of the design is used.
  template.schemas = [labelElements(template)];
  const elements = labelElements(template);

  // Render each distinct label once; copies reuse its page.
  const keyOf = (v: LabelValues) => JSON.stringify(v);
  const unique = new Map<string, number>();
  const inputs: Record<string, string>[] = [];
  const pageOf = labels.map((v) => {
    const key = keyOf(v);
    let index = unique.get(key);
    if (index === undefined) {
      index = inputs.length;
      unique.set(key, index);
      inputs.push(buildInput(elements, v));
    }
    return index;
  });

  const out = await PDFDocument.create();
  const pageW = sheet.pageWidthMm * PT_PER_MM;
  const pageH = sheet.pageHeightMm * PT_PER_MM;
  const perSheet = labelsPerSheet(sheet);
  const skip = Math.max(0, Math.min(perSheet - 1, Math.floor(opts.skip ?? 0)));
  const sheetCount = Math.max(1, Math.ceil((skip + labels.length) / perSheet));
  const pages = Array.from({ length: sheetCount }, () => out.addPage([pageW, pageH]));

  if (inputs.length > 0) {
    const bytes = await generate({ template, inputs, plugins: labelPlugins(opts.t) });
    const embedded = await out.embedPdf(bytes, inputs.map((_, i) => i));
    labels.forEach((_, i) => {
      const position = skip + i;
      const { x, y } = cellOrigin(sheet, position % perSheet);
      pages[Math.floor(position / perSheet)].drawPage(embedded[pageOf[i]], {
        x: x * PT_PER_MM,
        // pdf-lib measures from the bottom-left corner.
        y: pageH - (y + height) * PT_PER_MM,
        width: width * PT_PER_MM,
        height: height * PT_PER_MM,
      });
    });
  }

  if (opts.outlines) {
    for (const page of pages)
      for (let cell = 0; cell < perSheet; cell++) {
        const { x, y } = cellOrigin(sheet, cell);
        page.drawRectangle({
          x: x * PT_PER_MM,
          y: pageH - (y + height) * PT_PER_MM,
          width: width * PT_PER_MM,
          height: height * PT_PER_MM,
          borderColor: rgb(0.6, 0.6, 0.6),
          borderWidth: 0.4,
        });
      }
  }

  return out.save();
}

/** Opens a rendered PDF in a new tab to print, or downloads it when pop-ups are blocked. */
export function openPdf(bytes: Uint8Array, fileName: string) {
  const url = URL.createObjectURL(new Blob([bytes as BlobPart], { type: 'application/pdf' }));
  const win = window.open(url, '_blank');
  if (!win) {
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    a.click();
  }
  // Long enough for the new tab (or the download) to read it.
  setTimeout(() => URL.revokeObjectURL(url), 60_000);
}
