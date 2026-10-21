import { useEffect, useMemo, useRef, useState, type MutableRefObject } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation } from '@tanstack/react-query';
import {
  Alert,
  AppBar,
  Box,
  Button,
  Dialog,
  Divider,
  IconButton,
  List,
  ListItemButton,
  ListItemText,
  MenuItem,
  Stack,
  TextField,
  ToggleButton,
  ToggleButtonGroup,
  Toolbar,
  Typography,
  useTheme,
} from '@mui/material';
import CloseIcon from '@mui/icons-material/Close';
import PrintIcon from '@mui/icons-material/Print';
import { Designer } from '@pdfme/ui';
import { ApiError, api } from '../../api/client';
import type { LabelTemplateDto, LabelTemplateInput } from '../../api/types';
import { useFormatters } from '../../i18n/format';
import { defaultDesign, labelElements, newElement, parseDesign, withLabelSize } from '../../lib/labels/design';
import { FIELD_ELEMENT, LABEL_FIELD_KEYS, SAMPLE_CARD, defaultLinkBase, labelValues, type LabelFieldKey } from '../../lib/labels/fields';
import { labelPlugins } from '../../lib/labels/plugins';
import { openPdf, renderLabelSheets } from '../../lib/labels/render';
import {
  MM_PER_INCH,
  PAGE_SIZES,
  SHEET_PRESETS,
  labelSize,
  labelsPerSheet,
  pageSizeKey,
  type LengthUnit,
  type SheetLayout,
} from '../../lib/labels/sheet';

// The Label Designer (Settings ▸ Labels): the sheet layout on the left — page, margins, grid,
// spacing — and the pdfme designer for one label on the right. pdfme is the open-source WYSIWYG
// editor (drag, resize, fonts, QR codes and barcodes); OmniCard only adds the sheet and the card
// fields. Loaded on demand (it's large).

const PDFME_LANGS = ['ar', 'de', 'en', 'es', 'fr', 'it', 'ja', 'ko', 'pl', 'th', 'tr', 'zh'] as const;
type PdfmeLang = (typeof PDFME_LANGS)[number] | 'zh-TW';

function pdfmeLang(language: string): PdfmeLang {
  if (/^zh-(TW|HK|Hant)/i.test(language)) return 'zh-TW';
  const base = language.split('-')[0].toLowerCase();
  return (PDFME_LANGS as readonly string[]).includes(base) ? (base as PdfmeLang) : 'en';
}

const PX_PER_MM = 96 / 25.4;

/** A starting zoom that fills the canvas with the label — pdfme starts at 100%, and a label is tiny.
 * Leaves room for pdfme's element toolbar and field list. */
function fitZoom(container: HTMLElement, widthMm: number, heightMm: number): number {
  const byWidth = (container.clientWidth - 480) / (widthMm * PX_PER_MM);
  const byHeight = (container.clientHeight - 160) / (heightMm * PX_PER_MM);
  return Math.max(1, Math.min(8, Math.floor(Math.min(byWidth, byHeight) * 10) / 10));
}

