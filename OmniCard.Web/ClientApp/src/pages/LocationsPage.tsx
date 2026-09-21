import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { TFunction } from 'i18next';
import { Link as RouterLink } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Button,
  ButtonBase,
  CircularProgress,
  Collapse,
  FormControlLabel,
  IconButton,
  Link,
  Menu,
  MenuItem,
  Stack,
  Switch,
  TextField,
  Typography,
} from '@mui/material';
import { DataGrid, type GridColDef } from '@mui/x-data-grid';
import AddIcon from '@mui/icons-material/Add';
import MoreVertIcon from '@mui/icons-material/MoreVert';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import ChevronRightIcon from '@mui/icons-material/ChevronRight';

const COLLAPSED_KEY = 'omnicard.locations.collapsed';
const HIDE_EMPTY_KEY = 'omnicard.locations.hideEmpty';
import { api } from '../api/client';
import type { LocationSummaryDto } from '../api/types';
import { useGame } from '../context/GameContext';
import { useFormatters } from '../i18n/format';

import { groupLocations, type LocationGroup } from '../lib/locationGroups';
import { LOCATION_TYPES, isDeckBoxType } from '../lib/locationTypes';
import { DeckBoxGamePicker } from '../components/DeckBoxGamePicker';
import { DeckBoxGameBanner } from '../components/DeckBoxGameBanner';
import { DeckBoxGameDialog } from '../components/dialogs/DeckBoxGameDialog';

function AddLocationBar({ onAdded }: { onAdded: () => void }) {
  const { t } = useTranslation();
  const [name, setName] = useState('');
  const [type, setType] = useState('Box');
  const [game, setGame] = useState('');
  const [deckTypeId, setDeckTypeId] = useState<number | null>(null);

  const isDeckBox = isDeckBoxType(type);
  const trimmed = name.trim();
  const nameCheck = useQuery({
    queryKey: ['loc-name-available', trimmed],
    queryFn: () => api.locationNameAvailable(trimmed),
    enabled: trimmed.length > 0,
  });
  const taken = trimmed.length > 0 && nameCheck.data?.available === false;
  const needsGame = isDeckBox && game.length === 0;

  const create = useMutation({
    mutationFn: () =>
      api.locationCreate({
        name: trimmed,
        type,
        game: isDeckBox ? game : null,
        deckTypeId: isDeckBox ? deckTypeId : null,
      }),
    onSuccess: () => {
      setName('');
      setGame('');
      setDeckTypeId(null);
      onAdded();
    },
  });

  return (
    <Stack direction="row" spacing={1} alignItems="flex-start" flexWrap="wrap" useFlexGap>
      <TextField
        size="small"
        label={t('locations.addBar.nameLabel')}
        value={name}
        onChange={(e) => setName(e.target.value)}
        error={taken}
        helperText={taken ? t('locations.addBar.nameTaken') : ' '}
        sx={{ width: 260 }}
      />
      <TextField
        select
        size="small"
        label={t('common.labels.type')}
        value={type}
        onChange={(e) => setType(e.target.value)}
        sx={{ width: 150 }}
      >
        {LOCATION_TYPES.map((lt) => (
          <MenuItem key={lt.value} value={lt.value}>
            {t(`locations.types.${lt.value}`)}
          </MenuItem>
        ))}
      </TextField>
      {isDeckBox && (
        <DeckBoxGamePicker
          game={game}
          deckTypeId={deckTypeId}
          onGameChange={setGame}
          onDeckTypeChange={setDeckTypeId}
        />
      )}
      <Button
        variant="contained"
        startIcon={<AddIcon />}
        disabled={trimmed.length === 0 || taken || needsGame || create.isPending}
        onClick={() => create.mutate()}
        sx={{ mt: 0.5 }}
      >
        {t('common.actions.add')}
      </Button>
    </Stack>
  );
}

function LocationMenu({ loc, onChanged }: { loc: LocationSummaryDto; onChanged: () => void }) {
  const { t } = useTranslation();
  const [anchor, setAnchor] = useState<null | HTMLElement>(null);
  const [editingDeckBox, setEditingDeckBox] = useState(false);
  const close = () => setAnchor(null);
  const isDeckBox = loc.type === 'Deck Box';

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
            const name = prompt(t('locations.menu.renamePrompt'), loc.name);
            if (name && name.trim() && name.trim() !== loc.name) rename.mutate(name.trim());
          }}
        >
          {t('locations.menu.rename')}
        </MenuItem>
        {isDeckBox && (
          <MenuItem
            onClick={() => {
              close();
              setEditingDeckBox(true);
            }}
          >
            {t('locations.menu.gameDeckType')}
          </MenuItem>
        )}
        <MenuItem
          disabled={loc.isSystem}
          onClick={() => {
            close();
            toggleAlways.mutate();
          }}
        >
          {loc.isAlwaysAvailable
            ? t('locations.menu.unsetAlwaysAvailable')
            : t('locations.menu.setAlwaysAvailable')}
        </MenuItem>
        <MenuItem
          disabled={loc.isSystem}
          onClick={() => {
            close();
            if (!confirm(t('locations.menu.deleteConfirm', { name: loc.name }))) return;
            const moveToBulk = confirm(t('locations.menu.moveToBulkConfirm'));
            remove.mutate(moveToBulk);
          }}
        >
          {t('locations.menu.delete')}
        </MenuItem>
      </Menu>
      {isDeckBox && (
        <DeckBoxGameDialog
          open={editingDeckBox}
          deckBoxId={loc.id}
          deckBoxName={loc.name}
          initialGame={loc.game}
          initialDeckTypeId={loc.deckTypeId}
          onClose={() => setEditingDeckBox(false)}
          onSaved={onChanged}
        />
      )}
    </>
  );
}

