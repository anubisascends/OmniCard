import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  AlertTitle,
  Box,
  Button,
  Checkbox,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControlLabel,
  ListSubheader,
  MenuItem,
  Radio,
  RadioGroup,
  Stack,
  Step,
  StepLabel,
  Stepper,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  TextField,
  Typography,
} from '@mui/material';
import { api, ApiError } from '../../api/client';
import type {
  BulkLocationDeletePreviewDto,
  BulkSetsMode,
  LocationBulkFailureDto,
  LocationBulkResultDto,
  LocationSummaryDto,
  SetInfoDto,
  SiteDto,
} from '../../api/types';
import { useFormatters } from '../../i18n/format';
import { usePermissions } from '../../context/usePermissions';
import { ALL_SITES, SiteSelect } from '../SiteSelect';
import { SetMultiSelect } from '../SetMultiSelect';
import { SelectField } from '../SelectField';

const isDeckBox = (l: LocationSummaryDto) => l.type === 'Deck Box';

/** The per-location reasons a bulk edit was refused (422), or null for any other error. */
function failuresOf(error: unknown): LocationBulkFailureDto[] | null {
  if (!(error instanceof ApiError) || error.status !== 422) return null;
  const failures = (error.body as { failures?: LocationBulkFailureDto[] } | undefined)?.failures;
  return failures && failures.length > 0 ? failures : null;
}

/**
 * Runs an all-or-nothing bulk edit. When the server refuses some locations nothing is saved; the dialog shows
 * them and offers to apply the change to the rest.
 */
function useBulkEdit(
  locs: LocationSummaryDto[],
  apply: (ids: number[]) => Promise<LocationBulkResultDto>,
  onDone: (result: LocationBulkResultDto) => void,
) {
  const [skipped, setSkipped] = useState<Set<number>>(new Set());
  const ids = locs.map((l) => l.id).filter((id) => !skipped.has(id));
  const mutation = useMutation({ mutationFn: (run: number[]) => apply(run), onSuccess: onDone });
  const failures = failuresOf(mutation.error);
  const remaining = failures ? ids.filter((id) => !failures.some((f) => f.id === id)) : [];
  return {
    ids,
    mutation,
    run: () => mutation.mutate(ids),
    failures,
    remaining,
    /** Leave the refused locations out and apply to the others. */
    skipFailures: () => {
      if (!failures) return;
      setSkipped((prev) => new Set([...prev, ...failures.map((f) => f.id)]));
      mutation.mutate(remaining);
    },
  };
}

function BulkErrors({ edit }: { edit: ReturnType<typeof useBulkEdit> }) {
  const { t } = useTranslation();
  const { mutation, failures, remaining, skipFailures } = edit;
  if (!mutation.isError) return null;
  if (!failures) return <Alert severity="error">{(mutation.error as Error).message}</Alert>;
  return (
    <Alert
      severity="warning"
      action={
        remaining.length > 0 && (
          <Button color="inherit" size="small" onClick={skipFailures} disabled={mutation.isPending}>
            {t('locations.bulk.skipFailures', { count: remaining.length })}
          </Button>
        )
      }
    >
      <AlertTitle>{t('locations.bulk.failuresTitle')}</AlertTitle>
      <Box component="ul" sx={{ m: 0, pl: 2 }}>
        {failures.map((f) => (
          <li key={f.id}>
            <strong>{f.name}</strong>: {f.error}
          </li>
        ))}
      </Box>
    </Alert>
  );
}

// --- Game & sets ---

const KEEP = '__keep__';
const ANY = '__any__';

