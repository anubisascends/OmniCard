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
  Stack,
  Switch,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material';
import {
  DataGrid,
  GRID_BOOLEAN_COL_DEF,
  type GridColDef,
  type GridColumnVisibilityModel,
  type GridPaginationModel,
  type GridRowSelectionModel,
  type GridSortModel,
} from '@mui/x-data-grid';
import ChecklistIcon from '@mui/icons-material/Checklist';
import ChevronRightIcon from '@mui/icons-material/ChevronRight';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import UnfoldLessIcon from '@mui/icons-material/UnfoldLess';
import UnfoldMoreIcon from '@mui/icons-material/UnfoldMore';
import DeleteIcon from '@mui/icons-material/Delete';
import DriveFileMoveIcon from '@mui/icons-material/DriveFileMove';
import EditIcon from '@mui/icons-material/Edit';
import FileDownloadIcon from '@mui/icons-material/FileDownload';
import SellIcon from '@mui/icons-material/Sell';
import { api } from '../api/client';
import { EXPORT_FORMATS } from '../lib/exportFormats';
import { LanguageChip, languageName } from '../lib/cardLanguages';
import type { CardDto, CardGroupDto } from '../api/types';
import { CardHoverPreview, type CardHover } from './CardHoverPreview';
import { CardEditDrawer } from './dialogs/CardEditDrawer';
import { LocationPickerDialog } from './dialogs/LocationPickerDialog';
import { BulkEditCardsDialog } from './dialogs/BulkEditCardsDialog';
import { useFormatters } from '../i18n/format';
import { orderColumns, PAGE_SIZES, type ViewState } from '../lib/savedViews';
import { ColumnLayoutMenu } from './views/ColumnLayoutMenu';
import { GROUPABLE_FIELDS, GroupByMenu } from './views/GroupByMenu';

/** A grid row: a card, or (in grouped mode) a group header dressed as an empty card with a negative id. */
type TableRow = CardDto & { group?: CardGroupDto };

const EMPTY_CARD: CardDto = {
  id: 0, game: '', gameCardId: '', name: '', setName: '', setCode: '', number: '', rarity: '',
  condition: '', language: '', isFoil: false, quantity: 0, stackedIds: [], tags: [], marketPrice: 0,
  isMissing: false, isTraded: false,
};

/** Which groups are collapsed: every group follows `byDefault` except those listed in `toggled`. */
interface CollapseState {
  byDefault: boolean;
  toggled: string[];
}
const ALL_EXPANDED: CollapseState = { byDefault: false, toggled: [] };

// CSV export formats offered for a selection, mirroring the whole-collection export options.
// Labels are resolved from `collection.exportFormats.<value>` at render time.

/**
 * Shared collection card list used by both the Collection page and Location detail. Server-paginated;
 * supports name-stacking, hover artwork preview, a Select mode with bulk move/delete, click-to-open
 * detail drawer, and striped rows. Scope it with `containerId` (location) and/or `game`. The layout —
 * search, sort, page size, stacking, columns and row grouping — is the page's saved-view state
 * (`view`), changed through `onViewChange`.
 *
 * Row grouping (`view.groupColumns`, outermost first) is done by the server over the whole result set
 * and paged as one sequence of group-header + card rows; headers span the row and click to collapse.
 * Which groups are collapsed is per-visit state (groups open expanded), not part of the saved view.
 */
