import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Divider,
  MenuItem,
  Stack,
  Tab,
  Tabs,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material';
import PrintIcon from '@mui/icons-material/Print';
import InventoryIcon from '@mui/icons-material/Inventory2';
import { api } from '../../api/client';
import type { DecklistCheckDto, DecklistCheckRequest, DecklistPickDto } from '../../api/types';
import { useGame } from '../../context/GameContext';
import { usePermissions } from '../../context/usePermissions';
import { useFormatters } from '../../i18n/format';
import { isDeckBoxType } from '../../lib/locationTypes';
import { LocationPickerDialog } from './LocationPickerDialog';

/** "Binder A · Reds · Pg 3 · Slot 5" — only the parts the pick has. */
function usePickWhere() {
  const { t } = useTranslation();
  return (p: DecklistPickDto) =>
    [
      p.locationName,
      p.section,
      p.page != null ? t('collection.decklist.page', { page: p.page }) : null,
      p.slot != null ? t('collection.decklist.slot', { slot: p.slot }) : null,
    ]
      .filter(Boolean)
      .join(' · ');
}

function PullTab({ data }: { data: DecklistCheckDto }) {
  const { t } = useTranslation();
  const where = usePickWhere();
  if (data.owned.length === 0)
    return (
      <Typography variant="body2" color="text.secondary" sx={{ py: 2 }}>
        {t('collection.decklist.noneOwned')}
      </Typography>
    );
  return (
    <Stack spacing={1} sx={{ py: 1 }}>
      {data.owned.map((e, i) => (
        <Box key={i}>
          <Typography variant="body2" fontWeight={600}>
            {e.quantityNeeded}× {e.cardName}
          </Typography>
          {(e.picks ?? []).map((p, j) => (
            <Stack key={j} direction="row" spacing={1} alignItems="center" sx={{ pl: 2, flexWrap: 'wrap' }} useFlexGap>
              <Typography variant="caption" color="text.secondary">
                {p.quantity}× {p.setCode}
                {p.collectorNumber ? ` #${p.collectorNumber}` : ''} · {p.condition}
                {p.isFoil ? ' ✦' : ''} — {where(p)}
              </Typography>
              {p.isListed && <Chip size="small" color="warning" variant="outlined" label={t('collection.decklist.listedChip')} />}
              {p.locationType && isDeckBoxType(p.locationType) && (
                <Chip size="small" variant="outlined" label={t('collection.decklist.inDeckBoxChip')} />
              )}
            </Stack>
          ))}
        </Box>
      ))}
    </Stack>
  );
}

function MissingTab({ data }: { data: DecklistCheckDto }) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  if (data.missing.length === 0)
    return (
      <Typography variant="body2" color="text.secondary" sx={{ py: 2 }}>
        {t('collection.decklist.noneMissing')}
      </Typography>
    );
  return (
    <Stack spacing={0.5} sx={{ py: 1 }}>
      {data.missing.map((m, i) => (
        <Stack key={i} direction="row" spacing={1} justifyContent="space-between">
          <Typography variant="body2">
            {m.quantityNeeded}× {m.cardName}
            {m.setCode ? (
              <Typography component="span" variant="caption" color="text.secondary">
                {' '}
                ({m.setCode}
                {m.collectorNumber ? ` #${m.collectorNumber}` : ''})
              </Typography>
            ) : null}
          </Typography>
          <Typography variant="body2" color="text.secondary">
            {m.marketPrice != null ? fmt.money(m.marketPrice * m.quantityNeeded) : '—'}
          </Typography>
        </Stack>
      ))}
    </Stack>
  );
}

/**
 * Check a decklist (Moxfield/Archidekt URL or pasted text) against the collection: which copies to
 * pull and from where, what's missing and what it costs. Prints either as a tick-box checklist, and —
 * once every card is owned — moves the picked copies straight into a deck box.
 */
