import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useParams, useLocation, useNavigate, Link as RouterLink } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Box,
  Breadcrumbs,
  Button,
  Chip,
  Link,
  Stack,
  ToggleButton,
  ToggleButtonGroup,
  Typography,
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import FactCheckIcon from '@mui/icons-material/FactCheck';
import UploadFileIcon from '@mui/icons-material/UploadFile';
import ViewListIcon from '@mui/icons-material/ViewList';
import ViewColumnIcon from '@mui/icons-material/ViewColumn';
import { api } from '../api/client';
import { useGame } from '../context/GameContext';
import { usePermissions } from '../context/usePermissions';
import { CardTable } from '../components/CardTable';
import { AddCardDialog } from '../components/dialogs/AddCardDialog';
import { LocationImportDialog } from '../components/dialogs/LocationImportDialog';
import { AuditSummaryDialog } from '../components/dialogs/AuditSummaryDialog';
import type { AuditReturnState } from './AuditPage';
import { DeckBoxPanel } from '../components/DeckBoxPanel';
import { DeckStackView } from '../components/deckstack/DeckStackView';
import { SearchBox } from '../components/SearchBox';
import { SavedViewPicker } from '../components/views/SavedViewPicker';
import { useSavedViews } from '../components/views/useSavedViews';
import { useFormatters } from '../i18n/format';

export function LocationDetailPage() {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const { id } = useParams();
  const locationId = Number(id);
  const { game } = useGame();
  const { can } = usePermissions();
  const qc = useQueryClient();
  // Search, sort, columns, table-vs-stacks and grouping all come from the location's saved view.
  const sv = useSavedViews({ page: 'Location', containerId: locationId });
  const [search, setSearch] = useState(sv.state.q);
  useEffect(() => setSearch(sv.state.q), [sv.state.q]);
  const [addOpen, setAddOpen] = useState(false);
  const [importOpen, setImportOpen] = useState(false);
  // An audit commit returns here with its summary in router state (see AuditPage).
  const location = useLocation();
  const navigate = useNavigate();
  const auditSummary = (location.state as AuditReturnState | null)?.auditSummary ?? null;
  // Clear the state on close so a reload or Back/Forward doesn't reopen the summary.
  const closeAuditSummary = () => navigate(location.pathname + location.search, { replace: true, state: null });
  // Table (flat grid) vs. Stacks (Archidekt-style grouped stacks); location-only.
  const view = sv.state.display ?? 'table';
  const setViewMode = (v: 'table' | 'stacks') => sv.setState({ display: v });

  const locQuery = useQuery({ queryKey: ['location', locationId], queryFn: () => api.location(locationId) });
  const isDeckBox = locQuery.data?.type === 'Deck Box';
  // Locations in a site the user can only view hide every write action (the server enforces it too).
  const canWrite = locQuery.data?.canWrite !== false;
  // A game-locked deck box forces its game onto the add dialog.
  const deckBoxGame = isDeckBox ? locQuery.data?.game ?? undefined : undefined;
  const refresh = () => {
    qc.invalidateQueries({ queryKey: ['location', locationId] });
    qc.invalidateQueries({ queryKey: ['locations'] });
  };

  return (
    <Stack spacing={2} sx={{ height: 'calc(100vh - 120px)' }}>
      <Breadcrumbs>
        <Link component={RouterLink} to="/locations">
          {t('locations.title')}
        </Link>
        <Typography color="text.primary">{locQuery.data?.name ?? '…'}</Typography>
      </Breadcrumbs>
      <Stack direction="row" spacing={2} alignItems="center">
        <Typography variant="h4">{locQuery.data?.name ?? t('locations.detail.fallbackName')}</Typography>
        {locQuery.data && <Chip label={locQuery.data.type} />}
        {locQuery.data?.siteName && (
          <Chip
            variant="outlined"
            label={
              canWrite
                ? locQuery.data.siteName
                : `${locQuery.data.siteName} · ${t('locations.sites.readOnly')}`
            }
          />
        )}
        {locQuery.data?.type === 'Binder' && (
          <Link component={RouterLink} to={`/binder/${locationId}`}>
            {t('locations.detail.openBinderView')}
          </Link>
        )}
        <Box sx={{ flexGrow: 1 }} />
        {canWrite && can('collection.delete') && (
          <Button
            variant="outlined"
            startIcon={<FactCheckIcon />}
            component={RouterLink}
            to={`/audit/${locationId}`}
          >
            {t('locations.detail.audit')}
          </Button>
        )}
        {canWrite && can('import.run') && (
          <Button variant="outlined" startIcon={<UploadFileIcon />} onClick={() => setImportOpen(true)}>
            {t('common.actions.import')}
          </Button>
        )}
        {canWrite && (
          <Button variant="contained" startIcon={<AddIcon />} onClick={() => setAddOpen(true)}>
            {t('locations.detail.addCard')}
          </Button>
        )}
      </Stack>
      {locQuery.data && (
        <Typography variant="body2" color="text.secondary">
          {t('locations.detail.summary', {
            count: locQuery.data.cardCount,
            countText: fmt.number(locQuery.data.cardCount),
            value: fmt.money(locQuery.data.totalMarketValue),
          })}
        </Typography>
      )}
      {isDeckBox && locQuery.data && <DeckBoxPanel loc={locQuery.data} onChanged={refresh} />}
      <Stack direction="row" spacing={1} alignItems="center">
        <Box sx={{ flexGrow: 1 }}>
          <SearchBox value={search} onChange={setSearch} onSubmit={(q) => sv.setState({ q })} />
        </Box>
        <SavedViewPicker sv={sv} />
        <ToggleButtonGroup
          size="small"
          exclusive
          value={view}
          onChange={(_, v) => v && setViewMode(v)}
          aria-label={t('locations.detail.viewModeLabel')}
        >
          <ToggleButton value="table" aria-label={t('locations.detail.tableView')}>
            <ViewListIcon fontSize="small" />
          </ToggleButton>
          <ToggleButton value="stacks" aria-label={t('locations.detail.stackedView')}>
            <ViewColumnIcon fontSize="small" />
          </ToggleButton>
        </ToggleButtonGroup>
      </Stack>
      {view === 'stacks' ? (
        <DeckStackView
          containerId={locationId}
          game={game}
          q={sv.state.q}
          groupMode={sv.state.groupBy ?? 'type'}
          onGroupModeChange={(groupBy) => sv.setState({ groupBy })}
        />
      ) : (
        <CardTable game={game} containerId={locationId} view={sv.state} onViewChange={sv.setState} />
      )}

      <AddCardDialog
        open={addOpen}
        locationId={locationId}
        locationName={locQuery.data?.name ?? t('locations.detail.thisLocation')}
        defaultGame={deckBoxGame ?? game}
        lockGame={isDeckBox && !!deckBoxGame}
        onClose={() => setAddOpen(false)}
      />
      <LocationImportDialog
        open={importOpen}
        locationId={locationId}
        locationName={locQuery.data?.name ?? t('locations.detail.thisLocation')}
        onClose={() => setImportOpen(false)}
      />
      <AuditSummaryDialog
        open={auditSummary != null}
        result={auditSummary}
        locationName={locQuery.data?.name ?? t('locations.detail.thisLocation')}
        onClose={closeAuditSummary}
      />
    </Stack>
  );
}
