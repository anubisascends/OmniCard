import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
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
  Tooltip,
  Typography,
} from '@mui/material';
import { api } from '../../api/client';
import { useFormatters } from '../../i18n/format';
import { LanguageChip, languageName } from '../../lib/cardLanguages';
import type { CardListDto, ListItemSubstitutesDto, ListSubstituteCandidateDto } from '../../api/types';

const pickKey = (itemId: number, lotId: number) => `${itemId}:${lotId}`;

/**
 * "Find in collection": for every list card the exact-printing match left short, offers owned copies of
 * other printings with the same name (in the list's forced language, if any). Quantities start at the
 * server's suggestion; nothing changes until the user applies them, which turns the chosen copies into
 * stand-in list items pointing at those lots.
 */
export function ListSubstitutesDialog({
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
  const found = useQuery({
    queryKey: ['list-substitutes', list.id],
    queryFn: () => api.listSubstitutes(list.id),
    enabled: open,
    refetchOnMount: 'always',
    gcTime: 0,
  });
  const [picks, setPicks] = useState<Record<string, number>>({});

  useEffect(() => {
    if (!found.data) return;
    const initial: Record<string, number> = {};
    for (const item of found.data)
      for (const c of item.candidates) if (c.suggested > 0) initial[pickKey(item.itemId, c.lotId)] = c.suggested;
    setPicks(initial);
  }, [found.data]);

  const apply = useMutation({
    mutationFn: () =>
      api.listApplySubstitutes(
        list.id,
        Object.entries(picks)
          .filter(([, quantity]) => quantity > 0)
          .map(([key, quantity]) => {
            const [itemId, lotId] = key.split(':').map(Number);
            return { itemId, lotId, quantity };
          }),
      ),
    onSuccess: () => {
      onApplied();
      onClose();
    },
  });

  const items = found.data ?? [];
  const withCandidates = items.filter((i) => i.candidates.length > 0);
  const chosen = (item: ListItemSubstitutesDto) =>
    item.candidates.reduce((sum, c) => sum + (picks[pickKey(item.itemId, c.lotId)] ?? 0), 0);

  // A lot can be offered to several cards; its copies can only be used once in total.
  const overUsedLots = useMemo(() => {
    const used = new Map<number, { total: number; available: number }>();
    for (const item of items)
      for (const c of item.candidates) {
        const q = picks[pickKey(item.itemId, c.lotId)] ?? 0;
        const u = used.get(c.lotId) ?? { total: 0, available: c.available };
        used.set(c.lotId, { total: u.total + q, available: c.available });
      }
    return new Set([...used].filter(([, u]) => u.total > u.available).map(([lotId]) => lotId));
  }, [items, picks]);
  const overNeeded = items.some((i) => chosen(i) > i.missing);
  const totalChosen = Object.values(picks).reduce((a, b) => a + b, 0);

  const position = (c: ListSubstituteCandidateDto) =>
    [
      c.locationName,
      c.section,
      c.page != null ? t('lists.substitutes.page', { page: c.page }) : null,
      c.slot != null ? t('lists.substitutes.slot', { slot: c.slot }) : null,
    ]
      .filter(Boolean)
      .join(' · ');

  return (
    <Dialog open={open} onClose={onClose} maxWidth="md" fullWidth>
      <DialogTitle>{t('lists.substitutes.title')}</DialogTitle>
      <DialogContent dividers>
        <Stack spacing={2}>
          <Typography variant="body2" color="text.secondary">
            {list.language
              ? t('lists.substitutes.introLanguage', { language: languageName(t, list.language) })
              : t('lists.substitutes.intro')}
          </Typography>
          {found.isLoading && <CircularProgress size={28} />}
          {found.error && <Alert severity="error">{(found.error as Error).message}</Alert>}
          {found.data && items.length === 0 && <Alert severity="success">{t('lists.substitutes.nothingMissing')}</Alert>}
          {found.data && items.length > 0 && withCandidates.length === 0 && (
            <Alert severity="info">{t('lists.substitutes.noneFound')}</Alert>
          )}

          {withCandidates.map((item) => (
            <Box key={item.itemId}>
              <Typography variant="subtitle2">
                {item.cardName}
                {item.isFoil ? ' ✦' : ''}{' '}
                <Typography component="span" variant="body2" color="text.secondary">
                  {item.setCode?.toUpperCase()}
                  {item.collectorNumber ? ` #${item.collectorNumber}` : ''}
                  {' · '}
                  {t('lists.substitutes.need', { count: item.missing })}
                </Typography>
              </Typography>
              {chosen(item) > item.missing && (
                <Typography variant="caption" color="error">
                  {t('lists.substitutes.tooMany', { count: item.missing })}
                </Typography>
              )}
              <Table size="small">
                <TableHead>
                  <TableRow>
                    <TableCell>{t('lists.substitutes.columns.printing')}</TableCell>
                    <TableCell>{t('lists.substitutes.columns.condition')}</TableCell>
                    <TableCell>{t('common.labels.location')}</TableCell>
                    <TableCell align="right">{t('lists.substitutes.columns.available')}</TableCell>
                    <TableCell align="right">{t('lists.substitutes.columns.use')}</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {item.candidates.map((c) => {
                    const key = pickKey(item.itemId, c.lotId);
                    return (
                      <TableRow key={c.lotId} hover>
                        <TableCell>
                          <Tooltip
                            disableInteractive
                            slotProps={{ tooltip: { sx: { bgcolor: 'transparent', p: 0, maxWidth: 'none' } } }}
                            title={
                              c.imageUri ? (
                                <Box
                                  component="img"
                                  src={c.imageUri}
                                  alt=""
                                  sx={{ width: 220, borderRadius: 2, display: 'block', boxShadow: 6 }}
                                />
                              ) : (
                                ''
                              )
                            }
                          >
                            <Stack direction="row" spacing={0.5} alignItems="center" component="span">
                              <span>
                                {c.setCode?.toUpperCase()}
                                {c.collectorNumber ? ` #${c.collectorNumber}` : ''}
                                {c.isFoil ? ' ✦' : ''}
                              </span>
                              <LanguageChip language={c.language} />
                            </Stack>
                          </Tooltip>
                        </TableCell>
                        <TableCell>{c.condition ? t(`common.conditions.${c.condition}`, { defaultValue: c.condition }) : '—'}</TableCell>
                        <TableCell>{position(c)}</TableCell>
                        <TableCell align="right">{fmt.number(c.available)}</TableCell>
                        <TableCell align="right">
                          <TextField
                            type="number"
                            size="small"
                            value={picks[key] ?? 0}
                            error={overUsedLots.has(c.lotId)}
                            onChange={(e) => {
                              const q = Math.max(0, Math.min(Number(e.target.value) || 0, c.available));
                              setPicks((prev) => ({ ...prev, [key]: q }));
                            }}
                            slotProps={{ htmlInput: { min: 0, max: c.available } }}
                            sx={{ width: 76 }}
                          />
                        </TableCell>
                      </TableRow>
                    );
                  })}
                </TableBody>
              </Table>
            </Box>
          ))}
          {overUsedLots.size > 0 && <Alert severity="error">{t('lists.substitutes.lotOverUsed')}</Alert>}
          {apply.error && <Alert severity="error">{(apply.error as Error).message}</Alert>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('common.actions.cancel')}</Button>
        <Button
          variant="contained"
          disabled={totalChosen === 0 || overNeeded || overUsedLots.size > 0 || apply.isPending}
          onClick={() => apply.mutate()}
        >
          {t('lists.substitutes.apply', { count: totalChosen })}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
