import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import {
  Box,
  CircularProgress,
  MenuItem,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import { api } from '../../api/client';
import { groupDeckCards, totalDeckCards, type DeckGroupMode } from '../../lib/deckGrouping';
import { CardEditDrawer } from '../dialogs/CardEditDrawer';
import { DeckStack } from './DeckStack';

// One page big enough to hold any real deck, so the whole thing groups in a single fetch.
const DECK_PAGE_SIZE = 500;
const CARD_WIDTH = 170;
const GROUP_MODE_KEY = 'omnicard.deckstack.groupmode';

/**
 * Archidekt-style stacked deck view for a location: cards grouped into type columns (Commander first),
 * each an overlapping stack whose cards expand on hover/selection. Clicking a card selects it and
 * opens the detail drawer to read/edit it. Game-agnostic — grouping is derived from each card's type.
 */
export function DeckStackView({
  containerId,
  game,
  q,
}: {
  containerId: number;
  game?: string;
  q?: string;
}) {
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [detailId, setDetailId] = useState<number | null>(null);
  const [groupMode, setGroupMode] = useState<DeckGroupMode>(
    () => (localStorage.getItem(GROUP_MODE_KEY) === 'tag' ? 'tag' : 'type'),
  );
  const setMode = (m: DeckGroupMode) => {
    setGroupMode(m);
    localStorage.setItem(GROUP_MODE_KEY, m);
  };

  const query = useQuery({
    queryKey: ['deck-stack', containerId, game ?? null, q ?? ''],
    // stacked:true collapses identical copies into one row with a quantity, so counts are right.
    queryFn: () => api.collection({ containerId, game, q, stacked: true, skip: 0, take: DECK_PAGE_SIZE }),
  });

  const cards = query.data?.items ?? [];
  const groups = useMemo(() => groupDeckCards(cards, groupMode), [cards, groupMode]);
  const total = useMemo(() => totalDeckCards(cards), [cards]);

  const select = (id: number) => {
    setSelectedId(id);
    setDetailId(id);
  };

  if (query.isLoading) {
    return (
      <Stack alignItems="center" sx={{ py: 4 }}>
        <CircularProgress />
      </Stack>
    );
  }

  if (cards.length === 0) {
    return (
      <Typography variant="body2" color="text.secondary" sx={{ py: 2 }}>
        No cards to show.
      </Typography>
    );
  }

  return (
    <Stack spacing={1} sx={{ minHeight: 0, flex: 1 }}>
      <Stack direction="row" spacing={1.5} alignItems="center">
        <Typography variant="body2" color="text.secondary">
          {total} card{total === 1 ? '' : 's'} · {groups.length} group{groups.length === 1 ? '' : 's'}
        </Typography>
        <Box sx={{ flexGrow: 1 }} />
        <TextField
          select
          size="small"
          label="Group by"
          value={groupMode}
          onChange={(e) => setMode(e.target.value as DeckGroupMode)}
          sx={{ minWidth: 130 }}
        >
          <MenuItem value="type">Type</MenuItem>
          <MenuItem value="tag">Tags</MenuItem>
        </TextField>
      </Stack>
      <Box
        sx={{
          display: 'flex',
          gap: 2,
          alignItems: 'flex-start',
          overflowX: 'auto',
          overflowY: 'auto',
          flex: 1,
          pt: 1,
          px: 0.5,
        }}
      >
        {groups.map((g) => (
          <DeckStack
            key={g.key}
            group={g}
            width={CARD_WIDTH}
            selectedId={selectedId}
            onSelect={select}
          />
        ))}
      </Box>

      <CardEditDrawer cardId={detailId} onClose={() => setDetailId(null)} />
    </Stack>
  );
}
