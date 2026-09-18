import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Box,
  Button,
  Card,
  CardContent,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Divider,
  Drawer,
  IconButton,
  MenuItem,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  TextField,
  Typography,
} from '@mui/material';
import { DataGrid, type GridColDef, type GridRowParams } from '@mui/x-data-grid';
import AddIcon from '@mui/icons-material/Add';
import EditIcon from '@mui/icons-material/Edit';
import DeleteIcon from '@mui/icons-material/Delete';
import { api, type LotFields, type ProductFields } from '../api/client';
import type { InventoryLotDto, ProductDto } from '../api/types';
import { locationSelectOptions } from '../components/LocationSelectOptions';
import { useGame } from '../context/GameContext';
import { useFormatters } from '../i18n/format';
import type { TFunction } from 'i18next';

// Sealed categories (Single is managed in the Collection). Server identifiers — not translated.
const CATEGORIES = ['Case', 'Box', 'Pack', 'Deck', 'Bundle', 'Other'];

function buildColumns(
  t: TFunction,
  fmt: ReturnType<typeof useFormatters>,
): GridColDef<ProductDto>[] {
  return [
    { field: 'name', headerName: t('inventory.columns.product'), flex: 2, minWidth: 240 },
    {
      field: 'category',
      headerName: t('inventory.columns.type'),
      width: 110,
      valueFormatter: (v: string) =>
        v && CATEGORIES.includes(v) ? t(`inventory.categories.${v}`) : v,
    },
    { field: 'setCode', headerName: t('inventory.columns.set'), width: 90 },
    { field: 'totalQuantity', headerName: t('inventory.columns.qty'), width: 80, type: 'number' },
    {
      field: 'lastMarketPrice',
      headerName: t('inventory.columns.market'),
      width: 120,
      align: 'right',
      headerAlign: 'right',
      valueFormatter: (v: number | null) => fmt.money(v),
    },
    { field: 'upc', headerName: t('inventory.columns.upc'), width: 140 },
  ];
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <Card sx={{ minWidth: 160, flex: 1 }}>
      <CardContent>
        <Typography variant="overline" color="text.secondary">
          {label}
        </Typography>
        <Typography variant="h5">{value}</Typography>
      </CardContent>
    </Card>
  );
}

