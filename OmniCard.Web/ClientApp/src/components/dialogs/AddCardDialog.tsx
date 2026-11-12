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
  createFilterOptions,
  type SxProps,
  type Theme,
} from '@mui/material';
import SearchIcon from '@mui/icons-material/Search';
import UndoIcon from '@mui/icons-material/Undo';
import CollectionsBookmarkIcon from '@mui/icons-material/CollectionsBookmark';
import TravelExploreIcon from '@mui/icons-material/TravelExplore';
import { api } from '../../api/client';
import type { BuyListPriority, CardDto, CardListItemDto, ScanSearchResultDto, SetInfoDto } from '../../api/types';
import { usePermissions } from '../../context/usePermissions';
import { CardHoverPreview, type CardHover } from '../CardHoverPreview';
import { Kbd } from '../Kbd';
import { SetMultiSelect } from '../SetMultiSelect';
import {
  ENGLISH,
  LanguageChip,
  LanguageFilterSelect,
  LanguageSelect,
  languageName,
  useCatalogLanguages,
  useRememberedLanguageFilter,
} from '../../lib/cardLanguages';
import { LONG_PRESS_SX, useCanHover, useLongPress } from '../../lib/responsive';

import { SelectField } from '../SelectField';
const CONDITIONS = ['NM', 'LP', 'MP', 'HP', 'DMG'];
/** How many of this sitting's adds the dialog lists back. */
const RECENT_LIMIT = 5;

/** Where the dialog adds cards — each target decides which results and fields it offers. */
export type AddCardTarget =
  /** New copies into a location (catalog only). `lockGame` fixes the game — a location fixed to one game
   * (a deck box, or a binder/box given a game), so a card of another game can't be added (the server
   * hard-blocks it too). */
  | { kind: 'location'; locationId: number; locationName: string; lockGame?: boolean }
  /** One card into a binder pocket: an owned copy from elsewhere is moved in, a catalog card is created
   * there. A pocket holds one card, so the dialog closes after a successful add. */
  | {
      kind: 'pocket';
      containerId: number;
      page: number;
      slot: number;
      occupantName?: string | null;
      /** The binder is fixed to `defaultGame`. */
      lockGame?: boolean;
    }
  /** A list line: an owned copy is referenced (moved only when the list is put away), a catalog printing
   * is recorded as wholly new. `language` is the list's forced card language, which the search starts on. */
  | { kind: 'list'; listId: number; language?: string | null }
  /** A card the owner wants: the picked printing, or any printing of it, on its game's Buy List. */
  | { kind: 'buyList' };

/** How to take one add back: delete the new lot, or take the added copies off a (possibly merged) list line. */
type Undo =
  | { kind: 'lot'; lotId: number }
  | { kind: 'listItem'; itemId: number; quantity: number }
  | { kind: 'buyListEntry'; entryId: number };

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

/** Foil finishes starting with what's typed ("r" → Reverse Holofoil). */
const filterFinishes = createFilterOptions<string>({ matchFrom: 'start' });

/** The keys the simplified dialog lists under its search boxes, as [keys, i18n key of what they do]. */
const KEY_HINTS: [string[], string][] = [
  [['↑', '↓'], 'dialogs.addCard.keys.choose'],
  [['Enter'], 'dialogs.addCard.keys.pickThenAdd'],
  [['Esc'], 'dialogs.addCard.keys.back'],
];
/** Added to the hints when the dialog offers a set filter. */
const SET_KEY_HINTS: [string[], string][] = [
  [['F2'], 'dialogs.addCard.keys.sets'],
  [['Alt', 'A'], 'dialogs.addCard.keys.allSets'],
];

/** A row of key caps, each followed by what it does (an i18n key). */
function KeyHints({ hints, sx }: { hints: [string[], string][]; sx?: SxProps<Theme> }) {
  const { t } = useTranslation();
  return (
    <Stack direction="row" spacing={0.75} alignItems="center" flexWrap="wrap" useFlexGap sx={sx}>
      {hints.map(([keys, label]) => (
        <Stack key={label} direction="row" spacing={0.5} alignItems="center">
          {keys.map((k) => (
            <Kbd key={k}>{k}</Kbd>
          ))}
          <Typography variant="caption" color="text.secondary" sx={{ pr: 0.75 }}>
            {t(label)}
          </Typography>
        </Stack>
      ))}
    </Stack>
  );
}

