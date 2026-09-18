import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  Collapse,
  Divider,
  IconButton,
  Link,
  Paper,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutline';
import SwapHorizIcon from '@mui/icons-material/SwapHoriz';
import ImageNotSupportedIcon from '@mui/icons-material/ImageNotSupported';
import { api } from '../../api/client';
import type { TradeSearchResult, TradeSessionState } from '../../api/types';
import { CardImage } from '../CardImage';
import { useFormatters } from '../../i18n/format';

/**
 * Build up a multi-card trade (cards you're giving away), then finalize with a note, the value you
 * received, and an optional photo. Finalizing applies the trade to your collection (the outgoing
 * lots are marked traded) and it appears in the history below. Owned cards are added by search;
 * off-catalog card-show pickups are captured by name/value/photo.
 */
export function TradeBuilder() {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const money = (n?: number | null) => (n == null ? '—' : fmt.money(n));
  const qc = useQueryClient();
  const sessionQuery = useQuery({ queryKey: ['trade-session'], queryFn: api.tradeSession });
  const session = sessionQuery.data ?? null;

  const setSession = (s: TradeSessionState | null) => qc.setQueryData(['trade-session'], s);
  const [error, setError] = useState<string | null>(null);
  const run = <T,>(p: Promise<T>, after?: (v: T) => void) => {
    setError(null);
    p.then((v) => after?.(v)).catch((e) => setError((e as Error).message));
  };

  // --- Add owned card (debounced search) ---
  const [term, setTerm] = useState('');
  const [debounced, setDebounced] = useState('');
  useEffect(() => {
    const t = setTimeout(() => setDebounced(term.trim()), 250);
    return () => clearTimeout(t);
  }, [term]);
  const searchQuery = useQuery({
    queryKey: ['trade-search', debounced],
    queryFn: () => api.tradeSearch(debounced),
    enabled: !!session && debounced.length >= 2,
  });

  // --- Off-catalog card form ---
  const [offOpen, setOffOpen] = useState(false);
  const [offName, setOffName] = useState('');
  const [offValue, setOffValue] = useState('');
  const [offPhoto, setOffPhoto] = useState<File | null>(null);

  // --- Finalize form ---
  const [note, setNote] = useState('');
  const [receivedValue, setReceivedValue] = useState('');
  const [receivedPhoto, setReceivedPhoto] = useState<File | null>(null);

  const start = useMutation({ mutationFn: api.tradeStart, onSuccess: setSession });
  const addOwned = useMutation({ mutationFn: (lotId: number) => api.tradeAddOwned(lotId), onSuccess: setSession });
  const removeItem = useMutation({ mutationFn: (index: number) => api.tradeRemoveItem(index), onSuccess: setSession });
  const addOffDb = useMutation({
    mutationFn: () => api.tradeAddOffDb(offName, offValue ? Number(offValue) : null, offPhoto),
    onSuccess: (s) => {
      setSession(s);
      setOffName('');
      setOffValue('');
      setOffPhoto(null);
      setOffOpen(false);
    },
  });
  const cancel = useMutation({
    mutationFn: api.tradeCancel,
    onSuccess: () => setSession(null),
  });
  const finalize = useMutation({
    mutationFn: () => api.tradeFinalize(note, receivedValue ? Number(receivedValue) : null, receivedPhoto),
    onSuccess: () => {
      setSession(null);
      setNote('');
      setReceivedValue('');
      setReceivedPhoto(null);
      setTerm('');
      // The trade is now applied: outgoing lots are marked traded and a history row exists.
      qc.invalidateQueries({ queryKey: ['trades'] });
      qc.invalidateQueries({ queryKey: ['collection'] });
      qc.invalidateQueries({ queryKey: ['locations'] });
      qc.invalidateQueries({ queryKey: ['location'] });
      qc.invalidateQueries({ queryKey: ['dashboard'] });
    },
  });

  if (sessionQuery.isLoading) return null;

  // No active trade → a single call-to-action.
  if (!session) {
    return (
      <Paper variant="outlined" sx={{ p: 2 }}>
        <Stack direction="row" spacing={2} alignItems="center" flexWrap="wrap" useFlexGap>
          <Box sx={{ flexGrow: 1 }}>
            <Typography variant="h6">{t('trades.builder.start.title')}</Typography>
            <Typography variant="body2" color="text.secondary">
              {t('trades.builder.start.subtitle')}
            </Typography>
          </Box>
          <Button
            variant="contained"
            startIcon={<SwapHorizIcon />}
            onClick={() => run(start.mutateAsync())}
            disabled={start.isPending}
          >
            {t('trades.builder.start.newTrade')}
          </Button>
        </Stack>
        {error && <Alert severity="error" sx={{ mt: 2 }} onClose={() => setError(null)}>{error}</Alert>}
      </Paper>
    );
  }

  const canFinalize = session.items.length > 0 && !finalize.isPending;

  return (
    <Paper variant="outlined" sx={{ p: 2 }}>
      <Stack spacing={2}>
        <Stack direction="row" spacing={2} alignItems="center" flexWrap="wrap" useFlexGap>
          <Typography variant="h6" sx={{ flexGrow: 1 }}>
            {t('trades.builder.inProgress')}
          </Typography>
          <Typography variant="body2" color="text.secondary">
            {t('trades.builder.giving', {
              count: session.items.length,
              total: money(session.outgoingTotal),
            })}
          </Typography>
          <Button
            size="small"
            color="error"
            onClick={() => {
              if (confirm(t('trades.builder.confirmCancel'))) run(cancel.mutateAsync());
            }}
            disabled={cancel.isPending}
          >
            {t('trades.builder.cancelTrade')}
          </Button>
        </Stack>

        {error && <Alert severity="error" onClose={() => setError(null)}>{error}</Alert>}

        {/* Outgoing cards */}
        {session.items.length === 0 ? (
          <Typography variant="body2" color="text.secondary">
            {t('trades.builder.noCardsYet')}
          </Typography>
        ) : (
          <Stack spacing={0.5}>
            {session.items.map((it) => (
              <Stack key={it.index} direction="row" spacing={1} alignItems="center" sx={{ py: 0.5 }}>
                {it.imageUrl ? (
                  <CardImage
                    src={it.imageUrl}
                    alt={it.cardName}
                    foil={it.foil}
                    wrapperSx={{ flexShrink: 0, borderRadius: 0.5, overflow: 'hidden' }}
                    sx={{ width: 34, aspectRatio: '0.72', objectFit: 'contain', display: 'block' }}
                  />
                ) : (
                  <Box
                    sx={{
                      width: 34,
                      aspectRatio: '0.72',
                      display: 'grid',
                      placeItems: 'center',
                      bgcolor: 'action.hover',
                      borderRadius: 0.5,
                      flexShrink: 0,
                    }}
                  >
                    <ImageNotSupportedIcon fontSize="small" color="disabled" />
                  </Box>
                )}
                <Box sx={{ flexGrow: 1, minWidth: 0 }}>
                  <Typography variant="body2" noWrap>
                    {it.cardName}
                    {it.foil ? t('trades.foilSuffix') : ''}
                    {it.isOffDatabase ? t('trades.offCatalogSuffix') : ''}
                  </Typography>
                  <Typography variant="caption" color="text.secondary" noWrap sx={{ display: 'block' }}>
                    {it.setCode ? it.setCode.toUpperCase() : ''}
                    {it.collectorNumber ? ` #${it.collectorNumber}` : ''}
                    {it.estimatedValue != null ? ` · ${fmt.money(it.estimatedValue)}` : ''}
                    {it.tcgPlayerUrl ? ' · ' : ''}
                    {it.tcgPlayerUrl && (
                      <Link href={it.tcgPlayerUrl} target="_blank" rel="noopener" variant="caption">
                        {t('common.channels.TcgPlayer')}
                      </Link>
                    )}
                  </Typography>
                </Box>
                <IconButton
                  size="small"
                  aria-label={t('common.actions.remove')}
                  onClick={() => run(removeItem.mutateAsync(it.index))}
                >
                  <DeleteOutlineIcon fontSize="small" />
                </IconButton>
              </Stack>
            ))}
          </Stack>
        )}

        <Divider />

        {/* Add an owned card */}
        <Box>
          <TextField
            fullWidth
            size="small"
            label={t('trades.builder.addOwnedLabel')}
            placeholder={t('trades.builder.addOwnedPlaceholder')}
            value={term}
            onChange={(e) => setTerm(e.target.value)}
          />
          {debounced.length >= 2 && (
            <Paper variant="outlined" sx={{ mt: 0.5, maxHeight: 240, overflowY: 'auto' }}>
              {searchQuery.isFetching && (
                <Typography variant="caption" color="text.secondary" sx={{ p: 1, display: 'block' }}>
                  {t('common.states.searching')}
                </Typography>
              )}
              {searchQuery.data?.length === 0 && !searchQuery.isFetching && (
                <Typography variant="caption" color="text.secondary" sx={{ p: 1, display: 'block' }}>
                  {t('trades.builder.noOwnedMatch')}
                </Typography>
              )}
              {searchQuery.data?.map((r: TradeSearchResult) => (
                <Stack
                  key={r.lotId}
                  direction="row"
                  spacing={1}
                  alignItems="center"
                  onClick={() => run(addOwned.mutateAsync(r.lotId))}
                  sx={{ p: 0.5, cursor: 'pointer', '&:hover': { bgcolor: 'action.hover' } }}
                >
                  <CardImage
                    src={r.imageUrl ?? undefined}
                    alt={r.name}
                    foil={r.isFoil}
                    wrapperSx={{ flexShrink: 0, borderRadius: 0.5, overflow: 'hidden' }}
                    sx={{ width: 28, aspectRatio: '0.72', objectFit: 'contain', display: 'block' }}
                  />
                  <Box sx={{ flexGrow: 1, minWidth: 0 }}>
                    <Typography variant="body2" noWrap>
                      {r.name}
                      {r.isFoil ? t('trades.foilSuffix') : ''}
                    </Typography>
                    <Typography variant="caption" color="text.secondary" noWrap sx={{ display: 'block' }}>
                      {r.setCode?.toUpperCase()} · #{r.number} · {r.condition} · {fmt.money(r.marketPrice)}
                    </Typography>
                  </Box>
                  <AddIcon fontSize="small" color="action" />
                </Stack>
              ))}
            </Paper>
          )}
        </Box>

        {/* Add an off-catalog card */}
        <Box>
          <Button size="small" onClick={() => setOffOpen((v) => !v)}>
            {offOpen ? t('trades.builder.hideOffCatalog') : t('trades.builder.showOffCatalog')}
          </Button>
          <Collapse in={offOpen}>
            <Stack spacing={1} sx={{ mt: 1 }}>
              <TextField size="small" label={t('trades.builder.cardNameOptional')} value={offName} onChange={(e) => setOffName(e.target.value)} />
              <TextField
                size="small"
                label={t('trades.builder.estimatedValueOptional')}
                type="number"
                value={offValue}
                onChange={(e) => setOffValue(e.target.value)}
                inputProps={{ step: '0.01', min: 0 }}
              />
              <Button component="label" size="small" variant="outlined">
                {offPhoto ? offPhoto.name : t('trades.builder.photoOptional')}
                <input
                  hidden
                  type="file"
                  accept="image/*"
                  capture="environment"
                  onChange={(e) => setOffPhoto(e.target.files?.[0] ?? null)}
                />
              </Button>
              <Button
                variant="contained"
                size="small"
                startIcon={<AddIcon />}
                onClick={() => run(addOffDb.mutateAsync())}
                disabled={addOffDb.isPending}
                sx={{ alignSelf: 'flex-start' }}
              >
                {t('trades.builder.addCard')}
              </Button>
            </Stack>
          </Collapse>
        </Box>

        <Divider />

        {/* Finalize */}
        <Stack spacing={1}>
          <Typography variant="subtitle2">{t('trades.builder.finalize')}</Typography>
          <TextField
            size="small"
            label={t('trades.builder.noteLabel')}
            multiline
            minRows={2}
            value={note}
            onChange={(e) => setNote(e.target.value)}
          />
          <TextField
            size="small"
            label={t('trades.builder.valueReceivedOptional')}
            type="number"
            value={receivedValue}
            onChange={(e) => setReceivedValue(e.target.value)}
            inputProps={{ step: '0.01', min: 0 }}
          />
          <Button component="label" size="small" variant="outlined" sx={{ alignSelf: 'flex-start' }}>
            {receivedPhoto ? receivedPhoto.name : t('trades.builder.receivedPhotoOptional')}
            <input
              hidden
              type="file"
              accept="image/*"
              capture="environment"
              onChange={(e) => setReceivedPhoto(e.target.files?.[0] ?? null)}
            />
          </Button>
          <Button
            variant="contained"
            color="success"
            startIcon={<SwapHorizIcon />}
            onClick={() => {
              if (confirm(t('trades.builder.confirmFinalize', { count: session.items.length })))
                run(finalize.mutateAsync());
            }}
            disabled={!canFinalize}
            sx={{ alignSelf: 'flex-start' }}
          >
            {finalize.isPending ? t('trades.builder.finalizing') : t('trades.builder.finalizeTrade')}
          </Button>
        </Stack>
      </Stack>
    </Paper>
  );
}
