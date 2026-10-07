import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery } from '@tanstack/react-query';
import {
  Alert,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Stack,
  TextField,
  ToggleButton,
  ToggleButtonGroup,
  Typography,
} from '@mui/material';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import DownloadIcon from '@mui/icons-material/Download';
import { api } from '../../api/client';
import { copyToClipboard } from '../../lib/exportFormats';
import type { CardListDto, ListExportFormat, ListExportScope } from '../../api/types';

const SCOPES: ListExportScope[] = ['all', 'buy', 'owned'];
const FORMATS: ListExportFormat[] = ['text', 'csv'];

/**
 * Export a list (all cards, the copies still to buy, or the copies already owned) as decklist text —
 * "1x Aragorn, the Uniter (LTR) 192", `*F*` for foils, the format Moxfield / Archidekt import — or as CSV
 * (Qty, Card Name, Set, Collector Number, Foil). The server builds it from the same owned / to-buy split as
 * the grids and the pick / buy lists; the dialog previews it and can copy or download it.
 */
export function ListExportDialog({
  open,
  list,
  initialScope = 'all',
  onClose,
}: {
  open: boolean;
  list: CardListDto;
  initialScope?: ListExportScope;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const [scope, setScope] = useState<ListExportScope>(initialScope);
  const [format, setFormat] = useState<ListExportFormat>('text');
  const [copied, setCopied] = useState<boolean | null>(null);

  // Re-apply the requested scope each time the dialog opens (e.g. "Export" on the To buy grid).
  const [wasOpen, setWasOpen] = useState(false);
  if (open && !wasOpen) {
    setWasOpen(true);
    setScope(initialScope);
    setCopied(null);
  }
  if (!open && wasOpen) setWasOpen(false);

  const preview = useQuery({
    queryKey: ['list-export', list.id, scope, format],
    queryFn: () => api.listExportText(list.id, scope, format),
    enabled: open,
    staleTime: 0,
  });
  const download = useMutation({ mutationFn: () => api.listExportDownload(list.id, scope, format) });

  const text = preview.data ?? '';
  const empty = preview.isSuccess && (format === 'csv' ? text.trim().split('\n').length <= 1 : !text.trim());

  return (
    <Dialog open={open} onClose={onClose} maxWidth="md" fullWidth>
      <DialogTitle>{t('lists.export.title', { name: list.name })}</DialogTitle>
      <DialogContent dividers>
        <Stack direction="row" spacing={2} sx={{ mb: 2 }} flexWrap="wrap" useFlexGap>
          <ToggleButtonGroup
            size="small"
            exclusive
            value={scope}
            onChange={(_, v: ListExportScope | null) => {
              if (v) {
                setScope(v);
                setCopied(null);
              }
            }}
          >
            {SCOPES.map((s) => (
              <ToggleButton key={s} value={s}>
                {t(`lists.export.scopes.${s}`)}
              </ToggleButton>
            ))}
          </ToggleButtonGroup>
          <ToggleButtonGroup
            size="small"
            exclusive
            value={format}
            onChange={(_, v: ListExportFormat | null) => {
              if (v) {
                setFormat(v);
                setCopied(null);
              }
            }}
          >
            {FORMATS.map((f) => (
              <ToggleButton key={f} value={f}>
                {t(`lists.export.formats.${f}`)}
              </ToggleButton>
            ))}
          </ToggleButtonGroup>
        </Stack>

        {preview.error && <Alert severity="error">{(preview.error as Error).message}</Alert>}
        {download.error && <Alert severity="error">{(download.error as Error).message}</Alert>}
        {empty ? (
          <Typography color="text.secondary" variant="body2">
            {t(`lists.export.empty.${scope}`)}
          </Typography>
        ) : (
          <TextField
            multiline
            fullWidth
            minRows={8}
            maxRows={20}
            value={preview.isLoading ? t('common.states.loading') : text}
            slotProps={{ htmlInput: { readOnly: true, sx: { fontFamily: 'monospace', fontSize: 13 } } }}
          />
        )}
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>
          {t(`lists.export.hint.${format}`)}
        </Typography>
      </DialogContent>
      <DialogActions>
        {copied !== null && (
          <Typography variant="body2" color={copied ? 'success.main' : 'error'} sx={{ mr: 'auto', ml: 2 }}>
            {copied ? t('lists.export.copied') : t('lists.export.copyFailed')}
          </Typography>
        )}
        <Button onClick={onClose}>{t('common.actions.close')}</Button>
        <Button
          startIcon={<ContentCopyIcon />}
          disabled={!preview.isSuccess || empty}
          onClick={async () => setCopied(await copyToClipboard(text))}
        >
          {t('lists.export.copy')}
        </Button>
        <Button
          variant="contained"
          startIcon={<DownloadIcon />}
          disabled={!preview.isSuccess || empty || download.isPending}
          onClick={() => download.mutate()}
        >
          {t('lists.export.download')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
