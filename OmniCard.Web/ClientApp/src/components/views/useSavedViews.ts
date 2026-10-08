import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useLocation, useSearchParams } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../../api/client';
import type { SavedViewDto } from '../../api/types';
import { useGame } from '../../context/GameContext';
import {
  ALL_GAMES_KEY,
  builtInViewState,
  normalizeViewState,
  rememberBuiltInPrefs,
  sameViewState,
  type ViewState,
} from '../../lib/savedViews';

const VIEW_PARAM = 'view';

export interface SavedViewsScope {
  page: 'Collection' | 'Location';
  /** The location, on a Location page. */
  containerId?: number;
}

export interface SavedViews extends SavedViewsScope {
  /** The selected game (undefined = All Games) and its saved-view key. */
  game: string | undefined;
  gameKey: string;
  views: SavedViewDto[];
  /** The view in use; null = the built-in layout. */
  activeView: SavedViewDto | null;
  /** The layout on screen (the active view plus any unsaved changes). */
  state: ViewState;
  setState: (patch: Partial<ViewState>) => void;
  /** True when the layout on screen differs from the active view. */
  isModified: boolean;
  /** Switch to a view (null = the built-in layout). */
  select: (view: SavedViewDto | null) => void;
  /** Throw away unsaved changes. */
  reset: () => void;
  /** After saving: `view` is now active and the layout on screen is its saved state. */
  markSaved: (view: SavedViewDto) => void;
  /** Re-pick the page's default view (after the active view was deleted). */
  reloadDefault: () => void;
  refresh: () => Promise<void>;
}

/**
 * Saved views for one Collection / Location page. Picks the layout the page opens with — the view in
 * the `?view=` link, else the user's / everyone's default for the page and selected game (resolved by
 * the server), else the built-in layout — re-picks it when the game or location changes, and keeps the
 * address bar's `?view=` pointing at the active view so the page can be bookmarked. A link to a view
 * saved for another game switches the selected game to it.
 */
export function useSavedViews({ page, containerId }: SavedViewsScope): SavedViews {
  const qc = useQueryClient();
  const { game, setGame } = useGame();
  const onLocation = page === 'Location';
  const gameKey = game ?? ALL_GAMES_KEY;
  const scopeKey = `${page}:${containerId ?? ''}:${gameKey}`;
  const [searchParams, setSearchParams] = useSearchParams();
  const routerLocation = useLocation();
  const urlViewId = Number(searchParams.get(VIEW_PARAM)) || null;

  const listQuery = useQuery({
    queryKey: ['saved-views', page, containerId ?? null, gameKey],
    queryFn: () => api.savedViews(page, containerId, game),
    enabled: !onLocation || !!containerId,
  });
  const views = useMemo(() => listQuery.data?.views ?? [], [listQuery.data]);

  const [activeId, setActiveId] = useState<number | null>(null);
  const [state, setStateRaw] = useState<ViewState>(() => builtInViewState(onLocation));
  // What the active view looks like as saved — "modified" compares against this.
  const [baseline, setBaseline] = useState<ViewState>(state);
  const appliedScope = useRef<string | null>(null);
  // View links already followed to another game, so a view that still isn't offered there can't loop.
  const followedLinks = useRef(new Set<number>());
  const pendingLink = useRef<number | null>(null);

  const setViewParam = useCallback(
    (id: number | null) => {
      if ((Number(new URLSearchParams(window.location.search).get(VIEW_PARAM)) || null) === id) return;
      setSearchParams(
        (prev) => {
          const next = new URLSearchParams(prev);
          if (id) next.set(VIEW_PARAM, String(id));
          else next.delete(VIEW_PARAM);
          return next;
        },
        // Keep router state (e.g. the audit summary a Location page was opened with).
        { replace: true, state: routerLocation.state },
      );
    },
    [setSearchParams, routerLocation.state],
  );

  const apply = useCallback(
    (view: SavedViewDto | null) => {
      const next = view ? normalizeViewState(view.state, onLocation) : builtInViewState(onLocation);
      setActiveId(view?.id ?? null);
      setStateRaw(next);
      setBaseline(next);
      setViewParam(view?.id ?? null);
    },
    [onLocation, setViewParam],
  );

  // Pick the starting view once per page + game, when that scope's list arrives.
  useEffect(() => {
    const data = listQuery.data;
    if (!data || listQuery.isFetching || appliedScope.current === scopeKey || pendingLink.current) return;
    const pickDefault = () => {
      appliedScope.current = scopeKey;
      apply(data.views.find((v) => v.id === data.defaultViewId) ?? null);
    };
    const linked = urlViewId ? data.views.find((v) => v.id === urlViewId) : undefined;
    if (urlViewId && !linked && !followedLinks.current.has(urlViewId)) {
      // The link may be to a view saved for another game: switch to that game, then pick again.
      followedLinks.current.add(urlViewId);
      pendingLink.current = urlViewId;
      api
        .savedView(urlViewId)
        .then((v) => {
          pendingLink.current = null;
          const offeredHere =
            (v.page === page && (v.page !== 'Location' || v.containerId === containerId)) ||
            (onLocation && v.page === 'AllLocations');
          if (offeredHere && v.game && v.game !== gameKey) setGame(v.game === ALL_GAMES_KEY ? undefined : v.game);
          else pickDefault();
        })
        .catch(() => {
          pendingLink.current = null;
          pickDefault();
        });
      return;
    }
    if (linked) {
      appliedScope.current = scopeKey;
      apply(linked);
    } else pickDefault();
  }, [listQuery.data, listQuery.isFetching, scopeKey, urlViewId, page, onLocation, containerId, gameKey, setGame, apply]);

  // Following a link to another view of the same page (e.g. a bookmark) switches to it.
  useEffect(() => {
    if (appliedScope.current !== scopeKey || !urlViewId || urlViewId === activeId) return;
    const linked = views.find((v) => v.id === urlViewId);
    if (linked) apply(linked);
  }, [urlViewId, activeId, views, scopeKey, apply]);

  const activeView = useMemo(() => views.find((v) => v.id === activeId) ?? null, [views, activeId]);

  const setState = useCallback(
    (patch: Partial<ViewState>) => {
      setStateRaw((prev) => {
        const next = { ...prev, ...patch };
        // The built-in layout keeps remembering the browser's last stacking / display / grouping.
        if (activeId === null) rememberBuiltInPrefs(next);
        return next;
      });
    },
    [activeId],
  );

  return {
    page,
    containerId,
    game,
    gameKey,
    views,
    activeView,
    state,
    setState,
    isModified: !sameViewState(state, baseline),
    select: apply,
    reset: () => setStateRaw(baseline),
    markSaved: (view) => {
      setActiveId(view.id);
      setBaseline(state);
      setViewParam(view.id);
    },
    reloadDefault: () => {
      appliedScope.current = null;
      setViewParam(null);
    },
    refresh: () => qc.invalidateQueries({ queryKey: ['saved-views'] }),
  };
}
