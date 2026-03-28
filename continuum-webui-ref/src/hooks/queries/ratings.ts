import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/client";
import { ratingKeys } from "./keys";
import { invalidateRatingSurfaceQueries } from "./ratingsSurfaceRefresh";

interface RatingResponse {
  rating: number;
  rated_at: string;
}

export function useRating(itemId: string) {
  return useQuery({
    queryKey: ratingKeys.item(itemId),
    queryFn: async () => {
      try {
        return await api<RatingResponse>(`/ratings/${itemId}`);
      } catch {
        return null;
      }
    },
    staleTime: 60_000,
  });
}

export function useSetRating(itemId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (rating: number) =>
      api(`/ratings/${itemId}`, {
        method: "PUT",
        body: JSON.stringify({ rating }),
      }),
    onSuccess: () => {
      return invalidateRatingSurfaceQueries(queryClient, itemId);
    },
  });
}

export function useDeleteRating(itemId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => api(`/ratings/${itemId}`, { method: "DELETE" }),
    onSuccess: () => {
      return invalidateRatingSurfaceQueries(queryClient, itemId);
    },
  });
}
