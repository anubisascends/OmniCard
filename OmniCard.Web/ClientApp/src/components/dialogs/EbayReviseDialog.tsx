import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
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
import { api } from '../../api/client';

const CONDITIONS = ['NM', 'LP', 'MP', 'HP', 'DMG'];
const AUCTION_DURATIONS = [1, 3, 5, 7, 10];

export interface EbayReviseTarget {
  listingId: number;
  lotId: number;
  name: string;
  currentPrice: number;
}

/**
 * Updates (revises) an already-published eBay listing. Prefills the eBay fields from a fresh draft
 * (suggested title/description, current condition, catalog category candidates) and the current
 * listed price, lets the user edit them, then re-pushes the offer to eBay and keeps the local
 * listing's price in sync.
 */
export function EbayReviseDialog({
  target,
  onClose,
}: {
  target: EbayReviseTarget | null;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const open = target != null;

  const [priceText, setPriceText] = useState('0.00');
  const [price, setPrice] = useState(0);
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [condition, setCondition] = useState('NM');
  const [listingType, setListingType] = useState('FixedPrice');
  const [auctionDuration, setAuctionDuration] = useState(7);
  const [categoryId, setCategoryId] = useState('');
  const [pushError, setPushError] = useState<string | null>(null);

  // Reset when the dialog opens for a different listing.
  const key = target?.listingId ?? 'none';
  const [formKey, setFormKey] = useState<string | number>(key);
  if (open && formKey !== key) {
    setFormKey(key);
    setPrice(target!.currentPrice);
    setPriceText(target!.currentPrice.toFixed(2));
    setTitle('');
    setDescription('');
    setCondition('NM');
    setListingType('FixedPrice');
    setAuctionDuration(7);
    setCategoryId('');
    setPushError(null);
  }

  const draft = useQuery({
    queryKey: ['ebay-listing-draft', target?.lotId],
    queryFn: () => api.ebayListingPrepare(target!.lotId),
    enabled: open && target != null,
  });

  // Prefill from the draft once, without clobbering later edits (title is the sentinel).
  useEffect(() => {
    if (draft.data && title === '') {
      setTitle(draft.data.suggestedTitle);
      setDescription(draft.data.suggestedDescription);
      setCondition(draft.data.condition || 'NM');
      if (draft.data.categories.length > 0) setCategoryId(draft.data.categories[0].categoryId);
    }
  }, [draft.data, title]);

  const revise = useMutation({
    mutationFn: () =>
      api.ebayListingRevise({
        listingId: target!.listingId,
        lotId: target!.lotId,
        price,
        title,
        description,
        condition,
        listingType,
        auctionDuration: listingType === 'Auction' ? auctionDuration : null,
        categoryId: categoryId || null,
      }),
    onSuccess: (result) => {
      qc.invalidateQueries({ queryKey: ['listings'] });
      if (!result.success) {
        setPushError(result.error || t('sales.ebayRevise.failed'));
        return;
      }
      onClose();
    },
  });

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>{target ? t('sales.ebayRevise.titleNamed', { name: target.name }) : t('sales.ebayRevise.title')}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <TextField
            label={t('common.labels.price')}
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
            autoFocus
          />
          <TextField
            label={t('sales.listForSale.ebay.title')}
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            helperText={t('sales.listForSale.ebay.titleHelper', { len: title.length })}
            slotProps={{ htmlInput: { maxLength: 80 } }}
          />
          <TextField
            label={t('sales.listForSale.ebay.description')}
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            multiline
            minRows={2}
          />
          <TextField
            select
            label={t('sales.listForSale.ebay.condition')}
            value={condition}
            onChange={(e) => setCondition(e.target.value)}
          >
            {CONDITIONS.map((c) => (
              <MenuItem key={c} value={c}>{t(`common.conditions.${c}`, c)}</MenuItem>
            ))}
          </TextField>
          <TextField
            select
            label={t('sales.listForSale.ebay.listingType')}
            value={listingType}
            onChange={(e) => setListingType(e.target.value)}
          >
            <MenuItem value="FixedPrice">{t('sales.listForSale.ebay.fixedPrice')}</MenuItem>
            <MenuItem value="Auction">{t('sales.listForSale.ebay.auction')}</MenuItem>
          </TextField>
          {listingType === 'Auction' && (
            <TextField
              select
              label={t('sales.listForSale.ebay.auctionDuration')}
              value={auctionDuration}
              onChange={(e) => setAuctionDuration(Number(e.target.value))}
            >
              {AUCTION_DURATIONS.map((d) => (
                <MenuItem key={d} value={d}>{t('sales.listForSale.ebay.days', { count: d })}</MenuItem>
              ))}
            </TextField>
          )}
          <TextField
            select
            label={t('sales.listForSale.ebay.category')}
            value={categoryId}
            onChange={(e) => setCategoryId(e.target.value)}
            disabled={draft.isLoading}
            helperText={
              draft.isLoading
                ? t('sales.listForSale.ebay.loadingCategories')
                : (draft.data?.categories.length ?? 0) === 0
                  ? t('sales.listForSale.ebay.noCategories')
                  : undefined
            }
          >
            <MenuItem value="">{t('sales.listForSale.ebay.defaultCategory')}</MenuItem>
            {draft.data?.categories.map((c) => (
              <MenuItem key={c.categoryId} value={c.categoryId}>{c.title}</MenuItem>
            ))}
          </TextField>

          {pushError && <Alert severity="error">{t('sales.ebayRevise.failedWithError', { error: pushError })}</Alert>}
          {revise.error && <Typography color="error" variant="body2">{(revise.error as Error).message}</Typography>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{pushError ? t('common.actions.close') : t('common.actions.cancel')}</Button>
        {!pushError && (
          <Button variant="contained" disabled={price < 0 || revise.isPending || draft.isLoading} onClick={() => revise.mutate()}>
            {revise.isPending ? t('sales.ebayRevise.updating') : t('sales.ebayRevise.update')}
          </Button>
        )}
      </DialogActions>
    </Dialog>
  );
}
