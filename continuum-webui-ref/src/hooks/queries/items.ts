import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/client";
import type { AdminJob, ItemDetail, WatchDetail } from "@/api/types";
import { catalogKeys, episodeKeys, itemKeys } from "./keys";
import { toast } from "sonner";
import { getCachedWatchedInvalidationKeys } from "@/pages/ItemDetail/watchedState";
import { invalidateMediaSurfaceQueries } from "./mediaSurfaceRefresh";

export function useWatchDetail(id: string | undefined, fileId?: number) {
  return useQuery({
    queryKey: itemKeys.watchDetail(id!, fileId),
    queryFn: () => api<WatchDetail>(fileId ? `/watch/${id}?fileId=${fileId}` : `/watch/${id}`),
    enabled: !!id,
    staleTime: 0,
  });
}

type RefreshMutationItem = Pick<ItemDetail, "content_id" | "type" | "series_id" | "season_number">;

interface ItemRefreshJobResult {
  requested_content_id?: string;
  refresh_content_id?: string;
  scan_path?: string;
  matched_files?: number;
  scan_result?: {
    new?: number;
  };
}

async function waitForAdminJob(jobId: string): Promise<AdminJob> {
  for (;;) {
    const job = await api<AdminJob>(`/admin/jobs/${jobId}`);
    if (job.status === "completed") {
      return job;
    }
    if (job.status === "failed") {
      throw new Error(job.error_message || job.message || "Refresh failed");
    }
    await new Promise((resolve) => window.setTimeout(resolve, 2_000));
  }
}

export function useRefreshItemMetadata() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (item: RefreshMutationItem) => {
      const job = await api<AdminJob>(`/admin/items/${item.content_id}/refresh-metadata`, {
        method: "POST",
      });
      const completed = await waitForAdminJob(job.id);
      return { job: completed, item };
    },
    onSuccess: async ({ job, item }) => {
      const result = (job.result_payload ?? {}) as ItemRefreshJobResult;
      const refreshContentID = result.refresh_content_id;
      const newFiles = result.scan_result?.new ?? 0;

      if (newFiles > 0) {
        toast.success(
          `Metadata refreshed. Found ${newFiles} new file version${newFiles === 1 ? "" : "s"}`,
        );
      } else {
        toast.success("Metadata refreshed");
      }

      await Promise.all([
        queryClient.invalidateQueries({ queryKey: itemKeys.detail(item.content_id) }),
        queryClient.invalidateQueries({ queryKey: catalogKeys.itemDetail(item.content_id) }),
        queryClient.invalidateQueries({ queryKey: itemKeys.watchDetail(item.content_id) }),
      ]);

      if (refreshContentID && refreshContentID !== item.content_id) {
        await Promise.all([
          queryClient.invalidateQueries({ queryKey: itemKeys.detail(refreshContentID) }),
          queryClient.invalidateQueries({ queryKey: catalogKeys.itemDetail(refreshContentID) }),
        ]);
      }

      if (item.type === "series") {
        await Promise.all([
          queryClient.invalidateQueries({ queryKey: episodeKeys.seasons(item.content_id) }),
          queryClient.invalidateQueries({ queryKey: catalogKeys.seriesSeasons(item.content_id) }),
          queryClient.invalidateQueries({ queryKey: episodeKeys.all }),
        ]);
      } else if ((item.type === "season" || item.type === "episode") && item.series_id) {
        await Promise.all([
          queryClient.invalidateQueries({ queryKey: itemKeys.detail(item.series_id) }),
          queryClient.invalidateQueries({ queryKey: catalogKeys.itemDetail(item.series_id) }),
          queryClient.invalidateQueries({ queryKey: episodeKeys.seasons(item.series_id) }),
          queryClient.invalidateQueries({ queryKey: catalogKeys.seriesSeasons(item.series_id) }),
          queryClient.invalidateQueries({ queryKey: episodeKeys.all }),
        ]);
        if (item.season_number != null) {
          await queryClient.invalidateQueries({
            queryKey: episodeKeys.bySeason(item.series_id, item.season_number),
          });
          await queryClient.invalidateQueries({
            queryKey: episodeKeys.seasonDetail(item.series_id, item.season_number),
          });
          await queryClient.invalidateQueries({
            queryKey: catalogKeys.seasonEpisodes(item.series_id, item.season_number),
          });
          await queryClient.invalidateQueries({
            queryKey: catalogKeys.seasonDetail(item.series_id, item.season_number),
          });
        }
      }
    },
    onError: (err) => {
      toast.error(err instanceof Error ? err.message : "Refresh failed");
    },
  });
}

type WatchedMutationItem = Pick<
  ItemDetail,
  "content_id" | "type" | "series_id" | "season_number" | "user_data"
>;

export function useWatchedStateMutation(item: WatchedMutationItem) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (nextPlayed: boolean) =>
      api(`/watched/${item.content_id}`, {
        method: nextPlayed ? "POST" : "DELETE",
      }),
    onError: (err) => {
      toast.error(err instanceof Error ? err.message : "Failed to update watched state");
    },
    onSuccess: (_data, nextPlayed) => {
      toast.success(nextPlayed ? "Marked as watched" : "Marked as unwatched");
    },
    onSettled: () => {
      return invalidateMediaSurfaceQueries(queryClient, {
        itemId: item.content_id,
        watchedKeys: getCachedWatchedInvalidationKeys(queryClient, item),
      });
    },
  });
}
