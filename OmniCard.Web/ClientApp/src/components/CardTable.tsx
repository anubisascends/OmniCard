import { useEffect, useMemo, useState } from 'react';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Box,
  Button,
  FormControlLabel,
  Popover,
  Stack,
  Switch,
  Typography,
} from '@mui/material';
import {
  DataGrid,
  type GridColDef,
  type GridPaginationModel,
  type GridRowSelectionModel,
} from '@mui/x-data-grid';
import ChecklistIcon from '@mui/icons-material/Checklist';
import DeleteIcon from '@mui/icons-material/Delete';
import DriveFileMoveIcon from '@mui/icons-material/DriveFileMove';
import { api } from '../api/client';
import type { CardDto } from '../api/types';
import { CardEditDrawer } from './CardEditDrawer';
import { LocationPickerDialog } from './LocationPickerDialog';

const money = (n: number) => n.toLocaleString(undefined, { style: 'currency', currency: 'USD' });
const STACK_KEY = 'omnicard.stackDuplicates';

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
  const qc = useQueryClient();
  const [stacked, setStacked] = useState<boolean>(() => localStorage.getItem(STACK_KEY) !== 'false');
  const [pagination, setPagination] = useState<GridPaginationModel>({ page: 0, pageSize: 100 });
  const [selectMode, setSelectMode] = useState(false);
  const [selection, setSelection] = useState<GridRowSelectionModel>([]);
  const [detailCardId, setDetailCardId] = useState<number | null>(null);
  const [moveOpen, setMoveOpen] = useState(false);
  const [hover, setHover] = useState<{ el: HTMLElement; url: string } | null>(null);

  // Reset to the first page whenever the scope/mode changes so we never sit on an out-of-range page.
  useEffect(() => {
    setPagination((p) => ({ ...p, page: 0 }));
    setSelection([]);
  }, [game, q, containerId, stacked]);

  const query = useQuery({
    queryKey: ['collection', containerId ?? null, game ?? null, q ?? '', stacked, pagination.page, pagination.pageSize],
    queryFn: () =>
      api.collection({
        game,
        q,
        containerId,
        stacked,
        skip: pagination.page * pagination.pageSize,
        take: pagination.pageSize,
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

  const toggleStack = (v: boolean) => {
    setStacked(v);
    localStorage.setItem(STACK_KEY, String(v));
  };

  const columns: GridColDef<CardDto>[] = [
    {
      field: 'name',
      headerName: 'Name',
      flex: 2,
      minWidth: 200,
      renderCell: (p) => {
        const count = p.row.stackedIds.length;
        return (
          <Box
            component="span"
            onMouseEnter={(e) => p.row.imageUri && setHover({ el: e.currentTarget, url: p.row.imageUri })}
            onMouseLeave={() => setHover(null)}
          >
            {p.row.name}
            {count > 1 && (
              <Typography component="span" variant="caption" color="text.secondary">
                {' · '}
                {count} printings
              </Typography>
            )}
          </Box>
        );
      },
    },
    { field: 'setCode', headerName: 'Set', width: 90 },
    { field: 'number', headerName: 'No.', width: 80 },
    { field: 'rarity', headerName: 'Rarity', width: 90 },
    { field: 'condition', headerName: 'Cond', width: 80 },
    { field: 'isFoil', headerName: 'Foil', width: 70, type: 'boolean' },
    { field: 'quantity', headerName: 'Qty', width: 70, type: 'number', align: 'right', headerAlign: 'right' },
    {
      field: 'marketPrice',
      headerName: 'Market',
      width: 110,
      align: 'right',
      headerAlign: 'right',
      valueFormatter: (v: number) => (v ? money(v) : ''),
    },
    ...(showLocation
      ? [{ field: 'containerName', headerName: 'Location', flex: 1, minWidth: 120 } as GridColDef<CardDto>]
      : []),
  ];

  return (
    <>
      <Stack direction="row" spacing={2} alignItems="center" flexWrap="wrap" useFlexGap sx={{ mb: 1 }}>
        <FormControlLabel
          control={<Switch checked={stacked} onChange={(e) => toggleStack(e.target.checked)} />}
          label="Stack duplicates"
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
          {selectMode ? 'Done' : 'Select'}
        </Button>
        {selectMode && selectedLotIds.length > 0 && (
          <>
            <Typography variant="body2" color="text.secondary">
              {selectedLotIds.length} card(s) selected
            </Typography>
            <Button size="small" startIcon={<DriveFileMoveIcon />} onClick={() => setMoveOpen(true)}>
              Move to…
            </Button>
            <Button
              size="small"
              color="error"
              startIcon={<DeleteIcon />}
              disabled={del.isPending}
              onClick={() => {
                if (confirm(`Delete ${selectedLotIds.length} card(s)? This cannot be undone.`)) del.mutate();
              }}
            >
              Delete
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
          pageSizeOptions={[50, 100, 250]}
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
          <Box
            component="img"
            src={hover.url}
            alt=""
            sx={{ width: 240, maxHeight: 340, objectFit: 'contain', display: 'block' }}
          />
        )}
      </Popover>

      <CardEditDrawer cardId={detailCardId} onClose={() => setDetailCardId(null)} />

      <LocationPickerDialog
        open={moveOpen}
        title={`Move ${selectedLotIds.length} card(s) to…`}
        excludeId={containerId}
        onPick={(id) => {
          setMoveOpen(false);
          move.mutate(id);
        }}
        onClose={() => setMoveOpen(false)}
      />
    </>
  );
}
