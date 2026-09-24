import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Autocomplete,
  Box,
  Button,
  Chip,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Divider,
  Drawer,
  FormControlLabel,
  MenuItem,
  Stack,
  Switch,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material';
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutline';
import DriveFileMoveIcon from '@mui/icons-material/DriveFileMove';
import SwapHorizIcon from '@mui/icons-material/SwapHoriz';
import SellIcon from '@mui/icons-material/Sell';
import CallSplitIcon from '@mui/icons-material/CallSplit';
import { Snackbar } from '@mui/material';
import { api } from '../../api/client';
import { useFormatters } from '../../i18n/format';
import { CardImage } from '../CardImage';
import { LocationPickerDialog } from './LocationPickerDialog';
import { ListForSaleDialog } from './ListForSaleDialog';

const CONDITIONS = ['NM', 'LP', 'MP', 'HP', 'DMG'];

export function CardEditDrawer({ cardId, onClose }: { cardId: number | null; onClose: () => void }) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const qc = useQueryClient();
  const open = cardId != null;

  const cardQuery = useQuery({
    queryKey: ['card', cardId],
    queryFn: () => api.card(cardId!),
    enabled: open,
  });
  const locationsQuery = useQuery({ queryKey: ['locations', undefined], queryFn: () => api.locations() });
  const tagsQuery = useQuery({ queryKey: ['tags'], queryFn: api.tags });

  const [condition, setCondition] = useState('NM');
  const [isFoil, setIsFoil] = useState(false);
  const [quantity, setQuantity] = useState(1);
  const [purchasePrice, setPurchasePrice] = useState<string>('');
  const [note, setNote] = useState<string>('');
  const [containerId, setContainerId] = useState<number | ''>('');
  const [tags, setTags] = useState<string[]>([]);
  const [moveOpen, setMoveOpen] = useState(false);

  const card = cardQuery.data;
  useEffect(() => {
    if (card) {
      setCondition(card.condition);
      setIsFoil(card.isFoil);
      setQuantity(card.quantity);
      setPurchasePrice(card.purchasePrice?.toString() ?? '');
      setNote(card.note ?? '');
      setContainerId(card.containerId ?? '');
      setTags(card.tags);
    }
  }, [card]);

  const invalidate = () => {
    qc.invalidateQueries({ queryKey: ['collection'] });
    qc.invalidateQueries({ queryKey: ['location-cards'] });
    qc.invalidateQueries({ queryKey: ['locations'] });
    qc.invalidateQueries({ queryKey: ['dashboard'] });
  };

  const save = useMutation({
    mutationFn: async () => {
      if (!card) return;
      await api.cardUpdate(card.id, {
        condition,
        isFoil,
        foilType: card.foilType,
        quantity,
        purchasePrice: purchasePrice === '' ? null : Number(purchasePrice),
        note: note.trim() === '' ? null : note.trim(),
      });
      await api.cardSetTags(card.id, tags);
      if (containerId !== '' && containerId !== card.containerId) {
        await api.cardMove([card.id], containerId as number);
      }
    },
    onSuccess: () => {
      invalidate();
      onClose();
    },
  });

  const del = useMutation({
    mutationFn: () => api.cardDelete(card!.id),
    onSuccess: () => {
      invalidate();
      onClose();
    },
  });

  const [tradeToast, setTradeToast] = useState(false);
  const addToTrade = useMutation({
    mutationFn: () => api.tradeAddOwned(card!.id),
    onSuccess: (s) => {
      qc.setQueryData(['trade-session'], s);
      setTradeToast(true);
    },
  });

  const [listOpen, setListOpen] = useState(false);
  const [listToast, setListToast] = useState(false);

  // Split-stack: move some copies of a stacked lot into a new loose lot (e.g. to place each in its
  // own binder slot). Only offered when the lot holds more than one copy.
  const [splitOpen, setSplitOpen] = useState(false);
  const [splitToast, setSplitToast] = useState(false);
  const [splitQty, setSplitQty] = useState(1);
  const split = useMutation({
    mutationFn: () => api.cardSplit(card!.id, splitQty),
    onSuccess: () => {
      invalidate();
      // The new loose copies land in the binder's Unplaced pool — refresh those views too.
      qc.invalidateQueries({ queryKey: ['binder-unplaced'] });
      qc.invalidateQueries({ queryKey: ['binder'] });
      setSplitOpen(false);
      setSplitToast(true);
      onClose();
    },
  });

  return (
    <Drawer anchor="right" open={open} onClose={onClose}>
      <Box sx={{ width: 380, p: 2 }}>
        {!card ? (
          <CircularProgress />
        ) : (
          <Stack spacing={2}>
            <Typography variant="h6">{card.name}</Typography>
            <Typography variant="body2" color="text.secondary">
              {card.setName} · #{card.number} · {card.rarity}
            </Typography>
            {card.imageUri && (
              <CardImage
                src={card.imageUri}
                alt={card.name}
                foil={isFoil}
                wrapperSx={{ alignSelf: 'center' }}
                sx={{ maxHeight: 260, objectFit: 'contain' }}
              />
            )}
            <Stack direction="row" spacing={1} sx={{ alignSelf: 'flex-start' }}>
              <Chip
                label={t('dialogs.cardEdit.market', {
                  price: card.marketPrice ? fmt.money(card.marketPrice, 'USD') : t('dialogs.cardEdit.notAvailable'),
                })}
              />
              {card.listingStatus && (
                <Chip
                  color="warning"
                  variant="outlined"
                  icon={<SellIcon />}
                  label={card.listingStatus === 'Picked' ? t('dialogs.cardEdit.pickedForSale') : t('dialogs.cardEdit.listedForSale')}
                />
              )}
            </Stack>
            <Divider />

            <TextField
              select
              label={t('common.labels.condition')}
              size="small"
              value={condition}
              onChange={(e) => setCondition(e.target.value)}
            >
              {CONDITIONS.map((c) => (
                <MenuItem key={c} value={c}>
                  {t(`common.conditions.${c}`)}
                </MenuItem>
              ))}
            </TextField>

            <FormControlLabel
              control={<Switch checked={isFoil} onChange={(e) => setIsFoil(e.target.checked)} />}
              label={t('common.labels.foil')}
            />

            <TextField
              label={t('common.labels.quantity')}
              type="number"
              size="small"
              value={quantity}
              onChange={(e) => setQuantity(Math.max(1, Number(e.target.value)))}
              inputProps={{ min: 1 }}
            />

            <TextField
              label={t('common.labels.purchasePrice')}
              type="number"
              size="small"
              value={purchasePrice}
              onChange={(e) => setPurchasePrice(e.target.value)}
              inputProps={{ step: '0.01', min: 0 }}
            />

            <TextField
              label={t('common.labels.note')}
              size="small"
              multiline
              minRows={2}
              value={note}
              onChange={(e) => setNote(e.target.value)}
              placeholder={t('dialogs.cardEdit.notePlaceholder')}
            />

            <Stack direction="row" spacing={1} alignItems="center">
              <Box sx={{ flexGrow: 1 }}>
                <Typography variant="caption" color="text.secondary">
                  {t('common.labels.location')}
                </Typography>
                <Typography variant="body2">
                  {locationsQuery.data?.find((l) => l.id === containerId)?.name ??
                    card.containerName ??
                    t('dialogs.cardEdit.noLocation')}
                </Typography>
              </Box>
              <Button size="small" startIcon={<DriveFileMoveIcon />} onClick={() => setMoveOpen(true)}>
                {t('common.actions.change')}
              </Button>
            </Stack>

            <Autocomplete
              multiple
              freeSolo
              size="small"
              options={(tagsQuery.data ?? []).map((t) => t.name)}
              value={tags}
              onChange={(_, v) => setTags(v)}
              renderInput={(params) => <TextField {...params} label={t('common.labels.tags')} />}
            />

            <Tooltip
              title={card.listingStatus ? t('dialogs.cardEdit.alreadyListedTooltip') : ''}
            >
              {/* span keeps the tooltip working while the button is disabled */}
              <span>
                <Button
                  variant="outlined"
                  startIcon={<SellIcon />}
                  onClick={() => setListOpen(true)}
                  disabled={!!card.listingStatus}
                  fullWidth
                >
                  {card.listingStatus ? t('dialogs.cardEdit.alreadyListed') : t('dialogs.cardEdit.listForSale')}
                </Button>
              </span>
            </Tooltip>

            <Button
              variant="outlined"
              startIcon={<SwapHorizIcon />}
              onClick={() => addToTrade.mutate()}
              disabled={addToTrade.isPending}
            >
              {t('dialogs.cardEdit.addToTrade')}
            </Button>

            {card.quantity > 1 && (
              <Tooltip title={card.listingStatus ? t('dialogs.cardEdit.splitListedTooltip') : ''}>
                <span>
                  <Button
                    variant="outlined"
                    startIcon={<CallSplitIcon />}
                    onClick={() => { setSplitQty(1); setSplitOpen(true); }}
                    disabled={!!card.listingStatus}
                    fullWidth
                  >
                    {t('dialogs.cardEdit.splitStack')}
                  </Button>
                </span>
              </Tooltip>
            )}

            <Stack direction="row" spacing={1} justifyContent="space-between">
              <Button
                color="error"
                startIcon={<DeleteOutlineIcon />}
                onClick={() => {
                  if (confirm(t('dialogs.cardEdit.deleteConfirm', { name: card.name })))
                    del.mutate();
                }}
                disabled={del.isPending}
              >
                {t('common.actions.delete')}
              </Button>
              <Stack direction="row" spacing={1}>
                <Button onClick={onClose}>{t('common.actions.cancel')}</Button>
                <Button variant="contained" onClick={() => save.mutate()} disabled={save.isPending}>
                  {save.isPending ? t('common.states.saving') : t('common.actions.save')}
                </Button>
              </Stack>
            </Stack>
          </Stack>
        )}
      </Box>

      <Snackbar
        open={tradeToast}
        autoHideDuration={3000}
        onClose={() => setTradeToast(false)}
        message={t('dialogs.cardEdit.tradeToast')}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
      />

      <Snackbar
        open={listToast}
        autoHideDuration={3000}
        onClose={() => setListToast(false)}
        message={t('dialogs.cardEdit.listToast')}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
      />

      <Snackbar
        open={splitToast}
        autoHideDuration={3000}
        onClose={() => setSplitToast(false)}
        message={t('dialogs.cardEdit.splitToast')}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
      />

      {card && (
        <Dialog open={splitOpen} onClose={() => setSplitOpen(false)} fullWidth maxWidth="xs">
          <DialogTitle>{t('dialogs.cardEdit.splitTitle', { name: card.name })}</DialogTitle>
          <DialogContent>
            <Stack spacing={2} sx={{ mt: 1 }}>
              <TextField
                label={t('dialogs.cardEdit.splitQtyLabel')}
                type="number"
                value={splitQty}
                onChange={(e) =>
                  setSplitQty(Math.min(card.quantity - 1, Math.max(1, Math.floor(Number(e.target.value) || 1))))
                }
                slotProps={{ htmlInput: { min: 1, max: card.quantity - 1, step: 1 } }}
                helperText={t('dialogs.cardEdit.splitHelper', {
                  split: splitQty,
                  remaining: card.quantity - splitQty,
                })}
                autoFocus
              />
              {split.error && (
                <Typography color="error" variant="body2">{(split.error as Error).message}</Typography>
              )}
            </Stack>
          </DialogContent>
          <DialogActions>
            <Button onClick={() => setSplitOpen(false)}>{t('common.actions.cancel')}</Button>
            <Button variant="contained" onClick={() => split.mutate()} disabled={split.isPending}>
              {split.isPending ? t('dialogs.cardEdit.splitting') : t('dialogs.cardEdit.splitStack')}
            </Button>
          </DialogActions>
        </Dialog>
      )}

      <ListForSaleDialog
        target={
          listOpen && card
            ? { lotId: card.id, name: card.name, quantity: card.quantity, marketPrice: card.marketPrice }
            : null
        }
        onClose={() => setListOpen(false)}
        onListed={() => setListToast(true)}
      />

      <LocationPickerDialog
        open={moveOpen}
        title={t('dialogs.cardEdit.moveTitle')}
        cardGames={card ? [card.game] : undefined}
        onPick={(id) => {
          setContainerId(id);
          setMoveOpen(false);
        }}
        onClose={() => setMoveOpen(false)}
      />
    </Drawer>
  );
}
