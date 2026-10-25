import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  Checkbox,
  Chip,
  FormControlLabel,
  Paper,
  Stack,
  Switch,
  TextField,
  Typography,
} from '@mui/material';
import { api } from '../../api/client';
import type { CatalogScheduleDto } from '../../api/types';
import { useFormatters } from '../../i18n/format';

// Server day names (System.DayOfWeek), Sunday first.
const DAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
// Any week starting on a Sunday, used only to render localized weekday names.
const A_SUNDAY = new Date(2026, 9, 18);

// Order-insensitive: the server returns its lists sorted.
const canonical = (s: CatalogScheduleDto) =>
  JSON.stringify({ ...s, times: [...s.times].sort(), days: [...s.days].sort(), games: [...s.games].sort() });
const sameSchedule = (a: CatalogScheduleDto, b: CatalogScheduleDto) => canonical(a) === canonical(b);

/**
 * Settings ▸ Catalog data ▸ Automatic updates: the times/days at which the server refreshes each
 * selected game's card data and prices. Everyone who can see the Catalog tab sees the schedule and its
 * last run; only admins can change it or run it now (the server enforces that too).
 */
export function CatalogScheduleCard() {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const qc = useQueryClient();
  const games = useQuery({ queryKey: ['games'], queryFn: api.games });
  const query = useQuery({
    queryKey: ['catalog-schedule'],
    queryFn: api.catalogSchedule,
    refetchInterval: (q) => (q.state.data?.isRunning ? 3000 : 30000),
  });

  const [form, setForm] = useState<CatalogScheduleDto | null>(null);
  const [newTime, setNewTime] = useState('06:00');
  const [saved, setSaved] = useState(false);
  const [runStarted, setRunStarted] = useState(false);

  // Seed the form once; background refetches (status polling) never clobber unsaved edits.
  const loaded = query.data?.schedule;
  useEffect(() => {
    if (loaded && !form) setForm(loaded);
  }, [loaded, form]);

  const save = useMutation({
    mutationFn: (schedule: CatalogScheduleDto) => api.catalogSetSchedule(schedule),
    onSuccess: (response) => {
      qc.setQueryData(['catalog-schedule'], response);
      setForm(response.schedule);
      setSaved(true);
    },
  });

  const runNow = useMutation({
    mutationFn: api.catalogRunSchedule,
    onSuccess: () => {
      setRunStarted(true);
      // The run starts on the server's next check (within moments); pick up its progress.
      setTimeout(() => {
        qc.invalidateQueries({ queryKey: ['catalog-schedule'] });
        qc.invalidateQueries({ queryKey: ['catalog-status'] });
      }, 1500);
    },
  });

  const dayNames = useMemo(
    () =>
      DAYS.map((_, i) =>
        fmt.date(new Date(A_SUNDAY.getFullYear(), A_SUNDAY.getMonth(), A_SUNDAY.getDate() + i), { weekday: 'short' }),
      ),
    [fmt],
  );

  if (!query.data || !form) return null;
  const data = query.data;
  const canEdit = data.canEdit;
  const dirty = !sameSchedule(form, data.schedule);

  const update = (patch: Partial<CatalogScheduleDto>) => {
    setSaved(false);
    setForm({ ...form, ...patch });
  };
  const toggle = (list: string[], value: string, on: boolean) =>
    on ? [...list, value] : list.filter((v) => v !== value);
  const addTime = () => {
    if (!/^\d{2}:\d{2}$/.test(newTime) || form.times.includes(newTime)) return;
    update({ times: [...form.times, newTime].sort() });
  };
  const timeLabel = (time: string) => {
    const [h, m] = time.split(':').map(Number);
    return fmt.dateTime(new Date(2026, 0, 1, h, m), { timeStyle: 'short' });
  };

  let lastRun: string;
  if (!data.lastRunStartedAt) lastRun = t('settings.catalog.schedule.neverRun');
  else if (!data.lastRunFinishedAt && !data.isRunning)
    lastRun = t('settings.catalog.schedule.lastRunUnfinished', { when: fmt.dateTime(data.lastRunStartedAt) });
  else
    lastRun = t('settings.catalog.schedule.lastRun', {
      when: fmt.dateTime(data.lastRunStartedAt),
      succeeded: fmt.number(data.lastRunJobCount - data.lastRunFailedCount),
      total: fmt.number(data.lastRunJobCount),
    });

  return (
    <Paper variant="outlined" sx={{ p: 2, maxWidth: 640 }}>
      <Typography variant="h6" gutterBottom>
        {t('settings.catalog.schedule.title')}
      </Typography>
      <Typography variant="body2" color="text.secondary" gutterBottom>
        {t('settings.catalog.schedule.description')}
      </Typography>

      <Stack spacing={2} sx={{ mt: 1 }}>
        {!canEdit && <Alert severity="info">{t('settings.catalog.schedule.adminOnly')}</Alert>}

        <FormControlLabel
          control={
            <Switch
              checked={form.enabled}
              disabled={!canEdit}
              onChange={(e) => update({ enabled: e.target.checked })}
            />
          }
          label={t('settings.catalog.schedule.enabled')}
        />

        <Box>
          <Typography variant="subtitle2">{t('settings.catalog.schedule.timesLabel')}</Typography>
          <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap alignItems="center" sx={{ my: 0.5 }}>
            {form.times.length === 0 && (
              <Typography variant="body2" color="text.secondary">
                {t('settings.catalog.schedule.noTimes')}
              </Typography>
            )}
            {form.times.map((time) => (
              <Chip
                key={time}
                label={timeLabel(time)}
                onDelete={canEdit ? () => update({ times: form.times.filter((x) => x !== time) }) : undefined}
                aria-label={t('settings.catalog.schedule.removeTime', { time: timeLabel(time) })}
              />
            ))}
          </Stack>
          {canEdit && (
            <Stack direction="row" spacing={1} alignItems="center">
              <TextField
                type="time"
                size="small"
                value={newTime}
                onChange={(e) => setNewTime(e.target.value)}
                slotProps={{ htmlInput: { step: 300 } }}
              />
              <Button variant="outlined" onClick={addTime} disabled={!newTime || form.times.includes(newTime)}>
                {t('settings.catalog.schedule.addTime')}
              </Button>
            </Stack>
          )}
          <Typography variant="caption" color="text.secondary" display="block">
            {t('settings.catalog.schedule.timeZone', { zone: data.timeZone })}
          </Typography>
        </Box>

        <Box>
          <Typography variant="subtitle2">{t('settings.catalog.schedule.daysLabel')}</Typography>
          <Stack direction="row" flexWrap="wrap" useFlexGap columnGap={1}>
            {DAYS.map((day, i) => (
              <FormControlLabel
                key={day}
                label={dayNames[i]}
                control={
                  <Checkbox
                    size="small"
                    checked={form.days.includes(day)}
                    disabled={!canEdit}
                    onChange={(e) =>
                      update({ days: DAYS.filter((d) => (d === day ? e.target.checked : form.days.includes(d))) })
                    }
                  />
                }
              />
            ))}
          </Stack>
        </Box>

        <Box>
          <Typography variant="subtitle2">{t('settings.catalog.schedule.gamesLabel')}</Typography>
          <Stack direction="row" flexWrap="wrap" useFlexGap columnGap={1}>
            {games.data?.map((g) => (
              <FormControlLabel
                key={g.id}
                label={g.displayName}
                control={
                  <Checkbox
                    size="small"
                    checked={form.games.includes(g.id)}
                    disabled={!canEdit}
                    onChange={(e) => update({ games: toggle(form.games, g.id, e.target.checked) })}
                  />
                }
              />
            ))}
          </Stack>
        </Box>

        <Box>
          <Typography variant="subtitle2">{t('settings.catalog.schedule.updateLabel')}</Typography>
          <Stack direction="row" flexWrap="wrap" useFlexGap columnGap={1}>
            <FormControlLabel
              label={t('settings.catalog.schedule.updateCards')}
              control={
                <Checkbox
                  size="small"
                  checked={form.updateCards}
                  disabled={!canEdit}
                  onChange={(e) => update({ updateCards: e.target.checked })}
                />
              }
            />
            <FormControlLabel
              label={t('settings.catalog.schedule.updatePrices')}
              control={
                <Checkbox
                  size="small"
                  checked={form.updatePrices}
                  disabled={!canEdit}
                  onChange={(e) => update({ updatePrices: e.target.checked })}
                />
              }
            />
          </Stack>
        </Box>

        {save.error && <Alert severity="error">{(save.error as Error).message}</Alert>}
        {runNow.error && <Alert severity="error">{(runNow.error as Error).message}</Alert>}
        {saved && !dirty && <Alert severity="success">{t('settings.catalog.schedule.saved')}</Alert>}
        {runStarted && !runNow.isPending && <Alert severity="info">{t('settings.catalog.schedule.runStarted')}</Alert>}

        {canEdit && (
          <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
            <Button variant="contained" disabled={!dirty || save.isPending} onClick={() => save.mutate(form)}>
              {t('settings.catalog.schedule.save')}
            </Button>
            <Button
              variant="outlined"
              disabled={dirty || data.isRunning || runNow.isPending}
              onClick={() => {
                setRunStarted(false);
                runNow.mutate();
              }}
              title={t('settings.catalog.schedule.runNowHelp')}
            >
              {t('settings.catalog.schedule.runNow')}
            </Button>
          </Stack>
        )}

        <Stack spacing={0.5}>
          {data.isRunning && (
            <Typography variant="body2" fontWeight={600}>
              {t('settings.catalog.schedule.running')}
            </Typography>
          )}
          <Typography variant="body2">
            {data.schedule.enabled && data.nextRunAt
              ? t('settings.catalog.schedule.nextRun', { when: fmt.dateTime(data.nextRunAt) })
              : t('settings.catalog.schedule.off')}
          </Typography>
          <Typography variant="body2" color="text.secondary">
            {lastRun}
          </Typography>
          {data.lastRunFailures.length > 0 && (
            <Box>
              <Typography variant="subtitle2" color="error">
                {t('settings.catalog.schedule.failuresHeading')}
              </Typography>
              {data.lastRunFailures.map((f, i) => (
                <Typography key={i} variant="caption" color="text.secondary" display="block">
                  {f}
                </Typography>
              ))}
            </Box>
          )}
        </Stack>
      </Stack>
    </Paper>
  );
}
