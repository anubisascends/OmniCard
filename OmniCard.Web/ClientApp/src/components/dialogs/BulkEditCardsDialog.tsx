import { useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
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
  const { t } = useTranslation();
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
      <DialogTitle>{t('dialogs.bulkEdit.title', { count })}</DialogTitle>
      <DialogContent>
        <DialogContentText sx={{ mb: 2 }}>
          {t('dialogs.bulkEdit.instructions')}
        </DialogContentText>
        <Stack spacing={1.5}>
          {row(
            setCondition,
            setSetCondition,
            t('common.labels.condition'),
            <TextField
              select
              size="small"
              fullWidth
              value={condition}
              onChange={(e) => setCondition_(e.target.value)}
            >
              {CONDITIONS.map((c) => (
                <MenuItem key={c} value={c}>
                  {t(`common.conditions.${c}`)}
                </MenuItem>
              ))}
            </TextField>,
          )}
          {row(
            setFoil,
            setSetFoil,
            t('common.labels.foil'),
            <FormControlLabel
              control={<Switch checked={isFoil} onChange={(e) => setIsFoil(e.target.checked)} />}
              label={isFoil ? t('common.labels.foil') : t('dialogs.bulkEdit.nonFoil')}
            />,
          )}
          {row(
            setQuantity,
            setSetQuantity,
            t('common.labels.quantity'),
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
            t('common.labels.purchasePrice'),
            <TextField
              type="number"
              size="small"
              placeholder={t('dialogs.bulkEdit.pricePlaceholder')}
              value={price}
              onChange={(e) => setPrice_(e.target.value)}
              slotProps={{ htmlInput: { min: 0, step: 0.01 } }}
              sx={{ width: 140 }}
            />,
          )}
          {row(
            setNote,
            setSetNote,
            t('common.labels.note'),
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
            t('common.labels.tags'),
            <Stack spacing={1}>
              <TextField
                select
                size="small"
                value={tagsMode}
                onChange={(e) => setTagsMode(e.target.value as 'add' | 'replace')}
                sx={{ width: 160 }}
              >
                <MenuItem value="add">{t('dialogs.bulkEdit.tagsAdd')}</MenuItem>
                <MenuItem value="replace">{t('dialogs.bulkEdit.tagsReplace')}</MenuItem>
              </TextField>
              <Autocomplete
                multiple
                freeSolo
                size="small"
                options={tagOptions}
                value={tags}
                onChange={(_, v) => setTags_(v as string[])}
                renderInput={(params) => <TextField {...params} placeholder={t('common.labels.tags')} />}
              />
              {tagsMode === 'replace' && tags.length === 0 && (
                <Typography variant="caption" color="warning.main">
                  {t('dialogs.bulkEdit.replaceWarning')}
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
        <Button onClick={onClose}>{t('common.actions.cancel')}</Button>
        <Button variant="contained" disabled={!anyField || apply.isPending} onClick={() => apply.mutate()}>
          {t('dialogs.bulkEdit.applyTo', { count })}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
