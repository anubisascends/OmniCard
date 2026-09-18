import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Box,
  Button,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControlLabel,
  Menu,
  MenuItem,
  Popover,
  Stack,
  Switch,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material';
import {
  DataGrid,
  type GridColDef,
  type GridPaginationModel,
  type GridRowSelectionModel,
  type GridSortModel,
} from '@mui/x-data-grid';
import ChecklistIcon from '@mui/icons-material/Checklist';
import DeleteIcon from '@mui/icons-material/Delete';
import DriveFileMoveIcon from '@mui/icons-material/DriveFileMove';
import EditIcon from '@mui/icons-material/Edit';
import FileDownloadIcon from '@mui/icons-material/FileDownload';
import SellIcon from '@mui/icons-material/Sell';
import { api } from '../api/client';
import type { CardDto } from '../api/types';
import { CardImage } from './CardImage';
import { CardEditDrawer } from './dialogs/CardEditDrawer';
import { LocationPickerDialog } from './dialogs/LocationPickerDialog';
import { BulkEditCardsDialog } from './dialogs/BulkEditCardsDialog';
import {
  usePreviewScale,
  PREVIEW_BASE_WIDTH,
  PREVIEW_BASE_MAX_HEIGHT,
} from '../lib/previewScale';
import { useFormatters } from '../i18n/format';

const STACK_KEY = 'omnicard.stackDuplicates';

// CSV export formats offered for a selection, mirroring the whole-collection export options.
// Labels are resolved from `collection.exportFormats.<value>` at render time.
const EXPORT_FORMATS: string[] = ['appnative', 'tcgplayer', 'moxfield', 'manabox', 'ticker'];

/**
 * Shared collection card list used by both the Collection page and Location detail. Server-paginated;
 * supports name-stacking (default on, remembered), hover artwork preview, a Select mode with bulk
 * move/delete, click-to-open detail drawer, and striped rows. Scope it with `containerId` (location)
 * and/or `q`/`game` (collection search).
 */
