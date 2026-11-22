import { useCallback, useMemo } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../api/client';

// Remembered UI settings of the signed-in user, saved on the server (/api/account/preferences) so they
// follow the user from browser to browser. A copy is kept in this browser's storage, keyed by user, so the
// setting applies at once on load (before the server answers) and still works when the site has no sign-in.

const QUERY_KEY = ['user-preferences'] as const;

function storageKey(username: string | null | undefined, key: string) {
  return `omnicard.pref.${username || '_'}.${key}`;
}

function readLocal(username: string | null | undefined, key: string): string | null {
  try {
    return localStorage.getItem(storageKey(username, key));
  } catch {
    return null; // Storage unavailable (private mode).
  }
}

function writeLocal(username: string | null | undefined, key: string, value: string) {
  try {
    localStorage.setItem(storageKey(username, key), value);
  } catch {
    /* Storage unavailable: the server copy still holds it. */
  }
}

function parse<T extends object>(raw: string | null | undefined): Partial<T> | null {
  if (!raw) return null;
  try {
    const v = JSON.parse(raw);
    return v && typeof v === 'object' && !Array.isArray(v) ? (v as Partial<T>) : null;
  } catch {
    return null;
  }
}

/**
 * One remembered settings object for the signed-in user. Returns the stored value merged over
 * {@link defaults}, and a setter that merges a patch, applies it at once and saves it in the background.
 * Callers should still validate/clamp what they read (stored values may come from an older version).
 */
export function useUserPreference<T extends object>(key: string, defaults: T): [T, (patch: Partial<T>) => void] {
  const queryClient = useQueryClient();
  const authQuery = useQuery({ queryKey: ['auth-status'], queryFn: api.authStatus });
  const username = authQuery.data?.username ?? null;
  const signedIn = !!authQuery.data?.authenticated && !!username;
  const prefsQuery = useQuery({
    queryKey: QUERY_KEY,
    queryFn: api.accountPreferences,
    enabled: signedIn,
    staleTime: Infinity,
    retry: false,
  });

  const raw = prefsQuery.data?.[key] ?? readLocal(username, key);
  // `defaults` is usually a fresh literal each render: key the merge on its JSON so the result stays stable.
  const defaultsJson = JSON.stringify(defaults);
  const value = useMemo(
    () => ({ ...(JSON.parse(defaultsJson) as T), ...(parse<T>(raw) ?? {}) }),
    [defaultsJson, raw],
  );

  const set = useCallback(
    (patch: Partial<T>) => {
      const current = queryClient.getQueryData<Record<string, string>>(QUERY_KEY)?.[key] ?? readLocal(username, key);
      const next = JSON.stringify({ ...(parse<T>(current) ?? {}), ...patch });
      writeLocal(username, key, next);
      queryClient.setQueryData<Record<string, string>>(QUERY_KEY, (old) => ({ ...(old ?? {}), [key]: next }));
      if (signedIn) api.accountSetPreference(key, next).catch(() => {}); // Kept locally if the save fails.
    },
    [queryClient, key, username, signedIn],
  );

  return [value, set];
}
