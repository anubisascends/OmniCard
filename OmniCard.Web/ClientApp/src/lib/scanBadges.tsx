import { Box, Chip, Stack, Tooltip } from '@mui/material';
import StarIcon from '@mui/icons-material/Star';
import type { ScanBadgeSettingsDto } from '../api/types';

/**
 * "The List" badge for MTG scans where the Planeswalker glyph was detected (a plst reprint — a distinct,
 * cheaper printing). Renders nothing for non-List cards. When the plst printing couldn't be resolved the
 * badge turns into a warning so the reviewer knows the shown printing/price may be the pricier original.
 */
export function ListReprintChip({
  isListReprint,
  unresolved,
  size = 'small',
}: {
  isListReprint?: boolean;
  unresolved?: boolean;
  size?: 'small' | 'medium';
}) {
  if (!isListReprint) return null;
  return unresolved ? (
    <Tooltip title="Detected as a The List reprint, but its plst printing wasn't found in the catalog — the printing and price shown may be the more expensive original. Check before adding.">
      <Chip size={size} color="warning" variant="outlined" label="The List?" />
    </Tooltip>
  ) : (
    <Tooltip title="The List (plst) reprint — an official, cheaper printing. Matched to the plst printing.">
      <Chip size={size} color="secondary" variant="outlined" label="The List" />
    </Tooltip>
  );
}

/** The localized currency symbol (e.g. "$", "€", "£") for an ISO code, rendered per the browser's
 * locale — falls back to the raw code if the runtime can't resolve a narrow symbol. */
export function currencySymbol(currencyCode: string): string {
  try {
    const parts = new Intl.NumberFormat(undefined, {
      style: 'currency',
      currency: currencyCode,
      currencyDisplay: 'narrowSymbol',
    }).formatToParts(0);
    return parts.find((p) => p.type === 'currency')?.value ?? currencyCode;
  } catch {
    return currencyCode;
  }
}

/** Format an amount as localized currency (browser locale + the configured ISO code). */
function money(amount: number, currencyCode: string): string {
  try {
    return amount.toLocaleString(undefined, { style: 'currency', currency: currencyCode });
  } catch {
    return amount.toFixed(2);
  }
}

/** The 1-based value tier for a price given the ascending threshold ladder: tier N ⇒ N currency
 * signs. A price at or below `thresholds[i]` is tier `i+1`; above the last threshold is the top tier
 * (`thresholds.length + 1`). */
export function priceTier(price: number, thresholds: number[]): number {
  for (let i = 0; i < thresholds.length; i++) {
    if (price <= thresholds[i]) return i + 1;
  }
  return thresholds.length + 1;
}

/** A human-readable description of a tier's price range, for the badge tooltip. */
function tierRange(tier: number, thresholds: number[], currencyCode: string): string {
  const m = (n: number) => money(n, currencyCode);
  if (tier === 1) return `≤ ${m(thresholds[0])}`;
  if (tier > thresholds.length) return `> ${m(thresholds[thresholds.length - 1])}`;
  return `${m(thresholds[tier - 2])} – ${m(thresholds[tier - 1])}`;
}

// Ascending emphasis: cheap cards are muted, high-value cards pop gold/green.
const TIER_COLORS = ['text.secondary', 'info.main', 'primary.main', 'warning.main', 'success.main'];

/**
 * Scan-tile status badges: a gold star for a card not yet in the collection, and a run of localized
 * currency signs indicating the card's value tier. Both are optional — nothing renders when the card
 * is neither new nor priced.
 */
export function ScanValueBadges({
  isNew,
  price,
  settings,
  size = 'small',
}: {
  isNew?: boolean;
  price?: number | null;
  settings?: ScanBadgeSettingsDto;
  /** 'small' for the compact master row, 'medium' for the detail panel. */
  size?: 'small' | 'medium';
}) {
  const fontSize = size === 'medium' ? '1rem' : '0.8rem';
  const starSize = size === 'medium' ? 22 : 18;

  // A non-positive price means "no catalog data" (not a $0 card) — don't render a value tier for it.
  const showTier = price != null && price > 0 && settings != null && settings.thresholds.length > 0;
  const tier = showTier ? priceTier(price, settings.thresholds) : 0;
  const symbol = settings ? currencySymbol(settings.currencyCode) : '$';

  if (!isNew && !showTier) return null;

  return (
    <Stack direction="row" spacing={0.5} alignItems="center" component="span">
      {isNew && (
        <Tooltip title="New — not in your collection yet">
          <StarIcon sx={{ color: '#f5b301', fontSize: starSize }} aria-label="new card" />
        </Tooltip>
      )}
      {showTier && (
        <Tooltip
          title={`Value: ${money(price as number, settings!.currencyCode)} · ${tierRange(
            tier,
            settings!.thresholds,
            settings!.currencyCode,
          )}`}
        >
          <Box
            component="span"
            aria-label={`value tier ${tier} of ${settings!.thresholds.length + 1}`}
            sx={{
              fontWeight: 700,
              fontSize,
              letterSpacing: '-0.05em',
              lineHeight: 1,
              color: TIER_COLORS[Math.min(tier, TIER_COLORS.length) - 1],
              whiteSpace: 'nowrap',
            }}
          >
            {symbol.repeat(tier)}
          </Box>
        </Tooltip>
      )}
    </Stack>
  );
}
