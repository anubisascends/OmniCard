import { Box, Checkbox, FormControlLabel, Stack, Typography } from '@mui/material';
import type { PermissionCatalogDto } from '../../api/types';

/**
 * A grouped checklist over the permission catalog. Used for role permissions and for a user's
 * grant/deny override lists. Controlled: `selected` is the set of ticked permission keys,
 * `onChange` returns the next set.
 */
export function PermissionChecklist({
  catalog,
  selected,
  onChange,
  disabled,
}: {
  catalog: PermissionCatalogDto | undefined;
  selected: string[];
  onChange: (next: string[]) => void;
  disabled?: boolean;
}) {
  const set = new Set(selected);
  const toggle = (key: string, on: boolean) => {
    const next = new Set(set);
    if (on) next.add(key);
    else next.delete(key);
    onChange([...next]);
  };
  const toggleGroup = (keys: string[], on: boolean) => {
    const next = new Set(set);
    for (const k of keys) {
      if (on) next.add(k);
      else next.delete(k);
    }
    onChange([...next]);
  };

  return (
    <Stack spacing={1.5}>
      {catalog?.groups.map((g) => {
        const keys = g.permissions.map((p) => p.key);
        const allOn = keys.every((k) => set.has(k));
        const someOn = keys.some((k) => set.has(k));
        return (
          <Box key={g.key}>
            <FormControlLabel
              control={
                <Checkbox
                  size="small"
                  checked={allOn}
                  indeterminate={!allOn && someOn}
                  disabled={disabled}
                  onChange={(e) => toggleGroup(keys, e.target.checked)}
                />
              }
              label={<Typography variant="subtitle2">{g.label}</Typography>}
            />
            <Box sx={{ pl: 3, display: 'flex', flexWrap: 'wrap', gap: 0.5 }}>
              {g.permissions.map((p) => (
                <FormControlLabel
                  key={p.key}
                  sx={{ minWidth: 150 }}
                  control={
                    <Checkbox
                      size="small"
                      checked={set.has(p.key)}
                      disabled={disabled}
                      onChange={(e) => toggle(p.key, e.target.checked)}
                    />
                  }
                  label={<Typography variant="body2">{p.label}</Typography>}
                />
              ))}
            </Box>
          </Box>
        );
      })}
    </Stack>
  );
}