export function CardTable({
  game,
  q,
  containerId,
  showLocation = false,
}: {
  game?: string;
  q?: string;
  containerId?: number;
  showLocation?: boolean;
}) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const qc = useQueryClient();
  const [stacked, setStacked] = useState<boolean>(() => localStorage.getItem(STACK_KEY) !== 'false');
  const [pagination, setPagination] = useState<GridPaginationModel>({ page: 0, pageSize: 100 });
  const [sortModel, setSortModel] = useState<GridSortModel>([{ field: 'name', sort: 'asc' }]);
  const [selectMode, setSelectMode] = useState(false);
  const [selection, setSelection] = useState<GridRowSelectionModel>([]);
  const [detailCardId, setDetailCardId] = useState<number | null>(null);
  const [moveOpen, setMoveOpen] = useState(false);
  const [hover, setHover] = useState<{ el: HTMLElement; url: string; foil: boolean } | null>(null);
  const previewScale = usePreviewScale();

  // Reset to the first page whenever the scope/mode changes so we never sit on an out-of-range page.
  useEffect(() => {
    setPagination((p) => ({ ...p, page: 0 }));
    setSelection([]);
  }, [game, q, containerId, stacked]);

  const sortField = sortModel[0]?.field ?? 'name';
  const sortDir = sortModel[0]?.sort ?? 'asc';

  const query = useQuery({
    queryKey: [
      'collection', containerId ?? null, game ?? null, q ?? '', stacked,
      pagination.page, pagination.pageSize, sortField, sortDir,
    ],
    queryFn: () =>
      api.collection({
        game,
        q,
        containerId,
        stacked,
        skip: pagination.page * pagination.pageSize,
        take: pagination.pageSize,
        sort: sortField,
        dir: sortDir,
      }),
    placeholderData: keepPreviousData,
  });

  const rows = query.data?.items ?? [];
  const lotIdsOf = (c: CardDto) => (c.stackedIds.length ? c.stackedIds : [c.id]);
  const rowsById = useMemo(() => new Map(rows.map((c) => [c.id, c])), [rows]);
  const selectedLotIds = useMemo(
    () => selection.flatMap((id) => (rowsById.get(Number(id)) ? lotIdsOf(rowsById.get(Number(id))!) : [])),
    [selection, rowsById],
  );
  const selectedRows = useMemo(
    () => selection.map((id) => rowsById.get(Number(id))).filter((r): r is CardDto => !!r),
    [selection, rowsById],
  );
  // Bulk listing lists each selected lot as a whole at its row's market price. Rows already on the
  // market (listingStatus set) are excluded here so we never attempt to re-list them.
  const selectedListItems = useMemo(
    () =>
      selectedRows
        .filter((row) => !row.listingStatus)
        .flatMap((row) => lotIdsOf(row).map((lotId) => ({ lotId, price: row.marketPrice }))),
    [selectedRows],
  );
  const alreadyListedCount = useMemo(
    () => selectedRows.filter((r) => r.listingStatus).length,
    [selectedRows],
  );
  const [bulkListOpen, setBulkListOpen] = useState(false);
  const [bulkEditOpen, setBulkEditOpen] = useState(false);
  const [bulkChannel, setBulkChannel] = useState('Manual');
  const [bulkNote, setBulkNote] = useState('');
  const [exportAnchor, setExportAnchor] = useState<HTMLElement | null>(null);
  const exportCsv = useMutation({
    mutationFn: (format: string) => api.exportSelection(selectedLotIds, format),
    onSettled: () => setExportAnchor(null),
  });

  const refresh = () => {
    qc.invalidateQueries({ queryKey: ['collection'] });
    qc.invalidateQueries({ queryKey: ['location'] });
    qc.invalidateQueries({ queryKey: ['locations'] });
    qc.invalidateQueries({ queryKey: ['dashboard'] });
    setSelection([]);
  };
  const move = useMutation({
    mutationFn: (target: number) => api.cardMove(selectedLotIds, target),
    onSuccess: refresh,
  });
  const del = useMutation({
    mutationFn: () => Promise.all(selectedLotIds.map((id) => api.cardDelete(id))).then(() => undefined),
    onSuccess: refresh,
  });
  const bulkList = useMutation({
    mutationFn: () =>
      api.listingBulkCreate({ items: selectedListItems, channel: bulkChannel, note: bulkNote || null }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['listings'] });
      refresh();
      setBulkListOpen(false);
      setBulkNote('');
    },
  });

  const toggleStack = (v: boolean) => {
    setStacked(v);
    localStorage.setItem(STACK_KEY, String(v));
  };

  const columns: GridColDef<CardDto>[] = [
    {
      field: 'name',
      headerName: t('common.labels.name'),
      flex: 2,
      minWidth: 200,
      renderCell: (p) => {
        const count = p.row.stackedIds.length;
        return (
          <Box
            component="span"
            onMouseEnter={(e) => p.row.imageUri && setHover({ el: e.currentTarget, url: p.row.imageUri, foil: p.row.isFoil })}
            onMouseLeave={() => setHover(null)}
          >
            {p.row.name}
            {count > 1 && (
              <Typography component="span" variant="caption" color="text.secondary">
                {' · '}
                {t('collection.printings', { count })}
              </Typography>
            )}
          </Box>
        );
      },
    },
    { field: 'setCode', headerName: t('common.labels.set'), width: 90 },
    { field: 'number', headerName: t('collection.columns.number'), width: 80 },
    { field: 'rarity', headerName: t('common.labels.rarity'), width: 90 },
    { field: 'condition', headerName: t('collection.columns.condition'), width: 80 },
    { field: 'isFoil', headerName: t('common.labels.foil'), width: 70, type: 'boolean' },
    { field: 'quantity', headerName: t('collection.columns.quantity'), width: 70, type: 'number', align: 'right', headerAlign: 'right' },
    {
      field: 'marketPrice',
      headerName: t('collection.columns.market'),
      width: 110,
      align: 'right',
      headerAlign: 'right',
      valueFormatter: (v: number) => (v ? fmt.money(v) : ''),
    },
    {
      field: 'listingStatus',
      headerName: t('common.labels.status'),
      width: 100,
      sortable: false,
      renderCell: (p) =>
        p.row.listingStatus ? (
          <Tooltip
            title={
              p.row.listingStatus === 'Picked'
                ? t('collection.status.pickedTooltip')
                : t('collection.status.listedTooltip')
            }
          >
            <Chip
              size="small"
              color="warning"
              variant="outlined"
              label={t(`common.listingStatus.${p.row.listingStatus}`, { defaultValue: p.row.listingStatus })}
            />
          </Tooltip>
        ) : null,
    },
    ...(showLocation
      ? [{ field: 'containerName', headerName: t('common.labels.location'), flex: 1, minWidth: 120 } as GridColDef<CardDto>]
      : []),
  ];

  return (
    <>
      <Stack direction="row" spacing={2} alignItems="center" flexWrap="wrap" useFlexGap sx={{ mb: 1 }}>
        <FormControlLabel
          control={<Switch checked={stacked} onChange={(e) => toggleStack(e.target.checked)} />}
          label={t('collection.stackDuplicates')}
        />
        <Button
          size="small"
          variant={selectMode ? 'contained' : 'outlined'}
          startIcon={<ChecklistIcon />}
          onClick={() => {
            setSelectMode((v) => !v);
            setSelection([]);
          }}
        >
          {selectMode ? t('common.actions.done') : t('common.actions.select')}
        </Button>
        {selectMode && selectedLotIds.length > 0 && (
          <>
            <Typography variant="body2" color="text.secondary">
              {t('collection.selectedCount', { count: selectedLotIds.length })}
            </Typography>
            <Button size="small" startIcon={<DriveFileMoveIcon />} onClick={() => setMoveOpen(true)}>
              {t('collection.moveTo')}
            </Button>
            <Button size="small" startIcon={<EditIcon />} onClick={() => setBulkEditOpen(true)}>
              {t('collection.bulkEdit')}
            </Button>
            <Button size="small" startIcon={<SellIcon />} onClick={() => setBulkListOpen(true)}>
              {t('collection.listForSale')}
            </Button>
            <Button
              size="small"
              startIcon={<FileDownloadIcon />}
              disabled={exportCsv.isPending}
              onClick={(e) => setExportAnchor(e.currentTarget)}
            >
              {t('collection.exportCsv')}
            </Button>
            <Menu anchorEl={exportAnchor} open={!!exportAnchor} onClose={() => setExportAnchor(null)}>
              {EXPORT_FORMATS.map((f) => (
                <MenuItem key={f} onClick={() => exportCsv.mutate(f)}>
                  {t(`collection.exportFormats.${f}`)}
                </MenuItem>
              ))}
            </Menu>
            <Button
              size="small"
              color="error"
              startIcon={<DeleteIcon />}
              disabled={del.isPending}
              onClick={() => {
                if (confirm(t('collection.confirmDelete', { count: selectedLotIds.length }))) del.mutate();
              }}
            >
              {t('common.actions.delete')}
            </Button>
          </>
        )}
      </Stack>

      <Box sx={{ flexGrow: 1, minHeight: 0 }}>
        <DataGrid
          rows={rows}
          columns={columns}
          rowCount={query.data?.total ?? 0}
          loading={query.isFetching}
          paginationMode="server"
          paginationModel={pagination}
          onPaginationModelChange={setPagination}
          pageSizeOptions={[25, 50, 100]}
          sortingMode="server"
          sortModel={sortModel}
          onSortModelChange={(model) => {
            // Server sorts the whole result set, so jump back to page 1 for the new order.
            setSortModel(model.length ? model : [{ field: 'name', sort: 'asc' }]);
            setPagination((p) => ({ ...p, page: 0 }));
          }}
          density="compact"
          checkboxSelection={selectMode}
          disableRowSelectionOnClick
          rowSelectionModel={selection}
          onRowSelectionModelChange={setSelection}
          onRowClick={(p) => {
            if (!selectMode) setDetailCardId((p.row as CardDto).id);
          }}
          getRowClassName={(p) => (p.indexRelativeToCurrentPage % 2 === 0 ? 'row-even' : 'row-odd')}
          sx={{
            height: '100%',
            '& .row-odd': { bgcolor: 'action.hover' },
            '& .MuiDataGrid-row': { cursor: selectMode ? 'default' : 'pointer' },
          }}
        />
      </Box>

      {/* Hover artwork preview */}
      <Popover
        open={!!hover}
        anchorEl={hover?.el}
        onClose={() => setHover(null)}
        anchorOrigin={{ vertical: 'center', horizontal: 'right' }}
        transformOrigin={{ vertical: 'center', horizontal: 'left' }}
        disableRestoreFocus
        sx={{ pointerEvents: 'none' }}
        slotProps={{ paper: { sx: { p: 0.5 } } }}
      >
        {hover && (
          <CardImage
            src={hover.url}
            foil={hover.foil}
            sx={{
              width: (PREVIEW_BASE_WIDTH * previewScale) / 100,
              maxHeight: (PREVIEW_BASE_MAX_HEIGHT * previewScale) / 100,
              objectFit: 'contain',
              display: 'block',
            }}
          />
        )}
      </Popover>

      <CardEditDrawer cardId={detailCardId} onClose={() => setDetailCardId(null)} />

      <BulkEditCardsDialog
        open={bulkEditOpen}
        lotIds={selectedLotIds}
        onClose={() => setBulkEditOpen(false)}
        onApplied={refresh}
      />

      <LocationPickerDialog
        open={moveOpen}
        title={t('collection.moveDialogTitle', { count: selectedLotIds.length })}
        excludeId={containerId}
        cardGames={[...new Set(selectedRows.map((r) => r.game))]}
        onPick={(id) => {
          setMoveOpen(false);
          move.mutate(id);
        }}
        onClose={() => setMoveOpen(false)}
      />

      <Dialog open={bulkListOpen} onClose={() => setBulkListOpen(false)} fullWidth maxWidth="xs">
        <DialogTitle>{t('collection.bulkList.title', { count: selectedListItems.length })}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ mt: 1 }}>
            <Typography variant="body2" color="text.secondary">
              {t('collection.bulkList.description')}
            </Typography>
            {alreadyListedCount > 0 && (
              <Typography variant="body2" color="warning.main">
                {t('collection.bulkList.alreadyListed', { count: alreadyListedCount })}
              </Typography>
            )}
            <TextField select label={t('common.labels.channel')} value={bulkChannel} onChange={(e) => setBulkChannel(e.target.value)}>
              {(['Manual', 'TcgPlayer', 'Ebay'] as const).map((c) => (
                <MenuItem key={c} value={c}>{t(`common.channels.${c}`)}</MenuItem>
              ))}
            </TextField>
            <TextField label={t('common.labels.note')} value={bulkNote} onChange={(e) => setBulkNote(e.target.value)} multiline minRows={2} />
            {bulkList.error && <Typography color="error" variant="body2">{(bulkList.error as Error).message}</Typography>}
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setBulkListOpen(false)}>{t('common.actions.cancel')}</Button>
          <Button variant="contained" disabled={bulkList.isPending || !selectedListItems.length} onClick={() => bulkList.mutate()}>
            {bulkList.isPending ? t('collection.bulkList.listing') : t('collection.listForSale')}
          </Button>
        </DialogActions>
      </Dialog>
    </>
  );
}
