import { useQuery } from "@tanstack/react-query";
import { api } from "@/api/client";
import { recKeys } from "./keys";

interface ScoredItem {
  media_item_id: string;
  score: number;
  reason: string;
}

interface RecommendationResponse {
  items: ScoredItem[];
}

interface ForYouRow {
  type: string;
  label: string;
  cluster_index?: number;
  items: ScoredItem[];
}

interface ForYouResponse {
  rows: ForYouRow[];
}

interface ForYouMainResponse {
  row: ForYouRow | null;
}

interface TasteProfileResponse {
  top_genres: string[];
  favorite_directors: string[];
  signal_counts: Record<string, number>;
  updated_at: string;
}

export type { ScoredItem, ForYouRow, ForYouResponse };

export function useSimilarItems(itemId: string) {
  return useQuery({
    queryKey: recKeys.similar(itemId),
    queryFn: () => api<RecommendationResponse>(`/recommendations/similar/${itemId}`),
    staleTime: 3600_000,
    enabled: !!itemId,
  });
}

export function useForYouMain(enabled = true) {
  return useQuery({
    queryKey: recKeys.forYouMain(),
    queryFn: () => api<ForYouMainResponse>(`/recommendations/for-you/main`),
    staleTime: 300_000,
    enabled,
  });
}

export function useForYouRows(enabled = true) {
  return useQuery({
    queryKey: recKeys.forYouRows(),
    queryFn: () => api<ForYouResponse>(`/recommendations/for-you/rows`),
    staleTime: 300_000,
    enabled,
  });
}

export function useBecauseWatched(itemId: string) {
  return useQuery({
    queryKey: recKeys.becauseWatched(itemId),
    queryFn: () => api<RecommendationResponse>(`/recommendations/because-watched/${itemId}`),
    staleTime: 300_000,
    enabled: !!itemId,
  });
}

export function useSimilarUsers(enabled = true) {
  return useQuery({
    queryKey: recKeys.similarUsers(),
    queryFn: () => api<RecommendationResponse>(`/recommendations/similar-users`),
    staleTime: 300_000,
    enabled,
  });
}

export function useTasteProfile() {
  return useQuery({
    queryKey: recKeys.tasteProfile(),
    queryFn: () => api<TasteProfileResponse>(`/recommendations/taste-profile`),
    staleTime: 300_000,
  });
}

export function usePopular(days?: number) {
  const params = days ? `?days=${days}` : "";
  return useQuery({
    queryKey: [...recKeys.all, "popular", days ?? 30],
    queryFn: () => api<RecommendationResponse>(`/recommendations/popular${params}`),
    staleTime: 600_000,
  });
}

export function useRecentlyAdded(days?: number) {
  const params = days ? `?days=${days}` : "";
  return useQuery({
    queryKey: [...recKeys.all, "recently-added", days ?? 14],
    queryFn: () => api<RecommendationResponse>(`/recommendations/recently-added${params}`),
    staleTime: 600_000,
  });
}
