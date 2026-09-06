import { useMemo, useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Button,
  ButtonBase,
  CircularProgress,
  Collapse,
  IconButton,
  Link,
  Menu,
  MenuItem,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import { DataGrid, type GridColDef } from '@mui/x-data-grid';
import AddIcon from '@mui/icons-material/Add';
import MoreVertIcon from '@mui/icons-material/MoreVert';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import ChevronRightIcon from '@mui/icons-material/ChevronRight';

const COLLAPSED_KEY = 'omnicard.locations.collapsed';
import { api } from '../api/client';
import type { LocationSummaryDto } from '../api/types';
import { useGame } from '../context/GameContext';

const money = (n: number) => n.toLocaleString(undefined, { style: 'currency', currency: 'USD' });

const TYPES = [
  { value: 'Binder', label: 'Binder' },
  { value: 'Box', label: 'Box' },
  { value: 'DeckBox', label: 'Deck Box' },
  { value: 'DisplayCase', label: 'Display Case' },
];

import { groupLocations } from '../lib/locationGroups';

function AddLocationBar({ onAdded }: { onAdded: () => void }) {
  const [name, setName] = useState('');
  const [type, setType] = useState('Box');

  const trimmed = name.trim();
  const nameCheck = useQuery({
    queryKey: ['loc-name-available', trimmed],
    queryFn: () => api.locationNameAvailable(trimmed),
    enabled: trimmed.length > 0,
  });
  const taken = trimmed.length > 0 && nameCheck.data?.available === false;

  const create = useMutation({
    mutationFn: () => api.locationCreate({ name: trimmed, type }),
    onSuccess: () => {
      setName('');
      onAdded();
    },
  });

  return (
    <Stack direction="row" spacing={1} alignItems="flex-start">
      <TextField
        size="small"
        label="New location name"
        value={name}
        onChange={(e) => setName(e.target.value)}
        error={taken}
        helperText={taken ? 'This name is already in use' : ' '}
        sx={{ width: 260 }}
      />
      <TextField
        select
        size="small"
        label="Type"
        value={type}
        onChange={(e) => setType(e.target.value)}
        sx={{ width: 150 }}
      >
        {TYPES.map((t) => (
          <MenuItem key={t.value} value={t.value}>
            {t.label}
          </MenuItem>
        ))}
      </TextField>
      <Button
        variant="contained"
        startIcon={<AddIcon />}
        disabled={trimmed.length === 0 || taken || create.isPending}
        onClick={() => create.mutate()}
        sx={{ mt: 0.5 }}
      >
        Add
      </Button>
    </Stack>
  );
}

function LocationMenu({ loc, onChanged }: { loc: LocationSummaryDto; onChanged: () => void }) {
  const [anchor, setAnchor] = useState<null | HTMLElement>(null);
  const close = () => setAnchor(null);

  const rename = useMutation({
    mutationFn: (name: string) => api.locationRename(loc.id, name),
    onSuccess: onChanged,
  });
  const remove = useMutation({
    mutationFn: (moveToBulk: boolean) => api.locationDelete(loc.id, moveToBulk),
    onSuccess: onChanged,
  });
  const toggleAlways = useMutation({
    mutationFn: () => api.locationSetAlwaysAvailable(loc.id, !loc.isAlwaysAvailable),
    onSuccess: onChanged,
  });

  return (
    <>
      <IconButton size="small" onClick={(e) => setAnchor(e.currentTarget)}>
        <MoreVertIcon fontSize="small" />
      </IconButton>
      <Menu anchorEl={anchor} open={!!anchor} onClose={close}>
        <MenuItem
          onClick={() => {
            close();
            const name = prompt('Rename location', loc.name);
            if (name && name.trim() && name.trim() !== loc.name) rename.mutate(name.trim());
          }}
        >
          Rename…
        </MenuItem>
        <MenuItem
          disabled={loc.isSystem}
          onClick={() => {
            close();
            toggleAlways.mutate();
          }}
        >
          {loc.isAlwaysAvailable ? 'Unset always-available' : 'Set always-available'}
        </MenuItem>
        <MenuItem
          disabled={loc.isSystem}
          onClick={() => {
            close();
            if (!confirm(`Delete "${loc.name}"?`)) return;
            const moveToBulk = confirm('Move its cards to Bulk?  (Cancel = delete the cards)');
            remove.mutate(moveToBulk);
          }}
        >
          Delete…
        </MenuItem>
      </Menu>
    </>
  );
}

const locationHref = (loc: LocationSummaryDto) =>
  loc.type === 'Binder' ? `/binder/${loc.id}` : `/location/${loc.id}`;

const num = (n: number) => n.toLocaleString();

function buildColumns(onChanged: () => void): GridColDef<LocationSummaryDto>[] {
  return [
    {
      field: 'name',
      headerName: 'Name',
      flex: 2,
      minWidth: 200,
      renderCell: (p) => (
        <Link component={RouterLink} to={locationHref(p.row)} underline="hover" noWrap>
          {p.row.name}
        </Link>
      ),
    },
    { field: 'type', headerName: 'Type', width: 120 },
    {
      field: 'cardCount',
      headerName: 'Cards',
      width: 100,
      align: 'right',
      headerAlign: 'right',
      valueFormatter: (v: number) => num(v),
    },
    {
      field: 'uniquePrintCount',
      headerName: 'Unique',
      width: 100,
      align: 'right',
      headerAlign: 'right',
      valueFormatter: (v: number) => num(v),
    },
    {
      field: 'totalMarketValue',
      headerName: 'Market',
      width: 120,
      align: 'right',
      headerAlign: 'right',
      valueFormatter: (v: number) => money(v),
    },
    {
      field: 'totalPurchaseCost',
      headerName: 'Cost',
      width: 120,
      align: 'right',
      headerAlign: 'right',
      valueFormatter: (v: number) => money(v),
    },
    {
      field: 'priceDelta',
      headerName: 'Δ',
      width: 130,
      align: 'right',
      headerAlign: 'right',
      renderCell: (p) => {
        const d = p.row.priceDelta;
        const color = d > 0 ? 'success.main' : d < 0 ? 'error.main' : 'text.secondary';
        const sign = d > 0 ? '+' : '';
        return (
          <Typography variant="body2" sx={{ color }}>
            {sign}
            {money(d)} ({sign}
            {p.row.priceDeltaPercent.toFixed(0)}%)
          </Typography>
        );
      },
    },
    {
      field: 'actions',
      headerName: '',
      width: 56,
      sortable: false,
      filterable: false,
      align: 'center',
      renderCell: (p) => <LocationMenu loc={p.row} onChanged={onChanged} />,
    },
  ];
}

export function LocationsPage() {
  const { game } = useGame();
  const qc = useQueryClient();
  const { data, isLoading } = useQuery({
    queryKey: ['locations', game],
    queryFn: () => api.locations(game),
  });

  const refresh = () => qc.invalidateQueries({ queryKey: ['locations'] });
  const groups = useMemo(() => (data ? groupLocations(data) : []), [data]);
  const columns = useMemo(() => buildColumns(refresh), []);

  const [collapsed, setCollapsed] = useState<Set<string>>(() => {
    try {
      return new Set<string>(JSON.parse(localStorage.getItem(COLLAPSED_KEY) ?? '[]'));
    } catch {
      return new Set<string>();
    }
  });
  const toggle = (key: string) =>
    setCollapsed((prev) => {
      const next = new Set(prev);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      localStorage.setItem(COLLAPSED_KEY, JSON.stringify([...next]));
      return next;
    });

  return (
    <Stack spacing={3}>
      <Typography variant="h4">Locations</Typography>
      <AddLocationBar onAdded={refresh} />
      {isLoading || !data ? (
        <CircularProgress />
      ) : (
        groups.map((group) => {
          const isCollapsed = collapsed.has(group.key);
          return (
            <Stack key={group.key} spacing={1.5}>
              <ButtonBase
                onClick={() => toggle(group.key)}
                sx={{ justifyContent: 'flex-start', borderRadius: 1, py: 0.5, px: 0.5, width: 'fit-content' }}
              >
                {isCollapsed ? (
                  <ChevronRightIcon fontSize="small" sx={{ color: 'text.secondary' }} />
                ) : (
                  <ExpandMoreIcon fontSize="small" sx={{ color: 'text.secondary' }} />
                )}
                <Typography variant="overline" color="text.secondary">
                  {group.heading} · {group.items.length}
                </Typography>
              </ButtonBase>
              <Collapse in={!isCollapsed} unmountOnExit>
                <DataGrid
                  rows={group.items}
                  columns={columns}
                  getRowId={(r) => r.id}
                  density="compact"
                  autoHeight
                  hideFooter
                  disableRowSelectionOnClick
                  disableColumnMenu
                  initialState={{ sorting: { sortModel: [{ field: 'name', sort: 'asc' }] } }}
                />
              </Collapse>
            </Stack>
          );
        })
      )}
    </Stack>
  );
}
