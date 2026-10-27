import { useEffect, useMemo, useRef, useState, type KeyboardEvent, type MouseEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Autocomplete,
  Box,
  Button,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Divider,
  FormControlLabel,
  IconButton,
  InputAdornment,
  List,
  ListItemButton,
  MenuItem,
  Stack,
  Switch,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material';
import SearchIcon from '@mui/icons-material/Search';
import UndoIcon from '@mui/icons-material/Undo';
import CollectionsBookmarkIcon from '@mui/icons-material/CollectionsBookmark';
import TravelExploreIcon from '@mui/icons-material/TravelExplore';
import { api } from '../../api/client';
import type { CardDto, CardListItemDto, ScanSearchResultDto } from '../../api/types';
import { usePermissions } from '../../context/usePermissions';
import { CardHoverPreview, type CardHover } from '../CardHoverPreview';
import {
  ENGLISH,
  LanguageChip,
  LanguageFilterSelect,
  LanguageSelect,
  languageName,
  useCatalogLanguages,
  useRememberedLanguageFilter,
} from '../../lib/cardLanguages';
import { LONG_PRESS_SX, useLongPress } from '../../lib/responsive';

const CONDITIONS = ['NM', 'LP', 'MP', 'HP', 'DMG'];
/** How many of this sitting's adds the dialog lists back. */
const RECENT_LIMIT = 5;

/** Where the dialog adds cards — each target decides which results and fields it offers. */
export type AddCardTarget =
  /** New copies into a location (catalog only). `lockGame` fixes the game — a game-locked deck box, so a
   * card of another game can't be added (the server hard-blocks it too). */
  | { kind: 'location'; locationId: number; locationName: string; lockGame?: boolean }
  /** One card into a binder pocket: an owned copy from elsewhere is moved in, a catalog card is created
   * there. A pocket holds one card, so the dialog closes after a successful add. */
  | { kind: 'pocket'; containerId: number; page: number; slot: number; occupantName?: string | null }
  /** A list line: an owned copy is referenced (moved only when the list is put away), a catalog printing
   * is recorded as wholly new. `language` is the list's forced card language, which the search starts on. */
  | { kind: 'list'; listId: number; language?: string | null };

/** How to take one add back: delete the new lot, or take the added copies off a (possibly merged) list line. */
type Undo = { kind: 'lot'; lotId: number } | { kind: 'listItem'; itemId: number; quantity: number };

type RecentAdd = {
  key: number;
  quantity: number;
  name: string;
  setCode: string;
  collectorNumber: string;
  /** Already-localized extras (condition, language, finish) shown after the printing. */
  details: string[];
  undo: Undo | null;
};

type AddRequest = {
  close: boolean;
  summary: Omit<RecentAdd, 'key' | 'undo'>;
  run: () => Promise<Undo | null>;
};

type ResultRow = { kind: 'owned'; card: CardDto } | { kind: 'catalog'; result: ScanSearchResultDto };

const rowId = (i: number) => `add-card-result-${i}`;

/**
 * The one "add a card" dialog, for a location, a binder pocket or a list (see {@link AddCardTarget}).
 * Search the game's catalog by name and/or set code + collector number, optionally limited to some
 * languages (remembered per game; a list with a language starts on it). Pockets and lists also show
 * matching copies already in your collection. Pick a printing — hover (or long-press) a row for the art,
 * ↑/↓ + Enter from the search boxes — then set condition/language/foil (+ finish)/quantity/price as the
 * target needs, and add. Locations and lists stay open after "Add" (with the search kept, a running count
 * and the last few adds, each undoable); "Add & close" finishes. Catalog adds go through /api/scan/commit,
 * /api/binder/card/add-missing or /api/lists/{id}/items.
 */
export function AddCardDialog({
  open,
  target,
  defaultGame,
  onClose,
  onDone,
}: {
  open: boolean;
  target: AddCardTarget;
  defaultGame?: string;
  onClose: () => void;
  /** Called after each add (and undo) so the caller can refresh. */
  onDone?: () => void;
}) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const { can } = usePermissions();
  const games = useQuery({ queryKey: ['games'], queryFn: api.games, enabled: open });
  const startGame = defaultGame ?? 'Mtg';
  const multiAdd = target.kind !== 'pocket';
  const showOwned = target.kind !== 'location';
  const lockGame = target.kind === 'location' && !!target.lockGame;
  const listLanguage = target.kind === 'list' ? target.language || null : null;

  const [game, setGame] = useState(startGame);
  const [name, setName] = useState('');
  const [set, setSet] = useState('');
  const [cn, setCn] = useState('');
  const [selected, setSelected] = useState<ScanSearchResultDto | null>(null);
  const [condition, setCondition] = useState('NM');
  const [language, setLanguage] = useState(ENGLISH);
  const [isFoil, setIsFoil] = useState(false);
  const [foilType, setFoilType] = useState<string | null>(null);
  const [quantity, setQuantity] = useState(1);
  const [purchasePrice, setPurchasePrice] = useState('');
  const [addedCount, setAddedCount] = useState(0);
  const [recent, setRecent] = useState<RecentAdd[]>([]);
  const [hover, setHover] = useState<CardHover | null>(null);
  const [activeIndex, setActiveIndex] = useState(-1);
  // A list with a language starts its filter on that language, without touching the remembered choice.
  const [filterOverride, setFilterOverride] = useState<string[] | null>(null);
  const longPress = useLongPress();
  const nameRef = useRef<HTMLInputElement>(null);
  const recentKey = useRef(0);
  // Last known quantity of each list line this sitting touched (adds of the same printing merge).
  const listQuantities = useRef(new Map<number, number>());

  // Start each sitting fresh, on the caller's game.
  const [wasOpen, setWasOpen] = useState(false);
  if (open && !wasOpen) {
    setWasOpen(true);
    setGame(startGame);
    setName('');
    setSet('');
    setCn('');
    setSelected(null);
    setCondition('NM');
    setIsFoil(false);
    setFoilType(null);
    setQuantity(1);
    setPurchasePrice('');
    setAddedCount(0);
    setRecent([]);
    setActiveIndex(-1);
    setFilterOverride(listLanguage ? [listLanguage] : null);
    listQuantities.current.clear();
  }
  if (!open && wasOpen) setWasOpen(false);

  const catalogLanguages = useCatalogLanguages(game);
  const forcedLanguage = listLanguage && game === startGame ? listLanguage : null;
  const filterLanguages =
    forcedLanguage && !catalogLanguages.includes(forcedLanguage) ? [...catalogLanguages, forcedLanguage] : catalogLanguages;
  const multiLanguage = filterLanguages.length > 1;
  const [rememberedFilter, setRememberedFilter] = useRememberedLanguageFilter('addCard', game, filterLanguages);
  const languageFilter = filterOverride ? filterOverride.filter((l) => filterLanguages.includes(l)) : rememberedFilter;
  const setLanguageFilter = filterOverride ? setFilterOverride : setRememberedFilter;
  const filterKey = languageFilter.join(',');

  const foilTypes = useQuery({
    queryKey: ['scan-foil-types', game],
    queryFn: () => api.scanFoilTypes(game),
    enabled: open,
  });

  // Compose the same set:/cn: token grammar both collection and catalog search understand.
  const composed = useMemo(() => {
    const parts: string[] = [];
    if (name.trim()) parts.push(name.trim());
    if (set.trim()) parts.push(`set:${set.trim()}`);
    if (cn.trim()) parts.push(`cn:${cn.trim()}`);
    return parts.join(' ');
  }, [name, set, cn]);

  // Debounce so each keystroke doesn't hit the server.
  const [debounced, setDebounced] = useState('');
  useEffect(() => {
    const h = setTimeout(() => setDebounced(composed), 250);
    return () => clearTimeout(h);
  }, [composed]);

  const canSearch = open && !selected && debounced.trim().length >= 2;

  // Owned copies are searched in their own language (lang: on the collection is the copy's language).
  const ownedLanguageQuery =
    languageFilter.length === 0
      ? ''
      : languageFilter.length === 1
        ? ` lang:${languageFilter[0]}`
        : ` (${languageFilter.map((l) => `lang:${l}`).join(' or ')})`;
  const owned = useQuery({
    queryKey: ['collection', 'add-card', target.kind, game, debounced, ownedLanguageQuery],
    queryFn: () => api.collection({ game, q: debounced + ownedLanguageQuery, take: 25 }),
    enabled: canSearch && showOwned,
  });
  // A pocket only offers copies that live somewhere *else* — a lot already in this binder has nothing to
  // relocate (place unplaced-pool copies by dragging them from the sidebar instead).
  const ownedItems = (owned.data?.items ?? []).filter(
    (c) => target.kind !== 'pocket' || c.containerId !== target.containerId,
  );
  const catalog = useQuery({
    queryKey: ['catalog-search', game, debounced, languageFilter],
    queryFn: () => api.scanSearch(game, debounced, undefined, undefined, languageFilter),
    enabled: canSearch,
  });

  // Every row the keyboard can reach, in display order (collection first, then catalog).
  const rows: ResultRow[] = canSearch
    ? [
        ...(showOwned && !owned.isFetching ? ownedItems.map((card) => ({ kind: 'owned' as const, card })) : []),
        ...(!catalog.isFetching ? (catalog.data ?? []).map((result) => ({ kind: 'catalog' as const, result })) : []),
      ]
    : [];
  const rowImage = (row: ResultRow) => (row.kind === 'owned' ? row.card.imageUri : row.result.imageUri);

  // A new search starts unhighlighted.
  useEffect(() => setActiveIndex(-1), [debounced, filterKey, game]);
  // Keep the highlighted row in view and show its art, as hovering would.
  useEffect(() => {
    if (activeIndex < 0 || selected) return;
    const el = document.getElementById(rowId(activeIndex));
    el?.scrollIntoView({ block: 'nearest' });
    const row = rows[activeIndex];
    const url = row && rowImage(row);
    setHover(el && url ? { el, url, foil: row.kind === 'owned' && row.card.isFoil } : null);
    // rows is derived each render; the index (and the selection hiding the list) is what matters.
  }, [activeIndex, selected]);

  const invalidate = () => {
    if (target.kind === 'list') {
      qc.invalidateQueries({ queryKey: ['list-items', target.listId] });
      qc.invalidateQueries({ queryKey: ['lists'] });
      return;
    }
    if (target.kind === 'location') qc.invalidateQueries({ queryKey: ['location', target.locationId] });
    qc.invalidateQueries({ queryKey: ['collection'] });
    qc.invalidateQueries({ queryKey: ['location-cards'] });
    qc.invalidateQueries({ queryKey: ['locations'] });
    qc.invalidateQueries({ queryKey: ['dashboard'] });
  };

  const trackListItem = (item: CardListItemDto, added: number): Undo => {
    listQuantities.current.set(item.id, item.quantity);
    return { kind: 'listItem', itemId: item.id, quantity: added };
  };

  const add = useMutation({
    mutationFn: (r: AddRequest) => r.run(),
    onSuccess: (undo, r) => {
      invalidate();
      onDone?.();
      if (r.close) {
        onClose();
        return;
      }
      setAddedCount((n) => n + r.summary.quantity);
      setRecent((list) => [{ ...r.summary, key: ++recentKey.current, undo }, ...list].slice(0, RECENT_LIMIT));
      // Ready the next card: keep the search (another printing/language is one click or Enter away — or
      // just type over the selected name) and game/condition/foil so a batch stays fast.
      setSelected(null);
      setQuantity(1);
      setPurchasePrice('');
      requestAnimationFrame(() => {
        nameRef.current?.focus();
        nameRef.current?.select();
      });
    },
  });

  const undo = useMutation({
    mutationFn: async (r: RecentAdd) => {
      const u = r.undo!;
      if (u.kind === 'lot') {
        await api.cardDelete(u.lotId);
        return;
      }
      const left = (listQuantities.current.get(u.itemId) ?? u.quantity) - u.quantity;
      if (left < 1) {
        await api.listRemoveItem(u.itemId);
        listQuantities.current.delete(u.itemId);
      } else {
        await api.listSetItemQuantity(u.itemId, left);
        listQuantities.current.set(u.itemId, left);
      }
    },
    onSuccess: (_, r) => {
      setRecent((list) => list.filter((x) => x.key !== r.key));
      setAddedCount((n) => Math.max(0, n - r.quantity));
      invalidate();
      onDone?.();
    },
  });
  // Deleting a location's new copy needs the delete permission; a list line only needs list editing,
  // which adding already required.
  const canUndo = target.kind === 'list' || can('collection.delete');
  const busy = add.isPending || undo.isPending;

  const pick = (r: ScanSearchResultDto) => {
    setHover(null);
    setSelected(r);
    // The copy defaults to the printing's language; it can still be changed (e.g. a French copy of a
    // card whose catalog only has the English row).
    setLanguage(r.language || ENGLISH);
  };

  const finishLabel = (foil: boolean, type?: string | null) => (foil ? type || t('common.labels.foil') : null);

  /** Pocket: move this copy into the pocket. List: reference it on the list. */
  const addOwned = (card: CardDto) => {
    if (busy || target.kind === 'location') return;
    setHover(null);
    add.mutate({
      close: target.kind === 'pocket',
      summary: {
        quantity: 1,
        name: card.name,
        setCode: card.setCode,
        collectorNumber: card.number,
        details: [
          t(`common.conditions.${card.condition}`, { defaultValue: card.condition }),
          languageName(t, card.language || ENGLISH),
          finishLabel(card.isFoil, card.foilType),
          card.containerName ?? t('dialogs.addCard.unfiled'),
        ].filter((d): d is string => !!d),
      },
      run: async () => {
        if (target.kind === 'pocket') {
          await api.binderPlaceOwned(card.id, target.containerId, target.page, target.slot);
          return null;
        }
        if (target.kind === 'list') return trackListItem(await api.listAddFromCollection(target.listId, card.id, 1), 1);
        return null;
      },
    });
  };

  const submit = (close: boolean) => {
    if (!selected || busy) return;
    const s = selected;
    const qty = target.kind === 'pocket' ? 1 : Math.max(1, quantity);
    // Blank ⇒ the server applies the game's basic foil finish.
    const finish = isFoil ? foilType?.trim() || null : null;
    const price = purchasePrice === '' ? null : Number(purchasePrice);
    const details =
      target.kind === 'list'
        ? [s.language ? languageName(t, s.language) : null, finishLabel(isFoil, finish)]
        : [t(`common.conditions.${condition}`), languageName(t, language), finishLabel(isFoil, finish)];
    add.mutate({
      close: close || !multiAdd,
      summary: {
        quantity: qty,
        name: s.name,
        setCode: s.setCode,
        collectorNumber: s.collectorNumber,
        details: details.filter((d): d is string => !!d),
      },
      run: async () => {
        const printing = {
          name: s.name,
          setCode: s.setCode,
          setName: s.setName,
          collectorNumber: s.collectorNumber,
          rarity: s.rarity,
          imageUri: s.imageUri,
          isFoil,
          foilType: finish,
        };
        switch (target.kind) {
          case 'location': {
            const res = await api.scanCommit(
              target.locationId,
              [{ ...printing, game, gameCardId: s.gameCardId, condition, language, quantity: qty, purchasePrice: price }],
              true,
            );
            const lotId = res.lotIds?.[0];
            return lotId ? { kind: 'lot', lotId } : null;
          }
          case 'pocket':
            await api.binderAddMissing({
              ...printing,
              containerId: target.containerId,
              page: target.page,
              slot: target.slot,
              game,
              gameSpecificId: s.gameCardId,
              condition,
              language,
              purchasePrice: price,
            });
            return null;
          case 'list':
            return trackListItem(await api.listAddItem(target.listId, { ...printing, gameCardId: s.gameCardId, quantity: qty }), qty);
        }
      },
    });
  };

  const choose = (row: ResultRow) => (row.kind === 'owned' ? addOwned(row.card) : pick(row.result));

  // From the search boxes: ↑/↓ move through the results, Enter picks the highlighted one (or the only
  // one) — and, once a printing is picked, adds it.
  const onSearchKeyDown = (e: KeyboardEvent) => {
    if (selected) {
      if (e.key === 'Enter') {
        e.preventDefault();
        submit(false);
      }
      return;
    }
    if (rows.length === 0) return;
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      setActiveIndex((i) => Math.min(rows.length - 1, i + 1));
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setActiveIndex((i) => Math.max(-1, i - 1));
    } else if (e.key === 'Enter' && composed === debounced) {
      const i = activeIndex >= 0 ? activeIndex : rows.length === 1 ? 0 : -1;
      if (i >= 0 && rows[i]) {
        e.preventDefault();
        choose(rows[i]);
      }
    }
  };
  // Editing the search means picking again.
  const editSearch = (setter: (v: string) => void) => (v: string) => {
    setter(v);
    setSelected(null);
  };

  const gameName = games.data?.find((g) => g.id === game)?.displayName ?? game;
  const searchInputProps = {
    role: 'combobox',
    'aria-expanded': rows.length > 0,
    'aria-controls': 'add-card-results',
    'aria-activedescendant': activeIndex >= 0 ? rowId(activeIndex) : undefined,
  };

  const resultRowProps = (index: number, url: string | null | undefined, foil: boolean, onClick: () => void) => ({
    id: rowId(index),
    selected: index === activeIndex,
    onClick,
    onMouseEnter: (e: MouseEvent<HTMLElement>) => url && setHover({ el: e.currentTarget, url, foil }),
    onMouseLeave: () => setHover(null),
    ...longPress((el) => url && setHover({ el, url, foil, touch: true })),
    sx: LONG_PRESS_SX,
  });

  const catalogList =
    catalog.isFetching ? (
      <Typography variant="body2" color="text.secondary">{t('common.states.searching')}</Typography>
    ) : catalog.data && catalog.data.length > 0 ? (
      <List dense disablePadding sx={{ maxHeight: showOwned ? 200 : 260, overflowY: 'auto', border: 1, borderColor: 'divider', borderRadius: 1 }}>
        {catalog.data.map((r, i) => (
          <ListItemButton key={r.gameCardId} {...resultRowProps((showOwned ? ownedItems.length : 0) + i, r.imageUri, false, () => pick(r))}>
            <Stack direction="row" spacing={1} alignItems="center" sx={{ width: '100%' }}>
              {r.imageUri && (
                <Box component="img" src={r.imageUri} alt="" sx={{ width: 32, aspectRatio: '0.72', objectFit: 'contain', flexShrink: 0 }} />
              )}
              <Box sx={{ minWidth: 0, flexGrow: 1 }}>
                <Typography variant="body2" noWrap>{r.name}</Typography>
                <Typography variant="caption" color="text.secondary" noWrap sx={{ display: 'block' }}>
                  {r.setCode.toUpperCase()} · #{r.collectorNumber} · {r.rarity}
                </Typography>
              </Box>
              {/* A multi-language catalog holds one row per language — label every row (English too) so
                  look-alike printings can be told apart. */}
              <LanguageChip language={r.language} showEnglish={multiLanguage} />
            </Stack>
          </ListItemButton>
        ))}
      </List>
    ) : (
      <Typography variant="body2" color="text.secondary">
        {languageFilter.length > 0
          ? t('dialogs.addCard.noCatalogMatchesInLanguages', {
              game: gameName,
              languages: languageFilter.map((l) => languageName(t, l)).join(', '),
            })
          : t('dialogs.addCard.noCatalogMatches', { game: gameName })}
      </Typography>
    );

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>
        {target.kind === 'location' && t('dialogs.addCard.title', { location: target.locationName })}
        {target.kind === 'list' && t('dialogs.addToList.title')}
        {target.kind === 'pocket' && (
          <>
            {t('dialogs.addToSlot.title')}
            <Typography variant="body2" color="text.secondary">
              {t('dialogs.addToSlot.pageSlot', { page: target.page, slot: target.slot + 1 })}
              {target.occupantName ? t('dialogs.addToSlot.replaces', { name: target.occupantName }) : ''}
            </Typography>
          </>
        )}
      </DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
            <TextField
              select
              size="small"
              label={t('common.labels.game')}
              value={game}
              disabled={lockGame}
              helperText={lockGame ? t('dialogs.addCard.lockedHelper') : undefined}
              onChange={(e) => {
                setGame(e.target.value);
                setSelected(null);
                // Finishes are per game, and a list's language only applies to the list's game.
                setFoilType(null);
                setFilterOverride(null);
              }}
              sx={{ width: 220 }}
            >
              {(games.data ?? [{ id: 'Mtg', displayName: 'Magic: The Gathering' }]).map((g) => (
                <MenuItem key={g.id} value={g.id}>
                  {g.displayName}
                </MenuItem>
              ))}
            </TextField>
            {multiLanguage && (
              <LanguageFilterSelect languages={filterLanguages} value={languageFilter} onChange={setLanguageFilter} />
            )}
          </Stack>

          <Stack direction="row" spacing={1} onKeyDown={onSearchKeyDown}>
            <TextField
              size="small"
              label={t('common.labels.name')}
              value={name}
              onChange={(e) => editSearch(setName)(e.target.value)}
              fullWidth
              autoFocus
              inputRef={nameRef}
              slotProps={{
                input: {
                  startAdornment: (
                    <InputAdornment position="start">
                      <SearchIcon fontSize="small" />
                    </InputAdornment>
                  ),
                },
                htmlInput: searchInputProps,
              }}
            />
            <TextField
              size="small"
              label={t('common.labels.set')}
              value={set}
              onChange={(e) => editSearch(setSet)(e.target.value)}
              slotProps={{ htmlInput: searchInputProps }}
              sx={{ width: 120 }}
            />
            <TextField
              size="small"
              label={t('common.labels.collectorNumber')}
              value={cn}
              onChange={(e) => editSearch(setCn)(e.target.value)}
              slotProps={{ htmlInput: searchInputProps }}
              sx={{ width: 120 }}
            />
          </Stack>

          {/* Results — collection first (pockets and lists), then catalog. */}
          {canSearch && (
            <Stack spacing={1.5} id="add-card-results">
              {showOwned && (
                <Box>
                  <Stack direction="row" spacing={1} alignItems="center" sx={{ mb: 0.5 }}>
                    <CollectionsBookmarkIcon fontSize="small" color="action" />
                    <Typography variant="subtitle2">
                      {t('dialogs.addCard.inCollection')} {owned.data ? `(${ownedItems.length})` : ''}
                    </Typography>
                  </Stack>
                  {owned.isFetching ? (
                    <Typography variant="body2" color="text.secondary">{t('common.states.searching')}</Typography>
                  ) : ownedItems.length > 0 ? (
                    <List dense disablePadding sx={{ maxHeight: 200, overflowY: 'auto', border: 1, borderColor: 'divider', borderRadius: 1 }}>
                      {ownedItems.map((c, i) => (
                        <ListItemButton key={c.id} disabled={busy} {...resultRowProps(i, c.imageUri, c.isFoil, () => addOwned(c))}>
                          <Stack direction="row" spacing={1} alignItems="center" sx={{ width: '100%' }}>
                            {c.imageUri && (
                              <Box component="img" src={c.imageUri} alt="" sx={{ width: 32, aspectRatio: '0.72', objectFit: 'contain', flexShrink: 0 }} />
                            )}
                            <Box sx={{ minWidth: 0, flexGrow: 1 }}>
                              <Typography variant="body2" noWrap>{c.name}</Typography>
                              <Typography variant="caption" color="text.secondary" noWrap sx={{ display: 'block' }}>
                                {c.setCode.toUpperCase()} · #{c.number} · {c.condition}
                                {c.isFoil ? ` · ${c.foilType || t('common.labels.foil')}` : ''}
                              </Typography>
                            </Box>
                            <LanguageChip language={c.language} showEnglish={multiLanguage} />
                            <Chip
                              size="small"
                              variant="outlined"
                              label={c.containerName ?? t('dialogs.addCard.unfiled')}
                              sx={{ flexShrink: 0, maxWidth: 130 }}
                            />
                          </Stack>
                        </ListItemButton>
                      ))}
                    </List>
                  ) : (
                    <Typography variant="body2" color="text.secondary">
                      {target.kind === 'pocket' ? t('dialogs.addCard.noCopiesElsewhere') : t('dialogs.addCard.noOwnedCopies')}
                    </Typography>
                  )}
                </Box>
              )}
              {showOwned ? (
                <Box>
                  <Stack direction="row" spacing={1} alignItems="center" sx={{ mb: 0.5 }}>
                    <TravelExploreIcon fontSize="small" color="action" />
                    <Typography variant="subtitle2">
                      {t('dialogs.addCard.inCatalog')} {catalog.data ? `(${catalog.data.length})` : ''}
                    </Typography>
                  </Stack>
                  {catalogList}
                </Box>
              ) : (
                catalogList
              )}
            </Stack>
          )}

          {/* Selected catalog card + the fields this target needs */}
          {selected && (
            <Stack
              spacing={2}
              // Enter adds — unless a picker already used it (opening a select, choosing a foil-type
              // suggestion) or a button has focus (it acts on Enter itself).
              onKeyDown={(e) => {
                if (e.key === 'Enter' && !e.defaultPrevented && !(e.target as HTMLElement).closest('button')) {
                  e.preventDefault();
                  submit(false);
                }
              }}
            >
              <Divider />
              <Stack direction="row" spacing={1} alignItems="center">
                {selected.imageUri && (
                  <Box
                    component="img"
                    src={selected.imageUri}
                    alt=""
                    onMouseEnter={(e) => setHover({ el: e.currentTarget, url: selected.imageUri!, foil: isFoil })}
                    onMouseLeave={() => setHover(null)}
                    {...longPress((el) => setHover({ el, url: selected.imageUri!, foil: isFoil, touch: true }))}
                    sx={{ ...LONG_PRESS_SX, width: 48, aspectRatio: '0.72', objectFit: 'contain' }}
                  />
                )}
                <Box sx={{ flexGrow: 1, minWidth: 0 }}>
                  <Stack direction="row" spacing={1} alignItems="center">
                    <Typography variant="subtitle2" noWrap>{selected.name}</Typography>
                    <LanguageChip language={selected.language} showEnglish={multiLanguage} />
                  </Stack>
                  <Typography variant="caption" color="text.secondary">
                    {selected.setName} · #{selected.collectorNumber} · {selected.rarity}
                  </Typography>
                </Box>
                <Button size="small" onClick={() => setSelected(null)}>{t('common.actions.change')}</Button>
              </Stack>

              <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
                {target.kind !== 'list' && (
                  <>
                    <TextField select size="small" label={t('common.labels.condition')} value={condition} onChange={(e) => setCondition(e.target.value)} sx={{ width: 120 }}>
                      {CONDITIONS.map((c) => (
                        <MenuItem key={c} value={c}>{t(`common.conditions.${c}`)}</MenuItem>
                      ))}
                    </TextField>
                    <LanguageSelect game={game} value={language} onChange={setLanguage} sx={{ width: 150 }} />
                  </>
                )}
                {target.kind !== 'pocket' && (
                  <TextField
                    size="small"
                    label={t('common.labels.quantity')}
                    type="number"
                    value={quantity}
                    onChange={(e) => setQuantity(Math.max(1, Math.floor(Number(e.target.value) || 1)))}
                    slotProps={{ htmlInput: { min: 1, step: 1 } }}
                    sx={{ width: 100 }}
                  />
                )}
                {target.kind !== 'list' && (
                  <TextField
                    size="small"
                    label={t('common.labels.purchasePrice')}
                    type="number"
                    value={purchasePrice}
                    onChange={(e) => setPurchasePrice(e.target.value)}
                    slotProps={{ htmlInput: { min: 0, step: '0.01' } }}
                    sx={{ width: 140 }}
                  />
                )}
              </Stack>
              <Stack direction="row" spacing={2} alignItems="center" flexWrap="wrap" useFlexGap>
                <FormControlLabel
                  control={<Switch checked={isFoil} onChange={(e) => setIsFoil(e.target.checked)} />}
                  label={t('common.labels.foil')}
                />
                {isFoil && (
                  <Autocomplete
                    freeSolo
                    size="small"
                    options={foilTypes.data ?? []}
                    value={foilType ?? ''}
                    onChange={(_, v) => setFoilType(v || null)}
                    onInputChange={(_, v) => setFoilType(v || null)}
                    sx={{ minWidth: 220 }}
                    renderInput={(p) => (
                      <TextField {...p} label={t('common.labels.foilType')} placeholder={foilTypes.data?.[0]} />
                    )}
                  />
                )}
              </Stack>
            </Stack>
          )}

          {(add.error || undo.error) && (
            <Typography color="error" variant="body2">{((add.error ?? undo.error) as Error).message}</Typography>
          )}

          {addedCount > 0 && (
            <Box>
              <Typography variant="body2" color="success.main">
                {target.kind === 'location'
                  ? t('dialogs.addCard.added', { count: addedCount, location: target.locationName })
                  : t('dialogs.addToList.addedSoFar', { count: addedCount })}
              </Typography>
              {recent.map((r) => (
                <Stack key={r.key} direction="row" spacing={0.5} alignItems="center" sx={{ minWidth: 0 }}>
                  <Typography variant="caption" color="text.secondary" noWrap sx={{ flexGrow: 1, minWidth: 0 }}>
                    {[
                      t('dialogs.addCard.recentLine', {
                        quantity: r.quantity,
                        name: r.name,
                        set: r.setCode.toUpperCase(),
                        number: r.collectorNumber,
                      }),
                      ...r.details,
                    ].join(' · ')}
                  </Typography>
                  {r.undo && canUndo && (
                    <Tooltip title={t('dialogs.addCard.undo')}>
                      <span>
                        <IconButton size="small" aria-label={t('dialogs.addCard.undo')} disabled={busy} onClick={() => undo.mutate(r)}>
                          <UndoIcon fontSize="inherit" />
                        </IconButton>
                      </span>
                    </Tooltip>
                  )}
                </Stack>
              ))}
            </Box>
          )}
        </Stack>
        <CardHoverPreview hover={hover} onClose={() => setHover(null)} />
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{multiAdd ? t('common.actions.done') : t('common.actions.cancel')}</Button>
        {multiAdd && (
          <Button disabled={!selected || busy} onClick={() => submit(true)}>
            {t('dialogs.addCard.addAndClose')}
          </Button>
        )}
        <Button variant="contained" disabled={!selected || busy} onClick={() => submit(false)}>
          {add.isPending
            ? t('common.states.adding')
            : target.kind === 'pocket'
              ? t('dialogs.addToSlot.addToPocket')
              : target.kind === 'list'
                ? t('dialogs.addToList.addToList')
                : t('dialogs.addCard.addCard')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
