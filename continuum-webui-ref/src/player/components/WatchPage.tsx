import { useCallback } from "react";
import type { WatchPageProps } from "../types";
import { usePlaybackSession } from "../hooks/usePlaybackSession";
import { usePlayerConfig } from "../context/PlayerConfigContext";
import { playerFetch } from "../player-fetch";
import { resolvePlayableSubtitles } from "../utils/playableSubtitles";
import { VideoPlayer } from "./VideoPlayer";

/**
 * WatchPage is the top-level player component.
 * Starts a playback session, then renders the VideoPlayer once the stream is ready.
 */
export function WatchPage({
  contentId,
  title,
  year,
  fileId,
  versions,
  subtitles,
  initialPosition,
  qualityPreference,
  preferredSubtitleLanguage,
  subtitleMode,
  showForcedSubtitles,
  profileLanguage,
  intro,
  credits,
  seriesContext,
  onNavigateEpisode,
  onExit,
  resumeHints,
}: WatchPageProps) {
  const config = usePlayerConfig();
  const session = usePlaybackSession(
    versions,
    fileId,
    initialPosition,
    qualityPreference,
    resumeHints,
  );

  const audioTracks = versions.find((v) => v.file_id === session.mediaFileId)?.audio_tracks ?? [];
  const playableSubtitles = resolvePlayableSubtitles(session.subtitleUrls, subtitles);

  const handleSwitchVersion = useCallback(
    (newFileId: number, currentPosition: number) => {
      session.switchVersion(newFileId, currentPosition);
    },
    [session.switchVersion],
  );

  const handleSwitchAudio = useCallback(
    (index: number, currentPosition: number) => {
      session.switchAudioTrack(index, currentPosition);
      const seriesId = seriesContext?.seriesId;
      if (seriesId && audioTracks[index]) {
        playerFetch(config, `/audio-prefs/${seriesId}`, {
          method: "PUT",
          body: JSON.stringify({
            audio_track_index: index,
            audio_language: audioTracks[index].language ?? "",
          }),
        }).catch(() => {
          // Best effort.
        });
      }
    },
    [session.switchAudioTrack, seriesContext, audioTracks, config],
  );

  const handleSubtitleChanged = useCallback(
    (index: number | null) => {
      const seriesId = seriesContext?.seriesId ?? contentId;
      if (!seriesId) return;

      const track = index !== null ? playableSubtitles.find((s) => s.index === index) : null;

      playerFetch(config, `/subtitle-prefs/${seriesId}`, {
        method: "PUT",
        body: JSON.stringify({
          subtitle_language: track?.language ?? "",
          subtitle_track_index: index ?? -1,
          subtitle_mode: subtitleMode ?? "auto",
        }),
      }).catch(() => {
        // Best effort.
      });
    },
    [config, seriesContext, contentId, playableSubtitles, subtitleMode],
  );

  if (session.loading) {
    return (
      <div className="fixed inset-0 z-50 flex items-center justify-center bg-black">
        <div className="flex flex-col items-center gap-3">
          <div className="h-8 w-8 animate-spin rounded-full border-2 border-white/20 border-t-white" />
          <span className="text-sm text-white/60">Loading player...</span>
        </div>
      </div>
    );
  }

  if (session.error || !session.streamUrl || !session.sessionId) {
    return (
      <div className="fixed inset-0 z-50 flex items-center justify-center bg-black">
        <div className="flex flex-col items-center gap-4 px-6 text-center">
          <div className="text-sm text-white/60">{session.error ?? "Failed to start playback"}</div>
          <button
            onClick={() => {
              void onExit();
            }}
            type="button"
            className="rounded bg-white/10 px-4 py-2 text-sm text-white transition-colors hover:bg-white/20"
          >
            Go Back
          </button>
        </div>
      </div>
    );
  }

  // Find the duration of the selected file so the player knows the total
  // length even when the stream is chunked (no Content-Length header).
  const selectedDuration =
    session.durationSeconds ??
    versions.find((v) => v.file_id === session.mediaFileId)?.duration ??
    versions[0]?.duration;
  const selectedVersion = versions.find((v) => v.file_id === session.mediaFileId) ?? versions[0];

  return (
    <VideoPlayer
      key={`${session.sessionId}-${session.audioTrackIndex}`}
      title={title}
      year={year}
      streamUrl={session.streamUrl}
      playMethod={session.playMethod!}
      playbackInfo={session.playbackInfo}
      sessionId={session.sessionId}
      selectedVersion={selectedVersion}
      versions={versions}
      activeFileId={session.mediaFileId}
      onSwitchVersion={handleSwitchVersion}
      subtitleUrls={playableSubtitles}
      initialPosition={session.initialPosition}
      preferredSubtitleLanguage={preferredSubtitleLanguage}
      subtitleMode={subtitleMode}
      showForcedSubtitles={showForcedSubtitles}
      profileLanguage={profileLanguage}
      intro={intro}
      credits={credits}
      duration={selectedDuration}
      qualityPreference={qualityPreference}
      seriesContext={seriesContext}
      onNavigateEpisode={onNavigateEpisode}
      onExit={onExit}
      onRefreshSubtitles={session.refreshSubtitles}
      audioTracks={audioTracks}
      activeAudioIndex={session.audioTrackIndex}
      onAudioSelect={handleSwitchAudio}
      onSubtitleChanged={handleSubtitleChanged}
    />
  );
}
