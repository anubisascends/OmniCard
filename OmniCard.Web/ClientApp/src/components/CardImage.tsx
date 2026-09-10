import { Box, type SxProps, type Theme } from '@mui/material';
import type React from 'react';

/**
 * Card artwork with an optional iridescent "foil" overlay. When `foil` is set, a transparent
 * rainbow sheen is layered over the image (animated diagonally, like light catching a foil card)
 * so foil copies are visually distinguishable everywhere art is shown. The overlay is
 * `pointer-events: none`, so drag / click / context-menu handlers on the image still fire.
 *
 * `sx` styles the <img> (size it exactly as you would a bare image); `wrapperSx` styles the
 * positioning wrapper (only needed when the image must fill a sized parent — pass
 * `{ width: '100%', height: '100%' }`).
 */
export function CardImage({
  src,
  alt = '',
  foil = false,
  sx,
  wrapperSx,
  draggable,
  onDragStart,
  onContextMenu,
  onError,
  onMouseEnter,
  onMouseLeave,
}: {
  src?: string;
  alt?: string;
  foil?: boolean;
  sx?: SxProps<Theme>;
  wrapperSx?: SxProps<Theme>;
  draggable?: boolean;
  onDragStart?: React.DragEventHandler<HTMLImageElement>;
  onContextMenu?: React.MouseEventHandler<HTMLImageElement>;
  onError?: React.ReactEventHandler<HTMLImageElement>;
  onMouseEnter?: React.MouseEventHandler<HTMLElement>;
  onMouseLeave?: React.MouseEventHandler<HTMLElement>;
}) {
  return (
    <Box
      onMouseEnter={onMouseEnter}
      onMouseLeave={onMouseLeave}
      sx={{ position: 'relative', display: 'inline-flex', lineHeight: 0, ...wrapperSx }}
    >
      <Box
        component="img"
        src={src}
        alt={alt}
        draggable={draggable}
        onDragStart={onDragStart}
        onContextMenu={onContextMenu}
        onError={onError}
        sx={sx}
      />
      {foil && src && <Box aria-hidden sx={FOIL_SHEEN_SX} />}
    </Box>
  );
}

/** Animated transparent rainbow, blended over the art. `overlay` blend keeps the underlying image
 * readable while adding the iridescent shimmer; the gradient slides diagonally forever. */
const FOIL_SHEEN_SX: SxProps<Theme> = {
  position: 'absolute',
  inset: 0,
  borderRadius: 'inherit',
  pointerEvents: 'none',
  mixBlendMode: 'overlay',
  opacity: 0.65,
  backgroundImage:
    'linear-gradient(115deg,' +
    ' rgba(255,0,128,0) 0%,' +
    ' rgba(255,0,128,0.55) 18%,' +
    ' rgba(255,214,0,0.55) 32%,' +
    ' rgba(0,255,150,0.55) 46%,' +
    ' rgba(0,196,255,0.55) 60%,' +
    ' rgba(150,0,255,0.55) 74%,' +
    ' rgba(255,0,128,0) 92%)',
  backgroundSize: '300% 300%',
  // Ease-in-out + `alternate` sweeps the sheen across and gently back, easing at each turnaround —
  // no hard jump from a linear loop resetting its position.
  animation: 'foilSheen 7s ease-in-out infinite alternate',
  '@keyframes foilSheen': {
    '0%': { backgroundPosition: '0% 50%' },
    '100%': { backgroundPosition: '100% 50%' },
  },
  '@media (prefers-reduced-motion: reduce)': {
    animation: 'none',
    backgroundPosition: '50% 50%',
  },
};
