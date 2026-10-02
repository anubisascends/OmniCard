import { useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import type { SiteDto } from '../api/types';

/**
 * The sites (major physical locations — a home, a shop) the signed-in user can see, default first.
 * `writable` is the subset they may create/move locations in. `multiSite` is false while only the
 * default site exists, so site UI (filters, columns, pickers) can stay hidden until it matters.
 * The server is the hard gate; this is for presentation only.
 */
export function useSites() {
  const query = useQuery({ queryKey: ['sites'], queryFn: api.sites });
  const sites: SiteDto[] = query.data ?? [];
  const writable = sites.filter((s) => s.access === 'Write');
  const defaultSite = sites.find((s) => s.isDefault);
  return {
    sites,
    writable,
    defaultSite,
    multiSite: sites.length > 1,
    isLoading: query.isLoading,
  };
}
