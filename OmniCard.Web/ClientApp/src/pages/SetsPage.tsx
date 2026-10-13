import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import type { TFunction } from 'i18next';
import {
  Alert,
  Autocomplete,
  Box,
  Chip,
  FormControlLabel,
  LinearProgress,
  Stack,
  Switch,
  TextField,
  Typography,
} from '@mui/material';
import { DataGrid, type GridColDef } from '@mui/x-data-grid';
import { api } from '../api/client';
import type { SetChecklistCardDto, SetInfoDto } from '../api/types';
import { useGame } from '../context/GameContext';
import { CardHoverPreview, type CardHover } from '../components/CardHoverPreview';
import { useFormatters } from '../i18n/format';

const buildColumns = (
  t: TFunction,
  money: (n?: number | null) => string,
  setHover: (h: CardHover | null) => void,
): GridColDef<SetChecklistCardDto>[] => [
  { field: 'collectorNumber', headerName: t('sets.columns.no'), width: 80 },
  {
    field: 'name',
    headerName: t('common.labels.name'),
    flex: 2,
    minWidth: 200,
    renderCell: (p) => (
      <Box
        component="span"
        onMouseEnter={(e) =>
          p.row.imageUri && setHover({ el: e.currentTarget, url: p.row.imageUri, foil: p.row.hasFoil })
        }
        onMouseLeave={() => setHover(null)}
      >
        {p.row.name}
      </Box>
    ),
  },
  { field: 'rarity', headerName: t('common.labels.rarity'), width: 110 },
  {
    field: 'ownedQuantity',
    headerName: t('sets.columns.owned'),
    width: 100,
    renderCell: (p) =>
      p.value > 0 ? (
        <Chip size="small" color="success" label={t('sets.ownedCount', { count: p.value })} />
      ) : (
        '—'
      ),
  },
  {
    field: 'normalPrice',
    headerName: t('sets.columns.normal'),
    width: 100,
    align: 'right',
    headerAlign: 'right',
    valueFormatter: (v: number | null) => money(v),
  },
  {
    field: 'foilPrice',
    headerName: t('common.labels.foil'),
    width: 100,
    align: 'right',
    headerAlign: 'right',
    valueFormatter: (v: number | null) => money(v),
  },
];

// The "owned only" filters are per-browser preferences; storage can be unavailable (private mode).
const OWNED_SETS_KEY = 'omnicard.sets.ownedOnly';
const OWNED_CARDS_KEY = 'omnicard.sets.ownedCardsOnly';
function loadFlag(key: string): boolean {
  try {
    return localStorage.getItem(key) === 'true';
  } catch {
    return false;
  }
}
function saveFlag(key: string, value: boolean) {
  try {
    localStorage.setItem(key, String(value));
  } catch {
    /* not persisted */
  }
}