/** Give many locations one game and/or change their default sets. */
export function BulkGameSetsDialog({
  locs,
  onClose,
  onDone,
}: {
  locs: LocationSummaryDto[];
  onClose: () => void;
  onDone: (result: LocationBulkResultDto) => void;
}) {
  const { t } = useTranslation();
  const games = useQuery({ queryKey: ['games'], queryFn: () => api.games() });
  const [gameChoice, setGameChoice] = useState<string>(KEEP);
  const [setsMode, setSetsMode] = useState<BulkSetsMode>('keep');
  const [sets, setSets] = useState<SetInfoDto[]>([]);

  // Set codes belong to one game: picking sets needs every location to end up on the same game.
  const keptGames = [...new Set(locs.map((l) => l.game ?? null))];
  const resultingGame =
    gameChoice === KEEP ? (keptGames.length === 1 ? keptGames[0] : null) : gameChoice === ANY ? null : gameChoice;
  const needsSets = setsMode === 'replace' || setsMode === 'add' || setsMode === 'remove';
  const deckBoxes = locs.filter(isDeckBox).length;
  const changesGame = gameChoice !== KEEP;

  const edit = useBulkEdit(
    locs,
    (ids) =>
      api.locationsBulkSetCardFilter({
        ids,
        gameMode: gameChoice === KEEP ? 'keep' : gameChoice === ANY ? 'any' : 'set',
        game: gameChoice === KEEP || gameChoice === ANY ? null : gameChoice,
        setsMode,
        sets: sets.map((s) => s.setCode),
      }),
    (r) => {
      onDone(r);
      onClose();
    },
  );

  const canApply =
    !(gameChoice === KEEP && setsMode === 'keep') && !(needsSets && (sets.length === 0 || !resultingGame));

  return (
    <Dialog open onClose={onClose} maxWidth="sm" fullWidth>
      <DialogTitle>{t('locations.bulk.gameSets.title', { count: edit.ids.length })}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <SelectField
            size="small"
            label={t('locations.bulk.gameSets.game')}
            value={gameChoice}
            onChange={(e) => {
              setGameChoice(e.target.value);
              setSets([]);
              if (setsMode !== 'clear') setSetsMode('keep');
            }}
          >
            <MenuItem value={KEEP}>
              <em>{t('locations.bulk.gameSets.keepGame')}</em>
            </MenuItem>
            <MenuItem value={ANY}>{t('locations.cardFilter.anyGame')}</MenuItem>
            <ListSubheader>{t('locations.bulk.gameSets.onlyGame')}</ListSubheader>
            {(games.data ?? []).map((g) => (
              <MenuItem key={g.id} value={g.id}>
                {g.displayName}
              </MenuItem>
            ))}
          </SelectField>
          <SelectField
            size="small"
            label={t('locations.cardFilter.defaultSets')}
            value={setsMode}
            onChange={(e) => {
              setSetsMode(e.target.value as BulkSetsMode);
              setSets([]);
            }}
          >
            <MenuItem value="keep">{t('locations.bulk.gameSets.setsKeep')}</MenuItem>
            <MenuItem value="replace" disabled={!resultingGame}>
              {t('locations.bulk.gameSets.setsReplace')}
            </MenuItem>
            <MenuItem value="add" disabled={!resultingGame}>
              {t('locations.bulk.gameSets.setsAdd')}
            </MenuItem>
            <MenuItem value="remove" disabled={!resultingGame}>
              {t('locations.bulk.gameSets.setsRemove')}
            </MenuItem>
            <MenuItem value="clear">{t('locations.bulk.gameSets.setsClear')}</MenuItem>
          </SelectField>
          {needsSets && resultingGame && (
            <SetMultiSelect
              game={resultingGame}
              value={sets}
              onChange={setSets}
              label={t('locations.bulk.gameSets.sets')}
              helperText={t('locations.cardFilter.setsHelp')}
            />
          )}
          {!resultingGame && (
            <Typography variant="body2" color="text.secondary">
              {t('locations.bulk.gameSets.setsNeedOneGame')}
            </Typography>
          )}
          {changesGame && (
            <Typography variant="body2" color="text.secondary">
              {t('locations.bulk.gameSets.gameChangeNote')}
            </Typography>
          )}
          {deckBoxes > 0 && changesGame && (
            <Alert severity="info">{t('locations.bulk.gameSets.deckBoxNote', { count: deckBoxes })}</Alert>
          )}
          <BulkErrors edit={edit} />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('common.actions.cancel')}</Button>
        <Button variant="contained" disabled={!canApply || edit.mutation.isPending} onClick={edit.run}>
          {t('locations.bulk.apply', { count: edit.ids.length })}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

// --- Move to site ---

/** Move many locations — with every card in them — to one site. */
export function BulkMoveSiteDialog({
  locs,
  sites,
  onClose,
  onDone,
}: {
  locs: LocationSummaryDto[];
  sites: SiteDto[];
  onClose: () => void;
  onDone: (result: LocationBulkResultDto) => void;
}) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const [target, setTarget] = useState<number | undefined>(
    () => sites.find((s) => !locs.every((l) => l.siteId === s.id))?.id ?? sites[0]?.id,
  );
  const edit = useBulkEdit(
    locs,
    (ids) => api.locationsBulkSetSite(ids, target!),
    (r) => {
      onDone(r);
      onClose();
    },
  );
  const alreadyThere = locs.filter((l) => l.siteId === target).length;
  const cards = locs.reduce((n, l) => n + l.cardCount, 0);
  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>{t('locations.bulk.site.title', { count: locs.length })}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <Typography variant="body2" color="text.secondary">
            {t('locations.bulk.site.help', { count: cards, countText: fmt.number(cards) })}
          </Typography>
          {target !== undefined && (
            <SiteSelect
              sites={sites}
              value={target}
              onChange={(v) => v !== ALL_SITES && setTarget(v)}
              label={t('locations.sites.moveTo')}
            />
          )}
          {alreadyThere > 0 && (
            <Typography variant="body2" color="text.secondary">
              {t('locations.bulk.site.alreadyThere', { count: alreadyThere })}
            </Typography>
          )}
          <BulkErrors edit={edit} />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('common.actions.cancel')}</Button>
        <Button
          variant="contained"
          disabled={target === undefined || alreadyThere === locs.length || edit.mutation.isPending}
          onClick={edit.run}
        >
          {t('locations.sites.move')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

// --- Delete ---

/** Typed numbers may carry grouping separators or spaces ("1,234"); only the digits count. */
const typedNumber = (s: string) => {
  const digits = s.replace(/\D/g, '');
  return digits.length > 0 ? Number(digits) : NaN;
};

const blocksCardDelete = (r: { listedCopies: number; openOrderCopies: number; heldCopies: number }) =>
  r.listedCopies > 0 || r.openOrderCopies > 0 || r.heldCopies > 0;

/**
 * Deletes many locations in three deliberate steps: choose what happens to the cards (Bulk by default), review
 * every location with everything hanging off it (every game's cards, sealed lots, listings, orders, holds, list
 * lines) and untick any to keep, then type the number of locations — and, when the cards go too, tick the
 * acknowledgement and type the number of cards. The server repeats every check against the reviewed fingerprint.
 */
export function BulkDeleteLocationsDialog({
  locs,
  onClose,
  onDone,
}: {
  locs: LocationSummaryDto[];
  onClose: () => void;
  onDone: (message: string) => void;
}) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const qc = useQueryClient();
  const { can } = usePermissions();
  const canDeleteCards = can('collection.delete');
  const games = useQuery({ queryKey: ['games'], queryFn: () => api.games() });
  const gameName = (id?: string | null) =>
    id ? games.data?.find((g) => g.id === id)?.displayName ?? id : t('locations.cardFilter.anyGame');

  const [step, setStep] = useState(0);
  const [moveToBulk, setMoveToBulk] = useState(true);
  const [excluded, setExcluded] = useState<Set<number>>(new Set());
  const [typedLocations, setTypedLocations] = useState('');
  const [typedCards, setTypedCards] = useState('');
  const [acknowledged, setAcknowledged] = useState(false);
  const [staleNote, setStaleNote] = useState(false);

  const ids = useMemo(() => locs.map((l) => l.id).filter((id) => !excluded.has(id)), [locs, excluded]);
  const previewKey = ['locations-bulk-delete-preview', ids, moveToBulk];
  const preview = useQuery({
    queryKey: previewKey,
    queryFn: () => api.locationsBulkDeletePreview(ids, moveToBulk),
    enabled: step > 0 && ids.length > 0,
    staleTime: 0,
  });
  const p: BulkLocationDeletePreviewDto | undefined = preview.data;
  // The review always lists every chosen location; unticked ones just aren't in the preview any more.
  const rowsById = new Map(p?.locations.map((r) => [r.id, r]) ?? []);
  const blocked = !moveToBulk && !!p && p.locations.some(blocksCardDelete);

  const resetConfirm = () => {
    setTypedLocations('');
    setTypedCards('');
    setAcknowledged(false);
  };

  const remove = useMutation({
    mutationFn: () =>
      api.locationsBulkDelete({
        ids,
        moveCardsToBulk: moveToBulk,
        token: p!.token,
        confirmLocationCount: typedNumber(typedLocations),
        confirmCopies: moveToBulk ? null : typedNumber(typedCards),
      }),
    onSuccess: (r) => {
      onDone(
        moveToBulk
          ? t('locations.bulk.delete.doneBulk', {
              count: r.locationsDeleted,
              countText: fmt.number(r.locationsDeleted),
              cardsText: fmt.number(r.copiesMovedToBulk),
            })
          : t('locations.bulk.delete.doneDeleted', {
              count: r.locationsDeleted,
              countText: fmt.number(r.locationsDeleted),
              cardsText: fmt.number(r.copiesDeleted),
            }),
      );
      onClose();
    },
    onError: (e) => {
      // Something changed since the review: show the fresh review and make the user confirm again.
      if (e instanceof ApiError && e.status === 409) {
        setStaleNote(true);
        resetConfirm();
        setStep(1);
        qc.invalidateQueries({ queryKey: ['locations-bulk-delete-preview'] });
      }
    },
  });

  const locationsOk = p != null && typedNumber(typedLocations) === p.locations.length;
  const cardsOk = moveToBulk || (p != null && acknowledged && typedNumber(typedCards) === p.totalCopies);
  const canDelete = !!p && !preview.isFetching && !blocked && locationsOk && cardsOk && !remove.isPending;

  const note = (key: string, count: number) => (count > 0 ? t(`locations.bulk.delete.notes.${key}`, { count }) : null);

  return (
    <Dialog open onClose={onClose} maxWidth="md" fullWidth>
      <DialogTitle>{t('locations.bulk.delete.title', { count: locs.length })}</DialogTitle>
      <DialogContent>
        <Stepper activeStep={step} sx={{ mb: 3, mt: 1 }}>
          <Step>
            <StepLabel>{t('locations.bulk.delete.steps.cards')}</StepLabel>
          </Step>
          <Step>
            <StepLabel>{t('locations.bulk.delete.steps.review')}</StepLabel>
          </Step>
          <Step>
            <StepLabel>{t('locations.bulk.delete.steps.confirm')}</StepLabel>
          </Step>
        </Stepper>

        {step === 0 && (
          <Stack spacing={1}>
            <Typography>{t('locations.bulk.delete.cardsQuestion')}</Typography>
            <RadioGroup
              value={moveToBulk ? 'bulk' : 'delete'}
              onChange={(e) => {
                setMoveToBulk(e.target.value === 'bulk');
                resetConfirm();
              }}
            >
              <FormControlLabel
                value="bulk"
                control={<Radio />}
                label={
                  <Stack>
                    <Typography>{t('locations.bulk.delete.moveToBulk')}</Typography>
                    <Typography variant="body2" color="text.secondary">
                      {t('locations.bulk.delete.moveToBulkHelp')}
                    </Typography>
                  </Stack>
                }
                sx={{ alignItems: 'flex-start', mb: 1, '& .MuiRadio-root': { pt: 0.5 } }}
              />
              <FormControlLabel
                value="delete"
                disabled={!canDeleteCards}
                control={<Radio color="error" />}
                label={
                  <Stack>
                    <Typography color={canDeleteCards ? 'error' : undefined}>
                      {t('locations.bulk.delete.deleteCards')}
                    </Typography>
                    <Typography variant="body2" color="text.secondary">
                      {canDeleteCards
                        ? t('locations.bulk.delete.deleteCardsHelp')
                        : t('locations.bulk.delete.deleteCardsNoPermission')}
                    </Typography>
                  </Stack>
                }
                sx={{ alignItems: 'flex-start', '& .MuiRadio-root': { pt: 0.5 } }}
              />
            </RadioGroup>
          </Stack>
        )}

        {step === 1 && (
          <Stack spacing={2}>
            {staleNote && <Alert severity="warning">{t('locations.bulk.delete.stale')}</Alert>}
            <Typography variant="body2" color="text.secondary">
              {t('locations.bulk.delete.reviewIntro')}
            </Typography>
            {preview.isError && <Alert severity="error">{(preview.error as Error).message}</Alert>}
            <Box sx={{ maxHeight: 360, overflow: 'auto', border: 1, borderColor: 'divider', borderRadius: 1 }}>
              <Table size="small" stickyHeader>
                <TableHead>
                  <TableRow>
                    <TableCell padding="checkbox" />
                    <TableCell>{t('locations.bulk.delete.columns.location')}</TableCell>
                    <TableCell align="right">{t('locations.columns.cards')}</TableCell>
                    <TableCell align="right">{t('locations.bulk.delete.columns.value')}</TableCell>
                    <TableCell>{t('locations.bulk.delete.columns.alsoAffected')}</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {locs.map((loc) => {
                    const included = !excluded.has(loc.id);
                    const r = rowsById.get(loc.id);
                    const notes = r
                      ? [
                          note('sealed', r.sealedLots),
                          note('listed', r.listedCopies),
                          note('orders', r.openOrderCopies),
                          note('held', r.heldCopies),
                          note('lists', r.listReferences),
                          r.deckLinked ? t('locations.bulk.delete.notes.deckLinked') : null,
                          note('dividers', r.dividers),
                          note('reports', r.putBackReports),
                        ].filter(Boolean)
                      : [];
                    const rowBlocked = !moveToBulk && r != null && blocksCardDelete(r);
                    return (
                      <TableRow
                        key={loc.id}
                        sx={{
                          opacity: included ? 1 : 0.5,
                          bgcolor: rowBlocked ? 'action.selected' : undefined,
                        }}
                      >
                        <TableCell padding="checkbox">
                          <Checkbox
                            size="small"
                            checked={included}
                            disabled={included && ids.length === 1}
                            onChange={() => {
                              resetConfirm();
                              setExcluded((prev) => {
                                const next = new Set(prev);
                                if (next.has(loc.id)) next.delete(loc.id);
                                else next.add(loc.id);
                                return next;
                              });
                            }}
                          />
                        </TableCell>
                        <TableCell>
                          <Typography variant="body2" fontWeight={500}>
                            {loc.name}
                          </Typography>
                          <Typography variant="caption" color="text.secondary">
                            {[
                              t(`locations.types.${r?.type ?? loc.type.replace(/\s/g, '')}`, { defaultValue: loc.type }),
                              gameName(r?.game ?? loc.game),
                              r?.siteName ?? loc.siteName,
                            ]
                              .filter(Boolean)
                              .join(' · ')}
                          </Typography>
                          {rowBlocked && (
                            <Typography variant="caption" color="error" display="block">
                              {t('locations.bulk.delete.blockedRow')}
                            </Typography>
                          )}
                        </TableCell>
                        <TableCell align="right">{r ? fmt.number(r.copies) : included ? '…' : '—'}</TableCell>
                        <TableCell align="right">{r ? fmt.money(r.marketValue) : ''}</TableCell>
                        <TableCell>
                          <Typography variant="caption" color={notes.length ? 'warning.main' : 'text.secondary'}>
                            {notes.length ? notes.join(' · ') : '—'}
                          </Typography>
                        </TableCell>
                      </TableRow>
                    );
                  })}
                </TableBody>
              </Table>
            </Box>
            {p && (
              <Typography fontWeight={500}>
                {t('locations.bulk.delete.totals', {
                  count: p.locations.length,
                  countText: fmt.number(p.locations.length),
                  cardsText: fmt.number(p.totalCopies),
                  value: fmt.money(p.totalMarketValue),
                })}
              </Typography>
            )}
            {blocked && <Alert severity="error">{t('locations.bulk.delete.blocked')}</Alert>}
            {p && moveToBulk && p.totalListedCopies > 0 && (
              <Alert severity="info">{t('locations.bulk.delete.listedStayListed', { count: p.totalListedCopies })}</Alert>
            )}
            {p && !moveToBulk && p.totalListReferences > 0 && (
              <Alert severity="warning">{t('locations.bulk.delete.listRefsLost', { count: p.totalListReferences })}</Alert>
            )}
            {p && p.totalSealedLots > 0 && (
              <Alert severity="info">{t('locations.bulk.delete.sealedKept', { count: p.totalSealedLots })}</Alert>
            )}
            <Typography variant="body2" color="text.secondary">
              {t('locations.bulk.delete.alsoRemoved')}
            </Typography>
          </Stack>
        )}

        {step === 2 && p && (
          <Stack spacing={2}>
            <Alert severity={moveToBulk ? 'warning' : 'error'}>
              {moveToBulk
                ? t('locations.bulk.delete.summaryBulk', {
                    count: p.locations.length,
                    countText: fmt.number(p.locations.length),
                    cardsText: fmt.number(p.totalCopies),
                  })
                : t('locations.bulk.delete.summaryDelete', {
                    count: p.locations.length,
                    countText: fmt.number(p.locations.length),
                    cardsText: fmt.number(p.totalCopies),
                    value: fmt.money(p.totalMarketValue),
                  })}
            </Alert>
            <TextField
              size="small"
              autoFocus
              autoComplete="off"
              label={t('locations.bulk.delete.typeLocations', { countText: fmt.number(p.locations.length) })}
              value={typedLocations}
              onChange={(e) => setTypedLocations(e.target.value)}
              error={typedLocations.length > 0 && !locationsOk}
              inputProps={{ inputMode: 'numeric' }}
            />
            {!moveToBulk && (
              <>
                <FormControlLabel
                  control={
                    <Checkbox color="error" checked={acknowledged} onChange={(e) => setAcknowledged(e.target.checked)} />
                  }
                  label={t('locations.bulk.delete.acknowledge', {
                    cardsText: fmt.number(p.totalCopies),
                    value: fmt.money(p.totalMarketValue),
                  })}
                />
                <TextField
                  size="small"
                  autoComplete="off"
                  label={t('locations.bulk.delete.typeCards', { countText: fmt.number(p.totalCopies) })}
                  value={typedCards}
                  onChange={(e) => setTypedCards(e.target.value)}
                  error={typedCards.length > 0 && typedNumber(typedCards) !== p.totalCopies}
                  inputProps={{ inputMode: 'numeric' }}
                />
              </>
            )}
            {remove.isError && !(remove.error instanceof ApiError && remove.error.status === 409) && (
              <Alert severity="error">
                {(remove.error as Error).message}
                {failuresOf(remove.error) && (
                  <Box component="ul" sx={{ m: 0, pl: 2 }}>
                    {failuresOf(remove.error)!.map((f) => (
                      <li key={f.id}>
                        <strong>{f.name}</strong>: {f.error}
                      </li>
                    ))}
                  </Box>
                )}
              </Alert>
            )}
          </Stack>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('common.actions.cancel')}</Button>
        {step > 0 && (
          <Button
            onClick={() => {
              resetConfirm();
              setStep(step - 1);
            }}
          >
            {t('locations.bulk.delete.back')}
          </Button>
        )}
        {step < 2 ? (
          <Button
            variant="contained"
            disabled={step === 1 && (!p || preview.isFetching || blocked || ids.length === 0)}
            onClick={() => {
              setStaleNote(false);
              setStep(step + 1);
            }}
          >
            {t('locations.bulk.delete.next')}
          </Button>
        ) : (
          <Button variant="contained" color="error" disabled={!canDelete} onClick={() => remove.mutate()}>
            {t('locations.bulk.delete.button', { count: p?.locations.length ?? 0 })}
          </Button>
        )}
      </DialogActions>
    </Dialog>
  );
}
