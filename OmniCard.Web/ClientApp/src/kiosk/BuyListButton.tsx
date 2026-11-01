import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import {
  AppBar,
  Box,
  Button,
  Card,
  Chip,
  CircularProgress,
  Dialog,
  IconButton,
  Stack,
  ToggleButton,
  ToggleButtonGroup,
  Toolbar,
  Typography,
} from '@mui/material';
import CloseIcon from '@mui/icons-material/Close';
import TrackChangesIcon from '@mui/icons-material/TrackChanges';
import { useTranslation } from 'react-i18next';
import type { KioskBuyListEntryDto } from '../api/types';
import { CardImage } from '../components/CardImage';
import { kioskApi } from './kioskApi';

/**
 * Opens "Cards we're looking for": what the collection's owner wants to buy, so customers know what to
 * bring in. It's a screen of its own because most of these cards aren't in stock, so they could never
 * appear in the browse grid. Shows only what the server sends: art, name, a wanted printing or finish,
 * and how keenly it's wanted — never quantities, prices or staff notes. Hidden when nothing is wanted.
 */
export function BuyListButton({ inline = false }: { inline?: boolean }) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const entries = useQuery({ queryKey: ['kiosk-buy-list'], queryFn: kioskApi.buyList, refetchOnWindowFocus: false });
  if (!entries.data?.length) return null;
  return (
    <>
      <Button
        color={inline ? 'inherit' : 'primary'}
        size={inline ? 'medium' : 'large'}
        variant={inline ? 'text' : 'outlined'}
        startIcon={<TrackChangesIcon />}
        onClick={() => setOpen(true)}
      >
        {t('kiosk.app.buyList.open')}
      </Button>
      {open && <BuyListDialog entries={entries.data} loading={entries.isFetching} onClose={() => setOpen(false)} />}
    </>
  );
}

function BuyListDialog({
  entries,
  loading,
  onClose,
}: {
  entries: KioskBuyListEntryDto[];
  loading: boolean;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const games = useMemo(() => {
    const byId = new Map(entries.map((e) => [e.game, e.gameName]));
    return [...byId].sort((a, b) => a[1].localeCompare(b[1]));
  }, [entries]);
  const [game, setGame] = useState<string | null>(null);
  const activeGame = game ?? games[0]?.[0] ?? null;
  const shown = entries.filter((e) => e.game === activeGame);

  return (
    <Dialog fullScreen open onClose={onClose}>
      <AppBar position="sticky" color="default" elevation={1}>
        <Toolbar sx={{ gap: 2 }}>
          <TrackChangesIcon color="warning" />
          <Typography variant="h5" sx={{ flexGrow: 1 }} noWrap>
            {t('kiosk.app.buyList.title')}
          </Typography>
          {loading && <CircularProgress size={20} />}
          <IconButton onClick={onClose} aria-label={t('common.actions.close')}>
            <CloseIcon />
          </IconButton>
        </Toolbar>
      </AppBar>
      <Stack spacing={2} sx={{ p: 2 }}>
        <Typography variant="h6" color="text.secondary">
          {t('kiosk.app.buyList.body')}
        </Typography>
        {games.length > 1 && (
          <ToggleButtonGroup
            exclusive
            value={activeGame}
            onChange={(_, v: string | null) => v && setGame(v)}
            sx={{ flexWrap: 'wrap' }}
          >
            {games.map(([id, name]) => (
              <ToggleButton key={id} value={id} sx={{ px: 3 }}>
                {name}
              </ToggleButton>
            ))}
          </ToggleButtonGroup>
        )}
        <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: 'repeat(auto-fill, minmax(170px, 1fr))' }}>
          {shown.map((entry, i) => (
            <Tile key={`${entry.name}|${entry.setCode ?? ''}|${i}`} entry={entry} />
          ))}
        </Box>
      </Stack>
    </Dialog>
  );
}

function Tile({ entry }: { entry: KioskBuyListEntryDto }) {
  const { t } = useTranslation();
  return (
    <Card variant="outlined" sx={{ p: 1, display: 'flex', flexDirection: 'column' }}>
      <CardImage
        src={entry.imageUri ?? undefined}
        alt={entry.name}
        foil={entry.foil === true}
        wrapperSx={{ width: '100%' }}
        sx={{ width: '100%', aspectRatio: '0.716', objectFit: 'contain', borderRadius: 1 }}
      />
      <Typography variant="subtitle1" sx={{ mt: 1, fontWeight: 600, lineHeight: 1.2 }}>
        {entry.name}
      </Typography>
      {entry.setName && (
        <Typography variant="body2" color="text.secondary">
          {entry.setName}
        </Typography>
      )}
      <Stack direction="row" sx={{ mt: 0.5, flexWrap: 'wrap', gap: 0.5 }}>
        {entry.priority !== 'Normal' && (
          <Chip size="small" color="warning" label={t(`kiosk.app.buyList.priority.${entry.priority}`)} />
        )}
        {entry.foil != null && (
          <Chip size="small" variant="outlined" label={t(entry.foil ? 'kiosk.app.buyList.foilOnly' : 'kiosk.app.buyList.nonFoilOnly')} />
        )}
      </Stack>
    </Card>
  );
}
