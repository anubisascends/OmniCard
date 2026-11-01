import { Chip, Tooltip, type ChipProps, type SxProps, type Theme } from '@mui/material';
import DiamondIcon from '@mui/icons-material/Diamond';
import TrackChangesIcon from '@mui/icons-material/TrackChanges';
import { useTranslation } from 'react-i18next';
import type { BuyListHitDto, BuyListPriority } from '../api/types';

/**
 * The Buy List marker for staff screens: always a labelled chip (never colour alone), plus
 * {@link buyListFrameSx} to frame the row. Never use these on anything a customer sees; the kiosk's
 * screen has its own wording and the intake customer view shows no marker at all.
 */
export function BuyListChip({
  priority,
  wanted,
  size = 'small',
}: {
  priority: BuyListPriority;
  /** Copies still wanted, shown on the chip. */
  wanted?: number;
  size?: ChipProps['size'];
}) {
  const { t } = useTranslation();
  const label =
    wanted == null
      ? t(`buyList.marker.${priority}`)
      : t(`buyList.marker.wanted${priority}`, { count: wanted });
  return (
    <Chip
      size={size}
      color="warning"
      variant={priority === 'Normal' ? 'outlined' : 'filled'}
      icon={priority === 'Grail' ? <DiamondIcon /> : <TrackChangesIcon />}
      label={label}
    />
  );
}

/** The chip with its staff note on hover. */
export function BuyListHitChip({ hit }: { hit: BuyListHitDto }) {
  const chip = <BuyListChip priority={hit.priority} wanted={hit.wantedQuantity} />;
  return hit.note ? <Tooltip title={hit.note}>{chip}</Tooltip> : chip;
}

/** An inset gold frame, so it neither shifts the layout nor gets clipped. Grail is heavier and tinted. */
export function buyListFrameSx(priority: BuyListPriority | undefined): SxProps<Theme> {
  if (!priority) return {};
  const width = priority === 'Normal' ? 2 : 3;
  return {
    boxShadow: (theme) => `inset 0 0 0 ${width}px ${theme.palette.warning.main}`,
    ...(priority === 'Grail' && {
      backgroundImage: (theme: Theme) =>
        `linear-gradient(${theme.palette.warning.main}1f, ${theme.palette.warning.main}1f)`,
    }),
  };
}
