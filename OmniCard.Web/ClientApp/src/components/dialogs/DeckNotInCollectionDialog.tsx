import { useTranslation } from 'react-i18next';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  IconButton,
  List,
  ListItem,
  ListItemText,
  Tooltip,
  Typography,
} from '@mui/material';
import CloseIcon from '@mui/icons-material/Close';
import { api } from '../../api/client';
import { useFormatters } from '../../i18n/format';
import type { DeckBoxMissingCardDto } from '../../api/types';

/** The cards a deck box's linked deck needs that were marked "not in collection" at the last sync. */
export function DeckNotInCollectionDialog({
  open,
  deckBoxId,
  deckBoxName,
  cards,
  canEdit,
  onClose,
}: {
  open: boolean;
  deckBoxId: number;
  deckBoxName: string;
  cards: DeckBoxMissingCardDto[];
  canEdit: boolean;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const qc = useQueryClient();
  const remove = useMutation({
    mutationFn: (cardName: string) => api.removeDeckNotInCollection(deckBoxId, cardName),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['deck-not-in-collection', deckBoxId] }),
  });

  return (
    <Dialog open={open} onClose={onClose} maxWidth="xs" fullWidth>
      <DialogTitle>{t('deckbox.notInCollection.title', { name: deckBoxName })}</DialogTitle>
      <DialogContent dividers>
        <Typography variant="body2" color="text.secondary" gutterBottom>
          {t('deckbox.notInCollection.help')}
        </Typography>
        {cards.length === 0 ? (
          <Alert severity="success">{t('deckbox.notInCollection.empty')}</Alert>
        ) : (
          <List dense disablePadding>
            {cards.map((c) => (
              <ListItem
                key={c.cardName}
                disableGutters
                secondaryAction={
                  canEdit && (
                    <Tooltip title={t('deckbox.notInCollection.removeTooltip')}>
                      <IconButton
                        edge="end"
                        size="small"
                        disabled={remove.isPending}
                        onClick={() => remove.mutate(c.cardName)}
                        aria-label={t('deckbox.notInCollection.removeTooltip')}
                      >
                        <CloseIcon fontSize="small" />
                      </IconButton>
                    </Tooltip>
                  )
                }
              >
                <ListItemText
                  primary={`${fmt.number(c.quantity)}× ${c.cardName}`}
                  secondary={c.setCode ? `${c.setCode.toUpperCase()}${c.collectorNumber ? ` #${c.collectorNumber}` : ''}` : undefined}
                />
              </ListItem>
            ))}
          </List>
        )}
        {remove.error && <Alert severity="error">{(remove.error as Error).message}</Alert>}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('common.actions.close')}</Button>
      </DialogActions>
    </Dialog>
  );
}
