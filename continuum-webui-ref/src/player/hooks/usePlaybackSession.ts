import { useCallback, useEffect, useRef, useState } from "react";
import { usePlayerConfig } from "../context/PlayerConfigContext";
import { playerFetch } from "../player-fetch";
import {
  useCodecDetection,
  QUALITY_TO_RESOLUTION,
  RESOLUTION_ORDER,
  matchByTraits,
  getPlaybackEnvironmentSnapshot,
} from "./useCodecDetection";
import type {
  ChangeAudioResponse,
  PlaybackSessionPlaybackInfo,
  PlaybackSessionResponse,
  PlayMethod,
  PlayerFileVersion,
  PlayerSubtitleInfo,
  ResumeHints,
} from "../types";

interface PlaybackSessionState {
  streamUrl: string | null;
  playMethod: PlayMethod | null;
  sessionId: string | null;
  mediaFileId: number | null;
  initialPosition: number;
  audioTrackIndex: number;
  durationSeconds: number | null;
  subtitleUrls: PlayerSubtitleInfo[];
  playbackInfo: PlaybackSessionPlaybackInfo | null;
  loading: boolean;
  error: string | null;
}

interface DownloadedSubtitle {
  id: number;
  media_file_id: number;
  provider: string;
  language: string;
  format: string;
  release_name: string;
  score: number;
  hearing_impaired: boolean;
}

interface UsePlaybackSessionResult extends PlaybackSessionState {
  switchVersion: (fileId: number, currentPosition: number) => void;
  switchAudioTrack: (index: number, currentPosition: number) => void;
  refreshSubtitles: () => void;
}

function buildStreamUrl(
  apiBaseUrl: string,
  streamPath: string,
  token: string | null,
  playMethod: PlayMethod,
  initialPosition: number,
): string {
  const params = new URLSearchParams();

  if (token) {
    params.set("token", token);
  }

  if (playMethod === "remux" && initialPosition > 0) {
    params.set("seek", initialPosition.toFixed(3));
  }

  const query = params.toString();
  // If the backend returned an absolute URL (proxy mode), use it directly.
  const base =
    streamPath.startsWith("http://") || streamPath.startsWith("https://")
      ? streamPath
      : `${apiBaseUrl}${streamPath}`;
  return `${base}${query ? `?${query}` : ""}`;
}

/**
 * Manages the playback session lifecycle:
 * 1. On mount: detect codecs → select best version → POST /playback/start → get stream_url
 * 2. switchVersion(): stop current session → start new session at same position
 * 3. On unmount: DELETE /playback/{session_id} + keepalive fallback
 */
