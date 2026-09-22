import { Box, Chip } from '@mui/material';
import type { CardDto } from '../../api/types';
import { CardImage } from '../CardImage';

// Card aspect ratio (width / height) and how much of the top of a stacked card peeks out from under
// the one above it — enough to read the card's name.
const ASPECT = 5 / 7;
const REVEAL = 34; // px of the card's top strip left visible when collapsed

/**
 * One card in an overlapping deck stack. Collapsed, only its top strip (name) shows; on hover — or
 * when selected — it pops to the front (raised z-index + slight scale + shadow) so the whole card is
 * readable and clickable, and every card below it slides down (`shiftedDown`) to expose it fully.
 * Foil copies get the shared iridescent sheen. `index` drives the base stacking order (later cards
 * sit above earlier ones); `first` skips the overlap offset.
 */
export function DeckStackCard({
  card,
  width,
  index,
  first,
  selected,
  shiftedDown,
  onClick,
  onHoverChange,
}: {
  card: CardDto;
  width: number;
  index: number;
  first: boolean;
  selected: boolean;
  shiftedDown: boolean;
  onClick: () => void;
  onHoverChange: (hovered: boolean) => void;
}) {
  const height = width / ASPECT;

  // How far a card must drop to clear the card above it once that one is expanded: the overlap amount.
  const shift = height - REVEAL;

  // Expanded look, applied on hover and while selected. Cards below an expanded card instead slide
  // straight down by the overlap amount so the expanded card is fully visible.
  const expanded = {
    zIndex: 1000,
    transform: 'translateY(-2px) scale(1.03)',
    boxShadow: 6,
  } as const;
  const active = selected;

  return (
    <Box
      onClick={onClick}
      onMouseEnter={() => onHoverChange(true)}
      onMouseLeave={() => onHoverChange(false)}
      title={card.name}
      sx={{
        position: 'relative',
        width,
        height,
        flex: '0 0 auto',
        cursor: 'pointer',
        marginTop: first ? 0 : `-${shift}px`,
        zIndex: index, // later cards overlap earlier ones
        transition: 'transform 120ms ease, box-shadow 120ms ease',
        outline: selected ? '2px solid' : 'none',
        outlineColor: 'primary.main',
        outlineOffset: 1,
        ...(shiftedDown ? { transform: `translateY(${shift}px)` } : null),
        '&:hover': expanded,
        ...(active ? expanded : null),
      }}
    >
      <CardImage
        src={card.imageUri ?? undefined}
        alt={card.name}
        foil={card.isFoil}
        wrapperSx={{ width: '100%', height: '100%' }}
        sx={{
          width: '100%',
          height: '100%',
          objectFit: 'cover',
          objectPosition: 'top',
          display: 'block',
          boxShadow: '0 1px 3px rgba(0,0,0,0.4)',
        }}
      />
      {card.quantity > 1 && (
        <Chip
          label={`×${card.quantity}`}
          size="small"
          sx={{
            position: 'absolute',
            top: 4,
            right: 4,
            height: 20,
            fontWeight: 600,
            bgcolor: 'rgba(0,0,0,0.75)',
            color: '#fff',
          }}
        />
      )}
    </Box>
  );
}
