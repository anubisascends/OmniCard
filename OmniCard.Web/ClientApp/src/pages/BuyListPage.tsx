import { useMemo, useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  IconButton,
  InputAdornment,
  MenuItem,
  Paper,
  Stack,
  TextField,
  ToggleButton,
  ToggleButtonGroup,
  Tooltip,
  Typography,
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import DeleteIcon from '@mui/icons-material/Delete';
import EditIcon from '@mui/icons-material/Edit';
import ManageSearchIcon from '@mui/icons-material/ManageSearch';
import SearchIcon from '@mui/icons-material/Search';
import { useTranslation } from 'react-i18next';
import { api } from '../api/client';
import type { BuyListEntryDto, BuyListPriority } from '../api/types';
import { BuyListChip, buyListFrameSx } from '../components/BuyListBadge';
import { CardImage } from '../components/CardImage';
import { LocationPickerField } from '../components/LocationPickerField';
import { AddCardDialog } from '../components/dialogs/AddCardDialog';
import { useGame } from '../context/GameContext';
import { usePermissions } from '../context/usePermissions';
import { useIsPhone } from '../lib/responsive';

const BUY_LIST_PRIORITIES: BuyListPriority[] = ['Normal', 'High', 'Grail'];

/** "any" | "foil" | "nonfoil" ⇄ the entry's nullable foil flag, for a select. */
const foilChoice = (foil: boolean | null | undefined) => (foil == null ? 'any' : foil ? 'foil' : 'nonfoil');
const foilFromChoice = (choice: string) => (choice === 'any' ? null : choice === 'foil');

/**
 * The per-game Buy Lists: the cards the collection's owner wants to buy. Everyone can read them and jump
 * to matching copies in the collection (`is:buylist`); only administrators add, edit, remove cards or
 * choose the game's set-aside location. Follows the top bar's game ("All Games" shows every list).
 */
export function BuyListPage() {
  const { t } = useTranslation();
  const { game } = useGame();
  const { isAdmin } = usePermissions();
  const qc = useQueryClient();
  const [fulfilled, setFulfilled] = useState(false);
  const [search, setSearch] = useState('');
  const [addOpen, setAddOpen] = useState(false);
  const [editing, setEditing] = useState<BuyListEntryDto | null>(null);
  const [removing, setRemoving] = useState<BuyListEntryDto | null>(null);

  const list = useQuery({
    queryKey: ['buy-list', game ?? null, fulfilled],
    queryFn: () => api.buyList(game, fulfilled),
    placeholderData: keepPreviousData,
  });
  const games = useQuery({ queryKey: ['games'], queryFn: api.games });
  const refresh = () => void qc.invalidateQueries({ queryKey: ['buy-list'] });

  const remove = useMutation({
    mutationFn: (id: number) => api.buyListDelete(id),
    onSuccess: () => {
      setRemoving(null);
      refresh();
    },
  });
  const setAside = useMutation({
    mutationFn: (locationId: number | null) => api.buyListSetAside(game!, locationId),
    onSuccess: refresh,
  });

  const page = list.data;
  const shown = useMemo(() => {
    const term = search.trim().toLowerCase();
    return (page?.items ?? []).filter((e) => !term || e.name.toLowerCase().includes(term));
  }, [page, search]);

  return (
    <Stack spacing={2}>
      <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
        <Typography variant="h4" sx={{ flexGrow: 1 }}>
          {t('buyList.title')}
        </Typography>
        <Button component={RouterLink} to="/collection?q=is%3Abuylist" startIcon={<ManageSearchIcon />}>
          {t('buyList.findInCollection')}
        </Button>
        {isAdmin && (
          <Button variant="contained" startIcon={<AddIcon />} onClick={() => setAddOpen(true)}>
            {t('buyList.addCard')}
          </Button>
        )}
      </Stack>
      <Typography variant="body2" color="text.secondary" sx={{ mt: -1 }}>
        {t(isAdmin ? 'buyList.introAdmin' : 'buyList.intro')}
      </Typography>

      {isAdmin && (
        <Paper variant="outlined" sx={{ p: 2 }}>
          {game ? (
            <>
              <LocationPickerField
                label={t('buyList.setAside.label')}
                value={page?.setAsideLocationId ?? null}
                onChange={(id) => setAside.mutate(id)}
                clearable
                emptyText={t('buyList.setAside.none')}
                cardGames={[game]}
                disabled={setAside.isPending || !page}
                sx={{ maxWidth: 400 }}
              />
              <Typography variant="caption" color="text.secondary" component="div" sx={{ mt: 0.5 }}>
                {t('buyList.setAside.help')}
              </Typography>
              {setAside.isError && (
                <Alert severity="error" sx={{ mt: 1 }}>
                  {(setAside.error as Error).message}
                </Alert>
              )}
            </>
          ) : (
            <Typography variant="body2" color="text.secondary">
              {t('buyList.setAside.pickGame')}
            </Typography>
          )}
        </Paper>
      )}
      {!isAdmin && page?.setAsideLocationName && (
        <Typography variant="body2" color="text.secondary">
          {t('buyList.setAside.current', { location: page.setAsideLocationName })}
        </Typography>
      )}

      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} alignItems={{ sm: 'center' }}>
        <ToggleButtonGroup
          size="small"
          exclusive
          value={fulfilled ? 'fulfilled' : 'active'}
          onChange={(_, v: string | null) => v && setFulfilled(v === 'fulfilled')}
        >
          <ToggleButton value="active">{t('buyList.tabs.active', { count: page?.activeCount ?? 0 })}</ToggleButton>
          <ToggleButton value="fulfilled">{t('buyList.tabs.fulfilled', { count: page?.fulfilledCount ?? 0 })}</ToggleButton>
        </ToggleButtonGroup>
        <TextField
          size="small"
          placeholder={t('buyList.search')}
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          InputProps={{
            startAdornment: (
              <InputAdornment position="start">
                <SearchIcon fontSize="small" />
              </InputAdornment>
            ),
          }}
          sx={{ flexGrow: 1, maxWidth: { sm: 360 } }}
        />
      </Stack>

      {list.isError && <Alert severity="error">{(list.error as Error).message}</Alert>}
      {list.isLoading && <CircularProgress />}
      {page && shown.length === 0 && (
        <Typography color="text.secondary" sx={{ py: 4, textAlign: 'center' }}>
          {search.trim() ? t('buyList.noMatches') : t(fulfilled ? 'buyList.emptyFulfilled' : 'buyList.empty')}
        </Typography>
      )}
      <Stack spacing={1}>
        {shown.map((entry) => (
          <EntryRow
            key={entry.id}
            entry={entry}
            gameName={game ? undefined : (games.data?.find((g) => g.id === entry.game)?.displayName ?? entry.game)}
            canEdit={isAdmin}
            onEdit={() => setEditing(entry)}
            onRemove={() => setRemoving(entry)}
          />
        ))}
      </Stack>

      {isAdmin && (
        <AddCardDialog
          open={addOpen}
          target={{ kind: 'buyList' }}
          defaultGame={game}
          onClose={() => setAddOpen(false)}
          onDone={refresh}
        />
      )}
      {editing && <EntryDialog entry={editing} onClose={() => setEditing(null)} onSaved={refresh} />}
      <Dialog open={!!removing} onClose={() => setRemoving(null)}>
        <DialogTitle>{t('buyList.delete.title', { name: removing?.name })}</DialogTitle>
        <DialogContent>
          <DialogContentText>{t('buyList.delete.body')}</DialogContentText>
          {remove.isError && (
            <Alert severity="error" sx={{ mt: 1 }}>
              {(remove.error as Error).message}
            </Alert>
          )}
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setRemoving(null)}>{t('common.actions.cancel')}</Button>
          <Button color="error" variant="contained" disabled={remove.isPending} onClick={() => removing && remove.mutate(removing.id)}>
            {t('buyList.delete.confirm')}
          </Button>
        </DialogActions>
      </Dialog>
    </Stack>
  );
}

