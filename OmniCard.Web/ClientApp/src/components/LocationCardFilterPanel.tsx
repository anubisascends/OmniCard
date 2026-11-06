import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useQuery } from '@tanstack/react-query';
import { Alert, Button, Chip, Stack, Typography } from '@mui/material';
import FilterListIcon from '@mui/icons-material/FilterList';
import { api } from '../api/client';
import type { LocationSummaryDto } from '../api/types';
import { useFormatters } from '../i18n/format';
import { usePermissions } from '../context/usePermissions';
import { LocationCardFilterDialog } from './dialogs/LocationCardFilterDialog';
import { outsideSetsQuery, useDefaultSets } from './locationCardFilter';

/**
 * The location's game + default sets, under its title: what it's fixed to, an edit button, and a warning
 * when it holds cards outside its default sets (which are only a search default, so such cards are kept —
 * "Show them" searches for them). A deck box's game is shown by its deck-box panel, so only its sets are
 * listed here. Bulk holds every game and shows nothing.
 */
export function LocationCardFilterPanel({
  loc,
  onChanged,
  onShowOutside,
}: {
  loc: LocationSummaryDto;
  onChanged: () => void;
  /** Runs the given collection search on the page. */
  onShowOutside: (q: string) => void;
}) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const { can } = usePermissions();
  const [editing, setEditing] = useState(false);
  const isDeckBox = loc.type === 'Deck Box';
  const canEdit = loc.canWrite && can('locations.edit');
  const games = useQuery({ queryKey: ['games'], queryFn: () => api.games(), enabled: !!loc.game });
  const { sets } = useDefaultSets(loc.game, loc.defaultSets);
  const outsideQ = loc.game && loc.defaultSets.length > 0 ? outsideSetsQuery(loc.defaultSets) : null;
  const outside = useQuery({
    queryKey: ['location-outside-sets', loc.id, outsideQ],
    queryFn: () => api.collection({ containerId: loc.id, game: loc.game!, q: outsideQ!, take: 1 }),
    enabled: outsideQ != null && loc.cardCount > 0,
  });
  const outsideCount = outsideQ != null ? outside.data?.total ?? 0 : 0;

  if (loc.isSystem || (!loc.game && !canEdit)) return null;
  const gameName = games.data?.find((g) => g.id === loc.game)?.displayName ?? loc.game;

  return (
    <Stack spacing={1}>
      <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
        {!isDeckBox && (
          <Chip
            size="small"
            variant="outlined"
            color={loc.game ? 'primary' : 'default'}
            label={loc.game ? t('locations.cardFilter.onlyGame', { game: gameName }) : t('locations.cardFilter.anyGame')}
          />
        )}
        {sets.length > 0 && (
          <>
            <Typography variant="caption" color="text.secondary">
              {t('locations.cardFilter.defaultSetsLabel')}
            </Typography>
            {sets.map((s) => (
              <Chip key={s.setCode} size="small" label={s.setCode.toUpperCase()} title={s.setName} />
            ))}
          </>
        )}
        {canEdit && (
          <Button size="small" startIcon={<FilterListIcon />} onClick={() => setEditing(true)}>
            {t('locations.cardFilter.edit')}
          </Button>
        )}
      </Stack>
      {outsideCount > 0 && outsideQ && (
        <Alert
          severity="info"
          action={
            <Button color="inherit" size="small" onClick={() => onShowOutside(outsideQ)}>
              {t('locations.cardFilter.showOutside')}
            </Button>
          }
        >
          {t('locations.cardFilter.outsideSets', { count: outsideCount, countText: fmt.number(outsideCount) })}
        </Alert>
      )}
      {editing && (
        <LocationCardFilterDialog open loc={loc} onClose={() => setEditing(false)} onSaved={onChanged} />
      )}
    </Stack>
  );
}
