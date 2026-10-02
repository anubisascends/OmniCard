import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Button,
  Chip,
  CircularProgress,
  Dialog,
  DialogContent,
  DialogTitle,
  Divider,
  List,
  ListItemButton,
  ListSubheader,
  MenuItem,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import SearchIcon from '@mui/icons-material/Search';
import AddIcon from '@mui/icons-material/Add';
import { InputAdornment } from '@mui/material';
import { api } from '../../api/client';
import { useFormatters } from '../../i18n/format';
import { groupLocations } from '../../lib/locationGroups';
import { LOCATION_TYPES, isDeckBoxType } from '../../lib/locationTypes';
import { DeckBoxGamePicker } from '../DeckBoxGamePicker';
import { useSites } from '../../context/useSites';
import { ALL_SITES, SiteSelect } from '../SiteSelect';

/** Inline "create a new location" section, revealed from the picker so callers never have to leave. */
function CreateLocationSection({
  onCreated,
  types,
  defaultGame,
}: {
  onCreated: (id: number) => void;
  /** Restricts the type dropdown (the first entry is the default). */
  types?: string[];
  /** Pre-selected game for a new deck box. */
  defaultGame?: string;
}) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const { writable, defaultSite } = useSites();
  const [siteChoice, setSiteChoice] = useState<number | undefined>(undefined);
  const siteId = siteChoice ?? defaultSite?.id;
  const [open, setOpen] = useState(false);
  const [name, setName] = useState('');
  const typeOptions = types ? LOCATION_TYPES.filter((lt) => types.includes(lt.value)) : LOCATION_TYPES;
  const [type, setType] = useState<string>(typeOptions[0]?.value ?? 'Box');
  const [game, setGame] = useState(defaultGame ?? '');
  const [deckTypeId, setDeckTypeId] = useState<number | null>(null);
  const isDeckBox = isDeckBoxType(type);

  const trimmed = name.trim();
  const nameCheck = useQuery({
    queryKey: ['loc-name-available', trimmed],
    queryFn: () => api.locationNameAvailable(trimmed),
    enabled: open && trimmed.length > 0,
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
        siteId: siteId ?? null,
      }),
    onSuccess: (loc) => {
      qc.invalidateQueries({ queryKey: ['locations'] });
      qc.invalidateQueries({ queryKey: ['sites'] });
      setName('');
      setGame(defaultGame ?? '');
      setDeckTypeId(null);
      setOpen(false);
      onCreated(loc.id);
    },
  });

  return (
    <>
      <Divider sx={{ my: 1 }} />
      {open ? (
        <Stack spacing={1} sx={{ pt: 0.5 }}>
          <TextField
            size="small"
            autoFocus
            label={t('dialogs.locationPicker.newLocationName')}
            value={name}
            onChange={(e) => setName(e.target.value)}
            error={taken}
            helperText={taken ? t('dialogs.locationPicker.nameTaken') : ' '}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && trimmed.length > 0 && !taken && !needsGame && !create.isPending)
                create.mutate();
            }}
          />
          {writable.length > 1 && (
            <SiteSelect
              sites={writable}
              value={siteId ?? writable[0].id}
              onChange={(v) => v !== ALL_SITES && setSiteChoice(v)}
              label={t('locations.sites.createIn')}
            />
          )}
          <TextField select size="small" label={t('common.labels.type')} value={type} onChange={(e) => setType(e.target.value)}>
            {typeOptions.map((t) => (
              <MenuItem key={t.value} value={t.value}>
                {t.label}
              </MenuItem>
            ))}
          </TextField>
          {isDeckBox && (
            <DeckBoxGamePicker
              game={game}
              deckTypeId={deckTypeId}
              onGameChange={setGame}
              onDeckTypeChange={setDeckTypeId}
              direction="column"
            />
          )}
          {create.error && (
            <Typography variant="caption" color="error">
              {(create.error as Error).message}
            </Typography>
          )}
          <Stack direction="row" spacing={1} justifyContent="flex-end">
            <Button size="small" onClick={() => setOpen(false)}>
              {t('common.actions.cancel')}
            </Button>
            <Button
              size="small"
              variant="contained"
              startIcon={<AddIcon />}
              disabled={trimmed.length === 0 || taken || needsGame || create.isPending}
              onClick={() => create.mutate()}
            >
              {t('dialogs.locationPicker.createAndSelect')}
            </Button>
          </Stack>
        </Stack>
      ) : (
        <Button size="small" startIcon={<AddIcon />} onClick={() => setOpen(true)} sx={{ alignSelf: 'flex-start' }}>
          {t('dialogs.locationPicker.newLocation')}
        </Button>
      )}
    </>
  );
}