export function SetsPage() {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const { game } = useGame();
  const [set, setSet] = useState<SetInfoDto | null>(null);
  const [hover, setHover] = useState<CardHover | null>(null);
  const [ownedOnly, setOwnedOnly] = useState(() => loadFlag(OWNED_SETS_KEY));
  const [ownedCardsOnly, setOwnedCardsOnly] = useState(() => loadFlag(OWNED_CARDS_KEY));
  const columns = buildColumns(t, (n) => (n == null ? '' : fmt.money(n)), setHover);

  const setsQuery = useQuery({
    queryKey: ['sets', game],
    queryFn: () => api.sets(game!),
    enabled: !!game,
  });

  const ownedQuery = useQuery({
    queryKey: ['sets-owned', game],
    queryFn: () => api.ownedSetCodes(game!),
    enabled: !!game && ownedOnly,
  });

  const setOptions = useMemo(() => {
    const all = setsQuery.data ?? [];
    if (!ownedOnly) return all;
    const owned = new Set(ownedQuery.data ?? []);
    // Keep the open set pickable so switching the filter on doesn't drop the checklist you're on.
    return all.filter((s) => owned.has(s.setCode.toLowerCase()) || s.setCode === set?.setCode);
  }, [setsQuery.data, ownedQuery.data, ownedOnly, set]);

  const toggleOwnedOnly = (checked: boolean) => {
    setOwnedOnly(checked);
    saveFlag(OWNED_SETS_KEY, checked);
  };

  const toggleOwnedCardsOnly = (checked: boolean) => {
    setOwnedCardsOnly(checked);
    saveFlag(OWNED_CARDS_KEY, checked);
  };

  const checklistQuery = useQuery({
    queryKey: ['set-checklist', game, set?.setCode],
    queryFn: () => api.setChecklist(game!, set!.setCode),
    enabled: !!game && !!set,
  });

  if (!game) {
    return (
      <Stack spacing={2}>
        <Typography variant="h4">{t('sets.title')}</Typography>
        <Alert severity="info">{t('sets.pickGame')}</Alert>
      </Stack>
    );
  }

  const checklist = checklistQuery.data;
  // The summary/progress bar always reflect the whole set; this only narrows the grid.
  const rows = (checklist?.cards ?? []).filter((c) => !ownedCardsOnly || c.ownedQuantity > 0);

  return (
    <Stack spacing={2} sx={{ height: 'calc(100vh - 120px)' }}>
      <Typography variant="h4">{t('sets.title')}</Typography>
      <Stack direction="row" spacing={2} alignItems="center" flexWrap="wrap" useFlexGap>
        <Autocomplete
          options={setOptions}
          loading={setsQuery.isLoading || (ownedOnly && ownedQuery.isLoading)}
          getOptionLabel={(o) => `${o.setName} (${o.setCode})`}
          isOptionEqualToValue={(o, v) => o.setCode === v.setCode}
          value={set}
          onChange={(_, v) => setSet(v)}
          noOptionsText={ownedOnly ? t('sets.noOwnedSets') : undefined}
          sx={{ flex: 1, minWidth: 260, maxWidth: 480 }}
          renderInput={(params) => <TextField {...params} label={t('common.labels.set')} size="small" />}
        />
        <FormControlLabel
          control={
            <Switch checked={ownedOnly} onChange={(e) => toggleOwnedOnly(e.target.checked)} size="small" />
          }
          label={t('sets.ownedOnly')}
        />
        <FormControlLabel
          control={
            <Switch
              checked={ownedCardsOnly}
              onChange={(e) => toggleOwnedCardsOnly(e.target.checked)}
              size="small"
            />
          }
          label={t('sets.ownedCardsOnly')}
        />
      </Stack>

      {checklist && (
        <Box>
          <Typography variant="subtitle1">
            {t('sets.summary', {
              setName: checklist.setName,
              owned: fmt.number(checklist.ownedCount),
              total: fmt.number(checklist.totalCount),
              percent: fmt.number(checklist.completionPercent, {
                minimumFractionDigits: 1,
                maximumFractionDigits: 1,
              }),
            })}
          </Typography>
          <LinearProgress
            variant="determinate"
            value={Math.min(100, checklist.completionPercent)}
            sx={{ my: 1, height: 8, borderRadius: 1 }}
          />
        </Box>
      )}

      {set && (
        <DataGrid
          rows={rows}
          getRowId={(r) => r.gameCardId}
          columns={columns}
          loading={checklistQuery.isFetching}
          density="compact"
          disableRowSelectionOnClick
          localeText={ownedCardsOnly ? { noRowsLabel: t('sets.noOwnedCards') } : undefined}
          getRowClassName={(p) => (p.row.ownedQuantity > 0 ? '' : 'unowned-row')}
          sx={{ '& .unowned-row': { opacity: 0.55 } }}
        />
      )}

      {/* Hover artwork preview — mirrors the collection list */}
      <CardHoverPreview hover={hover} onClose={() => setHover(null)} />
    </Stack>
  );
}
