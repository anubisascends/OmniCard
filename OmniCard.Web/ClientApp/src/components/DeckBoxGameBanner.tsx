import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useQuery } from '@tanstack/react-query';
import { Alert, Button } from '@mui/material';
import { api } from '../api/client';
import { DeckBoxGameDialog } from './dialogs/DeckBoxGameDialog';

/**
 * Legacy prompt: deck boxes created before the game-system feature have no game. This banner lists
 * them and opens the assign dialog per box (pre-selecting the game inferred from the cards inside).
 * Hidden when there's nothing to resolve.
 */
export function DeckBoxGameBanner({ onResolved }: { onResolved: () => void }) {
  const { t } = useTranslation();
  const pending = useQuery({
    queryKey: ['deck-boxes-needs-game'],
    queryFn: () => api.deckBoxesNeedingGame(),
  });
  const [active, setActive] = useState<{ id: number; name: string; inferredGame?: string | null } | null>(
    null,
  );

  const boxes = pending.data ?? [];
  if (boxes.length === 0) return null;

  const next = boxes[0];

  return (
    <>
      <Alert
        severity="warning"
        action={
          <Button color="inherit" size="small" onClick={() => setActive(next)}>
            {t('deckbox.banner.assignGame')}
          </Button>
        }
      >
        {boxes.length === 1
          ? t('deckbox.banner.oneBoxNoGame', { name: next.name })
          : t('deckbox.banner.manyBoxesNoGame', { count: boxes.length, name: next.name })}
      </Alert>
      {active && (
        <DeckBoxGameDialog
          open
          deckBoxId={active.id}
          deckBoxName={active.name}
          initialGame={active.inferredGame ?? ''}
          onClose={() => setActive(null)}
          onSaved={() => {
            pending.refetch();
            onResolved();
          }}
        />
      )}
    </>
  );
}
