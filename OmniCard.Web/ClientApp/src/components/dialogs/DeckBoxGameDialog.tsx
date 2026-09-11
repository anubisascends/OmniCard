import { useEffect, useState } from 'react';
import { useMutation } from '@tanstack/react-query';
import {
  Alert,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Stack,
  Typography,
} from '@mui/material';
import { api } from '../../api/client';
import { DeckBoxGamePicker } from '../DeckBoxGamePicker';

/**
 * Assigns (or reassigns) a deck box's game system + deck type. Used both to correct a legacy deck box
 * that predates the feature and to edit an existing one. The server hard-blocks assigning a game that
 * conflicts with cards already in the box (409) — that error is surfaced inline.
 */
export function DeckBoxGameDialog({
  open,
  deckBoxId,
  deckBoxName,
  initialGame,
  initialDeckTypeId,
  onClose,
  onSaved,
}: {
  open: boolean;
  deckBoxId: number;
  deckBoxName: string;
  initialGame?: string | null;
  initialDeckTypeId?: number | null;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [game, setGame] = useState(initialGame ?? '');
  const [deckTypeId, setDeckTypeId] = useState<number | null>(initialDeckTypeId ?? null);

  // Reset to the incoming values each time the dialog opens for a (possibly different) box.
  useEffect(() => {
    if (open) {
      setGame(initialGame ?? '');
      setDeckTypeId(initialDeckTypeId ?? null);
    }
  }, [open, deckBoxId, initialGame, initialDeckTypeId]);

  const save = useMutation({
    mutationFn: () => api.locationSetDeckBox(deckBoxId, game, deckTypeId),
    onSuccess: () => {
      onSaved();
      onClose();
    },
  });

  return (
    <Dialog open={open} onClose={onClose} maxWidth="xs" fullWidth>
      <DialogTitle>Deck box game — {deckBoxName}</DialogTitle>
      <DialogContent>
        <Stack spacing={1} sx={{ mt: 1 }}>
          <Typography variant="body2" color="text.secondary">
            A deck box holds one game system. Cards from other games can't be added once a game is set.
          </Typography>
          <DeckBoxGamePicker
            game={game}
            deckTypeId={deckTypeId}
            onGameChange={setGame}
            onDeckTypeChange={setDeckTypeId}
            direction="column"
          />
          {save.isError && (
            <Alert severity="error">{(save.error as Error).message}</Alert>
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button
          variant="contained"
          disabled={game.length === 0 || save.isPending}
          onClick={() => save.mutate()}
        >
          Save
        </Button>
      </DialogActions>
    </Dialog>
  );
}
