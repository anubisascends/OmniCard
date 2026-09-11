import { useEffect } from 'react';
import { useQuery } from '@tanstack/react-query';
import { MenuItem, Stack, TextField } from '@mui/material';
import { api } from '../api/client';

/**
 * The Game + Deck Type pickers a deck box requires. Reused by the Locations add bar, the move
 * picker's inline create section, and the deck-box assign dialog. Fetches the games list and the
 * per-game deck types; when the game changes it clears a deck type that no longer belongs. The
 * parent owns the `game`/`deckTypeId` state (this is a controlled component).
 */
export function DeckBoxGamePicker({
  game,
  deckTypeId,
  onGameChange,
  onDeckTypeChange,
  direction = 'row',
}: {
  game: string;
  deckTypeId: number | null;
  onGameChange: (game: string) => void;
  onDeckTypeChange: (deckTypeId: number | null) => void;
  direction?: 'row' | 'column';
}) {
  const games = useQuery({ queryKey: ['games'], queryFn: () => api.games() });
  const deckTypes = useQuery({
    queryKey: ['deck-types', game],
    queryFn: () => api.deckTypes(game),
    enabled: !!game,
  });

  // If the selected deck type isn't in the current game's list, clear it (e.g. after a game switch).
  useEffect(() => {
    if (deckTypeId != null && deckTypes.data && !deckTypes.data.some((d) => d.id === deckTypeId))
      onDeckTypeChange(null);
  }, [game, deckTypes.data, deckTypeId, onDeckTypeChange]);

  return (
    <Stack direction={direction} spacing={1}>
      <TextField
        select
        size="small"
        label="Game"
        required
        value={game}
        onChange={(e) => onGameChange(e.target.value)}
        error={game.length === 0}
        helperText={game.length === 0 ? 'Required for a deck box' : ' '}
        sx={{ minWidth: 200 }}
      >
        {(games.data ?? []).map((g) => (
          <MenuItem key={g.id} value={g.id}>
            {g.displayName}
          </MenuItem>
        ))}
      </TextField>
      <TextField
        select
        size="small"
        label="Deck type"
        value={deckTypeId ?? ''}
        onChange={(e) => onDeckTypeChange(e.target.value === '' ? null : Number(e.target.value))}
        disabled={!game}
        helperText=" "
        sx={{ minWidth: 180 }}
      >
        <MenuItem value="">
          <em>None</em>
        </MenuItem>
        {(deckTypes.data ?? []).map((d) => (
          <MenuItem key={d.id} value={d.id}>
            {d.name}
          </MenuItem>
        ))}
      </TextField>
    </Stack>
  );
}
