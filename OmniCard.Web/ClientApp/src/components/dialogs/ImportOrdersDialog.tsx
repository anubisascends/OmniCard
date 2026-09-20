import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  Checkbox,
  Chip,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  IconButton,
  MenuItem,
  Stack,
  Step,
  StepLabel,
  Stepper,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material';
import UploadFileIcon from '@mui/icons-material/UploadFile';
import DeleteIcon from '@mui/icons-material/Delete';
import SaveIcon from '@mui/icons-material/Save';
import { api } from '../../api/client';
import type { OrderImportPreviewDto, OrderImportRowDto, OrderImportTemplateDto } from '../../api/types';
import { useFormatters } from '../../i18n/format';

const CHANNELS = ['Manual', 'TcgPlayer', 'Ebay'];
const REQUIRED_FIELD = 'OrderNumber';
const NONE = '';

/** CSV → orders importer with a selectable/editable column-mapping template. */
export function ImportOrdersDialog({
  open,
  onClose,
  onImported,
}: {
  open: boolean;
  onClose: () => void;
  onImported: (created: number) => void;
}) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const qc = useQueryClient();

  const [step, setStep] = useState(0);
  const [file, setFile] = useState<File | null>(null);
  const [headers, setHeaders] = useState<string[]>([]);
  const [templateId, setTemplateId] = useState<string>('');
  const [channel, setChannel] = useState<string>('Manual');
  const [mappings, setMappings] = useState<Record<string, string>>({});
  const [preview, setPreview] = useState<OrderImportPreviewDto | null>(null);
  const [rows, setRows] = useState<OrderImportRowDto[]>([]);
  const [saveName, setSaveName] = useState('');

  const templates = useQuery({
    queryKey: ['order-import-templates'],
    queryFn: api.orderImportTemplates,
    enabled: open,
  });
  const fields = useQuery({
    queryKey: ['order-import-fields'],
    queryFn: api.orderImportFields,
    enabled: open,
  });

  const reset = () => {
    setStep(0);
    setFile(null);
    setHeaders([]);
    setTemplateId('');
    setChannel('Manual');
    setMappings({});
    setPreview(null);
    setRows([]);
    setSaveName('');
  };

  const close = () => {
    reset();
    onClose();
  };

  // Default to the first template (built-in TCGPlayer) once templates load.
  useEffect(() => {
    if (open && templateId === '' && templates.data && templates.data.length > 0) {
      applyTemplate(templates.data[0]);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, templates.data]);

  function applyTemplate(tpl: OrderImportTemplateDto) {
    setTemplateId(tpl.id);
    setChannel(tpl.channel);
    setMappings({ ...tpl.columnMappings });
  }

  const readHeaders = useMutation({
    mutationFn: (f: File) => api.orderImportHeaders(f),
    onSuccess: (res) => {
      setHeaders(res.headers);
      setStep(1);
    },
  });

  const runPreview = useMutation({
    mutationFn: () => api.orderImportPreview(file!, cleanedMappings, channel),
    onSuccess: (res) => {
      setPreview(res);
      setRows(res.rows);
      setStep(2);
    },
  });

  const commit = useMutation({
    mutationFn: () => api.orderImportCommit(channel, rows.filter((r) => r.include && !r.isDuplicateOrder)),
    onSuccess: (res) => {
      qc.invalidateQueries({ queryKey: ['orders'] });
      qc.invalidateQueries({ queryKey: ['customers'] });
      qc.invalidateQueries({ queryKey: ['dashboard'] });
      onImported(res.created);
      close();
    },
  });

  const saveTemplate = useMutation({
    mutationFn: () =>
      api.orderImportTemplateSave({ name: saveName.trim(), channel, columnMappings: cleanedMappings }),
    onSuccess: (tpl) => {
      setSaveName('');
      qc.invalidateQueries({ queryKey: ['order-import-templates'] });
      setTemplateId(tpl.id);
    },
  });

  const deleteTemplate = useMutation({
    mutationFn: (id: string) => api.orderImportTemplateDelete(id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['order-import-templates'] });
      setTemplateId('');
    },
  });

  const cleanedMappings = useMemo(
    () => Object.fromEntries(Object.entries(mappings).filter(([, h]) => h && h.length > 0)),
    [mappings],
  );

  const orderNumberMapped = !!cleanedMappings[REQUIRED_FIELD];
  const includedCount = rows.filter((r) => r.include && !r.isDuplicateOrder).length;
  const selectedTemplate = templates.data?.find((tp) => tp.id === templateId) ?? null;

  const onPickFile = (f: File | null) => {
    if (!f) return;
    setFile(f);
    readHeaders.mutate(f);
  };

  const fieldLabel = (field: string) => t(`sales.orderImport.fields.${field}`, field);

  return (
    <Dialog open={open} onClose={close} fullWidth maxWidth="md">
      <DialogTitle>{t('sales.orderImport.title')}</DialogTitle>
      <DialogContent>
        <Stepper activeStep={step} sx={{ my: 2 }}>
          <Step><StepLabel>{t('sales.orderImport.steps.file')}</StepLabel></Step>
          <Step><StepLabel>{t('sales.orderImport.steps.map')}</StepLabel></Step>
          <Step><StepLabel>{t('sales.orderImport.steps.review')}</StepLabel></Step>
        </Stepper>

        {/* Step 0 — choose file */}
        {step === 0 && (
          <Stack spacing={2} alignItems="flex-start">
            <Typography variant="body2" color="text.secondary">
              {t('sales.orderImport.fileHelp')}
            </Typography>
            <Button component="label" variant="outlined" startIcon={<UploadFileIcon />} disabled={readHeaders.isPending}>
              {t('sales.orderImport.chooseFile')}
              <input
                hidden
                type="file"
                accept=".csv,text/csv"
                onChange={(e) => onPickFile(e.target.files?.[0] ?? null)}
              />
            </Button>
            {readHeaders.isPending && <CircularProgress size={22} />}
            {readHeaders.error && (
              <Alert severity="error">{(readHeaders.error as Error).message}</Alert>
            )}
          </Stack>
        )}

        {/* Step 1 — map columns */}
        {step === 1 && (
          <Stack spacing={2}>
            <Stack direction="row" spacing={2} flexWrap="wrap" useFlexGap>
              <TextField
                select
                size="small"
                label={t('sales.orderImport.template')}
                value={templateId}
                onChange={(e) => {
                  const tpl = templates.data?.find((tp) => tp.id === e.target.value);
                  if (tpl) applyTemplate(tpl);
                }}
                sx={{ minWidth: 240 }}
              >
                {templates.data?.map((tp) => (
                  <MenuItem key={tp.id} value={tp.id}>
                    {tp.name}{tp.isBuiltIn ? ` (${t('sales.orderImport.builtIn')})` : ''}
                  </MenuItem>
                ))}
              </TextField>
              <TextField
                select
                size="small"
                label={t('common.labels.channel')}
                value={channel}
                onChange={(e) => setChannel(e.target.value)}
                sx={{ minWidth: 160 }}
              >
                {CHANNELS.map((c) => (
                  <MenuItem key={c} value={c}>{t(`common.channels.${c}`)}</MenuItem>
                ))}
              </TextField>
              {selectedTemplate && !selectedTemplate.isBuiltIn && (
                <Tooltip title={t('sales.orderImport.deleteTemplate')}>
                  <span>
                    <IconButton
                      color="error"
                      disabled={deleteTemplate.isPending}
                      onClick={() => deleteTemplate.mutate(selectedTemplate.id)}
                    >
                      <DeleteIcon />
                    </IconButton>
                  </span>
                </Tooltip>
              )}
            </Stack>

            <Typography variant="body2" color="text.secondary">
              {t('sales.orderImport.mapHelp')}
            </Typography>

            <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' }, gap: 1.5 }}>
              {(fields.data ?? []).map((field) => {
                const required = field === REQUIRED_FIELD;
                return (
                  <TextField
                    key={field}
                    select
                    size="small"
                    required={required}
                    error={required && !mappings[field]}
                    label={fieldLabel(field)}
                    value={headers.includes(mappings[field]) ? mappings[field] : NONE}
                    onChange={(e) => setMappings((m) => ({ ...m, [field]: e.target.value }))}
                  >
                    <MenuItem value={NONE}>
                      <em>{t('sales.orderImport.notMapped')}</em>
                    </MenuItem>
                    {headers.map((h) => (
                      <MenuItem key={h} value={h}>{h}</MenuItem>
                    ))}
                  </TextField>
                );
              })}
            </Box>

            {!orderNumberMapped && (
              <Alert severity="warning">{t('sales.orderImport.orderNumberRequired')}</Alert>
            )}

            <Stack direction="row" spacing={1} alignItems="center">
              <TextField
                size="small"
                label={t('sales.orderImport.saveAsName')}
                value={saveName}
                onChange={(e) => setSaveName(e.target.value)}
                sx={{ minWidth: 240 }}
              />
              <Button
                startIcon={<SaveIcon />}
                disabled={!saveName.trim() || !orderNumberMapped || saveTemplate.isPending}
                onClick={() => saveTemplate.mutate()}
              >
                {t('sales.orderImport.saveAsTemplate')}
              </Button>
            </Stack>
            {saveTemplate.error && <Alert severity="error">{(saveTemplate.error as Error).message}</Alert>}
            {runPreview.error && <Alert severity="error">{(runPreview.error as Error).message}</Alert>}
          </Stack>
        )}

        {/* Step 2 — review */}
        {step === 2 && preview && (
          <Stack spacing={2}>
            {preview.warnings.length > 0 && (
              <Alert severity="warning">
                <Stack spacing={0.25}>
                  {preview.warnings.map((w, i) => (
                    <Typography key={i} variant="body2">{w}</Typography>
                  ))}
                </Stack>
              </Alert>
            )}
            {rows.length === 0 ? (
              <Alert severity="info">{t('sales.orderImport.noRows')}</Alert>
            ) : (
              <>
                <Typography variant="body2" color="text.secondary">
                  {t('sales.orderImport.selectedCount', { count: includedCount, total: rows.length })}
                </Typography>
                <Box sx={{ maxHeight: 360, overflow: 'auto' }}>
                  <Table size="small" stickyHeader>
                    <TableHead>
                      <TableRow>
                        <TableCell padding="checkbox" />
                        <TableCell>{t('sales.orderImport.col.order')}</TableCell>
                        <TableCell>{t('sales.orderImport.col.customer')}</TableCell>
                        <TableCell>{t('sales.orderImport.col.date')}</TableCell>
                        <TableCell align="right">{t('sales.orderImport.col.items')}</TableCell>
                        <TableCell align="right">{t('sales.orderImport.col.value')}</TableCell>
                        <TableCell>{t('sales.orderImport.col.status')}</TableCell>
                      </TableRow>
                    </TableHead>
                    <TableBody>
                      {rows.map((r, i) => (
                        <TableRow key={`${r.orderNumber}-${i}`} hover>
                          <TableCell padding="checkbox">
                            <Checkbox
                              size="small"
                              checked={r.include && !r.isDuplicateOrder}
                              disabled={!r.canInclude}
                              onChange={(e) =>
                                setRows((prev) =>
                                  prev.map((x, j) => (j === i ? { ...x, include: e.target.checked } : x)),
                                )
                              }
                            />
                          </TableCell>
                          <TableCell>{r.orderNumber}</TableCell>
                          <TableCell>{r.customerName}</TableCell>
                          <TableCell>{fmt.date(r.orderDate)}</TableCell>
                          <TableCell align="right">{fmt.number(r.itemCount)}</TableCell>
                          <TableCell align="right">{fmt.money(r.valueOfProducts)}</TableCell>
                          <TableCell>
                            <Chip
                              size="small"
                              label={r.statusText}
                              color={r.isDuplicateOrder ? 'default' : r.isNewCustomer ? 'success' : 'info'}
                              variant="outlined"
                            />
                          </TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </Box>
              </>
            )}
            {commit.error && <Alert severity="error">{(commit.error as Error).message}</Alert>}
          </Stack>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={close}>{t('common.actions.cancel')}</Button>
        {step === 1 && (
          <>
            <Button onClick={() => setStep(0)}>{t('common.actions.back')}</Button>
            <Button
              variant="contained"
              disabled={!orderNumberMapped || runPreview.isPending}
              onClick={() => runPreview.mutate()}
            >
              {runPreview.isPending ? t('sales.orderImport.previewing') : t('sales.orderImport.preview')}
            </Button>
          </>
        )}
        {step === 2 && (
          <>
            <Button onClick={() => setStep(1)}>{t('common.actions.back')}</Button>
            <Button
              variant="contained"
              disabled={includedCount === 0 || commit.isPending}
              onClick={() => commit.mutate()}
            >
              {commit.isPending
                ? t('sales.orderImport.importing')
                : t('sales.orderImport.importCount', { count: includedCount })}
            </Button>
          </>
        )}
      </DialogActions>
    </Dialog>
  );
}
