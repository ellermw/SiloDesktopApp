import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/client";
import type { BrowseItem } from "@/api/types";
import { watchlistKeys } from "./keys";
import { checkBooleanStatus } from "./utils";
import { toast } from "sonner";
import { invalidateMediaSurfaceQueries } from "./mediaSurfaceRefresh";

export function useWatchlist() {
  return useQuery({
    queryKey: watchlistKeys.list(),
    queryFn: () => api<{ items: BrowseItem[] }>("/watchlist").then((d) => d.items ?? []),
  });
}

export function useIsInWatchlist(itemId: string | undefined) {
  return useQuery({
    queryKey: watchlistKeys.check(itemId!),
    queryFn: () => checkBooleanStatus(`/watchlist/${itemId}`),
    enabled: !!itemId,
  });
}

export function useToggleWatchlist(itemId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (currentlyInWatchlist: boolean) =>
      api(`/watchlist/${itemId}`, {
        method: currentlyInWatchlist ? "DELETE" : "PUT",
      }),
    onMutate: async (currentlyInWatchlist: boolean) => {
      await queryClient.cancelQueries({
        queryKey: watchlistKeys.check(itemId),
      });
      const previous = queryClient.getQueryData<boolean>(watchlistKeys.check(itemId));
      queryClient.setQueryData(watchlistKeys.check(itemId), !currentlyInWatchlist);
      return { previous };
    },
    onError: (_err, _vars, context) => {
      if (context?.previous !== undefined) {
        queryClient.setQueryData(watchlistKeys.check(itemId), context.previous);
      }
      toast.error("Failed to update watchlist");
    },
    onSuccess: (_data, currentlyInWatchlist) => {
      toast.success(currentlyInWatchlist ? "Removed from watchlist" : "Added to watchlist");
    },
    onSettled: () => {
      return invalidateMediaSurfaceQueries(queryClient, { itemId });
    },
  });
}
