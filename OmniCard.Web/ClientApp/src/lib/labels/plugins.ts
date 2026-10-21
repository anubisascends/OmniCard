import type { Plugins } from '@pdfme/common';
import { barcodes, ellipse, image, line, multiVariableText, rectangle, svg, text } from '@pdfme/schemas';

// The pdfme element types a label can use. The keys are what the designer's sidebar shows (the
// stored schema keeps the plugin's own `type`), so they're translated; the generator only needs the
// same set of plugins, whatever they're called.

export type Translate = (key: string) => string;

export function labelPlugins(t: Translate): Plugins {
  return {
    [t('labels.elements.text')]: text,
    [t('labels.elements.textWithFields')]: multiVariableText,
    [t('labels.elements.qrcode')]: barcodes.qrcode,
    [t('labels.elements.code128')]: barcodes.code128,
    [t('labels.elements.code39')]: barcodes.code39,
    [t('labels.elements.upca')]: barcodes.upca,
    [t('labels.elements.upce')]: barcodes.upce,
    [t('labels.elements.ean13')]: barcodes.ean13,
    [t('labels.elements.ean8')]: barcodes.ean8,
    [t('labels.elements.itf14')]: barcodes.itf14,
    [t('labels.elements.datamatrix')]: barcodes.gs1datamatrix,
    [t('labels.elements.pdf417')]: barcodes.pdf417,
    [t('labels.elements.image')]: image,
    [t('labels.elements.svg')]: svg,
    [t('labels.elements.line')]: line,
    [t('labels.elements.rectangle')]: rectangle,
    [t('labels.elements.ellipse')]: ellipse,
  };
}

/** The plugin for an element type (e.g. "qrcode"), to clone its default schema from. */
export function pluginFor(plugins: Plugins, type: string) {
  return Object.values(plugins).find((p) => p.propPanel.defaultSchema.type === type);
}
