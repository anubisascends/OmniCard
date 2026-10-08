import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Box, Button, Stack, Typography } from '@mui/material';
import FactCheckIcon from '@mui/icons-material/FactCheck';
import { useGame } from '../context/GameContext';
import { CardTable } from '../components/CardTable';
import { SearchBox } from '../components/SearchBox';
import { DecklistCheckDialog } from '../components/dialogs/DecklistCheckDialog';
import { SavedViewPicker } from '../components/views/SavedViewPicker';
import { useSavedViews } from '../components/views/useSavedViews';

export function CollectionPage() {
  const { t } = useTranslation();
  const { game } = useGame();
  const sv = useSavedViews({ page: 'Collection' });
  const [search, setSearch] = useState(sv.state.q);
  // Switching views loads its search into the box.
  useEffect(() => setSearch(sv.state.q), [sv.state.q]);
  const [deckCheckOpen, setDeckCheckOpen] = useState(false);

  return (
    <Stack spacing={2} sx={{ height: 'calc(100vh - 120px)' }}>
      <Stack direction="row" alignItems="center" justifyContent="space-between" spacing={2}>
        <Typography variant="h4">{t('collection.title')}</Typography>
        <Button variant="outlined" startIcon={<FactCheckIcon />} onClick={() => setDeckCheckOpen(true)}>
          {t('collection.decklist.open')}
        </Button>
      </Stack>
      <Stack direction="row" spacing={1} alignItems="center">
        <Box sx={{ flexGrow: 1 }}>
          <SearchBox value={search} onChange={setSearch} onSubmit={(q) => sv.setState({ q })} />
        </Box>
        <SavedViewPicker sv={sv} />
      </Stack>
      <CardTable game={game} showLocation view={sv.state} onViewChange={sv.setState} />
      {/* Stays mounted so closing it to look at the collection keeps the last check's results. */}
      <DecklistCheckDialog open={deckCheckOpen} onClose={() => setDeckCheckOpen(false)} />
    </Stack>
  );
}
