import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, AlertTitle, Button, Chip, Paper, Stack, Typography } from '@mui/material';
import EditIcon from '@mui/icons-material/Edit';
import CasinoIcon from '@mui/icons-material/Casino';
import LinkIcon from '@mui/icons-material/Link';
import SyncIcon from '@mui/icons-material/Sync';
import ReportProblemOutlinedIcon from '@mui/icons-material/ReportProblemOutlined';
import { api } from '../api/client';
import type { LocationSummaryDto } from '../api/types';
import { useFormatters } from '../i18n/format';
import { usePermissions } from '../context/usePermissions';
import { DeckBoxGameDialog } from './dialogs/DeckBoxGameDialog';
import { DeckLinkDialog } from './dialogs/DeckLinkDialog';
import { DeckSyncDialog } from './dialogs/DeckSyncDialog';
import { DeckNotInCollectionDialog } from './dialogs/DeckNotInCollectionDialog';

/**
 * Deck-box header panel: shows the assigned game system + deck type, a commander/leader indicator,
 * and the advisory (non-blocking) legality warnings for the box's contents against its deck type's
 * rules. Editing the game/type opens the assign dialog. A deck box whose game a deck site serves can be
 * linked to its online deck (Moxfield/Archidekt) and synced from it; the cards the last sync marked
 * "not in collection" are listed from here.
 */
export function DeckBoxPanel({ loc, onChanged }: { loc: LocationSummaryDto; onChanged: () => void }) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const qc = useQueryClient();
  const { can } = usePermissions();
  const [editing, setEditing] = useState(false);
  const [linking, setLinking] = useState(false);
  const [syncing, setSyncing] = useState(false);
  const [missingOpen, setMissingOpen] = useState(false);
  const notInCollection = useQuery({
    queryKey: ['deck-not-in-collection', loc.id],
    queryFn: () => api.deckNotInCollection(loc.id),
    enabled: !!loc.sourceUrl,
  });
  const missingCount = (notInCollection.data ?? []).reduce((n, c) => n + c.quantity, 0);
  const canLink = loc.canWrite && can('locations.edit');
  const canSync = loc.canWrite && can('collection.edit');
  const games = useQuery({ queryKey: ['games'], queryFn: () => api.games() });
  const legality = useQuery({
    queryKey: ['deck-legality', loc.id],
    queryFn: () => api.locationDeckLegality(loc.id),
    // Only meaningful once a deck type is assigned; still safe (returns empty) otherwise.
    enabled: loc.deckTypeId != null,
  });

  const gameName = games.data?.find((g) => g.id === loc.game)?.displayName ?? loc.game ?? undefined;
  const warnings = legality.data?.warnings ?? [];

  return (
    <Paper variant="outlined" sx={{ p: 1.5 }}>
      <Stack spacing={1}>
        <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
          {gameName ? (
            <Chip size="small" color="primary" variant="outlined" label={gameName} />
          ) : (
            <Chip size="small" color="warning" label={t('deckbox.panel.noGameAssigned')} />
          )}
          {loc.deckTypeName && <Chip size="small" icon={<CasinoIcon />} label={loc.deckTypeName} />}
          {legality.data != null && legality.data.commanderCount > 0 && (
            <Chip
              size="small"
              variant="outlined"
              label={t('deckbox.panel.commander', { count: legality.data.commanderCount })}
            />
          )}
          {legality.data != null && legality.data.totalDeckCount > 0 && (
            <Typography variant="caption" color="text.secondary">
              {legality.data.commanderCount > 0
                ? t('deckbox.panel.cardsCountWithCommander', {
                    count: legality.data.totalDeckCount,
                    main: legality.data.mainDeckCount,
                  })
                : t('deckbox.panel.cardsCount', { count: legality.data.totalDeckCount })}
            </Typography>
          )}
          <Button size="small" startIcon={<EditIcon />} onClick={() => setEditing(true)} sx={{ ml: 'auto' }}>
            {t('deckbox.panel.gameAndDeckType')}
          </Button>
        </Stack>

        {(loc.sourceUrl || (loc.deckLinkSupported && canLink)) && (
          <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
            {loc.sourceUrl ? (
              <>
                <Chip
                  size="small"
                  icon={<LinkIcon />}
                  label={t('deckbox.link.linkedTo', { source: loc.sourceName ?? loc.sourceUrl })}
                  component="a"
                  href={loc.sourceUrl}
                  target="_blank"
                  rel="noopener noreferrer"
                  clickable
                />
                <Typography variant="caption" color="text.secondary">
                  {loc.sourceSyncedAt
                    ? t('deckbox.link.lastSynced', { date: fmt.dateTime(loc.sourceSyncedAt) })
                    : t('deckbox.link.neverSynced')}
                </Typography>
                {missingCount > 0 && (
                  <Chip
                    size="small"
                    color="warning"
                    variant="outlined"
                    icon={<ReportProblemOutlinedIcon />}
                    label={t('deckbox.notInCollection.chip', { count: missingCount })}
                    onClick={() => setMissingOpen(true)}
                  />
                )}
                <Stack direction="row" spacing={1} sx={{ ml: 'auto' }}>
                  {canLink && (
                    <Button size="small" onClick={() => setLinking(true)}>
                      {t('deckbox.link.editButton')}
                    </Button>
                  )}
                  {canSync && (
                    <Button size="small" variant="contained" startIcon={<SyncIcon />} onClick={() => setSyncing(true)}>
                      {t('deckbox.sync.button')}
                    </Button>
                  )}
                </Stack>
              </>
            ) : (
              <Button size="small" startIcon={<LinkIcon />} onClick={() => setLinking(true)}>
                {t('deckbox.link.linkButton')}
              </Button>
            )}
          </Stack>
        )}

        {warnings.length > 0 && (
          <Alert severity="info" variant="outlined">
            <AlertTitle>{t('deckbox.panel.legalityTitle', { deckType: loc.deckTypeName })}</AlertTitle>
            <Stack component="ul" spacing={0.25} sx={{ m: 0, pl: 2 }}>
              {warnings.map((w) => (
                <Typography key={w.code + w.message} component="li" variant="body2">
                  {w.message}
                </Typography>
              ))}
            </Stack>
          </Alert>
        )}
      </Stack>

      <DeckBoxGameDialog
        open={editing}
        deckBoxId={loc.id}
        deckBoxName={loc.name}
        initialGame={loc.game}
        initialDeckTypeId={loc.deckTypeId}
        onClose={() => setEditing(false)}
        onSaved={() => {
          legality.refetch();
          onChanged();
        }}
      />
      <DeckLinkDialog
        open={linking}
        loc={loc}
        onClose={() => setLinking(false)}
        onSaved={() => {
          qc.invalidateQueries({ queryKey: ['deck-not-in-collection', loc.id] });
          onChanged();
        }}
      />
      {syncing && <DeckSyncDialog open loc={loc} onClose={() => setSyncing(false)} />}
      <DeckNotInCollectionDialog
        open={missingOpen}
        deckBoxId={loc.id}
        deckBoxName={loc.name}
        cards={notInCollection.data ?? []}
        canEdit={canLink}
        onClose={() => setMissingOpen(false)}
      />
    </Paper>
  );
}
