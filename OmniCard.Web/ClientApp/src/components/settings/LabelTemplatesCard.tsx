import { lazy, Suspense, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Backdrop,
  Box,
  Button,
  CircularProgress,
  IconButton,
  Paper,
  Snackbar,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Tooltip,
  Typography,
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import DeleteIcon from '@mui/icons-material/DeleteOutline';
import EditIcon from '@mui/icons-material/Edit';
import { api } from '../../api/client';
import type { LabelTemplateDto, LabelTemplateInput } from '../../api/types';
import { usePermissions } from '../../context/usePermissions';
import { useFormatters } from '../../i18n/format';
import { MM_PER_INCH, SHEET_PRESETS, pageSizeKey } from '../../lib/labels/sheet';
import type { LabelDesignerProps } from '../labels/LabelDesignerDialog';

// The designer carries pdfme (its own React + UI kit), so it's loaded on demand — fetched in the
// background once this tab is open, so "New template" opens straight into it.
const loadDesigner = () => import('../labels/LabelDesignerDialog');
const LabelDesignerDialog = lazy(loadDesigner);

const toInput = (t: LabelTemplateDto): LabelTemplateInput => ({
  name: t.name,
  pageWidthMm: t.pageWidthMm,
  pageHeightMm: t.pageHeightMm,
  marginTopMm: t.marginTopMm,
  marginRightMm: t.marginRightMm,
  marginBottomMm: t.marginBottomMm,
  marginLeftMm: t.marginLeftMm,
  columns: t.columns,
  rows: t.rows,
  columnGapMm: t.columnGapMm,
  rowGapMm: t.rowGapMm,
  design: t.design,
});

/** Settings ▸ Labels: the shared label templates. */
export function LabelTemplatesCard() {
  const { t, i18n } = useTranslation();
  const fmt = useFormatters();
  const qc = useQueryClient();
  const { can } = usePermissions();
  const canEdit = can('labels.edit');
  const [editing, setEditing] = useState<LabelDesignerProps['initial'] | null>(null);
  const [saved, setSaved] = useState(false);
  const templates = useQuery({ queryKey: ['label-templates'], queryFn: api.labelTemplates });
  useEffect(() => {
    if (!canEdit) return;
    const id = setTimeout(() => void loadDesigner().catch(() => {}), 500);
    return () => clearTimeout(id);
  }, [canEdit]);

  const del = useMutation({
    mutationFn: (id: number) => api.labelTemplateDelete(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['label-templates'] }),
  });

  const inches = i18n.language === 'en-US';
  const size = (mm: number) =>
    inches
      ? `${fmt.number(mm / MM_PER_INCH, { maximumFractionDigits: 3 })} ${t('labels.designer.unitIn')}`
      : `${fmt.number(mm, { maximumFractionDigits: 1 })} ${t('labels.designer.unitMm')}`;
  const pageName = (tpl: LabelTemplateDto) => {
    const key = pageSizeKey(tpl);
    return key === 'custom' ? `${size(tpl.pageWidthMm)} × ${size(tpl.pageHeightMm)}` : t(`labels.pageSizes.${key}`);
  };

  const startNew = () =>
    setEditing({ id: null, input: { name: '', design: '', ...SHEET_PRESETS[0].sheet } });

  return (
    <Paper sx={{ p: 2 }}>
      <Stack spacing={2}>
        <Stack direction="row" alignItems="center" justifyContent="space-between">
          <Typography variant="h6">{t('labels.templates.title')}</Typography>
          {canEdit && (
            <Button variant="contained" startIcon={<AddIcon />} onClick={startNew}>
              {t('labels.templates.add')}
            </Button>
          )}
        </Stack>
        <Typography variant="body2" color="text.secondary">
          {t('labels.templates.intro')}
        </Typography>
        {!canEdit && <Alert severity="info">{t('labels.templates.noPermission')}</Alert>}
        {templates.isLoading ? (
          <CircularProgress size={24} />
        ) : templates.error ? (
          <Alert severity="error">{(templates.error as Error).message}</Alert>
        ) : !templates.data?.length ? (
          <Typography color="text.secondary">{t('labels.templates.empty')}</Typography>
        ) : (
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>{t('labels.designer.name')}</TableCell>
                <TableCell>{t('labels.designer.sheet')}</TableCell>
                <TableCell align="right" />
              </TableRow>
            </TableHead>
            <TableBody>
              {templates.data.map((tpl) => (
                <TableRow key={tpl.id} hover>
                  <TableCell>{tpl.name}</TableCell>
                  <TableCell>
                    {t('labels.templates.summary', {
                      columns: tpl.columns,
                      rows: tpl.rows,
                      page: pageName(tpl),
                      size: `${size(tpl.labelWidthMm)} × ${size(tpl.labelHeightMm)}`,
                    })}
                  </TableCell>
                  <TableCell align="right" sx={{ whiteSpace: 'nowrap' }}>
                    {canEdit && (
                      <>
                        <Tooltip title={t('labels.templates.design')}>
                          <IconButton size="small" onClick={() => setEditing({ id: tpl.id, input: toInput(tpl) })}>
                            <EditIcon fontSize="small" />
                          </IconButton>
                        </Tooltip>
                        <Tooltip title={t('labels.templates.duplicate')}>
                          <IconButton
                            size="small"
                            onClick={() =>
                              setEditing({
                                id: null,
                                input: { ...toInput(tpl), name: t('labels.templates.copyName', { name: tpl.name }) },
                              })
                            }
                          >
                            <ContentCopyIcon fontSize="small" />
                          </IconButton>
                        </Tooltip>
                        <Tooltip title={t('common.actions.delete')}>
                          <IconButton
                            size="small"
                            onClick={() => {
                              if (confirm(t('labels.templates.confirmDelete', { name: tpl.name }))) del.mutate(tpl.id);
                            }}
                          >
                            <DeleteIcon fontSize="small" />
                          </IconButton>
                        </Tooltip>
                      </>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </Stack>
      {editing && (
        <Suspense
          fallback={
            <Backdrop open sx={{ zIndex: (theme) => theme.zIndex.modal + 1, color: '#fff' }}>
              <Box sx={{ textAlign: 'center' }}>
                <CircularProgress color="inherit" />
                <Typography>{t('labels.designer.loading')}</Typography>
              </Box>
            </Backdrop>
          }
        >
          <LabelDesignerDialog
            initial={editing}
            onClose={() => setEditing(null)}
            onSaved={() => {
              setEditing(null);
              setSaved(true);
              qc.invalidateQueries({ queryKey: ['label-templates'] });
            }}
          />
        </Suspense>
      )}
      <Snackbar open={saved} autoHideDuration={3000} onClose={() => setSaved(false)} message={t('labels.designer.saved')} />
    </Paper>
  );
}
