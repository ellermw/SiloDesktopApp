import type { QueryClient } from "@tanstack/react-query";
import {
  adminKeys,
  catalogKeys,
  favoriteKeys,
  historyKeys,
  itemKeys,
  personKeys,
  progressKeys,
  recKeys,
  sectionKeys,
  watchlistKeys,
} from "./keys";

interface InvalidateMediaSurfaceOptions {
  itemId?: string;
  watchedKeys?: Array<readonly unknown[]>;
}

export async function invalidateMediaSurfaceQueries(
  queryClient: QueryClient,
  options: InvalidateMediaSurfaceOptions = {},
) {
  const invalidations: Array<Promise<void>> = [
    queryClient.invalidateQueries({ queryKey: itemKeys.all }),
    queryClient.invalidateQueries({ queryKey: catalogKeys.all }),
    queryClient.invalidateQueries({ queryKey: sectionKeys.all }),
    queryClient.invalidateQueries({ queryKey: progressKeys.all }),
    queryClient.invalidateQueries({ queryKey: historyKeys.all }),
    queryClient.invalidateQueries({ queryKey: favoriteKeys.all }),
    queryClient.invalidateQueries({ queryKey: watchlistKeys.all }),
    queryClient.invalidateQueries({ queryKey: recKeys.all }),
    queryClient.invalidateQueries({ queryKey: personKeys.all }),
    queryClient.invalidateQueries({
      predicate: (query) =>
        Array.isArray(query.queryKey) &&
        query.queryKey[0] === adminKeys.playbackHistory({})[0] &&
        query.queryKey[1] === adminKeys.playbackHistory({})[1],
    }),
  ];

  if (options.itemId) {
    invalidations.push(
      queryClient.invalidateQueries({ queryKey: catalogKeys.itemDetail(options.itemId) }),
      queryClient.invalidateQueries({ queryKey: itemKeys.detail(options.itemId) }),
      queryClient.invalidateQueries({ queryKey: itemKeys.watchDetail(options.itemId) }),
      queryClient.invalidateQueries({ queryKey: favoriteKeys.check(options.itemId) }),
      queryClient.invalidateQueries({ queryKey: watchlistKeys.check(options.itemId) }),
    );
  }

  for (const key of options.watchedKeys ?? []) {
    invalidations.push(queryClient.invalidateQueries({ queryKey: key }));
  }

  await Promise.all(invalidations);
}
