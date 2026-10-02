import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  Checkbox,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  InputAdornment,
  Stack,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material';
import LockOutlinedIcon from '@mui/icons-material/LockOutlined';
import SearchIcon from '@mui/icons-material/Search';
import { api } from '../../api/client';
import { useSites } from '../../context/useSites';
import { useFormatters } from '../../i18n/format';
import { groupLocations } from '../../lib/locationGroups';
import type { LocationSummaryDto } from '../../api/types';

/** One tick-box row: checked when every editable location under it is ignored, dashed when some are. */
function GroupCheckbox({
  locations,
  ignored,
  onChange,
}: {
  locations: LocationSummaryDto[];
  ignored: Set<number>;
  onChange: (ids: number[], value: boolean) => void;
}) {
  const editable = locations.filter((l) => l.canWrite);
  const count = editable.filter((l) => ignored.has(l.id)).length;
  return (
    <Checkbox
      size="small"
      disabled={editable.length === 0}
      checked={editable.length > 0 && count === editable.length}
      indeterminate={count > 0 && count < editable.length}
      onChange={(e) => onChange(editable.map((l) => l.id), e.target.checked)}
      sx={{ p: 0.5 }}
    />
  );
}

/**
 * Choose which locations lists (and decklist checks) ignore when looking for cards: a sales binder, a deck
 * in use, everything at another site… Locations are grouped by site, then by type; each site and type
 * heading has its own tick-box to (un)ignore the whole group at once. Changes are staged and saved together.
 * Locations in read-only sites are shown but can't be changed.
 */
export function IgnoredLocationsDialog({
  open,
  onClose,
  onSaved,
}: {
  open: boolean;
  onClose: () => void;
  onSaved?: () => void;
}) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const qc = useQueryClient();
  const { multiSite } = useSites();
  const { data, isLoading } = useQuery({
    queryKey: ['locations', undefined],
    queryFn: () => api.locations(),
    enabled: open,
  });
  const [ignored, setIgnored] = useState<Set<number>>(new Set());
  const [search, setSearch] = useState('');

  const initial = useMemo(() => new Set((data ?? []).filter((l) => l.ignoredForLists).map((l) => l.id)), [data]);
  useEffect(() => {
    if (open) {
      setIgnored(new Set(initial));
      setSearch('');
    }
  }, [open, initial]);

  const toIgnore = [...ignored].filter((id) => !initial.has(id));
  const toInclude = [...initial].filter((id) => !ignored.has(id));
  const changes = toIgnore.length + toInclude.length;

  const save = useMutation({
    mutationFn: async () => {
      if (toIgnore.length > 0) await api.locationsSetIgnoredForLists(toIgnore, true);
      if (toInclude.length > 0) await api.locationsSetIgnoredForLists(toInclude, false);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['locations'] });
      qc.invalidateQueries({ queryKey: ['list-items'] });
      qc.invalidateQueries({ queryKey: ['list-substitutes'] });
      onSaved?.();
      onClose();
    },
  });

  const setMany = (ids: number[], value: boolean) =>
    setIgnored((prev) => {
      const next = new Set(prev);
      for (const id of ids) {
        if (value) next.add(id);
        else next.delete(id);
      }
      return next;
    });

  // Site → type groups, after the search filter.
  const sections = useMemo(() => {
    const term = search.trim().toLowerCase();
    const visible = (data ?? []).filter((l) => !term || l.name.toLowerCase().includes(term));
    const bySite = new Map<number, LocationSummaryDto[]>();
    for (const loc of visible) (bySite.get(loc.siteId) ?? bySite.set(loc.siteId, []).get(loc.siteId)!).push(loc);
    return [...bySite.values()]
      .map((locs) => ({ siteId: locs[0].siteId, siteName: locs[0].siteName ?? '', locations: locs, groups: groupLocations(locs) }))
      .sort((a, b) => a.siteName.localeCompare(b.siteName, undefined, { sensitivity: 'base' }));
  }, [data, search]);

  const heading = (key: string, fallback: string) =>
    key === '__always__'
      ? t('locations.groups.alwaysAvailable')
      : t(`locations.groups.headings.${key}`, { defaultValue: fallback });

  return (
    <Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth>
      <DialogTitle>{t('locations.ignored.title')}</DialogTitle>
      <DialogContent dividers>
        <Stack spacing={1.5}>
          <Typography variant="body2" color="text.secondary">
            {t('locations.ignored.intro')}
          </Typography>
          <Stack direction="row" spacing={1} alignItems="center">
            <TextField
              size="small"
              fullWidth
              placeholder={t('locations.ignored.search')}
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              slotProps={{
                input: {
                  startAdornment: (
                    <InputAdornment position="start">
                      <SearchIcon fontSize="small" />
                    </InputAdornment>
                  ),
                },
              }}
            />
            <Typography variant="body2" sx={{ whiteSpace: 'nowrap' }}>
              {t('locations.ignored.count', { count: ignored.size })}
            </Typography>
          </Stack>

          {isLoading && <CircularProgress size={28} />}
          {data && sections.length === 0 && (
            <Typography variant="body2" color="text.secondary">
              {t('locations.ignored.noMatches')}
            </Typography>
          )}

          {sections.map((section) => (
            <Box key={section.siteId}>
              {multiSite && (
                <Stack direction="row" alignItems="center" spacing={0.5} sx={{ mt: 1 }}>
                  <GroupCheckbox locations={section.locations} ignored={ignored} onChange={setMany} />
                  <Typography variant="subtitle2">{section.siteName}</Typography>
                  <Typography variant="caption" color="text.secondary">
                    {t('locations.ignored.wholeSite')}
                  </Typography>
                </Stack>
              )}
              {section.groups.map((group) => (
                <Box key={group.key} sx={{ pl: multiSite ? 3 : 0 }}>
                  <Stack direction="row" alignItems="center" spacing={0.5} sx={{ mt: 0.5 }}>
                    <GroupCheckbox locations={group.items} ignored={ignored} onChange={setMany} />
                    <Typography variant="overline" color="text.secondary">
                      {heading(group.key, group.heading)}
                    </Typography>
                  </Stack>
                  {group.items.map((loc) => (
                    <Stack
                      key={loc.id}
                      direction="row"
                      alignItems="center"
                      spacing={0.5}
                      sx={{ pl: 3, opacity: loc.canWrite ? 1 : 0.6 }}
                    >
                      <Checkbox
                        size="small"
                        sx={{ p: 0.5 }}
                        disabled={!loc.canWrite}
                        checked={ignored.has(loc.id)}
                        onChange={(e) => setMany([loc.id], e.target.checked)}
                      />
                      <Typography variant="body2" sx={{ flexGrow: 1 }} noWrap>
                        {loc.name}
                      </Typography>
                      {!loc.canWrite && (
                        <Tooltip title={t('locations.sites.readOnlyTooltip', { site: loc.siteName ?? '' })}>
                          <LockOutlinedIcon sx={{ fontSize: 14, color: 'text.disabled' }} />
                        </Tooltip>
                      )}
                      <Typography variant="caption" color="text.secondary" sx={{ minWidth: 70, textAlign: 'right' }}>
                        {t('locations.ignored.cards', { count: loc.cardCount, formatted: fmt.number(loc.cardCount) })}
                      </Typography>
                    </Stack>
                  ))}
                </Box>
              ))}
            </Box>
          ))}
          {save.error && <Alert severity="error">{(save.error as Error).message}</Alert>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('common.actions.cancel')}</Button>
        <Button variant="contained" disabled={changes === 0 || save.isPending} onClick={() => save.mutate()}>
          {t('locations.ignored.save', { count: changes })}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
