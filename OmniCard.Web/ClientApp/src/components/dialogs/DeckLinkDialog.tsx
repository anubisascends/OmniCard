import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import LinkOffIcon from '@mui/icons-material/LinkOff';
import { api } from '../../api/client';
import type { LocationSummaryDto } from '../../api/types';

/** Link a deck box to its Moxfield/Archidekt deck URL, change the link, or remove it. */
export function DeckLinkDialog({
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
  const [url, setUrl] = useState('');

  const save = useMutation({
    mutationFn: (value: string | null) => api.setDeckLink(loc.id, value),
    onSuccess: () => {
      onSaved();
      onClose();
    },
  });
  const { reset } = save;

  useEffect(() => {
    if (!open) return;
    setUrl(loc.sourceUrl ?? '');
    reset();
  }, [open, loc.sourceUrl, reset]);

  const linked = !!loc.sourceUrl;
  const changed = url.trim() !== (loc.sourceUrl ?? '');

  return (
    <Dialog open={open} onClose={save.isPending ? undefined : onClose} maxWidth="sm" fullWidth>
      <DialogTitle>{t('deckbox.link.dialogTitle', { name: loc.name })}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ pt: 1 }}>
          <Typography variant="body2" color="text.secondary">
            {t('deckbox.link.help')}
          </Typography>
          <TextField
            size="small"
            label={t('deckbox.link.urlLabel')}
            placeholder="https://moxfield.com/decks/…"
            value={url}
            onChange={(e) => {
              setUrl(e.target.value);
              reset();
            }}
            autoFocus
          />
          {linked && changed && <Alert severity="info">{t('deckbox.link.changeWarning')}</Alert>}
          {save.error && <Alert severity="error">{(save.error as Error).message}</Alert>}
        </Stack>
      </DialogContent>
      <DialogActions>
        {linked && (
          <Button color="error" startIcon={<LinkOffIcon />} disabled={save.isPending} onClick={() => save.mutate(null)}>
            {t('deckbox.link.remove')}
          </Button>
        )}
        <Box sx={{ flexGrow: 1 }} />
        <Button onClick={onClose} disabled={save.isPending}>
          {t('common.actions.cancel')}
        </Button>
        <Button
          variant="contained"
          disabled={!url.trim() || !changed || save.isPending}
          onClick={() => save.mutate(url.trim())}
        >
          {t('common.actions.save')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
