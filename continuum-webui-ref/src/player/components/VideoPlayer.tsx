import { useCallback, useEffect, useRef, useState } from "react";
import { resolveSubtitleAutoSelect } from "../utils/subtitleSort";
import type HlsType from "hls.js";
import { PlayerControls } from "./PlayerControls";
import { PlaybackInfoOverlay } from "./PlaybackInfoOverlay";
import { PlaybackNoticeOverlay } from "./PlaybackNoticeOverlay";
import { IntroSkipButton } from "./IntroSkipButton";
import { NextEpisodeOverlay } from "./NextEpisodeOverlay";
import { usePlaybackRealtime } from "../hooks/usePlaybackRealtime";
import { useWatchProgress } from "../hooks/useWatchProgress";
import { useKeyboardShortcuts } from "../hooks/useKeyboardShortcuts";
import { useRemuxSeeking } from "../hooks/useRemuxSeeking";
import { useSubtitleTracks } from "../hooks/useSubtitleTracks";
import { useSubtitleAppearance } from "../hooks/useSubtitleAppearance";
import { useNextEpisode } from "../hooks/useNextEpisode";
import { useTranscodeQuality } from "../hooks/useTranscodeQuality";
import { getPersistedVolume, persistVolume } from "./VolumeControl";
import { usePlayerConfig } from "../context/PlayerConfigContext";
import { deriveDisplayedPlaybackState } from "../playback-info";
import type { PlaybackRealtimeCommandEnvelope } from "../realtime-protocol";
import { resolvePendingSeekTime } from "../utils/pendingSeek";
import type {
  PlaybackExitState,
  PlaybackSessionPlaybackInfo,
  PlayerAudioTrack,
  PlayMethod,
  PlayerFileVersion,
  PlayerSubtitleInfo,
  PlayerTimeRange,
  SeriesContext,
  SubtitleMode,
} from "../types";

interface VideoPlayerProps {
  title: string;
  year?: number;
  streamUrl: string;
  playMethod: PlayMethod;
  playbackInfo: PlaybackSessionPlaybackInfo | null;
  sessionId: string;
  selectedVersion?: PlayerFileVersion;
  versions?: PlayerFileVersion[];
  activeFileId?: number | null;
  onSwitchVersion?: (fileId: number, currentPosition: number) => void;
  subtitleUrls: PlayerSubtitleInfo[];
  initialPosition: number;
  preferredSubtitleLanguage?: string | null;
  subtitleMode?: SubtitleMode;
  showForcedSubtitles?: boolean;
  profileLanguage?: string | null;
  intro: PlayerTimeRange | null;
  credits: PlayerTimeRange | null;
  duration?: number;
  seriesContext?: SeriesContext;
  onNavigateEpisode?: (contentId: string) => void;
  qualityPreference?: string | null;
  onRefreshSubtitles?: () => void;
  audioTracks?: PlayerAudioTrack[];
  activeAudioIndex?: number;
  onAudioSelect?: (index: number, currentPosition: number) => void;
  onSubtitleChanged?: (index: number | null) => void;
  onExit: (state?: PlaybackExitState) => void | Promise<void>;
}

/** Preload hls.js eagerly so it's cached before the first transcode. */
const hlsPromise: Promise<typeof HlsType> = import("hls.js").then((m) => m.default);
const EXIT_PROGRESS_FLUSH_TIMEOUT_MS = 1_000;

interface PlaybackNoticeState {
  title?: string;
  message: string;
  tone: "info" | "warning";
}

function readNumericPayload(
  payload: Record<string, unknown> | undefined,
  ...keys: string[]
): number | null {
  for (const key of keys) {
    const value = payload?.[key];
    if (typeof value === "number" && Number.isFinite(value)) {
      return value;
    }
  }
  return null;
}

function readStringPayload(
  payload: Record<string, unknown> | undefined,
  ...keys: string[]
): string | null {
  for (const key of keys) {
    const value = payload?.[key];
    if (typeof value === "string" && value.trim() !== "") {
      return value;
    }
  }
  return null;
}

