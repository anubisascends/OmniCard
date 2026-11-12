import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery } from '@tanstack/react-query';
import {
  Alert,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  MenuItem,
  Stack,
  Typography,
} from '@mui/material';
import { api } from '../../api/client';
import type { LocationSummaryDto, SetInfoDto } from '../../api/types';
import { SetMultiSelect } from '../SetMultiSelect';
import { useDefaultSets } from '../locationCardFilter';

import { SelectField } from '../SelectField';
/**
 * Fixes a location to one game (or any game) and picks the sets its card searches start on. The game is a
 * hard rule: cards of other games can't be added, and the server refuses (409) a game the location's cards
 * already break. The sets are only a default for the binder's Sets filter and Add card; nothing outside them
 * is blocked or moved. A deck box always keeps a game — it's changed with Game & deck type.
 */
export function LocationCardFilterDialog({
  open,
  loc,
  onClose,
  onSaved,
}: {
  open: boolean;
  loc: LocationSummaryDto;
  onClose: () => void;
  onSaved: () => void;
}) {
  const { t } = useTranslation();
  const isDeckBox = loc.type === 'Deck Box';
  const games = useQuery({ queryKey: ['games'], queryFn: () => api.games(), enabled: open });
  const initial = useDefaultSets(loc.game, loc.defaultSets);
  const [game, setGame] = useState(loc.game ?? '');
  const [sets, setSets] = useState<SetInfoDto[]>([]);

  // Reset to the location's values each time the dialog opens (once the set names are in).
  const initialKey = initial.ready ? initial.sets.map((s) => `${s.setCode}:${s.setName}`).join('|') : null;
  useEffect(() => {
    if (!open || initialKey == null) return;
    setGame(loc.game ?? '');
    setSets(initial.sets);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, loc.id, loc.game, initialKey]);

  const save = useMutation({
    mutationFn: () =>
      api.locationSetCardFilter(
        loc.id,
        game || null,
        game ? sets.map((s) => s.setCode) : [],
      ),
    onSuccess: () => {
      onSaved();
      onClose();
    },
  });

  return (
    <Dialog open={open} onClose={onClose} maxWidth="xs" fullWidth>
      <DialogTitle>{t('locations.cardFilter.dialogTitle', { name: loc.name })}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <SelectField
            size="small"
            label={t('common.labels.game')}
            value={game}
            disabled={isDeckBox}
            onChange={(e) => {
              setGame(e.target.value);
              setSets([]);
            }}
            helperText={isDeckBox ? t('locations.cardFilter.deckBoxGameHelp') : t('locations.cardFilter.gameHelp')}
          >
            {!isDeckBox && (
              <MenuItem value="">
                <em>{t('locations.cardFilter.anyGame')}</em>
              </MenuItem>
            )}
            {(games.data ?? (loc.game ? [{ id: loc.game, displayName: loc.game }] : [])).map((g) => (
              <MenuItem key={g.id} value={g.id}>
                {g.displayName}
              </MenuItem>
            ))}
          </SelectField>
          {game ? (
            <SetMultiSelect
              game={game}
              value={sets}
              onChange={setSets}
              label={t('locations.cardFilter.defaultSets')}
              helperText={t('locations.cardFilter.setsHelp')}
            />
          ) : (
            <Typography variant="body2" color="text.secondary">
              {t('locations.cardFilter.setsNeedGame')}
            </Typography>
          )}
          {save.isError && <Alert severity="error">{(save.error as Error).message}</Alert>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('common.actions.cancel')}</Button>
        <Button
          variant="contained"
          disabled={save.isPending || initialKey == null}
          onClick={() => save.mutate()}
        >
          {t('common.actions.save')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
