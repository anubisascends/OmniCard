import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useQuery } from '@tanstack/react-query';
import { Link as RouterLink } from 'react-router-dom';
import {
  Alert,
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControlLabel,
  Link,
  MenuItem,
  Radio,
  RadioGroup,
  Stack,
  Switch,
  TextField,
  Typography,
} from '@mui/material';
import { api } from '../../api/client';
import type { LabelDataRequest } from '../../api/types';
import { useFormatters } from '../../i18n/format';
import { defaultLinkBase, isLocalOnlyBase, labelValues } from '../../lib/labels/fields';
import { labelsPerSheet } from '../../lib/labels/sheet';

// Remembered per browser: the last template and the address card-page QR codes point at.
const TEMPLATE_KEY = 'omnicard.labels.template';
const LINK_BASE_KEY = 'omnicard.labels.linkBase';

function stored(key: string): string | null {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

function store(key: string, value: string) {
  try {
    localStorage.setItem(key, value);
  } catch {
    /* storage unavailable — just not remembered */
  }
}

/**
 * Print labels for some cards with a saved label template. The cards are a selection (`lotIds`) or
 * a whole location (`containerId` + the page's game/search). The PDF is rendered in the browser and
 * opened in a new tab to print.
 */
export function PrintLabelsDialog({
  open,
  request,
  title,
  onClose,
}: {
  open: boolean;
  request: LabelDataRequest;
  title: string;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const templates = useQuery({ queryKey: ['label-templates'], queryFn: api.labelTemplates, enabled: open });
  const data = useQuery({
    queryKey: ['label-data', request],
    queryFn: () => api.labelData(request),
    enabled: open,
    staleTime: 0,
  });
  const [templateId, setTemplateId] = useState<number | ''>('');
  const [perCopy, setPerCopy] = useState(true);
  const [skip, setSkip] = useState(0);
  const [outlines, setOutlines] = useState(false);
  const [linkBase, setLinkBase] = useState(() => stored(LINK_BASE_KEY) ?? defaultLinkBase());
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Default to the last-used template (or the first one).
  useEffect(() => {
    const list = templates.data;
    if (!list?.length || (templateId !== '' && list.some((x) => x.id === templateId))) return;
    const last = Number(stored(TEMPLATE_KEY));
    setTemplateId(list.find((x) => x.id === last)?.id ?? list[0].id);
  }, [templates.data, templateId]);

  useEffect(() => {
    if (open) {
      setError(null);
      setSkip(0);
    }
  }, [open]);

  const template = templates.data?.find((x) => x.id === templateId) ?? null;
  const perSheet = template ? labelsPerSheet(template) : 1;
  const cards = data.data?.cards ?? [];
  const labelCount = useMemo(
    () => cards.reduce((n, c) => n + (perCopy ? Math.max(1, c.quantity) : 1), 0),
    [cards, perCopy],
  );
  const skipped = Math.min(skip, perSheet - 1);
  const sheets = Math.ceil((skipped + labelCount) / perSheet);
  const usesCardLinks = template?.design.includes('cardUrl') ?? false;

  const create = async () => {
    if (!template) return;
    setBusy(true);
    setError(null);
    try {
      store(TEMPLATE_KEY, String(template.id));
      store(LINK_BASE_KEY, linkBase);
      const money = (v: number) => fmt.money(v);
      const labels = cards.flatMap((c) => {
        const values = labelValues(c, { money, linkBase });
        return Array.from({ length: perCopy ? Math.max(1, c.quantity) : 1 }, () => values);
      });
      // pdfme + the barcode engine are only loaded when a PDF is made.
      const { renderLabelSheets, openPdf } = await import('../../lib/labels/render');
      const bytes = await renderLabelSheets({ sheet: template, design: template.design, labels, skip: skipped, outlines, t });
      openPdf(bytes, `${template.name}.pdf`);
      onClose();
    } catch (e) {
      setError(t('labels.printDialog.failed', { message: (e as Error).message }));
    } finally {
      setBusy(false);
    }
  };

  const loading = templates.isLoading || data.isLoading;

  return (
    <Dialog open={open} onClose={busy ? undefined : onClose} maxWidth="sm" fullWidth>
      <DialogTitle>{title}</DialogTitle>
      <DialogContent>
        {loading ? (
          <CircularProgress size={24} />
        ) : templates.error || data.error ? (
          <Alert severity="error">{((templates.error ?? data.error) as Error).message}</Alert>
        ) : !templates.data?.length ? (
          <Alert severity="info">
            {t('labels.printDialog.noTemplates')}{' '}
            <Link component={RouterLink} to="/settings?tab=labels" onClick={onClose}>
              {t('labels.printDialog.createTemplate')}
            </Link>
          </Alert>
        ) : (
          <Stack spacing={2} sx={{ pt: 1 }}>
            <TextField
              select
              label={t('labels.printDialog.template')}
              value={templateId}
              onChange={(e) => setTemplateId(Number(e.target.value))}
            >
              {templates.data.map((x) => (
                <MenuItem key={x.id} value={x.id}>
                  {x.name}
                </MenuItem>
              ))}
            </TextField>
            <RadioGroup row value={perCopy ? 'copy' : 'card'} onChange={(e) => setPerCopy(e.target.value === 'copy')}>
              <FormControlLabel value="copy" control={<Radio />} label={t('labels.printDialog.perCopy')} />
              <FormControlLabel value="card" control={<Radio />} label={t('labels.printDialog.perCard')} />
            </RadioGroup>
            <TextField
              type="number"
              label={t('labels.printDialog.skip')}
              value={skip}
              inputProps={{ min: 0, max: perSheet - 1 }}
              onChange={(e) => setSkip(Math.max(0, Math.min(perSheet - 1, Number.parseInt(e.target.value, 10) || 0)))}
              helperText={t('labels.printDialog.skipHelp', { position: fmt.number(skipped + 1) })}
              disabled={perSheet <= 1}
            />
            <FormControlLabel
              control={<Switch checked={outlines} onChange={(e) => setOutlines(e.target.checked)} />}
              label={t('labels.printDialog.outlines')}
            />
            {usesCardLinks && (
              <TextField
                label={t('labels.printDialog.linkBase')}
                value={linkBase}
                onChange={(e) => setLinkBase(e.target.value)}
                helperText={t('labels.printDialog.linkBaseHelp')}
              />
            )}
            {usesCardLinks && isLocalOnlyBase(linkBase) && <Alert severity="warning">{t('labels.printDialog.localOnly')}</Alert>}
            {data.data?.truncated && (
              <Alert severity="warning">{t('labels.printDialog.truncated', { countText: fmt.number(cards.length) })}</Alert>
            )}
            <Typography variant="body2" color="text.secondary">
              {labelCount > 0
                ? t('labels.printDialog.summary', { count: labelCount, countText: fmt.number(labelCount), sheets: fmt.number(sheets) })
                : t('labels.printDialog.noCards')}
            </Typography>
            {error && <Alert severity="error">{error}</Alert>}
          </Stack>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={busy}>
          {t('common.actions.cancel')}
        </Button>
        <Button
          variant="contained"
          onClick={create}
          disabled={busy || !template || labelCount === 0}
          startIcon={busy ? <CircularProgress size={16} color="inherit" /> : undefined}
        >
          {busy ? t('labels.printDialog.creating') : t('labels.printDialog.create')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
