import { useTranslation } from 'react-i18next';
import { MenuItem, TextField, type SxProps, type Theme } from '@mui/material';
import type { SiteDto } from '../api/types';

/** Value of the "All Sites" entry when `includeAll` is set. */
export const ALL_SITES = 'all' as const;
export type SiteFilterValue = typeof ALL_SITES | number;

/**
 * Site dropdown. Lists `sites` in server order (the default site first, then the rest), optionally
 * preceded by "All Sites" — the Locations page filter uses that form; pickers that need one concrete
 * site (create a location, move a location) don't.
 */
export function SiteSelect({
  sites,
  value,
  onChange,
  includeAll = false,
  label,
  sx,
  disabled,
}: {
  sites: SiteDto[];
  value: SiteFilterValue;
  onChange: (value: SiteFilterValue) => void;
  includeAll?: boolean;
  label?: string;
  sx?: SxProps<Theme>;
  disabled?: boolean;
}) {
  const { t } = useTranslation();
  // Fall back to a valid entry if the stored/requested site isn't in the list (e.g. access revoked).
  const known = value === ALL_SITES ? includeAll : sites.some((s) => s.id === value);
  const current = known ? value : includeAll ? ALL_SITES : (sites[0]?.id ?? '');

  return (
    <TextField
      select
      size="small"
      label={label ?? t('locations.sites.label')}
      value={current}
      disabled={disabled}
      onChange={(e) => {
        const raw = e.target.value;
        onChange(raw === ALL_SITES ? ALL_SITES : Number(raw));
      }}
      sx={{ minWidth: 200, ...sx }}
    >
      {includeAll && <MenuItem value={ALL_SITES}>{t('locations.sites.all')}</MenuItem>}
      {sites.map((s) => (
        <MenuItem key={s.id} value={s.id}>
          {s.name}
          {s.access === 'Read' ? ` (${t('locations.sites.readOnly')})` : ''}
        </MenuItem>
      ))}
    </TextField>
  );
}
