import { Backdrop, Popover } from '@mui/material';
import { CardImage } from './CardImage';
import { PREVIEW_BASE_MAX_HEIGHT, PREVIEW_BASE_WIDTH, usePreviewScale } from '../lib/previewScale';
import { useCanHover } from '../lib/responsive';

/**
 * What to preview and where: the hovered element anchors the popup to its right. `touch` marks a
 * long-press preview (see `useLongPress`), shown centered over the page until the next tap.
 */
export type CardHover = { el: HTMLElement; url: string; foil: boolean; touch?: boolean };

/**
 * Enlarged card-artwork popup shown while hovering a card name or thumbnail — the shared preview
 * used by the collection/location grids, the Sets checklist and the Scan correction search. Sized by
 * the user's Settings preview-scale preference. Pointer events pass through, so moving the mouse off
 * the anchor (which clears `hover`) is what closes it.
 *
 * Usage: keep `const [hover, setHover] = useState<CardHover | null>(null)`, set it from the anchor's
 * `onMouseEnter` (`{ el: e.currentTarget, url, foil }`), clear it `onMouseLeave`, and render
 * `<CardHoverPreview hover={hover} onClose={() => setHover(null)} />` once. On touch screens, also
 * spread `useLongPress()` onto the anchor and set `{ …, touch: true }` from it: a touch screen can't
 * hover, so the mouse-enter a tap fakes is ignored there.
 */
export function CardHoverPreview({ hover, onClose }: { hover: CardHover | null; onClose: () => void }) {
  const previewScale = usePreviewScale();
  const canHover = useCanHover();

  if (hover?.touch) {
    return (
      <Backdrop open onClick={onClose} sx={{ zIndex: (t) => t.zIndex.modal + 1, p: 2 }}>
        <CardImage
          src={hover.url}
          foil={hover.foil}
          sx={{ width: 'min(88vw, 420px)', maxHeight: '80vh', objectFit: 'contain', display: 'block', borderRadius: 3 }}
        />
      </Backdrop>
    );
  }

  return (
    <Popover
      open={!!hover && canHover}
      anchorEl={hover?.el}
      onClose={onClose}
      anchorOrigin={{ vertical: 'center', horizontal: 'right' }}
      transformOrigin={{ vertical: 'center', horizontal: 'left' }}
      // A passive preview: never take focus, so hovering a result doesn't interrupt typing in a search box.
      disableAutoFocus
      disableEnforceFocus
      disableRestoreFocus
      sx={{ pointerEvents: 'none' }}
      slotProps={{ paper: { sx: { p: 0.5 } } }}
    >
      {hover && (
        <CardImage
          src={hover.url}
          foil={hover.foil}
          sx={{
            width: (PREVIEW_BASE_WIDTH * previewScale) / 100,
            maxHeight: (PREVIEW_BASE_MAX_HEIGHT * previewScale) / 100,
            objectFit: 'contain',
            display: 'block',
          }}
        />
      )}
    </Popover>
  );
}