/** A length typed in the chosen unit, kept in millimetres. */
function LengthField({
  label,
  mm,
  unit,
  onChange,
}: {
  label: string;
  mm: number;
  unit: LengthUnit;
  onChange: (mm: number) => void;
}) {
  const shown = (v: number) => String(unit === 'in' ? Math.round((v / MM_PER_INCH) * 1000) / 1000 : Math.round(v * 100) / 100);
  const [text, setText] = useState(shown(mm));
  const committed = useRef(mm);
  // Re-sync when the value changes from outside (a preset, a page size) or the unit flips.
  useEffect(() => {
    if (Math.abs(committed.current - mm) > 1e-6) {
      committed.current = mm;
      setText(shown(mm));
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [mm]);
  useEffect(() => setText(shown(committed.current)), [unit]); // eslint-disable-line react-hooks/exhaustive-deps
  return (
    <TextField
      size="small"
      type="number"
      label={label}
      value={text}
      inputProps={{ step: unit === 'in' ? 0.0625 : 0.5, min: 0 }}
      onChange={(e) => {
        setText(e.target.value);
        const n = Number.parseFloat(e.target.value);
        if (Number.isFinite(n)) {
          const v = unit === 'in' ? n * MM_PER_INCH : n;
          committed.current = v;
          onChange(v);
        }
      }}
    />
  );
}

function CountField({ label, value, onChange }: { label: string; value: number; onChange: (v: number) => void }) {
  return (
    <TextField
      size="small"
      type="number"
      label={label}
      value={value}
      inputProps={{ min: 1, max: 50, step: 1 }}
      onChange={(e) => {
        const n = Number.parseInt(e.target.value, 10);
        if (Number.isFinite(n)) onChange(Math.max(1, Math.min(50, n)));
      }}
    />
  );
}

/** Hosts the pdfme designer. It mounts with the dialog's content (which a portal renders after the
 * dialog itself), creating pdfme once; the label page then follows the sheet. */
function DesignerCanvas({
  create,
  designerRef,
}: {
  create: (container: HTMLDivElement) => Designer;
  designerRef: MutableRefObject<Designer | null>;
}) {
  const containerRef = useRef<HTMLDivElement | null>(null);
  useEffect(() => {
    if (!containerRef.current) return;
    const designer = create(containerRef.current);
    designerRef.current = designer;
    return () => {
      designer.destroy();
      designerRef.current = null;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  // pdfme renders its own React tree (and UI kit) in here. pdfme re-measures this box whenever it
  // resizes, so nothing it draws may spill out: overflow reaching the dialog would add a scrollbar,
  // shrink the box, re-render pdfme smaller, drop the scrollbar… and flash in a loop.
  return (
    <Box
      ref={containerRef}
      sx={{
        flex: 1,
        minWidth: 0,
        minHeight: 0,
        position: 'relative',
        overflow: 'hidden',
        bgcolor: '#fff',
        // pdfme's element toolbar scrolls vertically on short screens; never sideways.
        '& .pdfme-designer-left-sidebar': { overflowX: 'hidden' },
      }}
    />
  );
}

export interface LabelDesignerProps {
  /** The template to edit (`id` set) or the starting point of a new one. */
  initial: { id: number | null; input: LabelTemplateInput };
  onClose: () => void;
  onSaved: (t: LabelTemplateDto) => void;
}

export default function LabelDesignerDialog({ initial, onClose, onSaved }: LabelDesignerProps) {
  const { t, i18n } = useTranslation();
  const fmt = useFormatters();
  const theme = useTheme();
  const [name, setName] = useState(initial.input.name);
  const [sheet, setSheet] = useState<SheetLayout>(() => {
    const { name: _n, design: _d, ...layout } = initial.input;
    return layout;
  });
  const [unit, setUnit] = useState<LengthUnit>(i18n.language === 'en-US' ? 'in' : 'mm');
  const [errors, setErrors] = useState<string[]>([]);
  const [printing, setPrinting] = useState(false);

  const plugins = useMemo(() => labelPlugins(t), [t]);
  const size = labelSize(sheet);
  const fits = size.width >= 1 && size.height >= 1;
  const designerRef = useRef<Designer | null>(null);
  const sample = useMemo(
    () => labelValues(SAMPLE_CARD, { money: (v) => fmt.money(v), linkBase: defaultLinkBase() }),
    [fmt],
  );

  // Resize the label page as the sheet changes (settled, so typing a margin doesn't thrash pdfme).
  // pdfme already opened at the starting size, so only an actual change redraws it.
  const appliedSize = useRef(`${size.width.toFixed(2)}x${size.height.toFixed(2)}`);
  useEffect(() => {
    if (!fits) return;
    const key = `${size.width.toFixed(2)}x${size.height.toFixed(2)}`;
    if (key === appliedSize.current) return;
    const id = setTimeout(() => {
      const d = designerRef.current;
      if (!d) return;
      appliedSize.current = key;
      d.updateTemplate(withLabelSize(d.getTemplate(), size.width, size.height));
    }, 300);
    return () => clearTimeout(id);
  }, [size.width, size.height, fits]);

  const update = (patch: Partial<SheetLayout>) => setSheet((s) => ({ ...s, ...patch }));

  const addField = (key: LabelFieldKey) => {
    const d = designerRef.current;
    if (!d) return;
    const tpl = d.getTemplate();
    const elements = labelElements(tpl);
    const names = new Set(elements.map((e) => e.name));
    let elementName: string = key;
    for (let i = 2; names.has(elementName); i++) elementName = `${key}${i}`;
    const type = FIELD_ELEMENT[key];
    const maxW = Math.max(2, size.width - 2);
    const maxH = Math.max(2, size.height - 2);
    const square = Math.min(maxH, 18);
    const box =
      type === 'qrcode'
        ? { x: 1, y: 1, width: square, height: square }
        : type === 'code128'
          ? { x: 1, y: 1, width: Math.min(maxW, 35), height: Math.min(maxH, 8) }
          : { x: 1, y: 1, width: Math.min(maxW, 40), height: Math.min(maxH, 5) };
    const element = newElement(plugins, type, elementName, box, {
      content: sample[key] || t(`labels.fields.${key}`),
      ...(type === 'text' ? { fontSize: 9, verticalAlignment: 'middle' } : {}),
    });
    d.updateTemplate({ ...tpl, schemas: [[...elements, element], ...tpl.schemas.slice(1)] });
  };

  const design = () => {
    const d = designerRef.current;
    return d ? JSON.stringify(withLabelSize(d.getTemplate(), size.width, size.height)) : initial.input.design;
  };

  const save = useMutation({
    mutationFn: () => {
      const input: LabelTemplateInput = { ...sheet, name: name.trim(), design: design() };
      return initial.id != null ? api.labelTemplateUpdate(initial.id, input) : api.labelTemplateCreate(input);
    },
    onSuccess: (saved) => {
      setErrors([]);
      onSaved(saved);
    },
    onError: (e) => {
      const body = e instanceof ApiError ? (e.body as { errors?: string[] } | undefined) : undefined;
      setErrors(body?.errors?.length ? body.errors : [e.message]);
    },
  });

  const testPrint = async () => {
    setPrinting(true);
    try {
      const bytes = await renderLabelSheets({
        sheet,
        design: design(),
        labels: Array.from({ length: labelsPerSheet(sheet) }, () => sample),
        outlines: true,
        t,
      });
      openPdf(bytes, `${name.trim() || 'labels'}-test.pdf`);
    } catch (e) {
      setErrors([t('labels.printDialog.failed', { message: (e as Error).message })]);
    } finally {
      setPrinting(false);
    }
  };

  const shownUnit = unit === 'in' ? t('labels.designer.unitIn') : t('labels.designer.unitMm');
  const shownLength = (mm: number) =>
    fmt.number(unit === 'in' ? mm / MM_PER_INCH : mm, { maximumFractionDigits: unit === 'in' ? 3 : 1 });
  const pageKey = pageSizeKey(sheet);

  return (
    <Dialog open fullScreen onClose={onClose} PaperProps={{ sx: { overflow: 'hidden' } }}>
      <AppBar position="static" color="default" elevation={1}>
        <Toolbar variant="dense" sx={{ gap: 1 }}>
          <IconButton edge="start" onClick={onClose} aria-label={t('common.actions.close')}>
            <CloseIcon />
          </IconButton>
          <Typography variant="h6" sx={{ flexGrow: 1 }}>
            {initial.id != null ? t('labels.designer.titleEdit') : t('labels.designer.titleNew')}
          </Typography>
          <Button startIcon={<PrintIcon />} onClick={testPrint} disabled={printing || !fits} title={t('labels.designer.testPrintHint')}>
            {t('labels.designer.testPrint')}
          </Button>
          <Button onClick={onClose}>{t('common.actions.cancel')}</Button>
          <Button variant="contained" onClick={() => save.mutate()} disabled={save.isPending || !fits || !name.trim()}>
            {t('common.actions.save')}
          </Button>
        </Toolbar>
      </AppBar>
      <Box sx={{ display: 'flex', flex: 1, minHeight: 0, overflow: 'hidden' }}>
        <Box sx={{ width: 340, flexShrink: 0, overflowY: 'auto', p: 2, borderRight: 1, borderColor: 'divider' }}>
          <Stack spacing={2}>
            {errors.length > 0 && (
              <Alert severity="error" onClose={() => setErrors([])}>
                {errors.map((e) => (
                  <div key={e}>{e}</div>
                ))}
              </Alert>
            )}
            <TextField size="small" label={t('labels.designer.name')} value={name} onChange={(e) => setName(e.target.value)} autoFocus />

            <Stack direction="row" alignItems="center" justifyContent="space-between">
              <Typography variant="subtitle2">{t('labels.designer.sheet')}</Typography>
              <ToggleButtonGroup
                size="small"
                exclusive
                value={unit}
                onChange={(_, v: LengthUnit | null) => v && setUnit(v)}
                aria-label={t('labels.designer.unit')}
              >
                <ToggleButton value="in">{t('labels.designer.unitIn')}</ToggleButton>
                <ToggleButton value="mm">{t('labels.designer.unitMm')}</ToggleButton>
              </ToggleButtonGroup>
            </Stack>
            <TextField
              select
              size="small"
              label={t('labels.presets.title')}
              value=""
              onChange={(e) => {
                const preset = SHEET_PRESETS.find((p) => p.key === e.target.value);
                if (preset) setSheet(preset.sheet);
              }}
            >
              {SHEET_PRESETS.map((p) => (
                <MenuItem key={p.key} value={p.key}>
                  {t(`labels.presets.${p.key}`)}
                </MenuItem>
              ))}
            </TextField>
            <TextField
              select
              size="small"
              label={t('labels.designer.pageSize')}
              value={pageKey}
              onChange={(e) => {
                const p = PAGE_SIZES.find((s) => s.key === e.target.value);
                if (p) update({ pageWidthMm: p.width, pageHeightMm: p.height });
              }}
            >
              {PAGE_SIZES.map((p) => (
                <MenuItem key={p.key} value={p.key}>
                  {t(`labels.pageSizes.${p.key}`)}
                </MenuItem>
              ))}
              <MenuItem value="custom" disabled>
                {t('labels.pageSizes.custom')}
              </MenuItem>
            </TextField>
            <Stack direction="row" spacing={1}>
              <LengthField label={t('labels.designer.pageWidth')} mm={sheet.pageWidthMm} unit={unit} onChange={(v) => update({ pageWidthMm: v })} />
              <LengthField label={t('labels.designer.pageHeight')} mm={sheet.pageHeightMm} unit={unit} onChange={(v) => update({ pageHeightMm: v })} />
            </Stack>

            <Typography variant="subtitle2">{t('labels.designer.margins')}</Typography>
            <Stack direction="row" spacing={1}>
              <LengthField label={t('labels.designer.marginTop')} mm={sheet.marginTopMm} unit={unit} onChange={(v) => update({ marginTopMm: v })} />
              <LengthField label={t('labels.designer.marginBottom')} mm={sheet.marginBottomMm} unit={unit} onChange={(v) => update({ marginBottomMm: v })} />
            </Stack>
            <Stack direction="row" spacing={1}>
              <LengthField label={t('labels.designer.marginLeft')} mm={sheet.marginLeftMm} unit={unit} onChange={(v) => update({ marginLeftMm: v })} />
              <LengthField label={t('labels.designer.marginRight')} mm={sheet.marginRightMm} unit={unit} onChange={(v) => update({ marginRightMm: v })} />
            </Stack>

            <Typography variant="subtitle2">{t('labels.designer.grid')}</Typography>
            <Stack direction="row" spacing={1}>
              <CountField label={t('labels.designer.columns')} value={sheet.columns} onChange={(v) => update({ columns: v })} />
              <CountField label={t('labels.designer.rows')} value={sheet.rows} onChange={(v) => update({ rows: v })} />
            </Stack>
            <Stack direction="row" spacing={1}>
              <LengthField label={t('labels.designer.columnGap')} mm={sheet.columnGapMm} unit={unit} onChange={(v) => update({ columnGapMm: v })} />
              <LengthField label={t('labels.designer.rowGap')} mm={sheet.rowGapMm} unit={unit} onChange={(v) => update({ rowGapMm: v })} />
            </Stack>
            {fits ? (
              <Typography variant="body2" color="text.secondary">
                {t('labels.designer.labelSize', {
                  width: shownLength(size.width),
                  height: shownLength(size.height),
                  unit: shownUnit,
                  count: labelsPerSheet(sheet),
                })}
              </Typography>
            ) : (
              <Alert severity="warning">{t('labels.designer.noRoom')}</Alert>
            )}

            <Divider />
            <Typography variant="subtitle2">{t('labels.designer.addField')}</Typography>
            <Typography variant="caption" color="text.secondary">
              {t('labels.designer.fieldsHelp')}
            </Typography>
            <List dense disablePadding>
              {LABEL_FIELD_KEYS.map((key) => (
                <ListItemButton key={key} onClick={() => addField(key)} sx={{ py: 0 }}>
                  <ListItemText
                    primary={t(`labels.fields.${key}`)}
                    secondary={key}
                    secondaryTypographyProps={{ sx: { fontFamily: 'monospace' } }}
                  />
                  <Typography variant="caption" color="text.secondary">
                    {FIELD_ELEMENT[key] === 'text' ? t('labels.elements.text') : t(`labels.elements.${FIELD_ELEMENT[key]}`)}
                  </Typography>
                </ListItemButton>
              ))}
            </List>
          </Stack>
        </Box>
        <DesignerCanvas
          create={(container) => {
            const s = labelSize(sheet);
            const w = Math.max(1, s.width);
            const h = Math.max(1, s.height);
            const stored = parseDesign(initial.input.design);
            return new Designer({
              domContainer: container,
              template: stored ? withLabelSize(stored, w, h) : defaultDesign(plugins, w, h),
              plugins,
              options: {
                lang: pdfmeLang(i18n.language),
                maxZoom: 800,
                zoomLevel: fitZoom(container, w, h),
                theme: { token: { colorPrimary: theme.palette.primary.main } },
              },
            });
          }}
          designerRef={designerRef}
        />
      </Box>
    </Dialog>
  );
}
