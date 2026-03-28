import { useQuery, useQueries } from "@tanstack/react-query";
import { api } from "@/api/client";
import type { ProgressListResponse, ProgressEntry, ItemDetail } from "@/api/types";
import { catalogKeys, progressKeys } from "./keys";
import { fetchCatalogItemDetail } from "./catalogRead";

export function useProgressList(libraryId?: number) {
  return useQuery({
    queryKey: progressKeys.list("in_progress", libraryId),
    queryFn: () => {
      const searchParams = new URLSearchParams({
        status: "in_progress",
        limit: "20",
      });
      if (libraryId) {
        searchParams.set("library_id", String(libraryId));
      }
      return api<ProgressListResponse>(`/progress?${searchParams.toString()}`);
    },
  });
}

export interface ContinueWatchingItem {
  progress: ProgressEntry;
  detail: ItemDetail | undefined;
  isLoading: boolean;
}

export function useContinueWatching(libraryId?: number): {
  items: ContinueWatchingItem[];
  isLoading: boolean;
} {
  const { data: progressData, isLoading: progressLoading } = useProgressList(libraryId);

  const entries = progressData?.progress ?? [];

  const detailQueries = useQueries({
    queries: entries.map((entry) => ({
      queryKey: catalogKeys.itemDetail(entry.media_item_id),
      queryFn: () => fetchCatalogItemDetail(entry.media_item_id),
      enabled: !!entry.media_item_id,
      staleTime: 2 * 60 * 1000,
    })),
  });

  const items: ContinueWatchingItem[] = entries.map((entry, i) => ({
    progress: entry,
    detail: detailQueries[i]?.data,
    isLoading: detailQueries[i]?.isLoading ?? true,
  }));

  return {
    items,
    isLoading: progressLoading,
  };
}
