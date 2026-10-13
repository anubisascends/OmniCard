import { useEffect, useMemo, useState, type ReactElement } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  AlertTitle,
  Box,
  Button,
  Checkbox,
  Chip,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Divider,
  FormControlLabel,
  MenuItem,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material';
import DoNotDisturbOnOutlinedIcon from '@mui/icons-material/DoNotDisturbOnOutlined';
import SearchIcon from '@mui/icons-material/Search';
import SyncIcon from '@mui/icons-material/Sync';
import { api } from '../../api/client';
import { useFormatters } from '../../i18n/format';
import { LanguageChip } from '../../lib/cardLanguages';
import { sourcePosition } from '../../lib/deckSync';
import { LocationPickerField } from '../LocationPickerField';
import { DeckFindCopyDialog } from './DeckFindCopyDialog';
import type {
  DeckSyncAddDto,
  DeckSyncCutAction,
  DeckSyncSourceDto,
  LocationSummaryDto,
} from '../../api/types';

const CUT_ACTIONS: DeckSyncCutAction[] = ['Move', 'Sideboard', 'Keep'];

/** Shows the card image when hovering a printing label. */
function HoverImage({ src, children }: { src: string | null; children: ReactElement }) {
  return (
    <Tooltip
      disableInteractive
      slotProps={{ tooltip: { sx: { bgcolor: 'transparent', p: 0, maxWidth: 'none' } } }}
      title={src ? <Box component="img" src={src} alt="" sx={{ width: 220, borderRadius: 2, display: 'block', boxShadow: 6 }} /> : ''}
    >
      {children}
    </Tooltip>
  );
}

function printingLabel(setCode: string | null, collectorNumber: string | null, isFoil: boolean) {
  return `${setCode?.toUpperCase() ?? ''}${collectorNumber ? ` #${collectorNumber}` : ''}${isFoil ? ' ✦' : ''}`;
}

/**
 * "Sync from URL" for a linked deck box: downloads the deck and compares it with the box by card name.
 * Cards the deck wants more of are pulled from elsewhere in the collection — suggested copies come
 * pre-filled (exact printing first), "Find a copy…" searches the collection for any other copy, and
 * whatever is still short must be marked "not in collection" before applying. Cards the deck dropped are
 * moved out, kept as sideboard, or left in the box. Nothing changes until Apply.
 */