function EntryRow({
  entry,
  gameName,
  canEdit,
  onEdit,
  onRemove,
}: {
  entry: BuyListEntryDto;
  /** Set when several games are listed. */
  gameName?: string;
  canEdit: boolean;
  onEdit: () => void;
  onRemove: () => void;
}) {
  const { t } = useTranslation();
  const printing = entry.pinPrinting
    ? `${entry.setName ?? entry.setCode?.toUpperCase() ?? ''} · #${entry.collectorNumber ?? ''}`
    : t('buyList.row.anyPrinting');
  return (
    <Paper variant="outlined" sx={{ p: 1.5, ...buyListFrameSx(entry.wantedQuantity > 0 && entry.priority !== 'Normal' ? entry.priority : undefined) }}>
      <Stack direction="row" spacing={1.5} alignItems="center">
        <CardImage
          src={entry.imageUri ?? undefined}
          alt={entry.name}
          foil={entry.foil === true}
          wrapperSx={{ flexShrink: 0 }}
          sx={{ width: 56, aspectRatio: '0.716', objectFit: 'contain', borderRadius: 1 }}
        />
        <Box sx={{ flexGrow: 1, minWidth: 0 }}>
          <Typography variant="subtitle1" noWrap sx={{ fontWeight: 600 }}>
            {entry.name}
          </Typography>
          <Typography variant="body2" color="text.secondary" noWrap>
            {printing}
          </Typography>
          <Stack direction="row" spacing={0.5} flexWrap="wrap" useFlexGap sx={{ mt: 0.5 }}>
            <BuyListChip priority={entry.priority} />
            {entry.wantedQuantity > 0 && <Chip size="small" label={t('buyList.row.wanted', { count: entry.wantedQuantity })} />}
            {entry.foil != null && (
              <Chip size="small" variant="outlined" label={t(entry.foil ? 'buyList.row.foilOnly' : 'buyList.row.nonFoilOnly')} />
            )}
            {entry.acquiredQuantity > 0 && (
              <Chip size="small" variant="outlined" color="success" label={t('buyList.row.acquired', { count: entry.acquiredQuantity })} />
            )}
            {entry.ownedCopies > 0 && (
              <Chip size="small" variant="outlined" color="info" label={t('buyList.row.owned', { count: entry.ownedCopies })} />
            )}
            {gameName && <Chip size="small" variant="outlined" label={gameName} />}
          </Stack>
          {entry.note && (
            <Typography variant="caption" color="text.secondary" component="div" sx={{ mt: 0.5, whiteSpace: 'pre-line' }}>
              {entry.note}
            </Typography>
          )}
        </Box>
        {canEdit && (
          <Stack direction={{ xs: 'column', sm: 'row' }}>
            <Tooltip title={t('common.actions.edit')}>
              <IconButton aria-label={t('common.actions.edit')} onClick={onEdit}>
                <EditIcon />
              </IconButton>
            </Tooltip>
            <Tooltip title={t('buyList.delete.confirm')}>
              <IconButton aria-label={t('buyList.delete.confirm')} onClick={onRemove}>
                <DeleteIcon />
              </IconButton>
            </Tooltip>
          </Stack>
        )}
      </Stack>
    </Paper>
  );
}