export function usePlaybackSession(
  versions: PlayerFileVersion[],
  fileId?: number,
  initialPosition = 0,
  qualityPreference?: string | null,
  resumeHints?: ResumeHints,
): UsePlaybackSessionResult {
  const config = usePlayerConfig();
  const { capabilities, selectBestVersion } = useCodecDetection();
  const [state, setState] = useState<PlaybackSessionState>({
    streamUrl: null,
    playMethod: null,
    sessionId: null,
    mediaFileId: null,
    initialPosition: 0,
    audioTrackIndex: 0,
    durationSeconds: null,
    subtitleUrls: [],
    playbackInfo: null,
    loading: true,
    error: null,
  });

  const sessionIdRef = useRef<string | null>(null);
  const startedRef = useRef(false);
  const switchingRef = useRef(false);

  const startSession = useCallback(
    async (targetFileId: number, position: number) => {
      const profileId = config.getProfileId();

      // Cap max_resolution with quality preference (use the lower of the two).
      let effectiveMaxRes = capabilities.max_resolution;
      if (qualityPreference && qualityPreference !== "auto") {
        const prefRes = QUALITY_TO_RESOLUTION[qualityPreference];
        if (prefRes) {
          const screenRank = RESOLUTION_ORDER[effectiveMaxRes] ?? 0;
          const prefRank = RESOLUTION_ORDER[prefRes] ?? 0;
          if (prefRank > 0 && prefRank < screenRank) {
            effectiveMaxRes = prefRes;
          }
        }
      }

      const body = JSON.stringify({
        file_id: targetFileId,
        profile_id: profileId ?? "",
        start_position: position > 0 ? position : undefined,
        codecs_video: capabilities.codecs_video,
        codecs_audio: capabilities.codecs_audio,
        containers: capabilities.containers,
        max_resolution: effectiveMaxRes,
        hdr: capabilities.hdr,
      });

      const environment = getPlaybackEnvironmentSnapshot();
      console.info("[playback/start] client capabilities", {
        targetFileId,
        qualityPreference: qualityPreference ?? "auto",
        effectiveMaxRes,
        capabilities,
        environment,
      });

      const session = await playerFetch<PlaybackSessionResponse>(config, "/playback/start", {
        method: "POST",
        body,
      });

      sessionIdRef.current = session.session_id;

      // Append token as query param for native media elements
      // that can't set Authorization headers.
      const token = config.getAccessToken();
      const restoredPosition = session.position ?? 0;

      setState({
        streamUrl: buildStreamUrl(
          config.apiBaseUrl,
          session.stream_url,
          token,
          session.play_method,
          restoredPosition,
        ),
        playMethod: session.play_method,
        sessionId: session.session_id,
        mediaFileId: session.media_file_id,
        initialPosition: restoredPosition,
        audioTrackIndex: session.audio_track_index ?? 0,
        durationSeconds: session.duration_seconds ?? null,
        subtitleUrls: (session.subtitle_urls ?? []).map((s) => ({
          ...s,
          url: buildStreamUrl(config.apiBaseUrl, s.url, token, "direct", 0),
        })),
        playbackInfo: session.playback_info ?? null,
        loading: false,
        error: null,
      });
    },
    [config, capabilities, qualityPreference],
  );

  const stopSession = useCallback(
    async (sessionId: string) => {
      await playerFetch(config, `/playback/${sessionId}`, {
        method: "DELETE",
      });
    },
    [config],
  );

  // Start session once on mount. Refs capture latest values without
  // needing them in the dependency array, preventing duplicate calls.
  useEffect(() => {
    if (startedRef.current) return;
    startedRef.current = true;

    (async () => {
      try {
        // Select the file to play.
        let selectedFileId = fileId;
        if (!selectedFileId && resumeHints?.lastFileId) {
          const exact = versions.find((v) => v.file_id === resumeHints.lastFileId);
          if (exact) selectedFileId = exact.file_id;
        }
        if (!selectedFileId && resumeHints) {
          const traitMatch = matchByTraits(versions, resumeHints);
          if (traitMatch) selectedFileId = traitMatch.file_id;
        }
        if (!selectedFileId) {
          const best = selectBestVersion(versions, qualityPreference);
          if (!best) {
            setState((s) => ({
              ...s,
              loading: false,
              error: "No compatible file version found",
            }));
            return;
          }
          selectedFileId = best.file_id;
        }

        await startSession(selectedFileId, initialPosition);
      } catch (err) {
        setState((s) => ({
          ...s,
          loading: false,
          error: err instanceof Error ? err.message : "Failed to start playback",
        }));
      }
    })();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Clean up session on unmount.
  useEffect(() => {
    return () => {
      const sid = sessionIdRef.current;
      if (!sid) return;

      const token = config.getAccessToken();
      const profileId = config.getProfileId();
      const url = `${config.apiBaseUrl}/playback/${sid}`;

      const headers: Record<string, string> = {};
      if (token) headers["Authorization"] = `Bearer ${token}`;
      if (profileId) headers["X-Profile-Id"] = profileId;

      // sendBeacon doesn't support DELETE, so use fetch with keepalive.
      fetch(url, {
        method: "DELETE",
        headers,
        keepalive: true,
      }).catch(() => {
        // Best effort — if fetch fails, session will time out server-side.
      });
    };
  }, [config]);

  const refreshSubtitles = useCallback(() => {
    const mediaFile = state.mediaFileId;
    const sid = sessionIdRef.current;
    if (!mediaFile || !sid) return;

    (async () => {
      try {
        const resp = await playerFetch<{ subtitles: DownloadedSubtitle[] }>(
          config,
          `/subtitles/${mediaFile}`,
        );
        const downloaded = resp.subtitles ?? [];
        if (downloaded.length === 0) return;

        setState((prev) => {
          // Filter out any previously added downloaded tracks
          const existing = prev.subtitleUrls.filter((s) => s.source !== "downloaded");
          const baseIndex = existing.length > 0 ? Math.max(...existing.map((s) => s.index)) + 1 : 0;
          const token = config.getAccessToken();
          const newTracks: PlayerSubtitleInfo[] = downloaded.map((dl, i) => ({
            index: baseIndex + i,
            language: dl.language,
            codec: dl.format,
            label: `${dl.release_name} (${dl.provider})`,
            source: "downloaded" as const,
            url: buildStreamUrl(
              config.apiBaseUrl,
              `/stream/${sid}/subtitles/${baseIndex + i}`,
              token,
              "direct",
              0,
            ),
          }));
          return { ...prev, subtitleUrls: [...existing, ...newTracks] };
        });
      } catch {
        // Best effort — subtitle refresh failure shouldn't disrupt playback.
      }
    })();
  }, [config, state.mediaFileId]);

  const switchAudioTrack = useCallback(
    (index: number, currentPosition: number) => {
      const sid = sessionIdRef.current;
      if (!sid) return;

      (async () => {
        try {
          const resp = await playerFetch<ChangeAudioResponse>(config, `/playback/${sid}/audio`, {
            method: "PATCH",
            body: JSON.stringify({
              audio_track_index: index,
              position: currentPosition,
            }),
          });

          const token = config.getAccessToken();
          setState((prev) => ({
            ...prev,
            streamUrl: buildStreamUrl(
              config.apiBaseUrl,
              resp.stream_url,
              token,
              resp.play_method,
              currentPosition,
            ),
            playMethod: resp.play_method,
            audioTrackIndex: resp.audio_track_index,
            playbackInfo: resp.playback_info ?? prev.playbackInfo,
            initialPosition: currentPosition,
          }));
        } catch (err) {
          console.error("Failed to switch audio track:", err);
        }
      })();
    },
    [config],
  );

  const switchVersion = useCallback(
    (newFileId: number, currentPosition: number) => {
      if (switchingRef.current) return;
      if (newFileId === state.mediaFileId) return;
      switchingRef.current = true;

      setState((s) => ({ ...s, loading: true, error: null }));

      (async () => {
        try {
          // Stop current session before starting the new one.
          const oldSessionId = sessionIdRef.current;
          if (oldSessionId) {
            await stopSession(oldSessionId).catch(() => {
              // Best effort — continue to start new session.
            });
          }

          await startSession(newFileId, currentPosition);
        } catch (err) {
          setState((s) => ({
            ...s,
            loading: false,
            error: err instanceof Error ? err.message : "Failed to switch version",
          }));
        } finally {
          switchingRef.current = false;
        }
      })();
    },
    [state.mediaFileId, startSession, stopSession],
  );

  return { ...state, switchVersion, switchAudioTrack, refreshSubtitles };
}