export function CardTable({
  game,
  containerId,
  showLocation = false,
  view,
  onViewChange,
}: {
  game?: string;
  containerId?: number;
  showLocation?: boolean;
  view: ViewState;
  onViewChange: (patch: Partial<ViewState>) => void;
}) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const qc = useQueryClient();
  const { q, stacked, pageSize, sort: sortField, dir: sortDir } = view;
  const [page, setPage] = useState(0);
  const pagination: GridPaginationModel = { page, pageSize };
  const sortModel: GridSortModel = [{ field: sortField, sort: sortDir }];
  const [selectMode, setSelectMode] = useState(false);
  const [selection, setSelection] = useState<GridRowSelectionModel>([]);
  const [detailCardId, setDetailCardId] = useState<number | null>(null);
  const [moveOpen, setMoveOpen] = useState(false);
  const [hover, setHover] = useState<CardHover | null>(null);

  // Row grouping. Game grouping only means something across games; location only where it's shown.
  const groupFields: string[] = GROUPABLE_FIELDS.filter(
    (f) => (f !== 'game' || !game) && (f !== 'containerName' || showLocation),
  );
  const groupColumns = (view.groupColumns ?? []).filter((f) => groupFields.includes(f));
  const grouped = groupColumns.length > 0;
  const groupKey = groupColumns.join(',');
  const [collapse, setCollapse] = useState<CollapseState>(ALL_EXPANDED);

  // Reset to the first page whenever the scope/mode changes so we never sit on an out-of-range page.
  // A new sort or page size also starts over: the server sorts the whole result set.
  useEffect(() => {
    setPage(0);
    setSelection([]);
  }, [game, q, containerId, stacked, sortField, sortDir, pageSize, groupKey]);

  // A different grouping (or scope) starts with every group open again.
  useEffect(() => setCollapse(ALL_EXPANDED), [game, containerId, groupKey]);

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
    enabled: !grouped,
  });

  const groupedQuery = useQuery({
    queryKey: [
      'collection', 'grouped', containerId ?? null, game ?? null, q ?? '', stacked,
      pagination.page, pagination.pageSize, sortField, sortDir, groupKey, collapse,
    ],
    queryFn: () =>
      api.collectionGrouped({
        game,
        q,
        containerId,
        stacked,
        skip: pagination.page * pagination.pageSize,
        take: pagination.pageSize,
        sort: sortField,
        dir: sortDir,
        groupBy: groupColumns,
        collapsedByDefault: collapse.byDefault,
        toggled: collapse.toggled,
      }),
    placeholderData: keepPreviousData,
    enabled: grouped,
  });
  const total = (grouped ? groupedQuery.data?.total : query.data?.total) ?? 0;

  // Collapsing groups shrinks the list; step back if this page no longer exists.
  useEffect(() => {
    const lastPage = Math.max(0, Math.ceil(total / pageSize) - 1);
    if (grouped && groupedQuery.data && page > lastPage) setPage(lastPage);
  }, [grouped, groupedQuery.data, total, page, pageSize]);

  const tableRows: TableRow[] = useMemo(
    () =>
      grouped
        ? (groupedQuery.data?.items ?? []).map((r, i) =>
            r.group ? { ...EMPTY_CARD, id: -(i + 1), group: r.group } : r.card!,
          )
        : query.data?.items ?? [],
    [grouped, groupedQuery.data, query.data],
  );
  const rows: CardDto[] = useMemo(() => tableRows.filter((r) => !r.group), [tableRows]);

  const toggleGroup = (id: string) =>
    setCollapse((c) => ({
      ...c,
      toggled: c.toggled.includes(id) ? c.toggled.filter((x) => x !== id) : [...c.toggled, id],
    }));
  const setAllCollapsed = (byDefault: boolean) => {
    setCollapse({ byDefault, toggled: [] });
    setPage(0);
  };

  /** A group's value as shown in its header; server data (set / location / game names) is shown as is. */
  const groupValueLabel = (g: CardGroupDto) => {
    if (g.field === 'isFoil') return g.key === 'true' ? t('common.labels.foil') : t('collection.grouping.nonFoil');
    if (!g.key) {
      if (g.field === 'listingStatus') return t('collection.grouping.notListed');
      if (g.field === 'containerName') return t('collection.grouping.noLocation');
      return t('collection.grouping.none');
    }
    if (g.field === 'language') return languageName(t, g.key);
    if (g.field === 'listingStatus') return t(`common.listingStatus.${g.key}`, { defaultValue: g.key });
    return g.label || g.key;
  };

  const renderGroupHeader = (g: CardGroupDto) => (
    <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.75, pl: g.level * 3, height: '100%', minWidth: 0 }}>
      {g.collapsed ? <ChevronRightIcon fontSize="small" /> : <ExpandMoreIcon fontSize="small" />}
      <Typography variant="body2" color="text.secondary" noWrap sx={{ flexShrink: 0 }}>
        {t(`collection.grouping.fields.${g.field}`)}:
      </Typography>
      <Typography variant="body2" fontWeight={600} noWrap>
        {groupValueLabel(g)}
      </Typography>
      {g.continued && (
        <Typography variant="caption" color="text.secondary" noWrap sx={{ flexShrink: 0 }}>
          {t('collection.grouping.continued')}
        </Typography>
      )}
      <Typography variant="caption" color="text.secondary" noWrap sx={{ flexShrink: 0, ml: 1 }}>
        {t('collection.grouping.summary', { count: g.quantity, value: fmt.money(g.value) })}
      </Typography>
    </Box>
  );
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

  const allColumns: GridColDef<TableRow>[] = [
    {
      field: 'name',
      headerName: t('common.labels.name'),
      hideable: false,
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
    {
      field: 'language',
      headerName: t('collection.columns.language'),
      width: 70,
      // English is the default — only call out foreign copies.
      renderCell: (p) => <LanguageChip language={p.row.language} />,
    },
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
      ? [{ field: 'containerName', headerName: t('common.labels.location'), flex: 1, minWidth: 120 } as GridColDef<TableRow>]
      : []),
  ];

  // The view's column order and hidden columns. Hidden columns stay in the grid (switched off through
  // its visibility model) so the grid's own column menu can show them again.
  const defaultOrder = allColumns.map((c) => c.field);
  const columnsByField = new Map(allColumns.map((c) => [c.field, c]));
  const columns = orderColumns(defaultOrder, view.columnOrder).map((f) => columnsByField.get(f)!);
  const columnVisibility: GridColumnVisibilityModel = Object.fromEntries(
    view.hiddenColumns.filter((f) => f !== 'name').map((f) => [f, false]),
  );

  // Grouped: a header row spans the whole row from the first visible column, which draws it.
  const visibleColumns = columns.filter((c) => columnVisibility[c.field] !== false);
  const gridColumns: GridColDef<TableRow>[] = grouped
    ? columns.map((c) => {
        if (c.field !== visibleColumns[0]?.field) return c;
        const renderCard = c.renderCell ?? (c.type === 'boolean' ? GRID_BOOLEAN_COL_DEF.renderCell : undefined);
        return {
          ...c,
          colSpan: (_value, row) => (row.group ? visibleColumns.length : undefined),
          renderCell: (p) =>
            p.row.group ? renderGroupHeader(p.row.group) : renderCard ? renderCard(p) : p.formattedValue ?? p.value,
        } as GridColDef<TableRow>;
      })
    : columns;

  return (
    <>
      <Stack direction="row" spacing={2} alignItems="center" flexWrap="wrap" useFlexGap sx={{ mb: 1 }}>
        <FormControlLabel
          control={<Switch checked={stacked} onChange={(e) => onViewChange({ stacked: e.target.checked })} />}
          label={t('collection.stackDuplicates')}
        />
        <ColumnLayoutMenu
          columns={columns.map((c) => ({ field: c.field, label: c.headerName ?? c.field, hideable: c.hideable !== false }))}
          defaultOrder={defaultOrder}
          hidden={view.hiddenColumns}
          onChange={onViewChange}
        />
        <GroupByMenu fields={groupFields} value={groupColumns} onChange={(groupColumns) => onViewChange({ groupColumns })} />
        {grouped && (
          <>
            <Tooltip describeChild title={t('collection.grouping.collapseAllTooltip')}>
              <Button size="small" startIcon={<UnfoldLessIcon />} onClick={() => setAllCollapsed(true)}>
                {t('collection.grouping.collapseAll')}
              </Button>
            </Tooltip>
            <Tooltip describeChild title={t('collection.grouping.expandAllTooltip')}>
              <Button size="small" startIcon={<UnfoldMoreIcon />} onClick={() => setAllCollapsed(false)}>
                {t('collection.grouping.expandAll')}
              </Button>
            </Tooltip>
          </>
        )}
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
          rows={tableRows}
          columns={gridColumns}
          rowCount={total}
          loading={grouped ? groupedQuery.isFetching : query.isFetching}
          paginationMode="server"
          paginationModel={pagination}
          onPaginationModelChange={(m) => {
            if (m.pageSize !== pageSize) onViewChange({ pageSize: m.pageSize });
            else setPage(m.page);
          }}
          pageSizeOptions={[...PAGE_SIZES]}
          sortingMode="server"
          sortModel={sortModel}
          onSortModelChange={(model) =>
            onViewChange(
              model[0]?.sort
                ? { sort: model[0].field, dir: model[0].sort }
                : { sort: 'name', dir: 'asc' },
            )
          }
          columnVisibilityModel={columnVisibility}
          onColumnVisibilityModelChange={(m) =>
            onViewChange({ hiddenColumns: Object.keys(m).filter((f) => m[f] === false) })
          }
          density="compact"
          checkboxSelection={selectMode}
          isRowSelectable={(p) => !p.row.group}
          disableRowSelectionOnClick
          rowSelectionModel={selection}
          onRowSelectionModelChange={setSelection}
          onRowClick={(p) => {
            const row = p.row as TableRow;
            if (row.group) toggleGroup(row.group.id);
            else if (!selectMode) setDetailCardId(row.id);
          }}
          getRowClassName={(p) =>
            p.row.group ? 'row-group' : p.indexRelativeToCurrentPage % 2 === 0 ? 'row-even' : 'row-odd'
          }
          sx={{
            height: '100%',
            '& .row-odd': { bgcolor: 'action.hover' },
            '& .MuiDataGrid-row': { cursor: selectMode ? 'default' : 'pointer' },
            '& .MuiDataGrid-row.row-group': { bgcolor: 'action.selected', cursor: 'pointer' },
          }}
        />
      </Box>

      {/* Hover artwork preview */}
      <CardHoverPreview hover={hover} onClose={() => setHover(null)} />

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
