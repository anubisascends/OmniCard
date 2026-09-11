import { useState, type ReactNode } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import {
  Autocomplete,
  Box,
  Button,
  Checkbox,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  FormControlLabel,
  MenuItem,
  Stack,
  Switch,
  TextField,
  Typography,
} from '@mui/material';
import { api } from '../../api/client';

const CONDITIONS = ['NM', 'LP', 'MP', 'HP', 'DMG'];

/**
 * Bulk-edit the given lots. Each field has a checkbox that opts it into the change — only ticked
 * fields are sent, so untouched fields keep each card's own value. Tags can be added (union) or
 * replace the card's whole tag set. Mirrors the single-card editor's fields for parity.
 */
export function BulkEditCardsDialog({
  open,
  lotIds,
  onClose,
  onApplied,
}: {
  open: boolean;
  lotIds: number[];
  onClose: () => void;
  onApplied: () => void;
}) {
  const count = lotIds.length;
  const tagsQuery = useQuery({ queryKey: ['tags'], queryFn: api.tags, enabled: open });

  // Per-field enable flags + values.
  const [setCondition, setSetCondition] = useState(false);
  const [condition, setCondition_] = useState('NM');
  const [setFoil, setSetFoil] = useState(false);
  const [isFoil, setIsFoil] = useState(false);
  const [setQuantity, setSetQuantity] = useState(false);
  const [quantity, setQuantity_] = useState(1);
  const [setPrice, setSetPrice] = useState(false);
  const [price, setPrice_] = useState('');
  const [setNote, setSetNote] = useState(false);
  const [note, setNote_] = useState('');
  const [setTags, setSetTags] = useState(false);
  const [tagsMode, setTagsMode] = useState<'add' | 'replace'>('add');
  const [tags, setTags_] = useState<string[]>([]);

  const anyField = setCondition || setFoil || setQuantity || setPrice || setNote || setTags;

  // Reset the form each time the dialog (re)opens.
  const [wasOpen, setWasOpen] = useState(false);
  if (open && !wasOpen) {
    setWasOpen(true);
    setSetCondition(false);
    setSetFoil(false);
    setSetQuantity(false);
    setSetPrice(false);
    setSetNote(false);
    setSetTags(false);
    setTags_([]);
  }
  if (!open && wasOpen) setWasOpen(false);

  const apply = useMutation({
    mutationFn: () =>
      api.cardBulkUpdate({
        cardIds: lotIds,
        setCondition,
        condition,
        setFoil,
        isFoil,
        setQuantity,
        quantity,
        setPurchasePrice: setPrice,
        purchasePrice: price.trim() === '' ? null : Number(price),
        setNote,
        note: note.trim() === '' ? null : note.trim(),
        setTags,
        tagsMode,
        tags,
      }),
    onSuccess: () => {
      onApplied();
      onClose();
    },
  });

  // One row: a checkbox that enables the field, plus its (dimmed when disabled) control.
  const row = (enabled: boolean, toggle: (v: boolean) => void, label: string, control: ReactNode) => (
    <Stack direction="row" spacing={1.5} alignItems="flex-start">
      <FormControlLabel
        sx={{ minWidth: 150, mr: 0 }}
        control={<Checkbox checked={enabled} onChange={(e) => toggle(e.target.checked)} />}
        label={label}
      />
      <Box sx={{ flexGrow: 1, opacity: enabled ? 1 : 0.5, pointerEvents: enabled ? 'auto' : 'none' }}>
        {control}
      </Box>
    </Stack>
  );

  const tagOptions = (tagsQuery.data ?? []).map((t) => t.name);

  return (
    <Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth>
      <DialogTitle>Edit {count} selected card{count === 1 ? '' : 's'}</DialogTitle>
      <DialogContent>
        <DialogContentText sx={{ mb: 2 }}>
          Tick a property to apply it to every selected card. Unticked properties are left unchanged.
        </DialogContentText>
        <Stack spacing={1.5}>
          {row(
            setCondition,
            setSetCondition,
            'Condition',
            <TextField
              select
              size="small"
              fullWidth
              value={condition}
              onChange={(e) => setCondition_(e.target.value)}
            >
              {CONDITIONS.map((c) => (
                <MenuItem key={c} value={c}>
                  {c}
                </MenuItem>
              ))}
            </TextField>,
          )}
          {row(
            setFoil,
            setSetFoil,
            'Foil',
            <FormControlLabel
              control={<Switch checked={isFoil} onChange={(e) => setIsFoil(e.target.checked)} />}
              label={isFoil ? 'Foil' : 'Non-foil'}
            />,
          )}
          {row(
            setQuantity,
            setSetQuantity,
            'Quantity',
            <TextField
              type="number"
              size="small"
              value={quantity}
              onChange={(e) => setQuantity_(Math.max(1, Number(e.target.value) || 1))}
              slotProps={{ htmlInput: { min: 1 } }}
              sx={{ width: 120 }}
            />,
          )}
          {row(
            setPrice,
            setSetPrice,
            'Purchase price',
            <TextField
              type="number"
              size="small"
              placeholder="0.00"
              value={price}
              onChange={(e) => setPrice_(e.target.value)}
              slotProps={{ htmlInput: { min: 0, step: 0.01 } }}
              sx={{ width: 140 }}
            />,
          )}
          {row(
            setNote,
            setSetNote,
            'Note',
            <TextField
              size="small"
              fullWidth
              multiline
              maxRows={3}
              value={note}
              onChange={(e) => setNote_(e.target.value)}
            />,
          )}
          {row(
            setTags,
            setSetTags,
            'Tags',
            <Stack spacing={1}>
              <TextField
                select
                size="small"
                value={tagsMode}
                onChange={(e) => setTagsMode(e.target.value as 'add' | 'replace')}
                sx={{ width: 160 }}
              >
                <MenuItem value="add">Add to existing</MenuItem>
                <MenuItem value="replace">Replace all</MenuItem>
              </TextField>
              <Autocomplete
                multiple
                freeSolo
                size="small"
                options={tagOptions}
                value={tags}
                onChange={(_, v) => setTags_(v as string[])}
                renderInput={(params) => <TextField {...params} placeholder="Tags" />}
              />
              {tagsMode === 'replace' && tags.length === 0 && (
                <Typography variant="caption" color="warning.main">
                  Replace with no tags will clear every selected card's tags.
                </Typography>
              )}
            </Stack>,
          )}
        </Stack>
        {apply.isError && (
          <Typography variant="caption" color="error" sx={{ mt: 1, display: 'block' }}>
            {(apply.error as Error).message}
          </Typography>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" disabled={!anyField || apply.isPending} onClick={() => apply.mutate()}>
          Apply to {count}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
