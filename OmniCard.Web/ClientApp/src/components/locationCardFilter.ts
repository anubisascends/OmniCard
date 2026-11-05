import { useQuery } from '@tanstack/react-query';
import { api } from '../api/client';
import type { SetInfoDto } from '../api/types';

/** A location's default set codes as `SetInfoDto`s (names from the game's set list; an unknown code keeps
 *  its code as its name), for `SetMultiSelect` and the Add card dialog's set filter. `ready` is false
 *  while the set list is still loading. */
export function useDefaultSets(game: string | null | undefined, codes: string[] | null | undefined) {
  const list = codes ?? [];
  const sets = useQuery({
    queryKey: ['sets', game],
    queryFn: () => api.sets(game!),
    enabled: !!game && list.length > 0,
  });
  const byCode = new Map((sets.data ?? []).map((s) => [s.setCode.toLowerCase(), s]));
  const resolved: SetInfoDto[] = list.map((c) => byCode.get(c.toLowerCase()) ?? { setCode: c, setName: c });
  return { sets: resolved, ready: !game || list.length === 0 || !sets.isLoading };
}

/** A collection search for the cards outside these sets: `-set:msh -set:msc`. */
export function outsideSetsQuery(codes: string[]): string {
  return codes.map((c) => `-set:${/\s/.test(c) ? `"${c}"` : c}`).join(' ');
}
