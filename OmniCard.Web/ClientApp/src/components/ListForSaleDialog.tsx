import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import {
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  InputAdornment,
  MenuItem,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import { api } from '../api/client';

const CHANNELS = ['Manual', 'TcgPlayer', 'Ebay'];

export interface ListForSaleTarget {
  lotId: number;
  name: string;
  quantity: number;
  marketPrice: number;
}

/**
 * Lists a single card lot for sale. When the lot is a stack (quantity > 1) the user can list only part
 * of it — the backend splits the lot so a later "mark picked" moves only the listed copies.
 */
export function ListForSaleDialog({
  target,
  onClose,
  onListed,
}: {
  target: ListForSaleTarget | null;
  onClose: () => void;
  onListed?: () => void;
}) {
  const qc = useQueryClient();
  const open = target != null;

  const [quantity, setQuantity] = useState(1);
  const [channel, setChannel] = useState('Manual');
  const [note, setNote] = useState('');
  // The price keeps its own text so the user can type freely; it reformats to two decimals on blur.
  const [priceText, setPriceText] = useState('0.00');
  const [price, setPrice] = useState(0);

  // Reset the form whenever the dialog opens for a different lot.
  const key = target?.lotId ?? 'none';
  const [formKey, setFormKey] = useState<string | number>(key);
  if (open && formKey !== key) {
    setFormKey(key);
    setQuantity(1);
    setChannel('Manual');
    setNote('');
    setPrice(target!.marketPrice);
    setPriceText(target!.marketPrice.toFixed(2));
  }

  const list = useMutation({
    mutationFn: () =>
      api.listingCreate({ lotId: target!.lotId, quantity, price, channel, note: note || null }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['listings'] });
      qc.invalidateQueries({ queryKey: ['collection'] });
      qc.invalidateQueries({ queryKey: ['location-cards'] });
      qc.invalidateQueries({ queryKey: ['locations'] });
      qc.invalidateQueries({ queryKey: ['dashboard'] });
      onListed?.();
      onClose();
    },
  });

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>List for sale{target ? ` — ${target.name}` : ''}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {target && target.quantity > 1 && (
            <TextField
              label={`Quantity (you have ${target.quantity})`}
              type="number"
              required
              value={quantity}
              onChange={(e) =>
                setQuantity(Math.min(target.quantity, Math.max(1, Math.floor(Number(e.target.value) || 1))))
              }
              slotProps={{ htmlInput: { min: 1, max: target.quantity, step: 1 } }}
              helperText={
                quantity < target.quantity
                  ? `Splits ${quantity} off your stack of ${target.quantity}.`
                  : 'Lists the whole stack.'
              }
              autoFocus
            />
          )}
          <TextField
            label="Price"
            required
            value={priceText}
            onChange={(e) => {
              const text = e.target.value;
              setPriceText(text);
              const n = Number(text);
              if (Number.isFinite(n)) setPrice(n);
            }}
            onBlur={() => {
              const n = Number(priceText);
              const p = Number.isFinite(n) && n >= 0 ? n : 0;
              setPrice(p);
              setPriceText(p.toFixed(2));
            }}
            slotProps={{
              input: { startAdornment: <InputAdornment position="start">$</InputAdornment> },
              htmlInput: { inputMode: 'decimal' },
            }}
            autoFocus={!target || target.quantity <= 1}
          />
          <TextField select label="Channel" value={channel} onChange={(e) => setChannel(e.target.value)}>
            {CHANNELS.map((c) => (
              <MenuItem key={c} value={c}>{c}</MenuItem>
            ))}
          </TextField>
          <TextField label="Note" value={note} onChange={(e) => setNote(e.target.value)} multiline minRows={2} />
          {list.error && <Typography color="error" variant="body2">{(list.error as Error).message}</Typography>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" disabled={price < 0 || list.isPending} onClick={() => list.mutate()}>
          {list.isPending ? 'Listing…' : 'List for sale'}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
