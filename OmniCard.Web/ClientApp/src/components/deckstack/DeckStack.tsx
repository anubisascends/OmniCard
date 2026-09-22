import { useState } from 'react';
import { Box, Stack, Typography } from '@mui/material';
import type { DeckGroup } from '../../lib/deckGrouping';
import { DeckStackCard } from './DeckStackCard';

/**
 * One type column of the deck view: a heading with the group's copy count, and the cards rendered as
 * a vertical overlapping stack. `overflow: visible` lets a hovered/selected card pop past the column
 * bounds. Selection is owned by the parent (single selected card across all columns).
 *
 * When a card is "active" (hovered, or selected while its drawer is open) every card below it in the
 * stack slides down to fully expose it, so you can read it and move on to the next without the
 * expanded card covering its neighbours.
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
  const [hoveredId, setHoveredId] = useState<number | null>(null);

  // The active card in this column drives the slide-down. Hover wins over selection; a selection in
  // another column leaves this one untouched (index -1 → nothing slides).
  const activeId = hoveredId ?? selectedId;
  const activeIndex = group.cards.findIndex((c) => c.id === activeId);

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
            shiftedDown={activeIndex >= 0 && i > activeIndex}
            onClick={() => onSelect(card.id)}
            onHoverChange={(hovered) => setHoveredId((prev) => (hovered ? card.id : prev === card.id ? null : prev))}
          />
        ))}
      </Box>
    </Stack>
  );
}