function EntryDialog({ entry, onClose, onSaved }: { entry: BuyListEntryDto; onClose: () => void; onSaved: () => void }) {
  const { t } = useTranslation();
  const isPhone = useIsPhone();
  const [wanted, setWanted] = useState(entry.wantedQuantity);
  const [pin, setPin] = useState(entry.pinPrinting);
  const [finish, setFinish] = useState(foilChoice(entry.foil));
  const [priority, setPriority] = useState(entry.priority);
  const [note, setNote] = useState(entry.note ?? '');
  const save = useMutation({
    mutationFn: () =>
      api.buyListUpdate(entry.id, {
        wantedQuantity: wanted,
        pinPrinting: pin,
        foil: foilFromChoice(finish),
        priority,
        note: note.trim() || null,
      }),
    onSuccess: () => {
      onSaved();
      onClose();
    },
  });
  return (
    <Dialog open onClose={onClose} maxWidth="xs" fullWidth fullScreen={isPhone}>
      <DialogTitle>{t('buyList.edit.title', { name: entry.name })}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ pt: 1 }}>
          <TextField
            type="number"
            label={t('buyList.edit.wanted')}
            helperText={t('buyList.edit.wantedHelp')}
            value={wanted}
            onChange={(e) => setWanted(Math.max(0, Math.floor(Number(e.target.value) || 0)))}
            inputProps={{ min: 0, step: 1 }}
          />
          <TextField select label={t('buyList.edit.printing')} value={pin ? 'one' : 'any'} onChange={(e) => setPin(e.target.value === 'one')}>
            <MenuItem value="any">{t('buyList.edit.printingAny')}</MenuItem>
            <MenuItem value="one">
              {t('buyList.edit.printingOne', { set: entry.setCode?.toUpperCase() ?? '', number: entry.collectorNumber ?? '' })}
            </MenuItem>
          </TextField>
          <TextField select label={t('buyList.edit.finish')} value={finish} onChange={(e) => setFinish(e.target.value)}>
            <MenuItem value="any">{t('buyList.edit.finishAny')}</MenuItem>
            <MenuItem value="foil">{t('buyList.edit.finishFoil')}</MenuItem>
            <MenuItem value="nonfoil">{t('buyList.edit.finishNonFoil')}</MenuItem>
          </TextField>
          <TextField select label={t('buyList.edit.priority')} value={priority} onChange={(e) => setPriority(e.target.value as BuyListPriority)}>
            {BUY_LIST_PRIORITIES.map((p) => (
              <MenuItem key={p} value={p}>
                {t(`buyList.priority.${p}`)}
              </MenuItem>
            ))}
          </TextField>
          <TextField
            label={t('buyList.edit.note')}
            helperText={t('buyList.edit.noteHelp')}
            value={note}
            onChange={(e) => setNote(e.target.value)}
            multiline
            minRows={2}
            inputProps={{ maxLength: 500 }}
          />
          {save.isError && <Alert severity="error">{(save.error as Error).message}</Alert>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('common.actions.cancel')}</Button>
        <Button variant="contained" disabled={save.isPending} onClick={() => save.mutate()}>
          {t('common.actions.save')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
