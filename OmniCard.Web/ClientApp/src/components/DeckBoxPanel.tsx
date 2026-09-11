import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Alert, AlertTitle, Button, Chip, Paper, Stack, Typography } from '@mui/material';
import EditIcon from '@mui/icons-material/Edit';
import CasinoIcon from '@mui/icons-material/Casino';
import { api } from '../api/client';
import type { LocationSummaryDto } from '../api/types';
import { DeckBoxGameDialog } from './dialogs/DeckBoxGameDialog';

/**
 * Deck-box header panel: shows the assigned game system + deck type, a commander/leader indicator,
 * and the advisory (non-blocking) legality warnings for the box's contents against its deck type's
 * rules. Editing the game/type opens the assign dialog.
 */
export function DeckBoxPanel({ loc, onChanged }: { loc: LocationSummaryDto; onChanged: () => void }) {
  const [editing, setEditing] = useState(false);
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
            <Chip size="small" color="warning" label="No game assigned" />
          )}
          {loc.deckTypeName && <Chip size="small" icon={<CasinoIcon />} label={loc.deckTypeName} />}
          {legality.data != null && legality.data.commanderCount > 0 && (
            <Chip
              size="small"
              variant="outlined"
              label={`${legality.data.commanderCount} commander${legality.data.commanderCount === 1 ? '' : 's'}`}
            />
          )}
          {legality.data != null && legality.data.totalDeckCount > 0 && (
            <Typography variant="caption" color="text.secondary">
              {legality.data.totalDeckCount} cards
              {legality.data.commanderCount > 0
                ? ` (${legality.data.mainDeckCount} + commander)`
                : ''}
            </Typography>
          )}
          <Button size="small" startIcon={<EditIcon />} onClick={() => setEditing(true)} sx={{ ml: 'auto' }}>
            Game &amp; deck type
          </Button>
        </Stack>

        {warnings.length > 0 && (
          <Alert severity="info" variant="outlined">
            <AlertTitle>Deck legality ({loc.deckTypeName})</AlertTitle>
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
    </Paper>
  );
}
