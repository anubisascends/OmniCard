import { Box, Stack, Typography } from '@mui/material';
import type { DeckGroup } from '../../lib/deckGrouping';
import { DeckStackCard } from './DeckStackCard';

/**
 * One type column of the deck view: a heading with the group's copy count, and the cards rendered as
 * a vertical overlapping stack. `overflow: visible` lets a hovered/selected card pop past the column
 * bounds. Selection is owned by the parent (single selected card across all columns).
 */
export function DeckStack({
  group,
  width,
  selectedId,
  onSelect,
}: {
  group: DeckGroup;
  width: number;
  selectedId: number | null;
  onSelect: (id: number) => void;
}) {
  return (
    <Stack spacing={0.5} sx={{ width, flex: '0 0 auto' }}>
      <Typography variant="subtitle2" noWrap>
        {group.heading}{' '}
        <Typography component="span" variant="caption" color="text.secondary">
          ({group.count})
        </Typography>
      </Typography>
      <Box sx={{ display: 'flex', flexDirection: 'column', overflow: 'visible', pb: 2 }}>
        {group.cards.map((card, i) => (
          <DeckStackCard
            key={card.id}
            card={card}
            width={width}
            index={i}
            first={i === 0}
            selected={selectedId === card.id}
            onClick={() => onSelect(card.id)}
          />
        ))}
      </Box>
    </Stack>
  );
}