export function DeckSyncDialog({
  open,
  loc,
  onClose,
}: {
  open: boolean;
  loc: LocationSummaryDto;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const qc = useQueryClient();

  const preview = useQuery({
    queryKey: ['deck-sync-preview', loc.id],
    queryFn: () => api.deckSyncPreview(loc.id),
    enabled: open,
    refetchOnMount: 'always',
    refetchOnWindowFocus: false,
    gcTime: 0,
    retry: false,
  });
  const locations = useQuery({ queryKey: ['locations', undefined], queryFn: () => api.locations(), enabled: open });

  // Copies chosen per card (card name → lot id → quantity), copies added by search, and the cards marked
  // "not in collection".
  const [picks, setPicks] = useState<Record<string, Record<number, number>>>({});
  const [extra, setExtra] = useState<Record<string, DeckSyncSourceDto[]>>({});
  const [missing, setMissing] = useState<Record<string, boolean>>({});
  const [cutActions, setCutActions] = useState<Record<number, DeckSyncCutAction>>({});
  const [moveTo, setMoveTo] = useState<number | null>(null);
  const [finding, setFinding] = useState<DeckSyncAddDto | null>(null);

  const apply = useMutation({
    mutationFn: () => {
      const plan = preview.data!;
      return api.deckSyncApply(loc.id, {
        picks: Object.values(picks).flatMap((byLot) =>
          Object.entries(byLot)
            .filter(([, quantity]) => quantity > 0)
            .map(([lotId, quantity]) => ({ lotId: Number(lotId), quantity })),
        ),
        cuts: plan.cuts.map((c) => {
          const action = cutActions[c.lotId] ?? 'Move';
          return { lotId: c.lotId, quantity: c.quantity, action, destinationId: action === 'Move' ? moveTo : null };
        }),
        notInCollection: plan.adds
          .filter((a) => shortBy(a) > 0 && missing[a.cardName])
          .map((a) => ({ cardName: a.cardName, setCode: a.setCode, collectorNumber: a.collectorNumber, quantity: shortBy(a) })),
      });
    },
    onSuccess: () => {
      for (const key of ['collection', 'location-cards', 'deck-stack', 'locations', 'dashboard'])
        qc.invalidateQueries({ queryKey: [key] });
      qc.invalidateQueries({ queryKey: ['location', loc.id] });
      qc.invalidateQueries({ queryKey: ['deck-legality', loc.id] });
      qc.invalidateQueries({ queryKey: ['deck-not-in-collection', loc.id] });
    },
  });
  const { reset } = apply;

  // Fresh choices for every preview: suggested copies, previous "not in collection" marks, and cuts moved
  // out unless already tagged sideboard.
  useEffect(() => {
    if (!preview.data) return;
    const initialPicks: Record<string, Record<number, number>> = {};
    const initialMissing: Record<string, boolean> = {};
    for (const a of preview.data.adds) {
      initialPicks[a.cardName] = Object.fromEntries(
        a.sources.filter((s) => s.suggested > 0).map((s) => [s.lotId, s.suggested]),
      );
      if (a.markedNotInCollection) initialMissing[a.cardName] = true;
    }
    setPicks(initialPicks);
    setMissing(initialMissing);
    setExtra({});
    setCutActions(Object.fromEntries(preview.data.cuts.map((c) => [c.lotId, c.isSideboard ? 'Keep' : 'Move'])));
    reset();
  }, [preview.data, reset]);

  // Removed cards go to Bulk unless the user picks somewhere else.
  useEffect(() => {
    if (open) setMoveTo(null);
  }, [open]);
  const bulkId = locations.data?.find((l) => l.isSystem)?.id ?? null;
  const destination = moveTo ?? bulkId;

  const plan = preview.data;
  const sourcesOf = (a: DeckSyncAddDto) => [...a.sources, ...(extra[a.cardName] ?? [])];
  const chosen = (a: DeckSyncAddDto) => Object.values(picks[a.cardName] ?? {}).reduce((n, q) => n + q, 0);
  const shortBy = (a: DeckSyncAddDto) => Math.max(0, a.needed - chosen(a));

  // Cards with nothing suggested come first, so the ones that need attention are on top.
  const adds = useMemo(
    () =>
      [...(plan?.adds ?? [])].sort(
        (x, y) =>
          Number(x.sources.some((s) => s.suggested > 0)) - Number(y.sources.some((s) => s.suggested > 0)) ||
          x.cardName.localeCompare(y.cardName),
      ),
    [plan],
  );

  // A copy can be offered for several cards; it can only be used once in total.
  const overUsedLots = useMemo(() => {
    const used = new Map<number, { total: number; available: number }>();
    for (const a of plan?.adds ?? [])
      for (const s of [...a.sources, ...(extra[a.cardName] ?? [])]) {
        const q = picks[a.cardName]?.[s.lotId] ?? 0;
        const u = used.get(s.lotId) ?? { total: 0, available: s.available };
        used.set(s.lotId, { total: u.total + q, available: s.available });
      }
    return new Set([...used].filter(([, u]) => u.total > u.available).map(([lotId]) => lotId));
  }, [plan, picks, extra]);

  const unresolved = adds.filter((a) => shortBy(a) > 0 && !missing[a.cardName]);
  const overNeeded = adds.some((a) => chosen(a) > a.needed);
  const movingOut = (plan?.cuts ?? []).some((c) => (cutActions[c.lotId] ?? 'Move') === 'Move');
  const nothingToDo = !!plan && plan.adds.length === 0 && plan.cuts.length === 0;
  const canApply =
    !!plan &&
    !apply.isPending &&
    unresolved.length === 0 &&
    !overNeeded &&
    overUsedLots.size === 0 &&
    (!movingOut || destination != null);

  const setPick = (cardName: string, lotId: number, quantity: number) =>
    setPicks((prev) => ({ ...prev, [cardName]: { ...prev[cardName], [lotId]: quantity } }));

  const addFound = (a: DeckSyncAddDto, s: DeckSyncSourceDto) => {
    if (!sourcesOf(a).some((x) => x.lotId === s.lotId))
      setExtra((prev) => ({ ...prev, [a.cardName]: [...(prev[a.cardName] ?? []), s] }));
    if (!(picks[a.cardName]?.[s.lotId] ?? 0)) setPick(a.cardName, s.lotId, Math.max(1, Math.min(shortBy(a), s.available)));
    setFinding(null);
  };

  const result = apply.data;

  return (
    <Dialog open={open} onClose={apply.isPending ? undefined : onClose} maxWidth="lg" fullWidth>
      <DialogTitle>{t('deckbox.sync.title', { name: loc.name })}</DialogTitle>
      <DialogContent dividers>
        {result ? (
          <Alert severity="success">
            {t('deckbox.sync.result', {
              added: fmt.number(result.added),
              removed: fmt.number(result.removed),
              sideboarded: fmt.number(result.sideboarded),
              missing: fmt.number(result.notInCollection),
            })}
          </Alert>
        ) : (
          <Stack spacing={2}>
            {preview.isLoading && (
              <Stack direction="row" spacing={1} alignItems="center">
                <CircularProgress size={24} />
                <Typography variant="body2">{t('deckbox.sync.loading')}</Typography>
              </Stack>
            )}
            {preview.error && <Alert severity="error">{(preview.error as Error).message}</Alert>}
            {plan && (
              <Typography variant="body2" color="text.secondary">
                {t('deckbox.sync.summary', {
                  deck: plan.deckName,
                  cards: fmt.number(plan.deckCardCount),
                  keep: fmt.number(plan.keepCount),
                })}
              </Typography>
            )}
            {nothingToDo && <Alert severity="success">{t('deckbox.sync.upToDate')}</Alert>}
            {unresolved.length > 0 && (
              <Alert
                severity="warning"
                action={
                  <Button
                    color="inherit"
                    size="small"
                    onClick={() =>
                      setMissing((prev) => ({ ...prev, ...Object.fromEntries(unresolved.map((a) => [a.cardName, true])) }))
                    }
                  >
                    {t('deckbox.sync.markAllMissing')}
                  </Button>
                }
              >
                {t('deckbox.sync.unresolved', { count: unresolved.length })}
              </Alert>
            )}

            {adds.length > 0 && (
              <>
                <Typography variant="h6">{t('deckbox.sync.addsTitle', { count: adds.length })}</Typography>
                {adds.some((a) => a.sources.some((s) => s.ignoredReason === 'Location')) && (
                  <Typography variant="caption" color="text.secondary">
                    {t('deckbox.sync.ignoredLocationHint')}
                  </Typography>
                )}
                {adds.map((a) => {
                  const sources = sourcesOf(a);
                  const short = shortBy(a);
                  return (
                    <Box key={a.cardName}>
                      <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
                        <HoverImage src={a.imageUri}>
                          <Typography variant="subtitle2" component="span">
                            {a.cardName}
                            {a.isFoil ? ' ✦' : ''}{' '}
                            <Typography component="span" variant="body2" color="text.secondary">
                              {printingLabel(a.setCode, a.collectorNumber, false)}
                              {' · '}
                              {t('deckbox.sync.need', { count: a.needed })}
                            </Typography>
                          </Typography>
                        </HoverImage>
                        {short === 0 ? (
                          <Chip size="small" color="success" variant="outlined" label={t('deckbox.sync.covered')} />
                        ) : (
                          <Chip
                            size="small"
                            color={missing[a.cardName] ? 'default' : 'warning'}
                            label={t('deckbox.sync.short', { count: short })}
                          />
                        )}
                        {a.markedNotInCollection && (
                          <Typography variant="caption" color="text.secondary">
                            {t('deckbox.sync.previouslyMarked')}
                          </Typography>
                        )}
                        <Box sx={{ flexGrow: 1 }} />
                        <Button size="small" startIcon={<SearchIcon />} onClick={() => setFinding(a)}>
                          {t('deckbox.sync.findCopy')}
                        </Button>
                      </Stack>
                      {chosen(a) > a.needed && (
                        <Typography variant="caption" color="error">
                          {t('deckbox.sync.tooMany', { count: a.needed })}
                        </Typography>
                      )}
                      {sources.length === 0 ? (
                        <Typography variant="body2" color="warning.main" sx={{ my: 0.5 }}>
                          {t('deckbox.sync.noneFound')}
                        </Typography>
                      ) : (
                        <Table size="small">
                          <TableHead>
                            <TableRow>
                              <TableCell>{t('deckbox.sync.columns.printing')}</TableCell>
                              <TableCell>{t('common.labels.condition')}</TableCell>
                              <TableCell>{t('common.labels.location')}</TableCell>
                              <TableCell align="right">{t('deckbox.sync.columns.available')}</TableCell>
                              <TableCell align="right">{t('deckbox.sync.columns.use')}</TableCell>
                            </TableRow>
                          </TableHead>
                          <TableBody>
                            {sources.map((s) => {
                              const listed = s.ignoredReason === 'Listed';
                              const found = !a.sources.includes(s);
                              return (
                                <TableRow key={s.lotId} hover={!listed} sx={s.ignoredReason ? { opacity: 0.6 } : undefined}>
                                  <TableCell>
                                    <HoverImage src={s.imageUri}>
                                      <Stack direction="row" spacing={0.5} alignItems="center" component="span">
                                        <span>
                                          {s.cardName !== a.cardName ? `${s.cardName} · ` : ''}
                                          {printingLabel(s.setCode, s.collectorNumber, s.isFoil)}
                                        </span>
                                        <LanguageChip language={s.language} />
                                        {s.isExactPrinting && (
                                          <Chip size="small" variant="outlined" label={t('deckbox.sync.exact')} sx={{ height: 20, fontSize: 11 }} />
                                        )}
                                        {found && (
                                          <Chip size="small" variant="outlined" label={t('deckbox.sync.foundBySearch')} sx={{ height: 20, fontSize: 11 }} />
                                        )}
                                      </Stack>
                                    </HoverImage>
                                  </TableCell>
                                  <TableCell>
                                    {s.condition ? t(`common.conditions.${s.condition}`, { defaultValue: s.condition }) : '—'}
                                  </TableCell>
                                  <TableCell>{sourcePosition(t, s)}</TableCell>
                                  <TableCell align="right">{fmt.number(s.available)}</TableCell>
                                  <TableCell align="right">
                                    <Stack direction="row" spacing={1} alignItems="center" justifyContent="flex-end">
                                      {s.ignoredReason && (
                                        <Chip
                                          size="small"
                                          variant="outlined"
                                          icon={<DoNotDisturbOnOutlinedIcon />}
                                          label={t(`deckbox.sync.ignoredReasons.${s.ignoredReason}`)}
                                          sx={{ height: 22, fontSize: 11 }}
                                        />
                                      )}
                                      {!listed && (
                                        <TextField
                                          type="number"
                                          size="small"
                                          value={picks[a.cardName]?.[s.lotId] ?? 0}
                                          error={overUsedLots.has(s.lotId)}
                                          onChange={(e) =>
                                            setPick(a.cardName, s.lotId, Math.max(0, Math.min(Number(e.target.value) || 0, s.available)))
                                          }
                                          slotProps={{ htmlInput: { min: 0, max: s.available } }}
                                          sx={{ width: 76 }}
                                        />
                                      )}
                                    </Stack>
                                  </TableCell>
                                </TableRow>
                              );
                            })}
                          </TableBody>
                        </Table>
                      )}
                      {short > 0 && (
                        <FormControlLabel
                          control={
                            <Checkbox
                              size="small"
                              checked={!!missing[a.cardName]}
                              onChange={(e) => setMissing((prev) => ({ ...prev, [a.cardName]: e.target.checked }))}
                            />
                          }
                          label={t('deckbox.sync.notInCollection', { count: short })}
                        />
                      )}
                    </Box>
                  );
                })}
                {overUsedLots.size > 0 && <Alert severity="error">{t('deckbox.sync.lotOverUsed')}</Alert>}
              </>
            )}

            {plan && plan.cuts.length > 0 && (
              <>
                <Divider />
                <Typography variant="h6">{t('deckbox.sync.cutsTitle', { count: plan.cuts.length })}</Typography>
                <Typography variant="caption" color="text.secondary">
                  {t('deckbox.sync.sideboardHint')}
                </Typography>
                {movingOut && (
                  <LocationPickerField
                    size="small"
                    label={t('deckbox.sync.moveTo')}
                    value={destination}
                    onChange={setMoveTo}
                    cardGames={loc.game ? [loc.game] : undefined}
                    sx={{ maxWidth: 360 }}
                  />
                )}
                {movingOut && destination == null && (
                  <Typography variant="caption" color="error">
                    {t('deckbox.sync.moveToRequired')}
                  </Typography>
                )}
                <Table size="small">
                  <TableHead>
                    <TableRow>
                      <TableCell>{t('common.labels.name')}</TableCell>
                      <TableCell>{t('deckbox.sync.columns.printing')}</TableCell>
                      <TableCell align="right">{t('common.labels.quantity')}</TableCell>
                      <TableCell />
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {plan.cuts.map((c) => (
                      <TableRow key={c.lotId} hover>
                        <TableCell>{c.cardName}</TableCell>
                        <TableCell>
                          <HoverImage src={c.imageUri}>
                            <span>{printingLabel(c.setCode, c.collectorNumber, c.isFoil)}</span>
                          </HoverImage>
                        </TableCell>
                        <TableCell align="right">{fmt.number(c.quantity)}</TableCell>
                        <TableCell align="right">
                          <TextField
                            select
                            size="small"
                            value={cutActions[c.lotId] ?? 'Move'}
                            onChange={(e) =>
                              setCutActions((prev) => ({ ...prev, [c.lotId]: e.target.value as DeckSyncCutAction }))
                            }
                            sx={{ minWidth: 190 }}
                          >
                            {CUT_ACTIONS.map((action) => (
                              <MenuItem key={action} value={action}>
                                {t(`deckbox.sync.actions.${action}`)}
                              </MenuItem>
                            ))}
                          </TextField>
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </>
            )}
            {apply.error && (
              <Alert severity="error">
                <AlertTitle>{(apply.error as Error).message}</AlertTitle>
              </Alert>
            )}
          </Stack>
        )}
      </DialogContent>
      <DialogActions>
        {result ? (
          <Button variant="contained" onClick={onClose}>
            {t('common.actions.done')}
          </Button>
        ) : (
          <>
            <Button onClick={onClose} disabled={apply.isPending}>
              {t('common.actions.cancel')}
            </Button>
            <Button variant="contained" startIcon={<SyncIcon />} disabled={!canApply} onClick={() => apply.mutate()}>
              {apply.isPending ? t('deckbox.sync.applying') : t('deckbox.sync.apply')}
            </Button>
          </>
        )}
      </DialogActions>
      {finding && (
        <DeckFindCopyDialog
          open
          deckBoxId={loc.id}
          cardName={finding.cardName}
          onPick={(s) => addFound(finding, s)}
          onClose={() => setFinding(null)}
        />
      )}
    </Dialog>
  );
}
