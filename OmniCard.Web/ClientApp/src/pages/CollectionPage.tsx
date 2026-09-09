import { useState } from 'react';
import { Stack, Typography } from '@mui/material';
import { useGame } from '../context/GameContext';
import { CardTable } from '../components/CardTable';
import { SearchBox } from '../components/SearchBox';

export function CollectionPage() {
  const { game } = useGame();
  const [search, setSearch] = useState('');
  const [q, setQ] = useState('');

  return (
    <Stack spacing={2} sx={{ height: 'calc(100vh - 120px)' }}>
      <Typography variant="h4">Collection</Typography>
      <SearchBox value={search} onChange={setSearch} onSubmit={setQ} />
      <CardTable game={game} q={q} showLocation />
    </Stack>
  );
}
