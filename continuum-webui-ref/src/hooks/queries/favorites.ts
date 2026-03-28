import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/client";
import type { BrowseItem } from "@/api/types";
import { favoriteKeys } from "./keys";
import { checkBooleanStatus } from "./utils";
import { toast } from "sonner";
import { invalidateMediaSurfaceQueries } from "./mediaSurfaceRefresh";

export function useFavorites() {
  return useQuery({
    queryKey: favoriteKeys.list(),
    queryFn: () => api<{ items: BrowseItem[] }>("/favorites").then((d) => d.items ?? []),
  });
}

export function useIsFavorite(itemId: string | undefined) {
  return useQuery({
    queryKey: favoriteKeys.check(itemId!),
    queryFn: () => checkBooleanStatus(`/favorites/${itemId}`),
    enabled: !!itemId,
  });
}

export function useToggleFavorite(itemId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (currentlyFavorite: boolean) =>
      api(`/favorites/${itemId}`, {
        method: currentlyFavorite ? "DELETE" : "PUT",
      }),
    onMutate: async (currentlyFavorite: boolean) => {
      await queryClient.cancelQueries({
        queryKey: favoriteKeys.check(itemId),
      });
      const previous = queryClient.getQueryData<boolean>(favoriteKeys.check(itemId));
      queryClient.setQueryData(favoriteKeys.check(itemId), !currentlyFavorite);
      return { previous };
    },
    onError: (_err, _vars, context) => {
      if (context?.previous !== undefined) {
        queryClient.setQueryData(favoriteKeys.check(itemId), context.previous);
      }
      toast.error("Failed to update favorites");
    },
    onSuccess: (_data, currentlyFavorite) => {
      toast.success(currentlyFavorite ? "Removed from favorites" : "Added to favorites");
    },
    onSettled: () => {
      return invalidateMediaSurfaceQueries(queryClient, { itemId });
    },
  });
}