/** The caller's chosen sets (the binder's set filter), for the game they belong to. */
export type AddCardSetFilter = { game: string; sets: SetInfoDto[] };
/** The caller's foil rule (the binder's): a picked printing matching `query` starts out foil, in `foilType`. */
export type AddCardFoilRule = { query: string; foilType: string | null };

/**
 * The one "add a card" dialog, for a location, a binder pocket, a list or a Buy List (see {@link AddCardTarget}).
 * Search the game's catalog by name and/or set code + collector number — the Name box also takes a
 * collector number on its own ("0019", "LOB-EN019"), whose printings are listed first — optionally limited to some
 * languages (remembered per game; a list with a language starts on it). Pockets and lists also show
 * matching copies already in your collection. Pick a printing — hover (or long-press) a row for the art,
 * ↑/↓ + Enter from the search boxes — then set condition/language/foil (+ finish)/quantity/price as the
 * target needs, and add. Locations and lists stay open after "Add" (with the search kept, a running count
 * and the last few adds, each undoable); "Add & close" finishes. Catalog adds go through /api/scan/commit,
 * /api/binder/card/add-missing or /api/lists/{id}/items.
 *
 * Keyboard: picking a printing moves the keyboard off the search onto the picked card, where Enter adds it
 * (Ctrl+Enter adds and closes), Esc or ↑/↓ go back to the results, and C/L open Condition/Language, F flips
 * Foil (on → its finish box), P/Q go to Purchase price/Quantity. Enter or Esc in one of those returns to the card.
 * `simplified` (the binder's keyboard fill mode) searches the catalog only, highlights the first result as
 * soon as it arrives (an Enter pressed before then picks it) and shows the keys.
 *
 * `setFilter` (the binder's) adds a Sets picker that starts on the caller's sets and limits both searches to
 * them. Changing it only affects this sitting; text in the Set box wins over it; F2 moves between the picker
 * and the Name box, Alt+A searches all sets.
 *
 * `foilRule` (the binder's) checks each picked printing against a search query on the server; a match turns
 * Foil on with the rule's finish. Changing Foil by hand wins, and an add waits for the check to answer.
 */