export function VideoPlayer({
  title,
  year,
  streamUrl,
  playMethod,
  playbackInfo: _playbackInfo,
  sessionId,
  selectedVersion,
  versions = [],
  activeFileId,
  onSwitchVersion,
  subtitleUrls,
  initialPosition,
  preferredSubtitleLanguage,
  subtitleMode,
  showForcedSubtitles,
  profileLanguage,
  intro,
  credits,
  duration: propDuration,
  seriesContext,
  onNavigateEpisode,
  qualityPreference,
  onRefreshSubtitles,
  audioTracks = [],
  activeAudioIndex = 0,
  onAudioSelect,
  onSubtitleChanged,
  onExit,
}: VideoPlayerProps) {
  const playerConfig = usePlayerConfig();

  // Refs
  const videoRef = useRef<HTMLVideoElement>(null);
  const containerRef = useRef<HTMLDivElement>(null);
  const hlsRef = useRef<HlsType | null>(null);
  const mediaRecoveryAttemptsRef = useRef(0);
  const lastRecoveryRef = useRef(0);
  const transcodeOffsetRef = useRef(0);
  const backendDurationRef = useRef(propDuration ?? 0);

  // Playback state
  const [playing, setPlaying] = useState(false);
  const [currentTime, setCurrentTime] = useState(0);
  const [pendingSeekTime, setPendingSeekTime] = useState<number | null>(null);
  const [duration, setDuration] = useState(propDuration ?? 0);
  const [buffered, setBuffered] = useState<TimeRanges | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isFullscreen, setIsFullscreen] = useState(false);
  const [buffering, setBuffering] = useState(false);
  const bufferingTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const [awaitingFirstFrame, setAwaitingFirstFrame] = useState(true);
  const [isExiting, setIsExiting] = useState(false);
  const exitInProgressRef = useRef(false);
  const [notice, setNotice] = useState<PlaybackNoticeState | null>(null);

  // Volume (persisted via localStorage)
  const [volume, setVolume] = useState(() => getPersistedVolume().volume);
  const [muted, setMuted] = useState(() => getPersistedVolume().muted);

  // Subtitles
  const [activeSubtitleIndex, setActiveSubtitleIndex] = useState<number | null>(null);
  const lastSubtitleIndexRef = useRef<number | null>(null);

  // -- Transcode quality switching --
  // Remux also uses HLS (codec copy) via the transcode pipeline.
  const transcodeQuality = useTranscodeQuality({
    sessionId,
    selectedVersion,
    versions,
    playMethod,
    initialPosition,
    qualityPreference,
    transcodeAudio: _playbackInfo?.transcode_audio,
  });

  // Derive effective stream URL and play method.
  // Both transcode and remux go through HLS, so treat them as "transcode" for the player.
  const effectiveStreamUrl =
    playMethod === "transcode" || playMethod === "remux"
      ? (transcodeQuality.transcodeStreamUrl ?? "")
      : (transcodeQuality.transcodeStreamUrl ?? streamUrl);
  const effectivePlayMethod: PlayMethod =
    playMethod === "transcode" || playMethod === "remux" || transcodeQuality.transcodeStreamUrl
      ? "transcode"
      : playMethod;
  const backendDuration = transcodeQuality.durationSeconds ?? propDuration ?? 0;
  backendDurationRef.current = backendDuration;
  const effectiveInitialPosition = transcodeQuality.transcodeStreamUrl
    ? transcodeQuality.playerStartSeconds
    : initialPosition;
  const isPlayerReady = effectiveStreamUrl !== "";
  const displayedPlaybackState = deriveDisplayedPlaybackState({
    playMethod,
    playbackInfo: _playbackInfo,
    selectedVersion: transcodeQuality.effectiveVersion,
    transcodeStreamUrl: transcodeQuality.transcodeStreamUrl,
    activeQualityId: transcodeQuality.activeQualityId,
  });

  // -- Transcode time offset --
  useEffect(() => {
    transcodeOffsetRef.current =
      effectivePlayMethod === "transcode" ? transcodeQuality.timelineOffsetSeconds : 0;
  }, [effectivePlayMethod, transcodeQuality.timelineOffsetSeconds]);

  useEffect(() => {
    if (backendDuration > 0) {
      setDuration(backendDuration);
    }
  }, [backendDuration]);

  useEffect(() => {
    setNotice(null);
  }, [sessionId]);

  useEffect(() => {
    setPendingSeekTime(null);
  }, [effectiveStreamUrl]);

  // Promote fatal transcode errors to the player-level error state.
  // When transcode start fails (e.g. 4K blocked with no alternate file),
  // transcodeStreamUrl stays null, isPlayerReady stays false, and the
  // loading overlay covers the screen forever. Surface the error here
  // so the error overlay with "Go Back" appears instead.
  useEffect(() => {
    if (transcodeQuality.error && !isPlayerReady && !transcodeQuality.isTranscoding) {
      setError(transcodeQuality.error);
    }
  }, [transcodeQuality.error, isPlayerReady, transcodeQuality.isTranscoding]);

  // -- Remux seeking (callback-based) --
  // With remux now using HLS, this only handles direct play seeking.
  const { handleSeek } = useRemuxSeeking(
    videoRef,
    effectivePlayMethod,
    effectiveStreamUrl,
    effectiveInitialPosition,
  );

  const handlePlayerSeek = useCallback(
    (seconds: number) => {
      if (effectivePlayMethod !== "transcode") {
        handleSeek(seconds);
        return;
      }

      const video = videoRef.current;
      if (!video) return;

      setPendingSeekTime(seconds);
      setCurrentTime(seconds);

      const nativeSeconds = Math.max(0, seconds - transcodeOffsetRef.current);
      if (transcodeQuality.canSeekAnywhere) {
        video.currentTime = nativeSeconds;
        return;
      }

      const seekable = video.seekable;
      for (let i = 0; i < seekable.length; i++) {
        if (nativeSeconds >= seekable.start(i) && nativeSeconds <= seekable.end(i)) {
          video.currentTime = nativeSeconds;
          return;
        }
      }

      transcodeQuality.switchQuality(transcodeQuality.activeQualityId, seconds, true);
    },
    [
      effectivePlayMethod,
      handleSeek,
      transcodeQuality.activeQualityId,
      transcodeQuality.canSeekAnywhere,
      transcodeQuality.switchQuality,
    ],
  );

  // -- Keyboard seek adapter --
  // Keyboard shortcuts read native video.currentTime (e.g., 10) and add ±10s.
  // This wrapper adds the transcode offset so handlePlayerSeek receives
  // original-timeline values (e.g., 1210 instead of 20).
  const handleKeyboardSeek = useCallback(
    (seconds: number) => {
      handlePlayerSeek(seconds + transcodeOffsetRef.current);
    },
    [handlePlayerSeek],
  );

  // -- Watch progress reporting --
  const flushWatchProgress = useWatchProgress(sessionId, videoRef, transcodeOffsetRef);

  const buildExitState = useCallback((): PlaybackExitState => {
    const video = videoRef.current;
    const positionSeconds = Math.max(
      0,
      (video?.currentTime ?? currentTime) + transcodeOffsetRef.current,
    );
    const durationSeconds =
      duration > 0
        ? duration
        : backendDurationRef.current > 0
          ? backendDurationRef.current
          : undefined;

    return {
      positionSeconds,
      durationSeconds,
      lastFileId: activeFileId ?? selectedVersion?.file_id,
      lastResolution: selectedVersion?.resolution,
      lastHDR: selectedVersion?.hdr,
      lastCodecVideo: selectedVersion?.codec_video,
    };
  }, [activeFileId, currentTime, duration, selectedVersion]);

  const handleExit = useCallback(async () => {
    if (exitInProgressRef.current) return;

    exitInProgressRef.current = true;
    setIsExiting(true);

    const exitState = buildExitState();

    try {
      await Promise.race([
        flushWatchProgress(),
        new Promise<void>((resolve) => {
          window.setTimeout(resolve, EXIT_PROGRESS_FLUSH_TIMEOUT_MS);
        }),
      ]);
    } catch {
      // Best effort — cleanup still sends a keepalive progress update on unmount.
    } finally {
      await onExit(exitState);
    }
  }, [buildExitState, flushWatchProgress, onExit]);

  // -- Subtitle toggle callback --
  const toggleCaptions = useCallback(() => {
    if (activeSubtitleIndex !== null) {
      lastSubtitleIndexRef.current = activeSubtitleIndex;
      setActiveSubtitleIndex(null);
    } else {
      setActiveSubtitleIndex(lastSubtitleIndexRef.current);
    }
  }, [activeSubtitleIndex]);

  const handleSubtitleSelect = useCallback(
    (index: number | null) => {
      setActiveSubtitleIndex(index);
      onSubtitleChanged?.(index);
    },
    [onSubtitleChanged],
  );

  // -- Keyboard shortcuts --
  useKeyboardShortcuts(videoRef, containerRef, handleKeyboardSeek, toggleCaptions);

  // -- Next episode auto-play --
  const handleNavigate = useCallback(
    (contentId: string) => {
      onNavigateEpisode?.(contentId);
    },
    [onNavigateEpisode],
  );

  const nextEpisode = useNextEpisode(credits, seriesContext, currentTime, handleNavigate);

  // -- Intro skip --
  const showIntroSkip = intro != null && currentTime >= intro.start && currentTime < intro.end;

  const skipIntro = useCallback(() => {
    if (intro) handlePlayerSeek(intro.end);
  }, [intro, handlePlayerSeek]);

  // Stabilize the dependency – only the bitrate matters for buffer sizing.
  const selectedVersionBitrate = transcodeQuality.effectiveVersion?.bitrate ?? 0;

  // -- hls.js lifecycle --
  useEffect(() => {
    const video = videoRef.current;
    if (!video || !isPlayerReady) return;

    let hls: HlsType | null = null;
    let destroyed = false;
    let autoplayStarted = false;

    mediaRecoveryAttemptsRef.current = 0;
    setError(null);
    setAwaitingFirstFrame(true);

    const cleanupStartupListeners = () => {
      video.removeEventListener("loadeddata", attemptAutoplayWhenReady);
      video.removeEventListener("canplay", attemptAutoplayWhenReady);
    };

    const attemptAutoplayWhenReady = () => {
      if (destroyed || autoplayStarted) return;
      // HAVE_FUTURE_DATA means the browser has enough media to advance beyond
      // the current frame. Starting earlier can produce a visible first-frame
      // freeze where audio advances before video begins moving.
      if (video.readyState < HTMLMediaElement.HAVE_FUTURE_DATA) return;
      autoplayStarted = true;
      cleanupStartupListeners();
      video.play().catch(() => setPlaying(false));
    };

    video.addEventListener("loadeddata", attemptAutoplayWhenReady);
    video.addEventListener("canplay", attemptAutoplayWhenReady);

    async function init() {
      if (!video || destroyed) return;

      if (effectivePlayMethod === "transcode") {
        try {
          const Hls = await hlsPromise;
          if (destroyed) return;

          if (Hls.isSupported()) {
            const maxBufferLength = selectedVersionBitrate >= 25000 ? 60 : 120;
            const retryingLoadPolicy = {
              maxTimeToFirstByteMs: 45000,
              maxLoadTimeMs: 45000,
              timeoutRetry: { maxNumRetry: 3, retryDelayMs: 500, maxRetryDelayMs: 3000 },
              errorRetry: { maxNumRetry: 3, retryDelayMs: 500, maxRetryDelayMs: 3000 },
            };

            hls = new Hls({
              lowLatencyMode: false,
              backBufferLength: Infinity,
              maxBufferLength,
              maxMaxBufferLength: maxBufferLength,
              startPosition: effectiveInitialPosition,
              startFragPrefetch: true,
              // Segment requests may block while FFmpeg encodes on demand.
              // Remote transcode nodes can also briefly defer the initial
              // manifest until enough data is available for playback.
              manifestLoadPolicy: { default: retryingLoadPolicy },
              playlistLoadPolicy: { default: retryingLoadPolicy },
              fragLoadPolicy: {
                default: retryingLoadPolicy,
              },
            });

            hls.on(Hls.Events.ERROR, (_event, data) => {
              if (!data.fatal || destroyed) return;

              console.error("[hls.js] Fatal error:", {
                type: data.type,
                details: data.details,
                reason: data.reason,
                url: data.frag?.url ?? data.url,
                error: data.error?.message,
              });

              const now = Date.now();
              if (now - lastRecoveryRef.current < 3000) return;
              lastRecoveryRef.current = now;

              if (data.type === Hls.ErrorTypes.NETWORK_ERROR) {
                console.warn("[hls.js] Fatal network error, attempting recovery...");
                hls?.startLoad();
              } else if (data.type === Hls.ErrorTypes.MEDIA_ERROR) {
                if (mediaRecoveryAttemptsRef.current === 0) {
                  console.warn("[hls.js] Fatal media error, attempting recovery...");
                  hls?.recoverMediaError();
                } else if (mediaRecoveryAttemptsRef.current === 1) {
                  console.warn("[hls.js] Fatal media error (2nd), swapping audio codec...");
                  hls?.swapAudioCodec();
                  hls?.recoverMediaError();
                } else {
                  console.error("[hls.js] Fatal media error, giving up after 3 attempts");
                  setError("Playback failed. Please try again.");
                  hls?.destroy();
                  hlsRef.current = null;
                }
                mediaRecoveryAttemptsRef.current++;
              } else {
                console.error("[hls.js] Unrecoverable error:", data);
                setError("Playback failed. Please try again.");
                hls?.destroy();
                hlsRef.current = null;
              }
            });

            hls.on(Hls.Events.MANIFEST_PARSED, () => {
              if (destroyed) return;
              attemptAutoplayWhenReady();
            });

            hls.on(Hls.Events.BUFFER_APPENDED, () => {
              if (destroyed) return;
              attemptAutoplayWhenReady();
            });

            hls.loadSource(effectiveStreamUrl);
            hls.attachMedia(video);
            hlsRef.current = hls;
          } else if (video.canPlayType("application/vnd.apple.mpegurl")) {
            video.src = effectiveStreamUrl;
            video.addEventListener("loadedmetadata", attemptAutoplayWhenReady, { once: true });
          } else {
            setError("HLS playback is not supported in this browser.");
          }
        } catch {
          if (!destroyed) setError("Failed to load video player.");
        }
      } else {
        // Direct play — set video src directly.
        video.src = effectiveStreamUrl;
        video.currentTime = effectiveInitialPosition;
        video.play().catch(() => setPlaying(false));
      }
    }

    init();

    return () => {
      destroyed = true;
      cleanupStartupListeners();
      if (hls) {
        hls.destroy();
        hlsRef.current = null;
      }
      // Flush the video element's internal buffers so pre-downloaded
      // segments from a previous quality level don't play through
      // before the new quality takes effect.
      if (video) {
        video.removeAttribute("src");
        video.load();
      }
    };
  }, [
    effectiveStreamUrl,
    effectivePlayMethod,
    effectiveInitialPosition,
    isPlayerReady,
    selectedVersionBitrate,
  ]);

  // -- Video event listeners --
  useEffect(() => {
    const video = videoRef.current;
    if (!video) return;

    const onPlay = () => setPlaying(true);
    const onPause = () => setPlaying(false);
    const clearBuffering = () => {
      if (bufferingTimerRef.current) {
        clearTimeout(bufferingTimerRef.current);
        bufferingTimerRef.current = null;
      }
      setBuffering(false);
    };
    const onTimeUpdate = () => {
      const nextTime = video.currentTime + transcodeOffsetRef.current;
      const resolved = resolvePendingSeekTime(nextTime, pendingSeekTime);
      setCurrentTime(resolved.currentTime);
      if (resolved.pendingSeekTime !== pendingSeekTime) {
        setPendingSeekTime(resolved.pendingSeekTime);
      }
      // timeupdate is the most reliable signal that frames are rendering.
      // Also clears any stale buffering state from HLS segment transitions
      // where `waiting` fired but `canplay`/`playing` never followed.
      setAwaitingFirstFrame(false);
      clearBuffering();
    };
    const onSeeked = () => {
      setPendingSeekTime(null);
      setCurrentTime(video.currentTime + transcodeOffsetRef.current);
      setAwaitingFirstFrame(false);
      clearBuffering();
    };
    const onDurationChange = () => {
      if (video.duration && isFinite(video.duration)) {
        // For HLS EVENT playlists still being transcoded, the video element
        // reports duration based on segments produced so far. Prefer the
        // known total duration from metadata when available.
        if (backendDurationRef.current && video.duration < backendDurationRef.current) return;
        setDuration(video.duration);
      }
    };
    const onProgress = () => setBuffered(video.buffered);
    const onVolumeChange = () => {
      setVolume(video.volume);
      setMuted(video.muted);
      persistVolume(video.volume, video.muted);
    };
    const onWaiting = () => {
      // Delay showing the spinner so brief buffering between segments
      // or during initial HLS startup doesn't flash a spinner.
      if (!bufferingTimerRef.current) {
        bufferingTimerRef.current = setTimeout(() => {
          setBuffering(true);
          bufferingTimerRef.current = null;
        }, 500);
      }
    };
    const onCanPlay = clearBuffering;
    const onPlaying = () => {
      clearBuffering();
      setAwaitingFirstFrame(false);
    };
    const onError = () => {
      if (video.error) {
        setError(`Playback error: ${video.error.message || "Unknown error"}`);
      }
    };

    video.addEventListener("play", onPlay);
    video.addEventListener("pause", onPause);
    video.addEventListener("timeupdate", onTimeUpdate);
    video.addEventListener("seeked", onSeeked);
    video.addEventListener("durationchange", onDurationChange);
    video.addEventListener("progress", onProgress);
    video.addEventListener("volumechange", onVolumeChange);
    video.addEventListener("waiting", onWaiting);
    video.addEventListener("canplay", onCanPlay);
    video.addEventListener("playing", onPlaying);
    video.addEventListener("error", onError);

    return () => {
      video.removeEventListener("play", onPlay);
      video.removeEventListener("pause", onPause);
      video.removeEventListener("timeupdate", onTimeUpdate);
      video.removeEventListener("seeked", onSeeked);
      video.removeEventListener("durationchange", onDurationChange);
      video.removeEventListener("progress", onProgress);
      video.removeEventListener("volumechange", onVolumeChange);
      video.removeEventListener("waiting", onWaiting);
      video.removeEventListener("canplay", onCanPlay);
      video.removeEventListener("playing", onPlaying);
      video.removeEventListener("error", onError);
    };
  }, [pendingSeekTime]); // Listener behavior depends on pending seek reconciliation

  // Apply persisted volume on mount (separate from listener effect).
  useEffect(() => {
    const video = videoRef.current;
    if (!video) return;
    const saved = getPersistedVolume();
    video.volume = saved.volume;
    video.muted = saved.muted;
  }, []);

  // -- Control visibility (hover anywhere to show) --
  const [controlsVisible, setControlsVisible] = useState(true);
  const hideTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const resetControlsTimer = useCallback(() => {
    setControlsVisible(true);
    if (hideTimerRef.current) clearTimeout(hideTimerRef.current);
    hideTimerRef.current = setTimeout(() => {
      if (videoRef.current && !videoRef.current.paused) {
        setControlsVisible(false);
      }
    }, 3000);
  }, []);

  // Show controls when paused, start hide timer when playing.
  useEffect(() => {
    if (!playing) {
      setControlsVisible(true);
      if (hideTimerRef.current) clearTimeout(hideTimerRef.current);
    } else {
      resetControlsTimer();
    }
    return () => {
      if (hideTimerRef.current) clearTimeout(hideTimerRef.current);
    };
  }, [playing, resetControlsTimer]);

  // -- Playback info overlay --
  const [showPlaybackInfo, setShowPlaybackInfo] = useState(false);

  // -- Fullscreen tracking --
  useEffect(() => {
    const onChange = () => setIsFullscreen(!!document.fullscreenElement);
    document.addEventListener("fullscreenchange", onChange);
    return () => document.removeEventListener("fullscreenchange", onChange);
  }, []);

  // -- Subtitle appearance --
  const { containerStyle, cueStyle } = useSubtitleAppearance();

  // -- Subtitle cue matching --
  // Returns active cue texts for custom rendering instead of native TextTrack
  // (which has browser bugs with stale cues persisting after seek).
  const activeCueTexts = useSubtitleTracks(
    videoRef,
    subtitleUrls,
    activeSubtitleIndex,
    transcodeOffsetRef,
  );

  // -- Auto-select subtitle track based on mode --
  useEffect(() => {
    if (subtitleUrls.length === 0) return;

    const effectiveMode = subtitleMode ?? (preferredSubtitleLanguage ? "always" : "off");
    const audioLang = audioTracks[activeAudioIndex]?.language ?? null;

    const match = resolveSubtitleAutoSelect({
      mode: effectiveMode,
      tracks: subtitleUrls,
      preferredLanguage: preferredSubtitleLanguage ?? null,
      audioLanguage: audioLang,
      profileLanguage: profileLanguage ?? null,
      showForcedSubtitles: showForcedSubtitles ?? true,
    });

    if (match !== null) {
      setActiveSubtitleIndex(match);
      lastSubtitleIndexRef.current = match;
    }
  }, [
    preferredSubtitleLanguage,
    subtitleUrls,
    subtitleMode,
    showForcedSubtitles,
    profileLanguage,
    audioTracks,
    activeAudioIndex,
  ]);

  // -- Control callbacks --
  const handlePlayPause = useCallback(() => {
    const video = videoRef.current;
    if (!video) return;
    if (video.paused) video.play().catch(() => {});
    else video.pause();
  }, []);

  const handleVolumeChange = useCallback((v: number) => {
    const video = videoRef.current;
    if (!video) return;
    video.volume = v;
    if (v > 0 && video.muted) video.muted = false;
  }, []);

  const handleMutedChange = useCallback((m: boolean) => {
    const video = videoRef.current;
    if (!video) return;
    video.muted = m;
  }, []);

  const handleFullscreenToggle = useCallback(() => {
    if (document.fullscreenElement) {
      document.exitFullscreen().catch(() => {});
    } else {
      containerRef.current?.requestFullscreen().catch(() => {});
    }
  }, []);

  const handleQualitySelect = useCallback(
    (id: string) => {
      transcodeQuality.switchQuality(id, currentTime);
    },
    [transcodeQuality.switchQuality, currentTime],
  );

  const executeRealtimeCommand = useCallback(
    async (command: PlaybackRealtimeCommandEnvelope) => {
      const video = videoRef.current;

      switch (command.name) {
        case "pause":
          video?.pause();
          return;
        case "unpause":
          if (!video) return;
          await video.play();
          return;
        case "play_pause":
          if (!video) return;
          if (video.paused) {
            await video.play();
          } else {
            video.pause();
          }
          return;
        case "seek": {
          const position = readNumericPayload(
            command.payload,
            "position",
            "position_seconds",
            "seconds",
          );
          if (position === null) {
            throw new Error("missing_seek_position");
          }
          handlePlayerSeek(position);
          return;
        }
        case "set_volume": {
          const nextVolume = readNumericPayload(command.payload, "volume", "level");
          if (nextVolume === null || !video) {
            throw new Error("missing_volume");
          }
          video.volume = Math.min(1, Math.max(0, nextVolume));
          if (video.volume > 0 && video.muted) {
            video.muted = false;
          }
          return;
        }
        case "display_message":
          setNotice({
            title: readStringPayload(command.payload, "title") ?? "Playback notice",
            message:
              readStringPayload(command.payload, "message") ?? "A server message was received.",
            tone: "info",
          });
          return;
        case "server_restarting":
          setNotice({
            title: readStringPayload(command.payload, "title") ?? "Server restarting",
            message:
              readStringPayload(command.payload, "message") ??
              "Playback may end shortly while the server restarts.",
            tone: "warning",
          });
          return;
        case "server_shutting_down":
          setNotice({
            title: readStringPayload(command.payload, "title") ?? "Server shutting down",
            message:
              readStringPayload(command.payload, "message") ??
              "Playback may end shortly while the server shuts down.",
            tone: "warning",
          });
          return;
        case "stop":
        case "terminate":
          if (command.payload) {
            const message = readStringPayload(command.payload, "message");
            if (message) {
              setNotice({
                title:
                  readStringPayload(command.payload, "title") ??
                  (command.name === "terminate" ? "Playback ended" : "Playback stopping"),
                message,
                tone: "warning",
              });
            }
          }
          await handleExit();
          return;
        default:
          throw new Error("unsupported");
      }
    },
    [handleExit, handlePlayerSeek],
  );

  usePlaybackRealtime({
    sessionId,
    onCommand: executeRealtimeCommand,
  });

  // -- Render --
  return (
    <div
      ref={containerRef}
      className="fixed inset-0 z-50 bg-black"
      onMouseMove={resetControlsTimer}
    >
      {/* Back button + media info */}
      <div
        className={`absolute top-4 left-4 z-50 flex items-center gap-3 transition-opacity duration-300 ${
          controlsVisible ? "opacity-100" : "pointer-events-none opacity-0"
        }`}
      >
        <button
          onClick={() => {
            void handleExit();
          }}
          disabled={isExiting}
          className="flex items-center gap-2 rounded-full bg-black/60 px-4 py-2 text-sm text-white hover:bg-black/80"
          type="button"
        >
          <svg
            xmlns="http://www.w3.org/2000/svg"
            width="20"
            height="20"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
          >
            <path d="m15 18-6-6 6-6" />
          </svg>
          Back
        </button>
        <div className="flex flex-col text-sm text-white drop-shadow-lg">
          {seriesContext ? (
            <>
              <span className="font-medium">
                {seriesContext.seriesTitle ?? title}
                {year ? ` (${year})` : ""}
              </span>
              <span className="text-xs text-white/70">
                S{seriesContext.currentSeason}:E{seriesContext.currentEpisode}
                {title ? ` · ${title}` : ""}
              </span>
            </>
          ) : (
            <span className="font-medium">
              {title}
              {year ? ` (${year})` : ""}
            </span>
          )}
        </div>
      </div>

      {/* Loading overlay — stays up until the first frame renders */}
      {(awaitingFirstFrame || !isPlayerReady) && !error && (
        <div className="absolute inset-0 z-40 flex items-center justify-center bg-black">
          <div className="h-8 w-8 animate-spin rounded-full border-2 border-white/20 border-t-white" />
        </div>
      )}

      {/* Buffering spinner (mid-playback stalls only) */}
      {buffering && !awaitingFirstFrame && isPlayerReady && (
        <div className="pointer-events-none absolute inset-0 z-30 flex items-center justify-center">
          <div className="h-10 w-10 animate-spin rounded-full border-2 border-white/20 border-t-white" />
        </div>
      )}

      {notice ? (
        <PlaybackNoticeOverlay title={notice.title} message={notice.message} tone={notice.tone} />
      ) : null}

      {/* Error state */}
      {error && (
        <div className="absolute inset-0 z-40 flex items-center justify-center bg-black/80">
          <div className="text-center">
            <div className="mb-4 text-sm text-white/60">{error}</div>
            <button
              onClick={() => {
                void handleExit();
              }}
              disabled={isExiting}
              type="button"
              className="rounded bg-white/10 px-4 py-2 text-sm text-white hover:bg-white/20"
            >
              Go Back
            </button>
          </div>
        </div>
      )}

      {/* Video element — always rendered so the ref stays stable for
          event listeners and hls.js across quality switches. */}
      {/* Subtitle tracks are managed programmatically by useSubtitleTracks
          instead of <track> elements, for proper sync with remux seek offsets. */}
      <video
        ref={videoRef}
        className="absolute inset-0 h-full w-full"
        onClick={handlePlayPause}
        playsInline
        style={!isPlayerReady ? { visibility: "hidden" } : undefined}
      />

      {/* Subtitle overlay */}
      {activeCueTexts.length > 0 && (
        <div
          className="pointer-events-none absolute inset-x-0 z-20 flex flex-col items-center gap-1"
          style={containerStyle}
        >
          {activeCueTexts.map((text, i) => (
            <span
              key={i}
              className="inline-block rounded px-3 py-1 text-center leading-snug"
              style={{ ...cueStyle, whiteSpace: "pre-line" }}
            >
              {text}
            </span>
          ))}
        </div>
      )}

      {/* Intro skip button */}
      {showIntroSkip && <IntroSkipButton onSkip={skipIntro} />}

      {/* Next episode overlay */}
      {nextEpisode.showCountdown && nextEpisode.nextEpisode && (
        <NextEpisodeOverlay
          episode={nextEpisode.nextEpisode}
          secondsRemaining={nextEpisode.secondsRemaining}
          onSkip={nextEpisode.skipToNext}
          onCancel={nextEpisode.cancelAutoPlay}
        />
      )}

      {/* Controls */}
      {isPlayerReady && (
        <PlayerControls
          visible={controlsVisible}
          playing={playing}
          currentTime={currentTime}
          duration={duration}
          buffered={buffered}
          volume={volume}
          muted={muted}
          isFullscreen={isFullscreen}
          subtitleTracks={subtitleUrls}
          activeSubtitleIndex={activeSubtitleIndex}
          onSubtitleSelect={handleSubtitleSelect}
          mediaFileId={activeFileId ?? undefined}
          playerConfig={playerConfig}
          onRefreshSubtitles={onRefreshSubtitles}
          audioTracks={audioTracks}
          activeAudioIndex={activeAudioIndex}
          onAudioSelect={onAudioSelect}
          qualityOptions={transcodeQuality.qualityOptions}
          activeQualityId={transcodeQuality.activeQualityId}
          isTranscoding={transcodeQuality.isTranscoding}
          qualityError={transcodeQuality.error}
          onQualitySelect={handleQualitySelect}
          versions={
            versions.length > 1
              ? versions.map((v) => ({
                  fileId: v.file_id,
                  label: `${v.resolution} ${v.codec_video.toUpperCase()}${v.hdr ? " HDR" : ""}`,
                  isActive: v.file_id === activeFileId,
                }))
              : undefined
          }
          onSwitchVersion={
            onSwitchVersion ? (fileId) => onSwitchVersion(fileId, currentTime) : undefined
          }
          onPlayPause={handlePlayPause}
          onSeek={handlePlayerSeek}
          onVolumeChange={handleVolumeChange}
          onMutedChange={handleMutedChange}
          onFullscreenToggle={handleFullscreenToggle}
          showPlaybackInfo={showPlaybackInfo}
          onTogglePlaybackInfo={() => setShowPlaybackInfo((v) => !v)}
        />
      )}

      {/* Playback info overlay */}
      {showPlaybackInfo && (
        <PlaybackInfoOverlay
          videoRef={videoRef}
          containerRef={containerRef}
          streamUrl={effectiveStreamUrl}
          playMethod={displayedPlaybackState.playMethod}
          playbackInfo={displayedPlaybackState.playbackInfo}
          selectedVersion={transcodeQuality.effectiveVersion}
          onClose={() => setShowPlaybackInfo(false)}
        />
      )}
    </div>
  );
}
