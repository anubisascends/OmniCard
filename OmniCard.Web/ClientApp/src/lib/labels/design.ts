import type { Plugins, Schema, Template } from '@pdfme/common';
import { pluginFor } from './plugins';

// A label design is a pdfme template for ONE label: a blank page the size of a label (no padding) and
// its elements. The page size always follows the sheet layout, so it's re-applied whenever the
// layout changes and before rendering.

const MM_PER_PT = 25.4 / 72;

export function blankLabel(width: number, height: number): Template['basePdf'] {
  return { width: round(width), height: round(height), padding: [0, 0, 0, 0] };
}

const round = (v: number) => Math.round(Math.max(1, v) * 100) / 100;

/** The stored design, or null when it's missing/unreadable. */
export function parseDesign(json: string | undefined | null): Template | null {
  if (!json) return null;
  try {
    const t = JSON.parse(json) as Template;
    return t && Array.isArray(t.schemas) ? t : null;
  } catch {
    return null;
  }
}

export function withLabelSize(template: Template, width: number, height: number): Template {
  return { ...template, basePdf: blankLabel(width, height), schemas: template.schemas.length ? template.schemas : [[]] };
}

/** The elements of the (single) label page. */
export const labelElements = (template: Template): Schema[] => (template.schemas[0] ?? []) as Schema[];

/** A text size (pt) that fits a line in a box `heightMm` tall. */
const fontFor = (heightMm: number, max = 14) => Math.max(5, Math.min(max, Math.floor(heightMm / MM_PER_PT / 1.25)));

/** A new element of `type` cloned from its plugin's default, named `name` and placed in the box. */
export function newElement(
  plugins: Plugins,
  type: string,
  name: string,
  box: { x: number; y: number; width: number; height: number },
  extra: Record<string, unknown> = {},
): Schema {
  const base = pluginFor(plugins, type)?.propPanel.defaultSchema ?? { type, name, position: { x: 0, y: 0 }, width: 10, height: 10 };
  return {
    ...base,
    ...extra,
    type,
    name,
    position: { x: round(box.x), y: round(box.y) },
    width: round(box.width),
    height: round(box.height),
  } as Schema;
}

/** The starting design for a new template: a QR code to the card's page beside its name, set/number
 * and price, sized to the label. */
export function defaultDesign(plugins: Plugins, width: number, height: number): Template {
  const pad = Math.min(1.5, height * 0.08);
  const inner = height - 2 * pad;
  const qr = Math.max(4, Math.min(inner, width * 0.35));
  const textX = pad * 2 + qr;
  const textW = Math.max(5, width - textX - pad);
  const nameH = inner * 0.42;
  const setH = inner * 0.26;
  const priceH = inner * 0.28;
  return {
    basePdf: blankLabel(width, height),
    schemas: [
      [
        newElement(plugins, 'qrcode', 'cardUrl', { x: pad, y: pad + (inner - qr) / 2, width: qr, height: qr }, {
          content: 'https://example.com/app/card/1234',
        }),
        newElement(plugins, 'text', 'cardName', { x: textX, y: pad, width: textW, height: nameH }, {
          content: 'Lightning Bolt',
          fontSize: fontFor(nameH, 12),
          dynamicFontSize: { min: 5, max: fontFor(nameH, 12), fit: 'horizontal' },
          verticalAlignment: 'middle',
        }),
        // "Text with fields": the designer previews it with sample values; printing fills every field.
        newElement(plugins, 'multiVariableText', 'setLine', { x: textX, y: pad + nameH, width: textW, height: setH }, {
          text: '{setCode} · #{collectorNumber}',
          variables: ['setCode', 'collectorNumber'],
          content: JSON.stringify({ setCode: 'M11', collectorNumber: '149' }),
          fontSize: fontFor(setH, 9),
          dynamicFontSize: { min: 5, max: fontFor(setH, 9), fit: 'horizontal' },
          verticalAlignment: 'middle',
        }),
        newElement(plugins, 'text', 'marketPrice', { x: textX, y: pad + nameH + setH, width: textW, height: priceH }, {
          content: '$1.23',
          fontSize: fontFor(priceH, 11),
          verticalAlignment: 'middle',
        }),
      ],
    ],
  };
}