function ProductDialog({
  open,
  initial,
  defaultGame,
  onClose,
}: {
  open: boolean;
  initial: ProductDto | null;
  defaultGame: string;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const games = useQuery({ queryKey: ['games'], queryFn: api.games });
  const [fields, setFields] = useState<ProductFields>({
    game: defaultGame,
    category: 'Box',
    name: '',
  });

  const key = initial?.id ?? 'new';
  const [formKey, setFormKey] = useState<string | number>(key);
  if (open && formKey !== key) {
    setFormKey(key);
    setFields(
      initial
        ? {
            game: initial.game,
            category: CATEGORIES.includes(initial.category) ? initial.category : 'Other',
            name: initial.name,
            setName: initial.setName ?? '',
            setCode: initial.setCode ?? '',
            upc: initial.upc ?? '',
            lastMarketPrice: initial.lastMarketPrice ?? null,
          }
        : { game: defaultGame, category: 'Box', name: '' },
    );
  }

  const save = useMutation({
    mutationFn: async () => {
      if (initial) await api.inventoryProductUpdate(initial.id, fields);
      else await api.inventoryProductCreate(fields);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['inventory-products'] });
      qc.invalidateQueries({ queryKey: ['inventory-valuation'] });
      onClose();
    },
  });

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>
        {initial ? t('inventory.productDialog.editTitle') : t('inventory.productDialog.newTitle')}
      </DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <TextField
            label={t('common.labels.name')}
            required
            value={fields.name}
            onChange={(e) => setFields((f) => ({ ...f, name: e.target.value }))}
            autoFocus
          />
          <Stack direction="row" spacing={2}>
            <TextField
              select
              label={t('common.labels.game')}
              value={fields.game}
              onChange={(e) => setFields((f) => ({ ...f, game: e.target.value }))}
              fullWidth
            >
              {games.data?.map((g) => (
                <MenuItem key={g.id} value={g.id}>
                  {g.displayName}
                </MenuItem>
              ))}
            </TextField>
            <TextField
              select
              label={t('common.labels.type')}
              value={fields.category}
              onChange={(e) => setFields((f) => ({ ...f, category: e.target.value }))}
              sx={{ minWidth: 120 }}
            >
              {CATEGORIES.map((c) => (
                <MenuItem key={c} value={c}>
                  {t(`inventory.categories.${c}`)}
                </MenuItem>
              ))}
            </TextField>
          </Stack>
          <Stack direction="row" spacing={2}>
            <TextField
              label={t('inventory.productDialog.setName')}
              value={fields.setName ?? ''}
              onChange={(e) => setFields((f) => ({ ...f, setName: e.target.value }))}
              fullWidth
            />
            <TextField
              label={t('inventory.productDialog.setCode')}
              value={fields.setCode ?? ''}
              onChange={(e) => setFields((f) => ({ ...f, setCode: e.target.value }))}
              sx={{ width: 120 }}
            />
          </Stack>
          <Stack direction="row" spacing={2}>
            <TextField
              label={t('inventory.productDialog.upc')}
              value={fields.upc ?? ''}
              onChange={(e) => setFields((f) => ({ ...f, upc: e.target.value }))}
              fullWidth
            />
            <TextField
              label={t('common.labels.marketPrice')}
              type="number"
              value={fields.lastMarketPrice ?? ''}
              onChange={(e) =>
                setFields((f) => ({
                  ...f,
                  lastMarketPrice: e.target.value === '' ? null : Number(e.target.value),
                }))
              }
              sx={{ width: 140 }}
            />
          </Stack>
          {save.error && (
            <Typography color="error" variant="body2">
              {(save.error as Error).message}
            </Typography>
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('common.actions.cancel')}</Button>
        <Button
          variant="contained"
          disabled={!fields.name.trim() || save.isPending}
          onClick={() => save.mutate()}
        >
          {save.isPending ? t('common.states.saving') : t('common.actions.save')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

function LotDialog({
  open,
  productId,
  initial,
  onClose,
}: {
  open: boolean;
  productId: number;
  initial: InventoryLotDto | null;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const locations = useQuery({ queryKey: ['locations', undefined], queryFn: () => api.locations() });
  const [fields, setFields] = useState<LotFields>({ quantity: 1 });

  const key = initial?.id ?? 'new';
  const [formKey, setFormKey] = useState<string | number>(key);
  if (open && formKey !== key) {
    setFormKey(key);
    setFields(
      initial
        ? {
            quantity: initial.quantity,
            unitCost: initial.unitCost ?? null,
            locationId: initial.locationId ?? null,
            source: initial.source ?? '',
          }
        : { quantity: 1 },
    );
  }

  const save = useMutation({
    mutationFn: async () => {
      if (initial) await api.inventoryUpdateLot(initial.id, fields);
      else await api.inventoryAddLot(productId, fields);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['inventory-lots', productId] });
      qc.invalidateQueries({ queryKey: ['inventory-products'] });
      qc.invalidateQueries({ queryKey: ['inventory-valuation'] });
      onClose();
    },
  });

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>
        {initial ? t('inventory.lotDialog.editTitle') : t('inventory.lotDialog.addTitle')}
      </DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <TextField
            label={t('common.labels.quantity')}
            type="number"
            value={fields.quantity}
            onChange={(e) => setFields((f) => ({ ...f, quantity: Number(e.target.value) }))}
            autoFocus
          />
          <TextField
            label={t('inventory.lotDialog.unitCost')}
            type="number"
            value={fields.unitCost ?? ''}
            onChange={(e) =>
              setFields((f) => ({ ...f, unitCost: e.target.value === '' ? null : Number(e.target.value) }))
            }
          />
          <TextField
            select
            label={t('inventory.lotDialog.locationOptional')}
            value={fields.locationId ?? ''}
            onChange={(e) =>
              setFields((f) => ({ ...f, locationId: e.target.value === '' ? null : Number(e.target.value) }))
            }
          >
            {locationSelectOptions(locations.data, { label: t('inventory.lotDialog.none') })}
          </TextField>
          <TextField
            label={t('inventory.lotDialog.sourceOptional')}
            value={fields.source ?? ''}
            onChange={(e) => setFields((f) => ({ ...f, source: e.target.value }))}
          />
          {save.error && (
            <Typography color="error" variant="body2">
              {(save.error as Error).message}
            </Typography>
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('common.actions.cancel')}</Button>
        <Button
          variant="contained"
          disabled={fields.quantity < 1 || save.isPending}
          onClick={() => save.mutate()}
        >
          {save.isPending ? t('common.states.saving') : t('common.actions.save')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

function ProductDrawer({ product, onClose }: { product: ProductDto; onClose: () => void }) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const qc = useQueryClient();
  const { game } = useGame();
  const lots = useQuery({
    queryKey: ['inventory-lots', product.id],
    queryFn: () => api.inventoryLots(product.id),
  });
  const [editingProduct, setEditingProduct] = useState(false);
  const [lotDialog, setLotDialog] = useState<{ open: boolean; lot: InventoryLotDto | null }>({
    open: false,
    lot: null,
  });

  const delProduct = useMutation({
    mutationFn: () => api.inventoryProductDelete(product.id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['inventory-products'] });
      qc.invalidateQueries({ queryKey: ['inventory-valuation'] });
      onClose();
    },
  });
  const delLot = useMutation({
    mutationFn: (lotId: number) => api.inventoryDeleteLot(lotId),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['inventory-lots', product.id] });
      qc.invalidateQueries({ queryKey: ['inventory-products'] });
      qc.invalidateQueries({ queryKey: ['inventory-valuation'] });
    },
  });

  return (
    <Drawer anchor="right" open onClose={onClose}>
      <Box sx={{ width: 400, p: 2 }}>
        <Stack direction="row" alignItems="center" spacing={1}>
          <Typography variant="h6" sx={{ flexGrow: 1 }}>
            {product.name}
          </Typography>
          <IconButton onClick={() => setEditingProduct(true)} title={t('inventory.drawer.editProduct')}>
            <EditIcon />
          </IconButton>
          <IconButton
            onClick={() => {
              if (confirm(t('inventory.drawer.confirmDeleteProduct', { name: product.name })))
                delProduct.mutate();
            }}
            title={t('inventory.drawer.deleteProduct')}
          >
            <DeleteIcon />
          </IconButton>
        </Stack>
        <Typography variant="body2" color="text.secondary">
          {product.game} · {product.category}
          {product.setCode ? ` · ${product.setCode}` : ''} · {fmt.money(product.lastMarketPrice)}
        </Typography>

        <Divider sx={{ my: 2 }} />

        <Stack direction="row" alignItems="center" sx={{ mb: 1 }}>
          <Typography variant="subtitle1" sx={{ flexGrow: 1 }}>
            {t('inventory.drawer.lots')}
          </Typography>
          <Button size="small" startIcon={<AddIcon />} onClick={() => setLotDialog({ open: true, lot: null })}>
            {t('inventory.drawer.addLot')}
          </Button>
        </Stack>

        {lots.isLoading || !lots.data ? (
          <CircularProgress size={24} />
        ) : lots.data.length === 0 ? (
          <Typography variant="body2" color="text.secondary">
            {t('inventory.drawer.noLots')}
          </Typography>
        ) : (
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell align="right">{t('inventory.drawer.lotColumns.qty')}</TableCell>
                <TableCell align="right">{t('inventory.drawer.lotColumns.cost')}</TableCell>
                <TableCell>{t('inventory.drawer.lotColumns.source')}</TableCell>
                <TableCell align="right"></TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {lots.data.map((lot) => (
                <TableRow key={lot.id} hover>
                  <TableCell align="right">{lot.quantity}</TableCell>
                  <TableCell align="right">{fmt.money(lot.unitCost)}</TableCell>
                  <TableCell>{lot.source}</TableCell>
                  <TableCell align="right">
                    <IconButton size="small" onClick={() => setLotDialog({ open: true, lot })}>
                      <EditIcon fontSize="small" />
                    </IconButton>
                    <IconButton
                      size="small"
                      onClick={() => {
                        if (confirm(t('inventory.drawer.confirmDeleteLot'))) delLot.mutate(lot.id);
                      }}
                    >
                      <DeleteIcon fontSize="small" />
                    </IconButton>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </Box>

      <ProductDialog
        open={editingProduct}
        initial={product}
        defaultGame={game ?? product.game}
        onClose={() => setEditingProduct(false)}
      />
      <LotDialog
        open={lotDialog.open}
        productId={product.id}
        initial={lotDialog.lot}
        onClose={() => setLotDialog({ open: false, lot: null })}
      />
    </Drawer>
  );
}

export function InventoryPage() {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const { game } = useGame();
  const products = useQuery({
    queryKey: ['inventory-products', game],
    queryFn: () => api.inventoryProducts(game),
  });
  const valuation = useQuery({ queryKey: ['inventory-valuation'], queryFn: api.inventoryValuation });
  const [selected, setSelected] = useState<ProductDto | null>(null);
  const [creating, setCreating] = useState(false);

  return (
    <Stack spacing={2} sx={{ height: 'calc(100vh - 120px)' }}>
      <Stack direction="row" alignItems="center">
        <Typography variant="h4" sx={{ flexGrow: 1 }}>
          {t('inventory.title')}
        </Typography>
        <Button variant="contained" startIcon={<AddIcon />} onClick={() => setCreating(true)}>
          {t('inventory.newProduct')}
        </Button>
      </Stack>
      <Stack direction="row" spacing={2}>
        <Stat
          label={t('inventory.stats.totalUnits')}
          value={valuation.data ? fmt.number(valuation.data.totalUnits) : '…'}
        />
        <Stat
          label={t('inventory.stats.cost')}
          value={valuation.data ? fmt.money(valuation.data.totalCost) : '…'}
        />
        <Stat
          label={t('inventory.stats.market')}
          value={valuation.data ? fmt.money(valuation.data.totalMarket) : '…'}
        />
      </Stack>
      <Box sx={{ flexGrow: 1 }}>
        {products.isLoading || !products.data ? (
          <CircularProgress />
        ) : (
          <DataGrid
            rows={products.data}
            columns={buildColumns(t, fmt)}
            density="compact"
            disableRowSelectionOnClick
            onRowClick={(p: GridRowParams<ProductDto>) => setSelected(p.row)}
            sx={{ '& .MuiDataGrid-row': { cursor: 'pointer' } }}
          />
        )}
      </Box>

      {selected && <ProductDrawer product={selected} onClose={() => setSelected(null)} />}
      <ProductDialog
        open={creating}
        initial={null}
        defaultGame={game ?? 'Mtg'}
        onClose={() => setCreating(false)}
      />
    </Stack>
  );
}