const locationHref = (loc: LocationSummaryDto) =>
  loc.type === 'Binder' ? `/binder/${loc.id}` : `/location/${loc.id}`;

function buildColumns(
  onChanged: () => void,
  gameLabel: (id: string) => string,
  t: TFunction,
  fmt: ReturnType<typeof useFormatters>,
): GridColDef<LocationSummaryDto>[] {
  const num = (n: number) => fmt.number(n);
  const money = (n: number) => fmt.money(n);
  return [
    {
      field: 'name',
      headerName: t('common.labels.name'),
      flex: 2,
      minWidth: 200,
      renderCell: (p) => (
        <Link component={RouterLink} to={locationHref(p.row)} underline="hover" noWrap>
          {p.row.name}
        </Link>
      ),
    },
    {
      field: 'type',
      headerName: t('common.labels.type'),
      width: 200,
      renderCell: (p) => {
        // Deck boxes show their game + deck type inline; other types show just the type name.
        if (p.row.type === 'Deck Box' && (p.row.game || p.row.deckTypeName)) {
          const bits = [p.row.deckTypeName, t('locations.deckBoxLabel')].filter(Boolean).join(' · ');
          return (
            <Stack spacing={0} sx={{ lineHeight: 1.2 }}>
              <Typography variant="body2" noWrap>
                {bits}
              </Typography>
              {p.row.game && (
                <Typography variant="caption" color="text.secondary" noWrap>
                  {gameLabel(p.row.game)}
                </Typography>
              )}
            </Stack>
          );
        }
        return p.row.type;
      },
    },
    {
      field: 'cardCount',
      headerName: t('locations.columns.cards'),
      width: 100,
      align: 'right',
      headerAlign: 'right',
      valueFormatter: (v: number) => num(v),
    },
    {
      field: 'uniquePrintCount',
      headerName: t('locations.columns.unique'),
      width: 100,
      align: 'right',
      headerAlign: 'right',
      valueFormatter: (v: number) => num(v),
    },
    {
      field: 'totalMarketValue',
      headerName: t('locations.columns.market'),
      width: 120,
      align: 'right',
      headerAlign: 'right',
      valueFormatter: (v: number) => money(v),
    },
    {
      field: 'totalPurchaseCost',
      headerName: t('locations.columns.cost'),
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
            {fmt.number(p.row.priceDeltaPercent, { maximumFractionDigits: 0 })}%)
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
  const { t } = useTranslation();
  const fmt = useFormatters();
  const { game } = useGame();
  const qc = useQueryClient();
  const { data, isLoading } = useQuery({
    queryKey: ['locations', game],
    queryFn: () => api.locations(game),
  });
  const games = useQuery({ queryKey: ['games'], queryFn: () => api.games() });
  const gameLabel = useMemo(() => {
    const map = new Map((games.data ?? []).map((g) => [g.id, g.displayName]));
    return (id: string) => map.get(id) ?? id;
  }, [games.data]);

  const refresh = () => qc.invalidateQueries({ queryKey: ['locations'] });

  const [hideEmpty, setHideEmpty] = useState<boolean>(
    () => localStorage.getItem(HIDE_EMPTY_KEY) === '1',
  );
  const toggleHideEmpty = (value: boolean) => {
    setHideEmpty(value);
    localStorage.setItem(HIDE_EMPTY_KEY, value ? '1' : '0');
  };

  const groups = useMemo(() => {
    if (!data) return [];
    const all = groupLocations(data);
    if (!hideEmpty) return all;
    // Drop locations with no cards from the selected game, then drop groups left empty.
    return all
      .map((g) => ({ ...g, items: g.items.filter((loc) => loc.cardCount > 0) }))
      .filter((g) => g.items.length > 0);
  }, [data, hideEmpty]);
  const columns = useMemo(() => buildColumns(refresh, gameLabel, t, fmt), [gameLabel, t, fmt]);

  const headingFor = (g: LocationGroup) =>
    g.key === '__always__'
      ? t('locations.groups.alwaysAvailable')
      : t(`locations.groups.headings.${g.key}`, { defaultValue: g.heading });

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
      <Typography variant="h4">{t('locations.title')}</Typography>
      <DeckBoxGameBanner onResolved={refresh} />
      <AddLocationBar onAdded={refresh} />
      <FormControlLabel
        control={
          <Switch
            checked={hideEmpty}
            onChange={(e) => toggleHideEmpty(e.target.checked)}
            size="small"
          />
        }
        label={t('locations.hideEmpty')}
        sx={{ alignSelf: 'flex-start' }}
      />
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
                  {headingFor(group)} · {fmt.number(group.items.length)}
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
