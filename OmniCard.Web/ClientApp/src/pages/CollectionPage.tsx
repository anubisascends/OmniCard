import { useState } from 'react';
import { Box, Stack, TextField, Typography } from '@mui/material';
import { useGame } from '../context/GameContext';
import { CardTable } from '../components/CardTable';

export function CollectionPage() {
  const { game } = useGame();
  const [search, setSearch] = useState('');
  const [q, setQ] = useState('');

  return (
    <Stack spacing={2} sx={{ height: 'calc(100vh - 120px)' }}>
      <Typography variant="h4">Collection</Typography>
      <Box
        component="form"
        onSubmit={(e) => {
          e.preventDefault();
          setQ(search);
        }}
      >
        <TextField
          fullWidth
          size="small"
          placeholder="Search — try name, set:dom, cn:123, c:u, r:rare, is:foil, tag:trade"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
      </Box>
      <CardTable game={game} q={q} showLocation />
    </Stack>
  );
}