export function AddCardDialog({
  open,
  target,
  defaultGame,
  simplified = false,
  initialName = '',
  setFilter,
  foilRule,
  onClose,
  onDone,
}: {
  open: boolean;
  target: AddCardTarget;
  defaultGame?: string;
  /** Keyboard-first: catalog results only, first result auto-highlighted, key hints shown. */
  simplified?: boolean;
  /** Starts the Name box with this text (the caller caught the first keystroke). */
  initialName?: string;
  /** Offers a set filter, starting on these sets while the dialog is on their game. */
  setFilter?: AddCardSetFilter;
  /** Printings matching this rule start out foil. */
  foilRule?: AddCardFoilRule;
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
  const showOwned = (target.kind === 'pocket' || target.kind === 'list') && !simplified;
  const buyList = target.kind === 'buyList';
  const lockGame = (target.kind === 'location' || target.kind === 'pocket') && !!target.lockGame;
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
  const [pinPrinting, setPinPrinting] = useState(false);
  const [finishChoice, setFinishChoice] = useState('any');
  const [priority, setPriority] = useState<BuyListPriority>('Normal');
  const [note, setNote] = useState('');
  const [addedCount, setAddedCount] = useState(0);
  const [recent, setRecent] = useState<RecentAdd[]>([]);
  const [hover, setHover] = useState<CardHover | null>(null);
  const [activeIndex, setActiveIndex] = useState(-1);
  // A list with a language starts its filter on that language, without touching the remembered choice.
  const [filterOverride, setFilterOverride] = useState<string[] | null>(null);
  // The sets this search is limited to — the caller's filter to start with, changeable for this sitting.
  const [searchSets, setSearchSets] = useState<SetInfoDto[]>([]);
  const longPress = useLongPress();
  const nameRef = useRef<HTMLInputElement>(null);
  const setsRef = useRef<HTMLInputElement>(null);
  // A picked printing: the panel holds the keyboard (single-key shortcuts), and these are what they reach.
  const pickedRef = useRef<HTMLDivElement>(null);
  const priceRef = useRef<HTMLInputElement>(null);
  const quantityRef = useRef<HTMLInputElement>(null);
  const foilTypeRef = useRef<HTMLInputElement>(null);
  const conditionRef = useRef<HTMLInputElement>(null);
  const languageRef = useRef<HTMLInputElement>(null);
  const canHover = useCanHover();
  // Simplified: an Enter pressed while the search is still running picks the top result once it lands.
  const pendingEnter = useRef(false);
  // The foil rule's verdict on the picked printing ('checking' until the server answers). A Foil change by
  // hand clears it and wins over a late answer; an add asked for while checking waits for it.
  const [ruleFoil, setRuleFoil] = useState<'checking' | 'foil' | 'nonFoil' | null>(null);
  const foilTouched = useRef(false);
  const pickToken = useRef(0);
  const pendingSubmit = useRef<boolean | null>(null);
  const recentKey = useRef(0);
  // Last known quantity of each list line this sitting touched (adds of the same printing merge).
  const listQuantities = useRef(new Map<number, number>());

  // Start each sitting fresh, on the caller's game.
  const [wasOpen, setWasOpen] = useState(false);
  if (open && !wasOpen) {
    setWasOpen(true);
    setGame(startGame);
    setName(initialName);
    setSet('');
    setCn('');
    setSelected(null);
    setCondition('NM');
    setIsFoil(false);
    setFoilType(null);
    setQuantity(1);
    setPurchasePrice('');
    setPinPrinting(false);
    setFinishChoice('any');
    setPriority('Normal');
    setNote('');
    setAddedCount(0);
    setRecent([]);
    setActiveIndex(-1);
    setFilterOverride(listLanguage ? [listLanguage] : null);
    setSearchSets(setFilter?.game === startGame ? setFilter.sets : []);
    listQuantities.current.clear();
    pendingEnter.current = false;
    setRuleFoil(null);
    pendingSubmit.current = null;
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
  // The same three boxes apart, for the catalog search (it takes the Set box as its own scope).
  const [debouncedParts, setDebouncedParts] = useState({ name: '', set: '', cn: '' });
  useEffect(() => {
    const h = setTimeout(() => {
      setDebounced(composed);
      setDebouncedParts({ name: name.trim(), set: set.trim(), cn: cn.trim() });
    }, 250);
    return () => clearTimeout(h);
    // composed changes exactly when one of the boxes does.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [composed]);

  // Two characters before a name search, but a collector number can be one digit.
  const canSearch = open && !selected && (debounced.trim().length >= 2 || /^\d$/.test(debounced.trim()));

  // A code typed in the Set box overrides the chosen sets (the two would otherwise AND to nothing).
  // A code typed in the Set box overrides the chosen sets (the two would otherwise AND to nothing). A real set
  // code means exactly that set; anything else is looked up by code or name (set:, e.g. "Dominaria").
  const gameSets = useQuery({ queryKey: ['sets', game], queryFn: () => api.sets(game), enabled: open && !!game });
  const typedSet = debouncedParts.set;
  const typedSetCode = typedSet
    ? gameSets.data?.find((s) => s.setCode.toLowerCase() === typedSet.toLowerCase())?.setCode
    : undefined;
  const setQuery = typedSet && !typedSetCode ? typedSet : undefined;
  // Results come set by set in the order the sets were chosen.
  const scopeSets = typedSet
    ? typedSetCode
      ? [typedSetCode]
      : []
    : setFilter
      ? searchSets.map((s) => s.setCode)
      : [];
  const scopeKey = scopeSets.join(',');
  const anyOf = (field: string, values: string[]) =>
    values.length === 0
      ? ''
      : values.length === 1
        ? ` ${field}:${values[0]}`
        : ` (${values.map((v) => `${field}:${v}`).join(' or ')})`;
  // Owned copies are searched in their own language (lang: on the collection is the copy's language).
  const ownedQuery = anyOf('lang', languageFilter) + anyOf('set', scopeSets);
  const owned = useQuery({
    queryKey: ['collection', 'add-card', target.kind, game, debounced, ownedQuery],
    queryFn: () => api.collection({ game, q: debounced + ownedQuery, take: 25 }),
    enabled: canSearch && showOwned,
  });
  // A pocket only offers copies that live somewhere *else* — a lot already in this binder has nothing to
  // relocate (place unplaced-pool copies by dragging them from the sidebar instead).
  const setOrder = (code: string) => {
    const i = scopeSets.findIndex((s) => s.toLowerCase() === code.toLowerCase());
    return i < 0 ? scopeSets.length : i;
  };
  const ownedItems = (owned.data?.items ?? [])
    .filter((c) => target.kind !== 'pocket' || c.containerId !== target.containerId)
    // Array sort is stable: the server's order stays within each set.
    .sort((a, b) => (scopeSets.length > 1 ? setOrder(a.setCode) - setOrder(b.setCode) : 0));
  const catalog = useQuery({
    queryKey: ['catalog-search', game, debouncedParts.name, debouncedParts.cn, setQuery, languageFilter, scopeSets],
    // The Name box takes a collector number too ("0019", "LOB-EN019"): its printings come first. The Set box
    // goes on its own so it overrides the chosen sets — for a number search too.
    queryFn: () =>
      api.scanSearch(
        game,
        debouncedParts.name,
        scopeSets,
        debouncedParts.cn || undefined,
        languageFilter,
        true,
        setQuery,
      ),
    enabled: canSearch,
    // The catalog doesn't change while the dialog is open — going back from a picked printing (Esc/↑/↓)
    // should show the same results at once, not search again.
    staleTime: 5 * 60_000,
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
  useEffect(() => setActiveIndex(-1), [debounced, filterKey, scopeKey, game]);
  // Simplified: highlight the first result as soon as there is one, and honour an early Enter.
  useEffect(() => {
    if (!simplified || selected || rows.length === 0) return;
    if (pendingEnter.current && composed === debounced) {
      // The results weren't on screen when Enter was pressed, so the top one is what was meant.
      pendingEnter.current = false;
      choose(rows[0], 0);
    } else if (activeIndex < 0) {
      setActiveIndex(0);
    }
    // rows is derived each render; its length (a search landing) is what matters.
  }, [simplified, selected, rows.length, activeIndex, composed, debounced]);
  // Keep the highlighted row in view and show its art, as hovering would.
  useEffect(() => {
    if (activeIndex < 0 || selected) return;
    const el = document.getElementById(rowId(activeIndex));
    el?.scrollIntoView({ block: 'nearest' });
    const row = rows[activeIndex];
    const url = row && rowImage(row);
    setHover(el && url ? { el, url, foil: row.kind === 'owned' && row.card.isFoil } : null);
    // rows is derived each render; the index (and the selection hiding the list) is what matters.
  }, [activeIndex, selected, rows.length]);

  const focusName = () => requestAnimationFrame(() => nameRef.current?.focus());
  const focusPicked = () => requestAnimationFrame(() => pickedRef.current?.focus());
  /** Into a text field from the picked panel, its text selected so typing replaces it. */
  const focusField = (ref: { current: HTMLInputElement | null }) =>
    requestAnimationFrame(() => {
      ref.current?.focus();
      ref.current?.select();
    });

  const invalidate = () => {
    if (target.kind === 'list') {
      qc.invalidateQueries({ queryKey: ['list-items', target.listId] });
      qc.invalidateQueries({ queryKey: ['lists'] });
      return;
    }
    if (target.kind === 'buyList') {
      qc.invalidateQueries({ queryKey: ['buy-list'] });
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
      setNote('');
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
      if (u.kind === 'buyListEntry') {
        await api.buyListDelete(u.entryId);
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
  // which adding already required (likewise a Buy List entry: adding needed an administrator).
  const canUndo = target.kind === 'list' || target.kind === 'buyList' || can('collection.delete');
  const busy = add.isPending || undo.isPending;

  const pick = (r: ScanSearchResultDto) => {
    setHover(null);
    setSelected(r);
    // The copy defaults to the printing's language; it can still be changed (e.g. a French copy of a
    // card whose catalog only has the English row).
    setLanguage(r.language || ENGLISH);
    checkFoilRule(r);
  };
  const checkFoilRule = (r: ScanSearchResultDto | null) => {
    const token = ++pickToken.current;
    foilTouched.current = false;
    pendingSubmit.current = null;
    const rule = foilRule?.query.trim();
    if (!r || !rule || buyList) {
      setRuleFoil(null);
      return;
    }
    setRuleFoil('checking');
    api
      .binderFoilRuleCheck(game, rule, r)
      .then((matches) => {
        if (token !== pickToken.current || foilTouched.current) return;
        setIsFoil(matches);
        setFoilType(matches ? foilRule!.foilType : null);
        setRuleFoil(matches ? 'foil' : 'nonFoil');
      })
      // A rule the server can't check just doesn't apply.
      .catch(() => token === pickToken.current && setRuleFoil(null));
  };
  /** Foil set by hand: the rule's answer no longer applies. */
  const touchFoil = (on: boolean) => {
    foilTouched.current = true;
    setRuleFoil(null);
    setIsFoil(on);
  };
  // An add asked for while the rule was checking goes ahead once it has answered.
  useEffect(() => {
    if (ruleFoil === 'checking' || pendingSubmit.current == null) return;
    const close = pendingSubmit.current;
    pendingSubmit.current = null;
    submit(close);
    // submit is re-created each render; the verdict landing is what matters.
  }, [ruleFoil]);
  // Done searching: once the picked card is on screen, the keyboard moves to it (Enter adds, single keys
  // set its details). An effect, because a pick can come from an effect too (an early Enter).
  useEffect(() => {
    if (selected) pickedRef.current?.focus();
  }, [selected]);
  /** Back from a picked printing to the results, keeping the picked row highlighted. */
  const unpick = () => {
    setSelected(null);
    checkFoilRule(null);
    focusName();
  };

  const finishLabel = (foil: boolean, type?: string | null) => (foil ? type || t('common.labels.foil') : null);

  /** Pocket: move this copy into the pocket. List: reference it on the list. */
  const addOwned = (card: CardDto) => {
    if (busy || target.kind === 'location' || target.kind === 'buyList') return;
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
    if (ruleFoil === 'checking') {
      pendingSubmit.current = close;
      return;
    }
    const s = selected;
    const qty = target.kind === 'pocket' ? 1 : Math.max(1, quantity);
    // Blank ⇒ the server applies the game's basic foil finish.
    const finish = isFoil ? foilType?.trim() || null : null;
    const price = purchasePrice === '' ? null : Number(purchasePrice);
    const details =
      target.kind === 'buyList'
        ? [t(`buyList.priority.${priority}`)]
        : target.kind === 'list'
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
          case 'buyList': {
            const entry = await api.buyListCreate({
              game,
              name: s.name,
              gameCardId: s.gameCardId,
              setCode: s.setCode,
              setName: s.setName,
              collectorNumber: s.collectorNumber,
              imageUri: s.imageUri,
              pinPrinting,
              foil: finishChoice === 'any' ? null : finishChoice === 'foil',
              wantedQuantity: qty,
              priority,
              note: note.trim() || null,
            });
            // A merged add raised an existing entry's quantity; removing the entry wouldn't undo that.
            return entry.merged ? null : { kind: 'buyListEntry', entryId: entry.id };
          }
        }
      },
    });
  };

  const choose = (row: ResultRow, index: number) => {
    setActiveIndex(index);
    if (row.kind === 'owned') addOwned(row.card);
    else pick(row.result);
  };

  // From the search boxes: ↑/↓ move through the results, Enter picks the highlighted one (or the only
  // one) — and, once a printing is picked, adds it; ↑/↓ then go back to the results.
  const onSearchKeyDown = (e: KeyboardEvent) => {
    if (setFilter && e.key === 'F2') {
      e.preventDefault();
      setsRef.current?.focus();
      return;
    }
    if (setFilter && e.altKey && !e.ctrlKey && e.code === 'KeyA') {
      e.preventDefault();
      searchAllSets();
      return;
    }
    if (e.key === 'Escape') {
      // Handled here rather than by the dialog: while a highlighted row's art preview is open it is the
      // top modal, and the dialog would ignore Esc.
      e.preventDefault();
      e.stopPropagation();
      if (selected) unpick();
      else {
        setHover(null);
        onClose();
      }
      return;
    }
    if (selected) {
      if (e.key === 'Enter') {
        e.preventDefault();
        submit(e.ctrlKey || e.metaKey);
      } else if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
        e.preventDefault();
        unpick();
      }
      return;
    }
    if (e.key === 'Enter' && simplified && (composed !== debounced || catalog.isFetching)) {
      // Typed ahead of the search — pick the top result when it arrives, and search now rather than
      // after the typing pause.
      e.preventDefault();
      pendingEnter.current = true;
      if (composed !== debounced) {
        setDebounced(composed);
        setDebouncedParts({ name: name.trim(), set: set.trim(), cn: cn.trim() });
      }
      return;
    }
    if (rows.length === 0) return;
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      setActiveIndex((i) => Math.min(rows.length - 1, i + 1));
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setActiveIndex((i) => Math.max(simplified ? 0 : -1, i - 1));
    } else if (e.key === 'Enter' && composed === debounced) {
      const i = activeIndex >= 0 ? activeIndex : rows.length === 1 ? 0 : -1;
      if (i >= 0 && rows[i]) {
        e.preventDefault();
        choose(rows[i], i);
      }
    }
  };
  // Editing the search means picking again.
  const editSearch = <T,>(setter: (v: T) => void) => (v: T) => {
    setter(v);
    setSelected(null);
    pendingEnter.current = false;
  };
  // The copy details each target asks for (and so which shortcut keys do anything).
  const hasCopyDetails = target.kind !== 'list' && !buyList; // condition, language, price
  const hasQuantity = target.kind !== 'pocket';
  const hasFoil = !buyList;

  /** On the picked card: single keys reach its details, Enter adds, Esc goes back to the search. In a
   *  detail field, Enter or Esc finishes with it and returns here; Ctrl+Enter adds (and closes) from anywhere. */
  const onPickedKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
    const el = e.target as HTMLElement;
    if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
      e.preventDefault();
      submit(true);
      return;
    }
    if (el !== e.currentTarget) {
      if (el.closest('button')) return; // a button acts on Enter itself
      // Enter in a text box (even one whose suggestion list just used it) or Esc anywhere but an open
      // list (which closes itself): finished with this field. Enter on a closed list opens it.
      if ((e.key === 'Enter' && el.tagName === 'INPUT') || (e.key === 'Escape' && !el.closest('[role="listbox"]'))) {
        e.preventDefault();
        e.stopPropagation();
        focusPicked();
      }
      return;
    }
    if (e.key === 'Enter') {
      e.preventDefault();
      submit(false);
      return;
    }
    if (e.key === 'Escape' || e.key === 'ArrowUp' || e.key === 'ArrowDown') {
      // Wrong card: back to the results, still highlighted. (Handled here so the dialog doesn't close.)
      e.preventDefault();
      e.stopPropagation();
      unpick();
      return;
    }
    if (e.ctrlKey || e.altKey || e.metaKey) return;
    const key = e.key.toLowerCase();
    if (hasCopyDetails && (key === 'c' || key === 'l')) focusField(key === 'c' ? conditionRef : languageRef);
    else if (hasCopyDetails && key === 'p') focusField(priceRef);
    else if (hasQuantity && key === 'q') focusField(quantityRef);
    else if (hasFoil && key === 'f') {
      // Foil on goes straight to its finish; foil off stays here.
      touchFoil(!isFoil);
      if (!isFoil) focusField(foilTypeRef);
    } else return;
    e.preventDefault();
  };
  /** "Condition (C)": a field's label with its shortcut key, where there's a keyboard to press it. */
  const keyLabel = (label: string, key: string) => (canHover ? t('dialogs.addCard.withKey', { label, key }) : label);
  // [keys, what they do, whether this target has it]
  const pickedKeyHints = (
    [
      [['Enter'], 'add', true],
      [['Ctrl', 'Enter'], 'addAndClose', multiAdd],
      [['Esc'], 'back', true],
      [['C'], 'condition', hasCopyDetails],
      [['L'], 'language', hasCopyDetails],
      [['F'], 'foil', hasFoil],
      [['P'], 'price', hasCopyDetails],
      [['Q'], 'quantity', hasQuantity],
    ] as [string[], string, boolean][]
  )
    .filter(([, , shown]) => shown)
    .map(([keys, what]): [string[], string] => [keys, `dialogs.addCard.pickedKeys.${what}`]);
  const changeSearchSets = editSearch(setSearchSets);
  /** Drop the set filter for this sitting (the card is in a set that wasn't chosen). */
  const searchAllSets = () => {
    changeSearchSets([]);
    // The Set box too — it would otherwise still hold the search to its set.
    setSet('');
    focusName();
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
    onClick: () => {
      setActiveIndex(index);
      onClick();
    },
    // Rows are reached with ↑/↓ from the search boxes, so Tab skips them (on to the picked card's fields).
    tabIndex: -1,
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
      <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
        <Typography variant="body2" color="text.secondary">
          {scopeSets.length > 0
            ? t('dialogs.addCard.noCatalogMatchesInSets', { sets: scopeSets.map((s) => s.toUpperCase()).join(', ') })
            : languageFilter.length > 0
              ? t('dialogs.addCard.noCatalogMatchesInLanguages', {
                  game: gameName,
                  languages: languageFilter.map((l) => languageName(t, l)).join(', '),
                })
              : t('dialogs.addCard.noCatalogMatches', { game: gameName })}
        </Typography>
        {scopeSets.length > 0 && (
          <Stack direction="row" spacing={0.5} alignItems="center">
            <Button size="small" onClick={searchAllSets}>
              {t('dialogs.addCard.searchAllSets')}
            </Button>
            <Kbd>Alt</Kbd>
            <Kbd>A</Kbd>
          </Stack>
        )}
      </Stack>
    );

  return (
    <Dialog
      open={open}
      // Esc on a picked printing goes back to the results; otherwise it closes.
      onClose={(_, reason) => (reason === 'escapeKeyDown' && selected ? unpick() : onClose())}
      fullWidth
      maxWidth="sm"
    >
      <DialogTitle>
        {target.kind === 'location' && t('dialogs.addCard.title', { location: target.locationName })}
        {target.kind === 'list' && t('dialogs.addToList.title')}
        {target.kind === 'buyList' && t('buyList.add.title')}
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
            <SelectField
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
                // Sets are per game: back on the filter's game its sets come back.
                setSearchSets(setFilter?.game === e.target.value ? setFilter.sets : []);
              }}
              sx={{ width: 220 }}
            >
              {(games.data ?? [{ id: 'Mtg', displayName: 'Magic: The Gathering' }]).map((g) => (
                <MenuItem key={g.id} value={g.id}>
                  {g.displayName}
                </MenuItem>
              ))}
            </SelectField>
            {multiLanguage && (
              <LanguageFilterSelect languages={filterLanguages} value={languageFilter} onChange={setLanguageFilter} />
            )}
            {setFilter && (
              <Box
                sx={{ flex: '1 1 220px', minWidth: 200 }}
                // F2 goes back to the Name box (it came here from there).
                onKeyDown={(e) => {
                  if (e.key === 'F2') {
                    e.preventDefault();
                    focusName();
                  }
                }}
              >
                <SetMultiSelect
                  game={game}
                  value={searchSets}
                  onChange={changeSearchSets}
                  inputRef={setsRef}
                  helperText={set.trim() && searchSets.length > 0 ? t('dialogs.addCard.setBoxOverrides') : undefined}
                />
              </Box>
            )}
          </Stack>

          <Stack direction="row" spacing={1} onKeyDown={onSearchKeyDown}>
            <TextField
              size="small"
              label={t('dialogs.addCard.nameOrNumber')}
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
          {simplified && !selected && <KeyHints hints={setFilter ? [...KEY_HINTS, ...SET_KEY_HINTS] : KEY_HINTS} sx={{ mt: -1 }} />}

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
          {selected && <Divider />}
          {selected && (
            <Stack
              ref={pickedRef}
              tabIndex={-1}
              spacing={2}
              onKeyDown={onPickedKeyDown}
              // A ring while the picked card holds the keyboard, so it's clear the shortcut keys apply.
              sx={{
                p: 1,
                mx: -1,
                borderRadius: 1,
                outline: 'none',
                '&:focus': { boxShadow: (th) => `0 0 0 2px ${th.palette.primary.main}` },
              }}
            >
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
                <Button size="small" onClick={unpick}>{t('common.actions.change')}</Button>
              </Stack>
              {canHover && <KeyHints hints={pickedKeyHints} />}

              <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
                {hasCopyDetails && (
                  <>
                    <SelectField
                      size="small"
                      label={keyLabel(t('common.labels.condition'), 'C')}
                      value={condition}
                      onChange={(e) => setCondition(e.target.value)}
                      inputRef={conditionRef}
                      openOnFocus
                      sx={{ width: 140 }}
                    >
                      {CONDITIONS.map((c) => (
                        <MenuItem key={c} value={c}>{t(`common.conditions.${c}`)}</MenuItem>
                      ))}
                    </SelectField>
                    <LanguageSelect
                      game={game}
                      value={language}
                      onChange={setLanguage}
                      label={keyLabel(t('common.labels.language'), 'L')}
                      inputRef={languageRef}
                      openOnFocus
                      sx={{ width: 160 }}
                    />
                  </>
                )}
                {hasQuantity && (
                  <TextField
                    size="small"
                    label={keyLabel(buyList ? t('buyList.edit.wanted') : t('common.labels.quantity'), 'Q')}
                    type="number"
                    value={quantity}
                    onChange={(e) => setQuantity(Math.max(1, Math.floor(Number(e.target.value) || 1)))}
                    inputRef={quantityRef}
                    slotProps={{ htmlInput: { min: 1, step: 1 } }}
                    sx={{ width: 120 }}
                  />
                )}
                {hasCopyDetails && (
                  <TextField
                    size="small"
                    label={keyLabel(t('common.labels.purchasePrice'), 'P')}
                    type="number"
                    value={purchasePrice}
                    onChange={(e) => setPurchasePrice(e.target.value)}
                    inputRef={priceRef}
                    slotProps={{ htmlInput: { min: 0, step: '0.01' } }}
                    sx={{ width: 170 }}
                  />
                )}
              </Stack>
              {buyList ? (
                <>
                  <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
                    <SelectField
                      size="small"
                      label={t('buyList.edit.printing')}
                      value={pinPrinting ? 'one' : 'any'}
                      onChange={(e) => setPinPrinting(e.target.value === 'one')}
                      sx={{ minWidth: 180 }}
                    >
                      <MenuItem value="any">{t('buyList.edit.printingAny')}</MenuItem>
                      <MenuItem value="one">
                        {t('buyList.edit.printingOne', { set: selected.setCode.toUpperCase(), number: selected.collectorNumber })}
                      </MenuItem>
                    </SelectField>
                    <SelectField
                      size="small"
                      label={t('buyList.edit.finish')}
                      value={finishChoice}
                      onChange={(e) => setFinishChoice(e.target.value)}
                      sx={{ minWidth: 150 }}
                    >
                      <MenuItem value="any">{t('buyList.edit.finishAny')}</MenuItem>
                      <MenuItem value="foil">{t('buyList.edit.finishFoil')}</MenuItem>
                      <MenuItem value="nonfoil">{t('buyList.edit.finishNonFoil')}</MenuItem>
                    </SelectField>
                    <SelectField
                      size="small"
                      label={t('buyList.edit.priority')}
                      value={priority}
                      onChange={(e) => setPriority(e.target.value as BuyListPriority)}
                      sx={{ minWidth: 120 }}
                    >
                      {(['Normal', 'High', 'Grail'] as const).map((p) => (
                        <MenuItem key={p} value={p}>
                          {t(`buyList.priority.${p}`)}
                        </MenuItem>
                      ))}
                    </SelectField>
                  </Stack>
                  <TextField
                    size="small"
                    label={t('buyList.edit.note')}
                    helperText={t('buyList.edit.noteHelp')}
                    value={note}
                    onChange={(e) => setNote(e.target.value)}
                    inputProps={{ maxLength: 500 }}
                  />
                </>
              ) : (
                <Stack direction="row" spacing={2} alignItems="center" flexWrap="wrap" useFlexGap>
                  <FormControlLabel
                    control={<Switch checked={isFoil} onChange={(e) => touchFoil(e.target.checked)} />}
                    label={keyLabel(t('common.labels.foil'), 'F')}
                  />
                  {(ruleFoil === 'checking' || ruleFoil === 'foil') && (
                    <Typography variant="caption" color="text.secondary">
                      {t(ruleFoil === 'checking' ? 'dialogs.addCard.foilRuleChecking' : 'dialogs.addCard.foilRuleApplied')}
                    </Typography>
                  )}
                  {isFoil && (
                    <Autocomplete
                      freeSolo
                      autoHighlight
                      size="small"
                      options={foilTypes.data ?? []}
                      // The finish lives in the text: with no selected value, the list always filters by
                      // what's typed (a value equal to the text made MUI list every finish), from the start
                      // of each finish, and Enter takes the top one (or the typed text when none match).
                      value={null}
                      inputValue={foilType ?? ''}
                      filterOptions={filterFinishes}
                      onChange={(_, v) => setFoilType(v || null)}
                      onInputChange={(_, v) => setFoilType(v || null)}
                      sx={{ minWidth: 220 }}
                      renderInput={(p) => (
                        <TextField {...p} label={t('common.labels.foilType')} placeholder={foilTypes.data?.[0]} inputRef={foilTypeRef} />
                      )}
                    />
                  )}
                </Stack>
              )}
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
                  : buyList
                    ? t('buyList.add.addedSoFar', { count: addedCount })
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
                : buyList
                  ? t('buyList.add.button')
                  : t('dialogs.addCard.addCard')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
