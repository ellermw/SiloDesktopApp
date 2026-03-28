import { useEffect, useMemo } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useParams, useSearchParams, useNavigate } from "react-router";
import { getAccessToken } from "@/api/client";
import { useDocumentTitle } from "@/hooks/useDocumentTitle";
import { applyPlaybackProgressToCache } from "@/hooks/queries/playbackProgressCache";
import { storage } from "@/utils/storage";
import { useWatchDetail } from "@/hooks/queries/items";
import { useCurrentProfile } from "@/hooks/useCurrentProfile";
import { invalidatePlaybackSurfaceQueries } from "@/hooks/queries/playbackSurfaceRefresh";
import { PlayerConfigProvider, WatchPage } from "@/player";
import type {
  PlaybackExitState,
  PlayerConfig,
  PlayerSubtitleInfo,
  PlayerTimeRange,
  ResumeHints,
  SubtitleMode,
} from "@/player";

/**
 * WatchRoute is the thin glue between the app and the portable player module.
 * It fetches the watch detail, builds a PlayerConfig from the app's auth state,
 * and renders the player.
 */
export default function WatchRoute() {
  const queryClient = useQueryClient();
  const { id } = useParams<{ id: string }>();
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const fileIdParam = searchParams.get("fileId");
  const fileId = fileIdParam ? parseInt(fileIdParam, 10) : undefined;

  const { data: item, isLoading: loading, error } = useWatchDetail(id, fileId);
  const { profile: currentProfile } = useCurrentProfile();

  useDocumentTitle(item?.title ?? "Watch");

  const qualityPreference = currentProfile?.quality_preference || null;
  const preferredSubtitleLanguage =
    item?.effective_subtitle_language !== undefined
      ? item.effective_subtitle_language
      : currentProfile?.subtitle_language || null;
  const subtitleMode = (item?.effective_subtitle_mode ??
    currentProfile?.subtitle_mode ??
    "auto") as SubtitleMode;
  const showForcedSubtitles =
    item?.effective_show_forced_subtitles ?? currentProfile?.show_forced_subtitles ?? true;
  const profileLanguage = currentProfile?.language || null;

  // Memoized so the context reference stays stable — an unstable reference
  // would reset the progress reporting interval on every parent re-render.
  const playerConfig = useMemo<PlayerConfig>(
    () => ({
      apiBaseUrl: "/api/v1",
      getAccessToken: () => getAccessToken(),
      getProfileId: () => storage.get(storage.KEYS.PROFILE_ID),
    }),
    [],
  );

  useEffect(() => {
    return () => {
      void invalidatePlaybackSurfaceQueries(queryClient);
    };
  }, [queryClient]);

  if (loading) {
    return (
      <div className="fixed inset-0 z-50 flex items-center justify-center bg-[radial-gradient(circle_at_top,#1c1c24_0%,#09090c_58%,#030303_100%)] px-6">
        <div className="surface-panel-subtle flex min-w-[260px] flex-col items-center gap-4 rounded-[1.8rem] px-8 py-7 text-center">
          <div className="h-8 w-8 animate-spin rounded-full border-2 border-white/20 border-t-white" />
          <div className="space-y-1">
            <p className="text-sm font-medium text-white">Preparing playback</p>
            <p className="text-xs text-white/55">
              Loading stream details, subtitles, and resume state.
            </p>
          </div>
        </div>
      </div>
    );
  }

  if (error || !item || !id) {
    return (
      <div className="fixed inset-0 z-50 flex items-center justify-center bg-[radial-gradient(circle_at_top,#1c1c24_0%,#09090c_58%,#030303_100%)] px-6">
        <div className="surface-panel-subtle flex max-w-md flex-col items-center gap-4 rounded-[1.8rem] px-8 py-8 text-center">
          <div className="space-y-2">
            <p className="text-base font-semibold text-white">Playback unavailable</p>
            <div className="text-sm text-white/60">
            {error instanceof Error ? error.message : "Item not found"}
            </div>
          </div>
          <button
            onClick={() => navigate(-1)}
            type="button"
            className="rounded-[0.95rem] bg-white/10 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-white/20"
          >
            Go Back
          </button>
        </div>
      </div>
    );
  }

  // Map app types to player types.
  const subtitles: PlayerSubtitleInfo[] = item.subtitles.map((s, i) => ({
    index: i,
    language: s.language,
    codec: s.codec,
    label: s.title || s.language,
    source: s.source === "external" ? "external" : "embedded",
    forced: s.forced,
    url: "", // Will be populated by the playback session response
  }));

  const intro: PlayerTimeRange | null = item.intro ?? null;
  const credits: PlayerTimeRange | null = item.credits ?? null;
  const initialPosition =
    item.user_data?.played === true ? 0 : (item.user_data?.position_seconds ?? 0);

  const resumeHints: ResumeHints | undefined =
    item.user_data?.last_file_id != null
      ? {
          lastFileId: item.user_data.last_file_id,
          lastResolution: item.user_data.last_resolution,
          lastHDR: item.user_data.last_hdr,
          lastCodecVideo: item.user_data.last_codec_video,
        }
      : undefined;

  function handleExit(state?: PlaybackExitState) {
    const contentId = item?.content_id ?? id;
    if (contentId && state) {
      applyPlaybackProgressToCache(queryClient, {
        contentId,
        positionSeconds: state.positionSeconds,
        durationSeconds: state.durationSeconds,
        lastFileId: state.lastFileId,
        lastResolution: state.lastResolution,
        lastHDR: state.lastHDR,
        lastCodecVideo: state.lastCodecVideo,
      });
    }

    navigate(-1);
  }

  return (
    <PlayerConfigProvider config={playerConfig}>
      <WatchPage
        contentId={id}
        title={item.title}
        year={item.year}
        fileId={fileId}
        versions={item.versions}
        subtitles={subtitles}
        initialPosition={initialPosition}
        qualityPreference={qualityPreference}
        preferredSubtitleLanguage={preferredSubtitleLanguage}
        subtitleMode={subtitleMode}
        showForcedSubtitles={showForcedSubtitles}
        profileLanguage={profileLanguage}
        intro={intro}
        credits={credits}
        seriesContext={
          item.series_id
            ? {
                seriesId: item.series_id,
                seriesTitle: item.series_title,
                currentSeason: item.season_number ?? 0,
                currentEpisode: item.episode_number ?? 0,
                episodes: [],
              }
            : undefined
        }
        onNavigateEpisode={(nextContentId) => navigate(`/watch/${nextContentId}`)}
        onExit={handleExit}
        resumeHints={resumeHints}
      />
    </PlayerConfigProvider>
  );
}