/**
 * Grouped, searchable location picker. Locations are grouped (Always Available first, then by type)
 * and sorted A→Z, with a search box to filter by name. Used anywhere a card (or cards) is moved to a
 * different location. When `allowCreate` (the default), a new location can be created inline and is
 * auto-selected — so callers (e.g. the scan page) never have to navigate away and lose their work.
 */
export function LocationPickerDialog({
  open,
  title,
  excludeId,
  allowCreate = true,
  cardGames,
  types,
  onPick,
  onClose,
}: {
  open: boolean;
  title?: string;
  excludeId?: number;
  allowCreate?: boolean;
  /** Games of the card(s) being moved. When set, deck-box targets locked to a different game are
   * disabled (the server hard-blocks the move anyway — this is the matching UX guard). */
  cardGames?: string[];
  /** Only offer locations of these types (e.g. `['DeckBox']`); inline create is limited to them too. */
  types?: string[];
  onPick: (id: number) => void;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const { multiSite } = useSites();
  // A deck box locked to a game that none of the moving cards share can't receive them.
  const gameBlocked = (locGame?: string | null) =>
    !!locGame && cardGames != null && cardGames.length > 0 && !cardGames.includes(locGame);
  const { data, isLoading } = useQuery({ queryKey: ['locations', undefined], queryFn: () => api.locations(), enabled: open });
  const [search, setSearch] = useState('');

  const groups = useMemo(() => {
    const term = search.trim().toLowerCase();
    const filtered = (data ?? [])
      .filter((l) => l.id !== excludeId)
      // Only locations the user may change can receive cards (read-only sites are excluded).
      .filter((l) => l.canWrite !== false)
      .filter((l) => !types || types.includes(l.type))
      .filter((l) => (term ? l.name.toLowerCase().includes(term) : true));
    return groupLocations(filtered);
  }, [data, search, excludeId, types]);

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle sx={{ pb: 1 }}>{title ?? t('dialogs.locationPicker.defaultTitle')}</DialogTitle>
      <DialogContent>
        <TextField
          fullWidth
          size="small"
          autoFocus
          placeholder={t('dialogs.locationPicker.searchPlaceholder')}
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          slotProps={{
            input: {
              startAdornment: (
                <InputAdornment position="start">
                  <SearchIcon fontSize="small" />
                </InputAdornment>
              ),
            },
          }}
          sx={{ mb: 1 }}
        />
        {isLoading ? (
          <Stack alignItems="center" sx={{ py: 3 }}>
            <CircularProgress size={24} />
          </Stack>
        ) : groups.length === 0 ? (
          <Typography variant="body2" color="text.secondary" sx={{ py: 2 }}>
            {t('dialogs.locationPicker.noMatches')}
          </Typography>
        ) : (
          <List dense disablePadding sx={{ maxHeight: '55vh', overflowY: 'auto' }}>
            {groups.map((g) => (
              <li key={g.key}>
                <ul style={{ padding: 0 }}>
                  <ListSubheader disableSticky sx={{ bgcolor: 'transparent', lineHeight: '28px' }}>
                    {g.heading}
                  </ListSubheader>
                  {g.items.map((l) => {
                    const blocked = gameBlocked(l.game);
                    return (
                      <ListItemButton
                        key={l.id}
                        disabled={blocked}
                        onClick={() => onPick(l.id)}
                        sx={{ borderRadius: 1 }}
                        title={blocked ? t('dialogs.locationPicker.gameBlocked', { game: l.game }) : undefined}
                      >
                        <Stack direction="row" spacing={1} alignItems="center" sx={{ width: '100%' }}>
                          <Stack sx={{ flexGrow: 1, minWidth: 0 }}>
                            <Typography variant="body2" noWrap>
                              {l.name}
                            </Typography>
                            {multiSite && l.siteName && (
                              <Typography variant="caption" color="text.secondary" noWrap>
                                {l.siteName}
                              </Typography>
                            )}
                          </Stack>
                          <Typography variant="caption" color="text.secondary">
                            {fmt.number(l.cardCount)}
                          </Typography>
                          <Chip size="small" variant="outlined" label={l.type} />
                        </Stack>
                      </ListItemButton>
                    );
                  })}
                </ul>
              </li>
            ))}
          </List>
        )}
        {allowCreate && !isLoading && (
          <CreateLocationSection
            onCreated={onPick}
            types={types}
            defaultGame={cardGames?.length === 1 ? cardGames[0] : undefined}
          />
        )}
      </DialogContent>
    </Dialog>
  );
}
