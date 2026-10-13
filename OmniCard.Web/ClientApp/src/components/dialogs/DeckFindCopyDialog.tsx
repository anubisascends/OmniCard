import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useQuery } from '@tanstack/react-query';
import {
  Alert,
  Button,
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
import DoNotDisturbOnOutlinedIcon from '@mui/icons-material/DoNotDisturbOnOutlined';
import { api } from '../../api/client';
import { useFormatters } from '../../i18n/format';
import { LanguageChip } from '../../lib/cardLanguages';
import type { DeckSyncSourceDto } from '../../api/types';
import { sourcePosition } from '../../lib/deckSync';

/**
 * "Find a copy" during a deck sync: searches the collection (the deck box's game, outside the box, in sites
 * the user can change) by card name, so a copy the automatic match missed — another name spelling, a
 * different card standing in — can be put in the deck. Copies listed for sale can't be chosen.
 */
export function DeckFindCopyDialog({
  open,
  deckBoxId,
  cardName,
  onPick,
  onClose,
}: {
  open: boolean;
  deckBoxId: number;
  cardName: string;
  onPick: (source: DeckSyncSourceDto) => void;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const [query, setQuery] = useState('');
  const [debounced, setDebounced] = useState('');

  useEffect(() => {
    if (open) setQuery(cardName.split(' // ')[0]);
  }, [open, cardName]);
  useEffect(() => {
    const h = setTimeout(() => setDebounced(query.trim()), 250);
    return () => clearTimeout(h);
  }, [query]);

  const results = useQuery({
    queryKey: ['deck-sync-search', deckBoxId, debounced],
    queryFn: () => api.deckSyncSearch(deckBoxId, debounced),
    enabled: open && debounced.length >= 2,
  });
  const rows = results.data ?? [];

  return (
    <Dialog open={open} onClose={onClose} maxWidth="md" fullWidth>
      <DialogTitle>{t('deckbox.findCopy.title', { name: cardName })}</DialogTitle>
      <DialogContent dividers>
        <Stack spacing={2}>
          <Typography variant="body2" color="text.secondary">
            {t('deckbox.findCopy.help')}
          </Typography>
          <TextField
            size="small"
            label={t('deckbox.findCopy.searchLabel')}
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            autoFocus
          />
          {debounced.length < 2 && (
            <Typography variant="body2" color="text.secondary">
              {t('deckbox.findCopy.minChars')}
            </Typography>
          )}
          {results.isFetching && <CircularProgress size={24} />}
          {results.error && <Alert severity="error">{(results.error as Error).message}</Alert>}
          {results.data && rows.length === 0 && <Alert severity="info">{t('deckbox.findCopy.noResults')}</Alert>}
          {rows.length > 0 && (
            <Table size="small">
              <TableHead>
                <TableRow>
                  <TableCell>{t('common.labels.name')}</TableCell>
                  <TableCell>{t('deckbox.sync.columns.printing')}</TableCell>
                  <TableCell>{t('common.labels.location')}</TableCell>
                  <TableCell align="right">{t('deckbox.sync.columns.available')}</TableCell>
                  <TableCell />
                </TableRow>
              </TableHead>
              <TableBody>
                {rows.map((s) => (
                  <TableRow key={s.lotId} hover sx={s.ignoredReason === 'Listed' ? { opacity: 0.55 } : undefined}>
                    <TableCell>{s.cardName}</TableCell>
                    <TableCell>
                      <Stack direction="row" spacing={0.5} alignItems="center" component="span">
                        <span>
                          {s.setCode?.toUpperCase()}
                          {s.collectorNumber ? ` #${s.collectorNumber}` : ''}
                          {s.isFoil ? ' ✦' : ''}
                        </span>
                        <LanguageChip language={s.language} />
                      </Stack>
                    </TableCell>
                    <TableCell>{sourcePosition(t, s)}</TableCell>
                    <TableCell align="right">{fmt.number(s.available)}</TableCell>
                    <TableCell align="right">
                      {s.ignoredReason === 'Listed' ? (
                        <Chip
                          size="small"
                          variant="outlined"
                          icon={<DoNotDisturbOnOutlinedIcon />}
                          label={t('deckbox.sync.ignoredReasons.Listed')}
                          sx={{ height: 22, fontSize: 11 }}
                        />
                      ) : (
                        <Button size="small" onClick={() => onPick(s)}>
                          {t('deckbox.findCopy.use')}
                        </Button>
                      )}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('common.actions.close')}</Button>
      </DialogActions>
    </Dialog>
  );
}
