import type { QueryClient } from "@tanstack/react-query";
import type { ItemDetail, LeafItemUserData, ProgressListResponse, WatchDetail } from "@/api/types";
import { catalogKeys, itemKeys, progressKeys } from "./keys";

export interface PlaybackProgressSnapshot {
  contentId: string;
  positionSeconds: number;
  durationSeconds?: number;
  lastFileId?: number | null;
  lastResolution?: string;
  lastHDR?: boolean;
  lastCodecVideo?: string;
  updatedAt?: string;
}

function mergeLeafProgress(
  existing: LeafItemUserData | undefined,
  snapshot: PlaybackProgressSnapshot,
): LeafItemUserData {
  const positionSeconds = Math.max(0, snapshot.positionSeconds);
  const durationSeconds = snapshot.durationSeconds ?? existing?.duration_seconds;
  const played =
    durationSeconds != null && durationSeconds > 0
      ? positionSeconds >= durationSeconds
      : existing?.played === true && positionSeconds === 0;

  return {
    played,
    is_in_progress: positionSeconds > 0 && !played,
    position_seconds: positionSeconds,
    duration_seconds: durationSeconds,
    last_file_id: snapshot.lastFileId ?? existing?.last_file_id,
    last_resolution: snapshot.lastResolution ?? existing?.last_resolution,
    last_hdr: snapshot.lastHDR ?? existing?.last_hdr,
    last_codec_video: snapshot.lastCodecVideo ?? existing?.last_codec_video,
  };
}

function applySnapshotToItemDetail(
  existing: ItemDetail | undefined,
  snapshot: PlaybackProgressSnapshot,
): ItemDetail | undefined {
  if (!existing) return existing;

  const currentUserData =
    existing.user_data && "position_seconds" in existing.user_data ? existing.user_data : undefined;

  return {
    ...existing,
    user_data: mergeLeafProgress(currentUserData, snapshot),
  };
}

function applySnapshotToWatchDetail(
  existing: WatchDetail | undefined,
  snapshot: PlaybackProgressSnapshot,
): WatchDetail | undefined {
  if (!existing) return existing;

  return {
    ...existing,
    user_data: mergeLeafProgress(existing.user_data, snapshot),
  };
}

export function applyPlaybackProgressToCache(
  queryClient: QueryClient,
  snapshot: PlaybackProgressSnapshot,
) {
  queryClient.setQueryData<ItemDetail>(catalogKeys.itemDetail(snapshot.contentId), (existing) =>
    applySnapshotToItemDetail(existing, snapshot),
  );
  queryClient.setQueryData<ItemDetail>(itemKeys.detail(snapshot.contentId), (existing) =>
    applySnapshotToItemDetail(existing, snapshot),
  );
  queryClient.setQueryData<WatchDetail>(itemKeys.watchDetail(snapshot.contentId), (existing) =>
    applySnapshotToWatchDetail(existing, snapshot),
  );

  const updatedAt = snapshot.updatedAt ?? new Date().toISOString();
  for (const [queryKey, existing] of queryClient.getQueriesData<ProgressListResponse>({
    queryKey: progressKeys.all,
  })) {
    if (!existing) continue;

    const nextProgress = existing.progress.map((entry) =>
      entry.media_item_id === snapshot.contentId
        ? {
            ...entry,
            position_seconds: Math.max(0, snapshot.positionSeconds),
            duration_seconds: snapshot.durationSeconds ?? entry.duration_seconds,
            completed:
              (snapshot.durationSeconds ?? entry.duration_seconds) > 0 &&
              Math.max(0, snapshot.positionSeconds) >=
                (snapshot.durationSeconds ?? entry.duration_seconds),
            updated_at: updatedAt,
          }
        : entry,
    );

    queryClient.setQueryData<ProgressListResponse>(queryKey, {
      ...existing,
      progress: nextProgress,
    });
  }
}
