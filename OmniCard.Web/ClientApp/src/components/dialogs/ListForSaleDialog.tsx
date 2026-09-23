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
  Divider,
  InputAdornment,
  MenuItem,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import { api } from '../../api/client';

const CHANNELS = ['Manual', 'TcgPlayer', 'Ebay'];
const CONDITIONS = ['NM', 'LP', 'MP', 'HP', 'DMG'];
const AUCTION_DURATIONS = [1, 3, 5, 7, 10];

export interface ListForSaleTarget {
  lotId: number;
  name: string;
  quantity: number;
  marketPrice: number;
}

/**
 * Lists a single card lot for sale. When the lot is a stack (quantity > 1) the user can list only part
 * of it — the backend splits the lot so a later "mark picked" moves only the listed copies.
 *
 * When the eBay channel is chosen, an extra section appears (title, description, condition, listing
 * type, and an eBay category picked from a catalog search) and listing pushes a published offer to
 * eBay. The lot is always listed locally; if the eBay push fails the error is surfaced inline and the
 * local listing is kept so the user can fix setup and retry from the Manage Listings screen.
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
  const { t } = useTranslation();
  const qc = useQueryClient();
  const open = target != null;

  const [quantity, setQuantity] = useState(1);
  const [channel, setChannel] = useState('Manual');
  const [note, setNote] = useState('');
  // The price keeps its own text so the user can type freely; it reformats to two decimals on blur.
  const [priceText, setPriceText] = useState('0.00');
  const [price, setPrice] = useState(0);

  // eBay-specific fields (only used when channel === 'Ebay').
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [condition, setCondition] = useState('NM');
  const [listingType, setListingType] = useState('FixedPrice');
  const [auctionDuration, setAuctionDuration] = useState(7);
  const [categoryId, setCategoryId] = useState('');
  // Set once the local listing succeeded but the eBay push failed — keeps the error visible and stops
  // a re-submit (which would fail because the lot is already listed).
  const [ebayError, setEbayError] = useState<string | null>(null);

  const isEbay = channel === 'Ebay';

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
    setTitle('');
    setDescription('');
    setCondition('NM');
    setListingType('FixedPrice');
    setAuctionDuration(7);
    setCategoryId('');
    setEbayError(null);
  }

  const ebayStatus = useQuery({
    queryKey: ['ebay-status'],
    queryFn: () => api.ebayStatus(),
    enabled: open && isEbay,
  });

  const draft = useQuery({
    queryKey: ['ebay-listing-draft', target?.lotId],
    queryFn: () => api.ebayListingPrepare(target!.lotId),
    enabled: open && isEbay && target != null,
  });

  // Prefill the eBay fields once the draft arrives, without clobbering later user edits: apply only
  // while the fields are still empty (title is the sentinel — a draft always suggests one).
  useEffect(() => {
    if (isEbay && draft.data && title === '') {
      setTitle(draft.data.suggestedTitle);
      setDescription(draft.data.suggestedDescription);
      setCondition(draft.data.condition || 'NM');
      if (draft.data.categories.length > 0) setCategoryId(draft.data.categories[0].categoryId);
    }
  }, [isEbay, draft.data, title]);

  const invalidate = () => {
    qc.invalidateQueries({ queryKey: ['listings'] });
    qc.invalidateQueries({ queryKey: ['collection'] });
    qc.invalidateQueries({ queryKey: ['location-cards'] });
    qc.invalidateQueries({ queryKey: ['locations'] });
    qc.invalidateQueries({ queryKey: ['dashboard'] });
  };

  const list = useMutation({
    mutationFn: async () => {
      if (isEbay) {
        return api.ebayListingCreate({
          lotId: target!.lotId,
          quantity,
          price,
          note: note || null,
          title,
          description,
          condition,
          listingType,
          auctionDuration: listingType === 'Auction' ? auctionDuration : null,
          categoryId: categoryId || null,
        });
      }
      await api.listingCreate({ lotId: target!.lotId, quantity, price, channel, note: note || null });
      return null;
    },
    onSuccess: (result) => {
      invalidate();
      onListed?.();
      // eBay: the lot is listed locally even if the eBay push failed — keep the dialog open to show
      // the error, otherwise close as usual.
      if (result && !result.success) {
        setEbayError(result.error || t('sales.listForSale.ebay.pushFailed'));
        return;
      }
      onClose();
    },
  });

  const ebayNotReady = isEbay && ebayStatus.data != null && !ebayStatus.data.connected;
  const listDisabled =
    price < 0 || list.isPending || ebayError != null || ebayNotReady || (isEbay && draft.isLoading);

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>{target ? t('sales.listForSale.titleNamed', { name: target.name }) : t('sales.listForSale.title')}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {target && target.quantity > 1 && (
            <TextField
              label={t('sales.listForSale.quantityYouHave', { qty: target.quantity })}
              type="number"
              required
              value={quantity}
              onChange={(e) =>
                setQuantity(Math.min(target.quantity, Math.max(1, Math.floor(Number(e.target.value) || 1))))
              }
              slotProps={{ htmlInput: { min: 1, max: target.quantity, step: 1 } }}
              helperText={
                quantity < target.quantity
                  ? t('sales.listForSale.splitsHelper', { quantity, total: target.quantity })
                  : t('sales.listForSale.listsWholeStack')
              }
              autoFocus
            />
          )}
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
            autoFocus={!target || target.quantity <= 1}
          />
          <TextField select label={t('common.labels.channel')} value={channel} onChange={(e) => setChannel(e.target.value)}>
            {CHANNELS.map((c) => (
              <MenuItem key={c} value={c}>{t(`common.channels.${c}`)}</MenuItem>
            ))}
          </TextField>
          <TextField label={t('common.labels.note')} value={note} onChange={(e) => setNote(e.target.value)} multiline minRows={2} />

          {isEbay && (
            <>
              <Divider textAlign="left">
                <Typography variant="caption" color="text.secondary">{t('sales.listForSale.ebay.sectionTitle')}</Typography>
              </Divider>

              {ebayNotReady && (
                <Alert severity="warning">{t('sales.listForSale.ebay.notConnected')}</Alert>
              )}

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

              {ebayError && <Alert severity="error">{t('sales.listForSale.ebay.listedLocallyError', { error: ebayError })}</Alert>}
            </>
          )}

          {list.error && <Typography color="error" variant="body2">{(list.error as Error).message}</Typography>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{ebayError ? t('common.actions.close') : t('common.actions.cancel')}</Button>
        {!ebayError && (
          <Button variant="contained" disabled={listDisabled} onClick={() => list.mutate()}>
            {list.isPending
              ? t('sales.listForSale.listing')
              : isEbay
                ? t('sales.listForSale.ebay.listOnEbay')
                : t('sales.listForSale.title')}
          </Button>
        )}
      </DialogActions>
    </Dialog>
  );
}
