import { useQuery } from '@tanstack/react-query';
import { api } from '../api/client';

/**
 * Reads the signed-in user's effective permissions from the shared `auth-status` query and exposes
 * `can()` helpers for gating UI. The server resolves permissions fresh on every `/api/auth/status`
 * call, and the query refetches on window focus + route change (see AppShell), so an admin's change
 * shows up within seconds without a reload. Server-side checks remain the hard gate regardless.
 */
export function usePermissions() {
  const authQuery = useQuery({ queryKey: ['auth-status'], queryFn: api.authStatus });
  const isAdmin = !!authQuery.data?.isAdmin;
  const permissions = authQuery.data?.permissions ?? [];
  const set = new Set(permissions);

  // Admins hold every permission (the server also returns the full set for them, but short-circuit
  // here so gating works even before that payload arrives).
  const can = (permission: string) => isAdmin || set.has(permission);
  const canAny = (...perms: string[]) => isAdmin || perms.some((p) => set.has(p));

  return { isAdmin, permissions, can, canAny, isLoading: authQuery.isLoading };
}
