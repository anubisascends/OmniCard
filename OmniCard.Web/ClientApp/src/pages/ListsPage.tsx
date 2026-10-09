import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  Collapse,
  Divider,
  IconButton,
  Link,
  Menu,
  MenuItem,
  Paper,
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
import AddIcon from '@mui/icons-material/Add';
import ArrowDropDownIcon from '@mui/icons-material/ArrowDropDown';
import CollectionsBookmarkIcon from '@mui/icons-material/CollectionsBookmark';
import DeleteIcon from '@mui/icons-material/Delete';
import DoNotDisturbOnOutlinedIcon from '@mui/icons-material/DoNotDisturbOnOutlined';
import DownloadIcon from '@mui/icons-material/Download';
import EditIcon from '@mui/icons-material/Edit';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import IosShareIcon from '@mui/icons-material/IosShare';
import ManageSearchIcon from '@mui/icons-material/ManageSearch';
import PlaceIcon from '@mui/icons-material/Place';
import PrintIcon from '@mui/icons-material/Print';
import RefreshIcon from '@mui/icons-material/Refresh';
import ShoppingCartIcon from '@mui/icons-material/ShoppingCart';
import SwapHorizIcon from '@mui/icons-material/SwapHoriz';
import SyncIcon from '@mui/icons-material/Sync';
import { api } from '../api/client';
import { AddCardToListDialog } from '../components/dialogs/AddCardToListDialog';
import { IgnoredLocationsDialog } from '../components/dialogs/IgnoredLocationsDialog';
import { ListExportDialog } from '../components/dialogs/ListExportDialog';
import { ListSubstitutesDialog } from '../components/dialogs/ListSubstitutesDialog';
import { ListUpdateDialog } from '../components/dialogs/ListUpdateDialog';
import { LocationPickerDialog } from '../components/dialogs/LocationPickerDialog';
import { useGame } from '../context/GameContext';
import { useFormatters } from '../i18n/format';
import { LanguageSelect } from '../lib/cardLanguages';
import type { CardListDto, CardListItemDto, ListExportScope } from '../api/types';

const CONDITIONS = ['NM', 'LP', 'MP', 'HP', 'DMG'];

type PickerTarget = 'move' | 'add';

/** Print menu: the whole list, the pick list (owned copies by location) and the buy list (what's missing). */
function PrintMenu({ listId, disabled, onError }: { listId: number; disabled: boolean; onError: (e: Error) => void }) {
  const { t } = useTranslation();
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const print = (fn: (id: number) => Promise<void>) => {
    setAnchor(null);
    fn(listId).catch(onError);
  };
  return (
    <>
      <Button
        size="small"
        startIcon={<PrintIcon />}
        endIcon={<ArrowDropDownIcon />}
        disabled={disabled}
        onClick={(e) => setAnchor(e.currentTarget)}
      >
        {t('lists.detail.print')}
      </Button>
      <Menu anchorEl={anchor} open={anchor !== null} onClose={() => setAnchor(null)}>
        <MenuItem onClick={() => print(api.listPrintPdf)}>{t('lists.detail.printList')}</MenuItem>
        <MenuItem onClick={() => print(api.listPickListPdf)}>{t('lists.detail.printPickList')}</MenuItem>
        <MenuItem onClick={() => print(api.listBuyListPdf)}>{t('lists.detail.printBuyList')}</MenuItem>
      </Menu>
    </>
  );
}

/** Copies of the item the collection already covers (exact printing, list language, readable sites). */
const ownedPart = (it: CardListItemDto) => Math.min(it.ownedQuantity, it.quantity);
/** Copies of the item still to buy. */
const missingQty = (it: CardListItemDto) => Math.max(0, it.quantity - it.ownedQuantity);

type ItemsSectionKind = 'owned' | 'buy';

