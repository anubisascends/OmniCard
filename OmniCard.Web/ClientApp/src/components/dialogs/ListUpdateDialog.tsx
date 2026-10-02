import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation } from '@tanstack/react-query';
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
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  TextField,
  Typography,
} from '@mui/material';
import SyncIcon from '@mui/icons-material/Sync';
import { api } from '../../api/client';
import { useFormatters } from '../../i18n/format';
import type { CardListDto, ListUpdateRowDto } from '../../api/types';

const KIND_COLOR = { Add: 'success', Remove: 'error', Change: 'info' } as const;

const rowKey = (r: ListUpdateRowDto) => `${r.gameCardId}|${r.isFoil}`;

/**
 * "Update from URL": re-fetches the list's Moxfield / Archidekt deck and shows every difference (added,
 * removed, quantity changed) with a tick-box. Nothing changes until the user applies the ticked rows.
 * Removals of cards that weren't imported from a URL (added by hand, or stand-ins) start unticked.
 */
export function ListUpdateDialog({
  open,
  list,
  onClose,
  onApplied,
}: {
  open: boolean;
  list: CardListDto;
  onClose: () => void;
  onApplied: () => void;
}) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const [url, setUrl] = useState(list.sourceUrl ?? '');
  const [selected, setSelected] = useState<Set<string>>(new Set());

  const preview = useMutation({
    mutationFn: (fromUrl: string) => api.listUpdatePreview(list.id, fromUrl),
    onSuccess: (p) => setSelected(new Set(p.rows.filter((r) => !r.handAdded).map(rowKey))),
  });
  const apply = useMutation({
    mutationFn: () => {
      const p = preview.data!;
      return api.listUpdateApply(list.id, p.url, p.rows.filter((r) => selected.has(rowKey(r))));
    },
    onSuccess: () => {
      onApplied();
      onClose();
    },
  });

  // Check straight away when the list already knows its URL.
  const { mutate: runPreview, reset: resetPreview } = preview;
  const { reset: resetApply } = apply;
  useEffect(() => {
    if (!open) return;
    setUrl(list.sourceUrl ?? '');
    resetPreview();
    resetApply();
    if (list.sourceUrl) runPreview(list.sourceUrl);
  }, [open, list.id, list.sourceUrl, runPreview, resetPreview, resetApply]);

  const rows = preview.data?.rows ?? [];
  const allSelected = rows.length > 0 && rows.every((r) => selected.has(rowKey(r)));
  const toggle = (r: ListUpdateRowDto) =>
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(rowKey(r))) next.delete(rowKey(r));
      else next.add(rowKey(r));
      return next;
    });

  return (
    <Dialog open={open} onClose={onClose} maxWidth="md" fullWidth>
      <DialogTitle>{t('lists.update.title', { name: list.name })}</DialogTitle>
      <DialogContent dividers>
        <Stack spacing={2}>
          <Stack direction="row" spacing={1} alignItems="center">
            <TextField
              size="small"
              fullWidth
              label={t('lists.deckUrlLabel')}
              placeholder={t('lists.deckUrlPlaceholder')}
              value={url}
              onChange={(e) => setUrl(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter' && url.trim() && !preview.isPending) preview.mutate(url.trim());
              }}
            />
            <Button
              startIcon={<SyncIcon />}
              disabled={!url.trim() || preview.isPending}
              onClick={() => preview.mutate(url.trim())}
              sx={{ whiteSpace: 'nowrap' }}
            >
              {t('lists.update.check')}
            </Button>
          </Stack>

          {preview.isPending && <CircularProgress size={28} />}
          {preview.error && <Alert severity="error">{(preview.error as Error).message}</Alert>}

          {preview.data && (
            <>
              <Typography variant="body2" color="text.secondary">
                {t('lists.update.summary', {
                  deck: preview.data.deckName,
                  changes: rows.length,
                  unchanged: preview.data.unchangedCount,
                })}
              </Typography>
              {preview.data.unresolvedNames.length > 0 && (
                <Alert severity="warning">
                  {t('lists.update.unresolved', { names: preview.data.unresolvedNames.join(', ') })}
                </Alert>
              )}
              {rows.length === 0 ? (
                <Alert severity="success">{t('lists.update.upToDate')}</Alert>
              ) : (
                <Table size="small">
                  <TableHead>
                    <TableRow>
                      <TableCell padding="checkbox">
                        <Checkbox
                          size="small"
                          checked={allSelected}
                          indeterminate={!allSelected && selected.size > 0}
                          onChange={() => setSelected(allSelected ? new Set() : new Set(rows.map(rowKey)))}
                        />
                      </TableCell>
                      <TableCell>{t('lists.update.columns.change')}</TableCell>
                      <TableCell>{t('lists.detail.columns.card')}</TableCell>
                      <TableCell>{t('common.labels.set')}</TableCell>
                      <TableCell align="right">{t('lists.detail.columns.qty')}</TableCell>
                      <TableCell align="right">{t('common.labels.price')}</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {rows.map((r) => (
                      <TableRow key={rowKey(r)} hover onClick={() => toggle(r)} sx={{ cursor: 'pointer' }}>
                        <TableCell padding="checkbox">
                          <Checkbox size="small" checked={selected.has(rowKey(r))} />
                        </TableCell>
                        <TableCell>
                          <Chip
                            size="small"
                            variant="outlined"
                            color={KIND_COLOR[r.kind]}
                            label={t(`lists.update.kinds.${r.kind}`)}
                          />
                        </TableCell>
                        <TableCell>
                          {r.cardName}
                          {r.isFoil ? ' ✦' : ''}
                          {r.handAdded && (
                            <Typography component="span" variant="caption" color="text.secondary" sx={{ ml: 1 }}>
                              {t('lists.update.handAdded')}
                            </Typography>
                          )}
                          {r.kind === 'Add' && r.ownedQuantity > 0 && (
                            <Typography component="span" variant="caption" color="success.main" sx={{ ml: 1 }}>
                              {t('lists.update.owned', { count: Math.min(r.ownedQuantity, r.newQuantity) })}
                            </Typography>
                          )}
                        </TableCell>
                        <TableCell>
                          {r.setCode?.toUpperCase()}
                          {r.collectorNumber ? ` #${r.collectorNumber}` : ''}
                        </TableCell>
                        <TableCell align="right" sx={{ whiteSpace: 'nowrap' }}>
                          {r.kind === 'Change'
                            ? `${fmt.number(r.oldQuantity)} → ${fmt.number(r.newQuantity)}`
                            : fmt.number(r.kind === 'Add' ? r.newQuantity : r.oldQuantity)}
                        </TableCell>
                        <TableCell align="right">{r.price == null ? '—' : fmt.money(r.price)}</TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              )}
            </>
          )}
          {apply.error && <Alert severity="error">{(apply.error as Error).message}</Alert>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Box sx={{ flexGrow: 1 }} />
        <Button onClick={onClose}>{t('common.actions.cancel')}</Button>
        <Button
          variant="contained"
          disabled={!preview.data || selected.size === 0 || apply.isPending}
          onClick={() => apply.mutate()}
        >
          {t('lists.update.apply', { count: selected.size })}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
