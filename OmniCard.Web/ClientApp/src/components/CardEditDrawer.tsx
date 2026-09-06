import { useEffect, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Autocomplete,
  Box,
  Button,
  Chip,
  CircularProgress,
  Divider,
  Drawer,
  FormControlLabel,
  MenuItem,
  Stack,
  Switch,
  TextField,
  Typography,
} from '@mui/material';
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutline';
import DriveFileMoveIcon from '@mui/icons-material/DriveFileMove';
import SwapHorizIcon from '@mui/icons-material/SwapHoriz';
import SellIcon from '@mui/icons-material/Sell';
import { Snackbar } from '@mui/material';
import { api } from '../api/client';
import { LocationPickerDialog } from './LocationPickerDialog';
import { ListForSaleDialog } from './ListForSaleDialog';

const CONDITIONS = ['NM', 'LP', 'MP', 'HP', 'DMG'];
const money = (n: number) => n.toLocaleString(undefined, { style: 'currency', currency: 'USD' });

export function CardEditDrawer({ cardId, onClose }: { cardId: number | null; onClose: () => void }) {
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
              <Box
                component="img"
                src={card.imageUri}
                alt={card.name}
                sx={{ maxHeight: 260, objectFit: 'contain', alignSelf: 'center' }}
              />
            )}
            <Chip label={`Market ${card.marketPrice ? money(card.marketPrice) : 'n/a'}`} sx={{ alignSelf: 'flex-start' }} />
            <Divider />

            <TextField
              select
              label="Condition"
              size="small"
              value={condition}
              onChange={(e) => setCondition(e.target.value)}
            >
              {CONDITIONS.map((c) => (
                <MenuItem key={c} value={c}>
                  {c}
                </MenuItem>
              ))}
            </TextField>

            <FormControlLabel
              control={<Switch checked={isFoil} onChange={(e) => setIsFoil(e.target.checked)} />}
              label="Foil"
            />

            <TextField
              label="Quantity"
              type="number"
              size="small"
              value={quantity}
              onChange={(e) => setQuantity(Math.max(1, Number(e.target.value)))}
              inputProps={{ min: 1 }}
            />

            <TextField
              label="Purchase price"
              type="number"
              size="small"
              value={purchasePrice}
              onChange={(e) => setPurchasePrice(e.target.value)}
              inputProps={{ step: '0.01', min: 0 }}
            />

            <Stack direction="row" spacing={1} alignItems="center">
              <Box sx={{ flexGrow: 1 }}>
                <Typography variant="caption" color="text.secondary">
                  Location
                </Typography>
                <Typography variant="body2">
                  {locationsQuery.data?.find((l) => l.id === containerId)?.name ??
                    card.containerName ??
                    '— none —'}
                </Typography>
              </Box>
              <Button size="small" startIcon={<DriveFileMoveIcon />} onClick={() => setMoveOpen(true)}>
                Change
              </Button>
            </Stack>

            <Autocomplete
              multiple
              freeSolo
              size="small"
              options={(tagsQuery.data ?? []).map((t) => t.name)}
              value={tags}
              onChange={(_, v) => setTags(v)}
              renderInput={(params) => <TextField {...params} label="Tags" />}
            />

            <Button
              variant="outlined"
              startIcon={<SellIcon />}
              onClick={() => setListOpen(true)}
            >
              List for sale
            </Button>

            <Button
              variant="outlined"
              startIcon={<SwapHorizIcon />}
              onClick={() => addToTrade.mutate()}
              disabled={addToTrade.isPending}
            >
              Add to trade
            </Button>

            <Stack direction="row" spacing={1} justifyContent="space-between">
              <Button
                color="error"
                startIcon={<DeleteOutlineIcon />}
                onClick={() => {
                  if (confirm(`Delete "${card.name}"? This removes the card from your collection.`))
                    del.mutate();
                }}
                disabled={del.isPending}
              >
                Delete
              </Button>
              <Stack direction="row" spacing={1}>
                <Button onClick={onClose}>Cancel</Button>
                <Button variant="contained" onClick={() => save.mutate()} disabled={save.isPending}>
                  {save.isPending ? 'Saving…' : 'Save'}
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
        message="Added to your trade — finalize it on the Trades page."
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
      />

      <Snackbar
        open={listToast}
        autoHideDuration={3000}
        onClose={() => setListToast(false)}
        message="Listed for sale — pull it from Sales ▸ Listings."
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
      />

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
        title="Move card to…"
        onPick={(id) => {
          setContainerId(id);
          setMoveOpen(false);
        }}
        onClose={() => setMoveOpen(false)}
      />
    </Drawer>
  );
}