/** Whether a grid is expanded, remembered per browser (storage can be unavailable — default open). */
function useSectionExpanded(section: ItemsSectionKind): [boolean, (open: boolean) => void] {
  const key = `omnicard.lists.section.${section}`;
  const [open, setOpen] = useState(() => {
    try {
      return localStorage.getItem(key) !== 'collapsed';
    } catch {
      return true;
    }
  });
  const set = (next: boolean) => {
    setOpen(next);
    try {
      localStorage.setItem(key, next ? 'expanded' : 'collapsed');
    } catch {
      /* storage unavailable: keep the in-memory state */
    }
  };
  return [open, set];
}

/**
 * One collapsible grid of a list: the copies already owned, or the copies still to buy. A partly owned item
 * appears in both, each showing its share; the list quantity (editable) is the item's total.
 */
function ListItemsSection({
  section,
  list,
  rows,
  portion,
  onExport,
  onSetQuantity,
  onRemove,
}: {
  section: ItemsSectionKind;
  list: CardListDto;
  rows: CardListItemDto[];
  portion: (it: CardListItemDto) => number;
  onExport: () => void;
  onSetQuantity: (itemId: number, quantity: number) => void;
  onRemove: (itemId: number) => void;
}) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const [expanded, setExpanded] = useSectionExpanded(section);
  const copies = rows.reduce((sum, it) => sum + portion(it), 0);
  const listLanguage = list.language;

  return (
    <Box sx={{ mb: 1.5 }}>
      <Stack
        direction="row"
        spacing={1}
        alignItems="center"
        sx={{ px: 0.5, py: 0.25, borderRadius: 1, cursor: 'pointer', '&:hover': { bgcolor: 'action.hover' } }}
        onClick={() => setExpanded(!expanded)}
      >
        <ExpandMoreIcon
          fontSize="small"
          sx={{ transition: 'transform 150ms', transform: expanded ? 'none' : 'rotate(-90deg)' }}
        />
        {section === 'owned' ? (
          <CollectionsBookmarkIcon fontSize="small" color="success" />
        ) : (
          <ShoppingCartIcon fontSize="small" color="action" />
        )}
        <Typography variant="subtitle2">{t(`lists.detail.sections.${section}`)}</Typography>
        <Chip size="small" label={t('lists.detail.sections.copies', { count: copies })} />
        <Box sx={{ flexGrow: 1 }} />
        <Button
          size="small"
          startIcon={<IosShareIcon />}
          disabled={rows.length === 0}
          onClick={(e) => {
            e.stopPropagation();
            onExport();
          }}
        >
          {t('lists.detail.export')}
        </Button>
      </Stack>
      <Collapse in={expanded} unmountOnExit>
        {rows.length === 0 ? (
          <Typography color="text.secondary" variant="body2" sx={{ px: 1, py: 1 }}>
            {t(`lists.detail.sections.empty.${section}`)}
          </Typography>
        ) : (
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell padding="checkbox"></TableCell>
                <TableCell>{t('lists.detail.columns.card')}</TableCell>
                <TableCell>{t('common.labels.set')}</TableCell>
                <TableCell align="right">{t(`lists.detail.columns.${section}`)}</TableCell>
                <TableCell align="right">{t('lists.detail.columns.listQty')}</TableCell>
                <TableCell align="right">{t('common.labels.price')}</TableCell>
                <TableCell align="right"></TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {rows.map((it) => (
                <TableRow key={it.id} hover>
                  <TableCell padding="checkbox">
                    {it.awaitingPurchase ? (
                      <Tooltip title={t('lists.detail.awaitingPurchaseTooltip')}>
                        <ShoppingCartIcon fontSize="small" color="action" sx={{ display: 'block' }} />
                      </Tooltip>
                    ) : it.isSubstitute ? (
                      <Tooltip title={t('lists.detail.substituteTooltip')}>
                        <SwapHorizIcon
                          fontSize="small"
                          color={it.ownedQuantity >= it.quantity ? 'success' : 'warning'}
                          sx={{ display: 'block' }}
                        />
                      </Tooltip>
                    ) : (
                      it.ownedQuantity > 0 && (
                        <Tooltip
                          title={t('lists.detail.ownedTooltip', {
                            owned: fmt.number(ownedPart(it)),
                            qty: fmt.number(it.quantity),
                          })}
                        >
                          <CollectionsBookmarkIcon
                            fontSize="small"
                            color={it.ownedQuantity >= it.quantity ? 'success' : 'warning'}
                            sx={{ display: 'block' }}
                          />
                        </Tooltip>
                      )
                    )}
                  </TableCell>
                  <TableCell>
                    <Stack direction="row" spacing={0.75} alignItems="center">
                      <Tooltip
                        disableInteractive
                        slotProps={{ tooltip: { sx: { bgcolor: 'transparent', p: 0, maxWidth: 'none' } } }}
                        title={
                          it.imageUri ? (
                            <Box
                              component="img"
                              src={it.imageUri}
                              alt=""
                              sx={{ width: 240, borderRadius: 2, display: 'block', boxShadow: 6 }}
                            />
                          ) : (
                            ''
                          )
                        }
                      >
                        <Box component="span" sx={{ cursor: it.imageUri ? 'help' : 'default' }}>
                          {it.cardName}
                          {it.isFoil ? ' ✦' : ''}
                        </Box>
                      </Tooltip>
                      {listLanguage && it.language && it.language !== listLanguage && (
                        <Tooltip
                          title={t('lists.detail.notInLanguageTooltip', {
                            language: t(`common.languages.${listLanguage}`),
                            printing: t(`common.languages.${it.language}`),
                          })}
                        >
                          <Chip
                            size="small"
                            color="warning"
                            variant="outlined"
                            label={t('lists.detail.notInLanguage', { language: t(`common.languages.${listLanguage}`) })}
                          />
                        </Tooltip>
                      )}
                    </Stack>
                  </TableCell>
                  <TableCell>
                    {it.setCode?.toUpperCase()}
                    {it.collectorNumber ? ` #${it.collectorNumber}` : ''}
                  </TableCell>
                  <TableCell align="right">
                    <Stack direction="row" spacing={0.5} alignItems="center" justifyContent="flex-end">
                      {section === 'buy' && it.ignoredQuantity > 0 && (
                        <Tooltip title={t('lists.detail.ignoredTooltip', { count: it.ignoredQuantity })}>
                          <Stack direction="row" spacing={0.25} alignItems="center" sx={{ color: 'text.disabled' }}>
                            <DoNotDisturbOnOutlinedIcon sx={{ fontSize: 14 }} />
                            <Typography variant="caption">+{fmt.number(it.ignoredQuantity)}</Typography>
                          </Stack>
                        </Tooltip>
                      )}
                      <Box component="span" sx={{ fontWeight: 600 }}>
                        {fmt.number(portion(it))}
                      </Box>
                    </Stack>
                  </TableCell>
                  <TableCell align="right">
                    <TextField
                      type="number"
                      size="small"
                      value={it.quantity}
                      onChange={(e) => {
                        const q = Number(e.target.value);
                        if (q >= 1) onSetQuantity(it.id, q);
                      }}
                      sx={{ width: 70 }}
                    />
                  </TableCell>
                  <TableCell align="right">{it.isUnpriced ? '—' : fmt.money(it.marketPrice)}</TableCell>
                  <TableCell align="right">
                    <IconButton size="small" onClick={() => onRemove(it.id)}>
                      <DeleteIcon fontSize="small" />
                    </IconButton>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </Collapse>
    </Box>
  );
}

function ListDetail({ list, onDeleted }: { list: CardListDto; onDeleted: (message: string) => void }) {
  const { t } = useTranslation();
  const fmt = useFormatters();
  const qc = useQueryClient();
  const items = useQuery({ queryKey: ['list-items', list.id], queryFn: () => api.listItems(list.id) });
  const locations = useQuery({ queryKey: ['locations', undefined], queryFn: () => api.locations() });
  const [moveTo, setMoveTo] = useState<number | null>(null);
  const [addTo, setAddTo] = useState<number | null>(null);
  const [pickerTarget, setPickerTarget] = useState<PickerTarget | null>(null);
  const [condition, setCondition] = useState('NM');
  const [addUrl, setAddUrl] = useState('');
  const [addCardOpen, setAddCardOpen] = useState(false);
  const [printError, setPrintError] = useState<Error | null>(null);
  const [updateOpen, setUpdateOpen] = useState(false);
  const [substitutesOpen, setSubstitutesOpen] = useState(false);
  const [ignoredOpen, setIgnoredOpen] = useState(false);
  const [exportScope, setExportScope] = useState<ListExportScope | null>(null);
  const openExport = (scope: ListExportScope) => setExportScope(scope);
  const setLanguage = useMutation({
    mutationFn: (language: string | null) => api.listSetLanguage(list.id, language),
    onSuccess: () => invalidate(),
  });

  const invalidate = () => {
    qc.invalidateQueries({ queryKey: ['list-items', list.id] });
    qc.invalidateQueries({ queryKey: ['lists'] });
  };

  const importUrl = useMutation({
    mutationFn: () => api.listImportUrl(addUrl.trim(), list.game, list.id),
    onSuccess: (r) => {
      setAddUrl('');
      invalidate();
      return r;
    },
  });

  const removeItem = useMutation({ mutationFn: (itemId: number) => api.listRemoveItem(itemId), onSuccess: invalidate });
  const setQty = useMutation({
    mutationFn: ({ itemId, quantity }: { itemId: number; quantity: number }) =>
      api.listSetItemQuantity(itemId, quantity),
    onSuccess: invalidate,
  });
  const refreshPrices = useMutation({ mutationFn: () => api.listRefreshPrices(list.id), onSuccess: invalidate });
  const fulfill = useMutation({
    mutationFn: (what: { move: boolean; add: boolean }) =>
      api.listFulfill(list.id, {
        moveToContainerId: what.move ? moveTo : null,
        addToContainerId: what.add ? addTo : null,
        condition,
      }),
    onSuccess: (r) => {
      qc.invalidateQueries({ queryKey: ['lists'] });
      qc.invalidateQueries({ queryKey: ['collection'] });
      qc.invalidateQueries({ queryKey: ['locations'] });
      if (r.listDeleted) {
        onDeleted(t('lists.detail.fulfill.listDone', { name: list.name, moved: r.moved, added: r.added }));
      } else {
        qc.invalidateQueries({ queryKey: ['list-items', list.id] });
      }
    },
  });

  if (items.isLoading || !items.data) return <CircularProgress />;

  // Market-value roll-ups: the whole list, and the copies the collection doesn't cover ("to buy").
  // Unpriced items contribute nothing (matching the "—" shown per row).
  const price = (it: (typeof items.data)[number]) => (it.isUnpriced ? 0 : (it.marketPrice ?? 0));
  const totalValue = items.data.reduce((sum, it) => sum + price(it) * it.quantity, 0);
  const missingValue = items.data.reduce((sum, it) => sum + price(it) * missingQty(it), 0);
  const ownedCount = items.data.reduce((sum, it) => sum + ownedPart(it), 0);
  const missingCount = items.data.reduce((sum, it) => sum + missingQty(it), 0);

  const locationName = (id: number | null) =>
    id == null ? t('lists.detail.fulfill.chooseLocation') : (locations.data?.find((l) => l.id === id)?.name ?? `#${id}`);

  const onPick = (id: number) => {
    // Picking one side pre-fills the other when it's still empty: usually both go to the same place.
    if (pickerTarget === 'move') {
      setMoveTo(id);
      if (addTo == null) setAddTo(id);
    } else {
      setAddTo(id);
      if (moveTo == null) setMoveTo(id);
    }
    setPickerTarget(null);
  };

  const canMove = moveTo != null && ownedCount > 0;
  const canAdd = addTo != null && missingCount > 0;

  return (
    <Paper variant="outlined" sx={{ p: 2 }}>
      <Stack direction="row" spacing={2} alignItems="center" sx={{ mb: 1.5 }} flexWrap="wrap" useFlexGap>
        <Typography variant="h6">{list.name}</Typography>
        <Box sx={{ flexGrow: 1 }} />
        <LanguageSelect
          game={list.game}
          allowAuto
          emptyLabel={t('lists.anyLanguage')}
          label={t('lists.detail.languageLabel')}
          value={list.language ?? ''}
          onChange={(language) => setLanguage.mutate(language || null)}
          helperText={t('lists.detail.languageHelp')}
        />
      </Stack>
      {setLanguage.error && <Alert severity="error">{(setLanguage.error as Error).message}</Alert>}

      <Stack direction="row" spacing={1} alignItems="center" sx={{ mb: 1 }} flexWrap="wrap" useFlexGap>
        <Button size="small" variant="outlined" startIcon={<AddIcon />} onClick={() => setAddCardOpen(true)}>
          {t('lists.detail.addCard')}
        </Button>
        <Tooltip title={list.sourceUrl ?? t('lists.detail.updateFromUrlNoSource')}>
          <Button size="small" startIcon={<SyncIcon />} onClick={() => setUpdateOpen(true)}>
            {t('lists.detail.updateFromUrl')}
          </Button>
        </Tooltip>
        <Button
          size="small"
          startIcon={<ManageSearchIcon />}
          disabled={items.data.length === 0}
          onClick={() => setSubstitutesOpen(true)}
        >
          {t('lists.detail.findInCollection')}
        </Button>
        <Button
          size="small"
          startIcon={<RefreshIcon />}
          disabled={refreshPrices.isPending || items.data.length === 0}
          onClick={() => refreshPrices.mutate()}
        >
          {t('lists.detail.refreshPrices')}
        </Button>
        <Button
          size="small"
          startIcon={<IosShareIcon />}
          disabled={items.data.length === 0}
          onClick={() => openExport('all')}
        >
          {t('lists.detail.export')}
        </Button>
        <PrintMenu
          listId={list.id}
          disabled={items.data.length === 0}
          onError={(e) => setPrintError(e)}
        />
      </Stack>
      {printError && (
        <Alert severity="error" sx={{ mb: 1 }} onClose={() => setPrintError(null)}>
          {printError.message}
        </Alert>
      )}

      <Stack direction="row" spacing={1} alignItems="center" sx={{ mb: 1 }} flexWrap="wrap" useFlexGap>
        <TextField
          size="small"
          label={t('lists.detail.addFromUrlLabel')}
          placeholder="https://moxfield.com/decks/…"
          value={addUrl}
          onChange={(e) => setAddUrl(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter' && addUrl.trim() && !importUrl.isPending) importUrl.mutate();
          }}
          sx={{ flexGrow: 1, minWidth: 260 }}
        />
        <Button
          startIcon={<DownloadIcon />}
          disabled={!addUrl.trim() || importUrl.isPending}
          onClick={() => importUrl.mutate()}
        >
          {importUrl.isPending ? t('lists.importing') : t('common.actions.add')}
        </Button>
      </Stack>
      {importUrl.error && <Alert severity="error">{(importUrl.error as Error).message}</Alert>}
      {importUrl.data && (
        <Alert severity={importUrl.data.unresolvedNames.length ? 'warning' : 'success'} sx={{ mb: 1 }}>
          {t('lists.detail.added', { count: importUrl.data.addedCount })}
          {importUrl.data.unresolvedNames.length > 0 &&
            t('lists.couldNotMatch', { names: importUrl.data.unresolvedNames.join(', ') })}
        </Alert>
      )}

      {items.data.length > 0 && (
        <Box sx={{ mb: 1, p: 1.5, borderRadius: 1, border: 1, borderColor: 'divider' }}>
          <Typography variant="subtitle2" gutterBottom>
            {t('lists.detail.fulfill.title')}
          </Typography>
          <Stack spacing={1}>
            <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
              <Typography variant="body2" sx={{ minWidth: 190 }}>
                {t('lists.detail.fulfill.ownedTo', { count: ownedCount })}
              </Typography>
              <Button
                size="small"
                variant="outlined"
                startIcon={<PlaceIcon />}
                disabled={ownedCount === 0}
                onClick={() => setPickerTarget('move')}
              >
                {locationName(moveTo)}
              </Button>
            </Stack>
            <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
              <Typography variant="body2" sx={{ minWidth: 190 }}>
                {t('lists.detail.fulfill.newTo', { count: missingCount })}
              </Typography>
              <Button
                size="small"
                variant="outlined"
                startIcon={<PlaceIcon />}
                disabled={missingCount === 0}
                onClick={() => setPickerTarget('add')}
              >
                {locationName(addTo)}
              </Button>
              <TextField
                select
                size="small"
                label={t('lists.detail.conditionLabel')}
                value={condition}
                disabled={missingCount === 0}
                onChange={(e) => setCondition(e.target.value)}
                sx={{ width: 90 }}
              >
                {CONDITIONS.map((c) => (
                  <MenuItem key={c} value={c}>
                    {t(`common.conditions.${c}`)}
                  </MenuItem>
                ))}
              </TextField>
            </Stack>
            <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
              <Button
                size="small"
                disabled={!canMove || fulfill.isPending}
                onClick={() => fulfill.mutate({ move: true, add: false })}
              >
                {t('lists.detail.fulfill.move')}
              </Button>
              <Button
                size="small"
                disabled={!canAdd || fulfill.isPending}
                onClick={() => fulfill.mutate({ move: false, add: true })}
              >
                {t('lists.detail.fulfill.add')}
              </Button>
              <Button
                size="small"
                variant="contained"
                disabled={!canMove || !canAdd || fulfill.isPending}
                onClick={() => fulfill.mutate({ move: true, add: true })}
              >
                {fulfill.isPending ? t('lists.detail.fulfill.working') : t('lists.detail.fulfill.moveAndAdd')}
              </Button>
              <Typography variant="caption" color="text.secondary" sx={{ flexBasis: '100%' }}>
                {t('lists.detail.fulfill.hint')}{' '}
                <Link component="button" variant="caption" onClick={() => setIgnoredOpen(true)}>
                  {t('lists.detail.ignoredLocations')}
                </Link>
              </Typography>
            </Stack>
          </Stack>
          {fulfill.error && (
            <Alert severity="error" sx={{ mt: 1 }}>
              {(fulfill.error as Error).message}
            </Alert>
          )}
          {fulfill.data && !fulfill.data.listDeleted && (
            <Alert severity="success" sx={{ mt: 1 }} onClose={() => fulfill.reset()}>
              {t('lists.detail.fulfill.done', { moved: fulfill.data.moved, added: fulfill.data.added })}
              {!!fulfill.data.ruleTagged &&
                ` ${t('common.tagRules.tagged', {
                  count: fulfill.data.ruleTagged,
                  formatted: fmt.number(fulfill.data.ruleTagged),
                })}`}
            </Alert>
          )}
        </Box>
      )}

      {items.data.length > 0 && (
        <Stack
          direction="row"
          spacing={2}
          flexWrap="wrap"
          useFlexGap
          sx={{ mb: 1, px: 1, py: 0.75, borderRadius: 1, bgcolor: 'action.hover' }}
        >
          <Typography variant="body2">
            {t('lists.detail.totalValue')}{' '}
            <Box component="span" sx={{ fontWeight: 600 }}>{fmt.money(totalValue)}</Box>
          </Typography>
          <Typography variant="body2" color="text.secondary">
            {t('lists.detail.missingValue')}{' '}
            <Box component="span" sx={{ fontWeight: 600 }}>{fmt.money(missingValue)}</Box>
          </Typography>
        </Stack>
      )}

      <Divider sx={{ mb: 1 }} />

      <AddCardToListDialog
        open={addCardOpen}
        listId={list.id}
        defaultGame={list.game}
        language={list.language}
        onClose={() => setAddCardOpen(false)}
        onDone={invalidate}
      />

      <ListExportDialog
        open={exportScope !== null}
        list={list}
        initialScope={exportScope ?? 'all'}
        onClose={() => setExportScope(null)}
      />
      <IgnoredLocationsDialog open={ignoredOpen} onClose={() => setIgnoredOpen(false)} onSaved={invalidate} />
      <ListUpdateDialog open={updateOpen} list={list} onClose={() => setUpdateOpen(false)} onApplied={invalidate} />
      <ListSubstitutesDialog
        open={substitutesOpen}
        list={list}
        onClose={() => setSubstitutesOpen(false)}
        onApplied={invalidate}
      />

      <LocationPickerDialog
        open={pickerTarget !== null}
        title={
          pickerTarget === 'add' ? t('lists.detail.fulfill.pickNewTitle') : t('lists.detail.fulfill.pickOwnedTitle')
        }
        cardGames={[list.game]}
        onPick={onPick}
        onClose={() => setPickerTarget(null)}
      />

      {items.data.length === 0 ? (
        <Typography color="text.secondary" variant="body2">
          {t('lists.detail.empty')}
        </Typography>
      ) : (
        <>
          <ListItemsSection
            section="owned"
            list={list}
            rows={items.data.filter((it) => ownedPart(it) > 0)}
            portion={ownedPart}
            onExport={() => openExport('owned')}
            onSetQuantity={(itemId, quantity) => setQty.mutate({ itemId, quantity })}
            onRemove={(itemId) => removeItem.mutate(itemId)}
          />
          <ListItemsSection
            section="buy"
            list={list}
            rows={items.data.filter((it) => missingQty(it) > 0)}
            portion={missingQty}
            onExport={() => openExport('buy')}
            onSetQuantity={(itemId, quantity) => setQty.mutate({ itemId, quantity })}
            onRemove={(itemId) => removeItem.mutate(itemId)}
          />
        </>
      )}
    </Paper>
  );
}

export function ListsPage() {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const { game: contextGame } = useGame();
  const [game, setGame] = useState(contextGame ?? 'Mtg');
  const [newName, setNewName] = useState('');
  const [importUrl, setImportUrl] = useState('');
  const [importLanguage, setImportLanguage] = useState('');
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [doneMessage, setDoneMessage] = useState<string | null>(null);

  const games = useQuery({ queryKey: ['games'], queryFn: api.games });
  const lists = useQuery({ queryKey: ['lists', game], queryFn: () => api.lists(game) });

  const create = useMutation({
    mutationFn: () => api.listCreate(newName.trim(), game),
    onSuccess: (l) => {
      setNewName('');
      qc.invalidateQueries({ queryKey: ['lists'] });
      setSelectedId(l.id);
    },
  });
  const importNew = useMutation({
    mutationFn: () => api.listImportUrl(importUrl.trim(), game, undefined, importLanguage),
    onSuccess: (r) => {
      setImportUrl('');
      qc.invalidateQueries({ queryKey: ['lists'] });
      setSelectedId(r.listId);
    },
  });
  const rename = useMutation({
    mutationFn: ({ id, name }: { id: number; name: string }) => api.listRename(id, name),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['lists'] }),
  });
  const del = useMutation({
    mutationFn: (id: number) => api.listDelete(id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['lists'] });
      setSelectedId(null);
    },
  });

  const selected = lists.data?.find((l) => l.id === selectedId) ?? null;

  return (
    <Stack spacing={2}>
      <Typography variant="h4">{t('lists.title')}</Typography>

      <Paper variant="outlined" sx={{ p: 2 }}>
        <Stack direction="row" spacing={2} alignItems="center" flexWrap="wrap" useFlexGap>
          <TextField
            select
            size="small"
            label={t('common.labels.game')}
            value={game}
            onChange={(e) => {
              setGame(e.target.value);
              setSelectedId(null);
            }}
            sx={{ minWidth: 180 }}
          >
            {games.data?.map((g) => (
              <MenuItem key={g.id} value={g.id}>
                {g.displayName}
              </MenuItem>
            ))}
          </TextField>
          <Box sx={{ flexGrow: 1 }} />
          <TextField
            size="small"
            label={t('lists.newListName')}
            value={newName}
            onChange={(e) => setNewName(e.target.value)}
          />
          <Button
            variant="contained"
            startIcon={<AddIcon />}
            disabled={!newName.trim() || create.isPending}
            onClick={() => create.mutate()}
          >
            {t('common.actions.create')}
          </Button>
        </Stack>

        <Divider sx={{ my: 2 }}>{t('lists.orImportFromUrl')}</Divider>

        <Stack direction="row" spacing={2} alignItems="center" flexWrap="wrap" useFlexGap>
          <TextField
            size="small"
            label={t('lists.deckUrlLabel')}
            placeholder="https://moxfield.com/decks/…"
            value={importUrl}
            onChange={(e) => setImportUrl(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && importUrl.trim() && !importNew.isPending) importNew.mutate();
            }}
            sx={{ flexGrow: 1, minWidth: 280 }}
          />
          <LanguageSelect
            game={game}
            allowAuto
            emptyLabel={t('lists.anyLanguage')}
            value={importLanguage}
            onChange={setImportLanguage}
          />
          <Button
            variant="outlined"
            startIcon={<DownloadIcon />}
            disabled={!importUrl.trim() || importNew.isPending}
            onClick={() => importNew.mutate()}
          >
            {importNew.isPending ? t('lists.importing') : t('lists.importAsNewList')}
          </Button>
        </Stack>
        {importNew.error && (
          <Alert severity="error" sx={{ mt: 1 }}>
            {(importNew.error as Error).message}
          </Alert>
        )}
        {importNew.data && (
          <Alert
            severity={importNew.data.unresolvedNames.length ? 'warning' : 'success'}
            sx={{ mt: 1 }}
          >
            {t('lists.imported', {
              name: importNew.data.listName,
              count: importNew.data.addedCount,
            })}
            {importNew.data.unresolvedNames.length > 0 &&
              t('lists.couldNotMatch', { names: importNew.data.unresolvedNames.join(', ') })}
          </Alert>
        )}
      </Paper>

      {doneMessage && (
        <Alert severity="success" onClose={() => setDoneMessage(null)}>
          {doneMessage}
        </Alert>
      )}

      {lists.isLoading || !lists.data ? (
        <CircularProgress />
      ) : lists.data.length === 0 ? (
        <Typography color="text.secondary">{t('lists.noListsYet')}</Typography>
      ) : (
        <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
          {lists.data.map((l) => (
            <Chip
              key={l.id}
              label={t('lists.chipLabel', { name: l.name, count: l.itemCount })}
              color={l.id === selectedId ? 'primary' : 'default'}
              onClick={() => setSelectedId(l.id)}
              onDelete={() => {
                if (confirm(t('lists.confirmDelete', { name: l.name }))) del.mutate(l.id);
              }}
              deleteIcon={<DeleteIcon />}
            />
          ))}
        </Stack>
      )}

      {selected && (
        <>
          <Stack direction="row" spacing={1}>
            <Button
              size="small"
              startIcon={<EditIcon />}
              onClick={() => {
                const name = prompt(t('lists.renamePrompt'), selected.name);
                if (name?.trim()) rename.mutate({ id: selected.id, name: name.trim() });
              }}
            >
              {t('common.actions.rename')}
            </Button>
          </Stack>
          <ListDetail
            key={selected.id}
            list={selected}
            onDeleted={(message) => {
              setSelectedId(null);
              setDoneMessage(message);
            }}
          />
        </>
      )}
    </Stack>
  );
}
