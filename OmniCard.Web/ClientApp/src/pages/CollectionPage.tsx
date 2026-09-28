import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Button, Stack, Typography } from '@mui/material';
import FactCheckIcon from '@mui/icons-material/FactCheck';
import { useGame } from '../context/GameContext';
import { CardTable } from '../components/CardTable';
import { SearchBox } from '../components/SearchBox';
import { DecklistCheckDialog } from '../components/dialogs/DecklistCheckDialog';

export function CollectionPage() {
  const { t } = useTranslation();
  const { game } = useGame();
  const [search, setSearch] = useState('');
  const [q, setQ] = useState('');
  const [deckCheckOpen, setDeckCheckOpen] = useState(false);

  return (
    <Stack spacing={2} sx={{ height: 'calc(100vh - 120px)' }}>
      <Stack direction="row" alignItems="center" justifyContent="space-between" spacing={2}>
        <Typography variant="h4">{t('collection.title')}</Typography>
        <Button variant="outlined" startIcon={<FactCheckIcon />} onClick={() => setDeckCheckOpen(true)}>
          {t('collection.decklist.open')}
        </Button>
      </Stack>
      <SearchBox value={search} onChange={setSearch} onSubmit={setQ} />
      <CardTable game={game} q={q} showLocation />
      {/* Stays mounted so closing it to look at the collection keeps the last check's results. */}
      <DecklistCheckDialog open={deckCheckOpen} onClose={() => setDeckCheckOpen(false)} />
    </Stack>
  );
}