export function DecklistCheckDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const qc = useQueryClient();
  const { can } = usePermissions();
  const { game: activeGame } = useGame();
  const games = useQuery({ queryKey: ['games'], queryFn: api.games, enabled: open });

  const [game, setGame] = useState(activeGame ?? 'Mtg');
  const [url, setUrl] = useState('');
  const [text, setText] = useState('');
  const [tab, setTab] = useState<'pull' | 'missing'>('pull');
  const [pickerOpen, setPickerOpen] = useState(false);
  const [moveResult, setMoveResult] = useState<{ moved: number; deckBox: string } | null>(null);
  // The request behind the current result — prints and re-checks reuse it, not whatever is typed now.
  const [checkedReq, setCheckedReq] = useState<DecklistCheckRequest | null>(null);

  const check = useMutation({
    mutationFn: (req: DecklistCheckRequest) => api.decklistCheck(req),
    onSuccess: (_, req) => setCheckedReq(req),
  });
  const printPull = useMutation({ mutationFn: (req: DecklistCheckRequest) => api.decklistPullListPdf(req) });
  const printMissing = useMutation({ mutationFn: (req: DecklistCheckRequest) => api.decklistMissingListPdf(req) });
  const locations = useQuery({ queryKey: ['locations', undefined], queryFn: () => api.locations(), enabled: pickerOpen });

  const data = check.data;
  const picks = (data?.owned ?? []).flatMap((e) => e.picks ?? []);
  const listedCount = picks.filter((p) => p.isListed).reduce((n, p) => n + p.quantity, 0);
  const fromDeckBoxes = new Set(
    picks.filter((p) => p.locationType && isDeckBoxType(p.locationType)).map((p) => p.locationName),
  );

  const move = useMutation({
    mutationFn: (containerId: number) =>
      api.decklistMoveToDeckBox(
        containerId,
        picks.map((p) => ({ lotId: p.lotId, quantity: p.quantity })),
      ),
    onSuccess: (res, containerId) => {
      // A deck box created inline in the picker may not be in the cached list yet.
      const deckBox =
        locations.data?.find((l) => l.id === containerId)?.name ?? t('collection.decklist.theDeckBox');
      setMoveResult({ moved: res.moved, deckBox });
      qc.invalidateQueries({ queryKey: ['collection'] });
      qc.invalidateQueries({ queryKey: ['location'] });
      qc.invalidateQueries({ queryKey: ['locations'] });
      qc.invalidateQueries({ queryKey: ['dashboard'] });
      // Re-check so the pull list reflects the cards' new home.
      if (checkedReq) check.mutate(checkedReq);
    },
  });

  const runCheck = () => {
    setMoveResult(null);
    move.reset();
    check.mutate({ game, url: url.trim() || undefined, text: url.trim() ? undefined : text });
  };

  const complete = !!data && data.totalMissing === 0 && data.totalOwned > 0;
  const canMove = complete && can('collection.edit');
  const moveHint = !data
    ? ''
    : !can('collection.edit')
      ? t('collection.decklist.moveNoPermission')
      : !complete
        ? t('collection.decklist.moveNeedsAll', { count: data.totalMissing })
        : '';
  const printError = (printPull.error ?? printMissing.error) as Error | null;

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="md">
      <DialogTitle>{t('collection.decklist.title')}</DialogTitle>
      <DialogContent dividers>
        <Stack spacing={2}>
          <Typography variant="body2" color="text.secondary">
            {t('collection.decklist.description')}
          </Typography>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <TextField
              select
              size="small"
              label={t('common.labels.game')}
              value={game}
              onChange={(e) => setGame(e.target.value)}
              sx={{ minWidth: 180 }}
            >
              {(games.data ?? [{ id: game, displayName: game }]).map((g) => (
                <MenuItem key={g.id} value={g.id}>
                  {g.displayName}
                </MenuItem>
              ))}
            </TextField>
            <TextField
              size="small"
              fullWidth
              label={t('collection.decklist.urlLabel')}
              value={url}
              onChange={(e) => setUrl(e.target.value)}
            />
          </Stack>
          <Divider>{t('collection.decklist.orPaste')}</Divider>
          <TextField
            label={t('collection.decklist.textLabel')}
            multiline
            minRows={4}
            maxRows={10}
            value={text}
            onChange={(e) => setText(e.target.value)}
            placeholder={t('collection.decklist.textPlaceholder')}
            disabled={!!url.trim()}
          />
          <Box>
            <Button variant="contained" disabled={(!text.trim() && !url.trim()) || check.isPending} onClick={runCheck}>
              {check.isPending ? t('collection.decklist.checking') : t('collection.decklist.check')}
            </Button>
          </Box>
          {check.error && <Alert severity="error">{(check.error as Error).message}</Alert>}

          {data && checkedReq && (
            <Box>
              <Typography variant="subtitle1" fontWeight={600}>
                {data.deckName}
              </Typography>
              <Stack direction="row" spacing={1} sx={{ my: 1, flexWrap: 'wrap' }} useFlexGap>
                <Chip color="success" label={t('collection.decklist.owned', { count: data.totalOwned })} />
                <Chip
                  color={data.totalMissing > 0 ? 'warning' : 'default'}
                  label={t('collection.decklist.missing', { count: data.totalMissing })}
                />
                {data.totalMissing > 0 && (
                  <Chip label={t('collection.decklist.toComplete', { cost: fmt.money(data.estimatedCost) })} />
                )}
              </Stack>

              <Stack direction="row" spacing={1} sx={{ mb: 1, flexWrap: 'wrap' }} useFlexGap>
                <Button
                  variant="outlined"
                  startIcon={<PrintIcon />}
                  disabled={data.totalOwned === 0 || printPull.isPending}
                  onClick={() => printPull.mutate(checkedReq)}
                >
                  {t('collection.decklist.printPull')}
                </Button>
                <Button
                  variant="outlined"
                  startIcon={<PrintIcon />}
                  disabled={data.totalMissing === 0 || printMissing.isPending}
                  onClick={() => printMissing.mutate(checkedReq)}
                >
                  {t('collection.decklist.printMissing')}
                </Button>
                <Tooltip title={moveHint}>
                  <span>
                    <Button
                      variant="contained"
                      startIcon={<InventoryIcon />}
                      disabled={!canMove || move.isPending}
                      onClick={() => setPickerOpen(true)}
                    >
                      {move.isPending ? t('collection.decklist.moving') : t('collection.decklist.moveToDeckBox')}
                    </Button>
                  </span>
                </Tooltip>
              </Stack>

              {printError && <Alert severity="error" sx={{ mb: 1 }}>{printError.message}</Alert>}
              {move.error && <Alert severity="error" sx={{ mb: 1 }}>{(move.error as Error).message}</Alert>}
              {moveResult && (
                <Alert severity="success" sx={{ mb: 1 }}>
                  {t('collection.decklist.moved', { count: moveResult.moved, deckBox: moveResult.deckBox })}
                </Alert>
              )}
              {complete && listedCount > 0 && (
                <Alert severity="warning" sx={{ mb: 1 }}>
                  {t('collection.decklist.listedWarning', { count: listedCount })}
                </Alert>
              )}
              {complete && fromDeckBoxes.size > 0 && (
                <Alert severity="info" sx={{ mb: 1 }}>
                  {t('collection.decklist.fromOtherDeckBoxes', { names: [...fromDeckBoxes].join(', ') })}
                </Alert>
              )}

              <Tabs value={tab} onChange={(_, v) => setTab(v)} sx={{ borderBottom: 1, borderColor: 'divider' }}>
                <Tab value="pull" label={t('collection.decklist.pullTab', { count: data.totalOwned })} />
                <Tab value="missing" label={t('collection.decklist.missingTab', { count: data.totalMissing })} />
              </Tabs>
              {tab === 'pull' ? <PullTab data={data} /> : <MissingTab data={data} />}
            </Box>
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('common.actions.close')}</Button>
      </DialogActions>

      <LocationPickerDialog
        open={pickerOpen}
        title={t('collection.decklist.pickDeckBox', { count: data?.totalOwned ?? 0 })}
        types={['DeckBox']}
        cardGames={data ? [data.game] : undefined}
        onPick={(id) => {
          setPickerOpen(false);
          move.mutate(id);
        }}
        onClose={() => setPickerOpen(false)}
      />
    </Dialog>
  );
}
