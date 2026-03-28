import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { usePlayerConfig } from "../context/PlayerConfigContext";
import { PlayerFetchError, playerFetch } from "../player-fetch";
import type { PlayMethod, PlayerFileVersion, QualityOption, TranscodeStartRequest } from "../types";
import { QUALITY_TO_RESOLUTION } from "./useCodecDetection";

/** Quality tier definition. ID is frontend-only; backend receives resolution + bitrate separately. */
interface QualityTierDef {
  id: string;
  label: string;
  resolution: string;
  bitrate: number;
}

/** All transcode-able quality tiers in descending quality order. */
const QUALITY_TIERS: QualityTierDef[] = [
  { id: "1080p-high", label: "1080p High", resolution: "1080p", bitrate: 10000 },
  { id: "1080p", label: "1080p", resolution: "1080p", bitrate: 6000 },
  { id: "720p-high", label: "720p High", resolution: "720p", bitrate: 4000 },
  { id: "720p", label: "720p", resolution: "720p", bitrate: 2000 },
  { id: "480p", label: "480p", resolution: "480p", bitrate: 1500 },
  { id: "420p", label: "420p", resolution: "420p", bitrate: 720 },
];

/** Numeric height for each resolution string. */
const RESOLUTION_HEIGHT: Record<string, number> = {
  "2160p": 2160,
  "1080p": 1080,
  "720p": 720,
  "480p": 480,
  "420p": 420,
};

interface TranscodeStartResponse {
  session_id: string;
  status: string;
  switched_file_id?: number;
  manifest_url: string;
  duration_seconds: number | null;
  player_start_seconds: number;
  timeline_offset_seconds: number;
  can_seek_anywhere: boolean;
}

interface UseTranscodeQualityParams {
  sessionId: string | null;
  selectedVersion: PlayerFileVersion | undefined;
  versions: PlayerFileVersion[];
  playMethod: PlayMethod | null;
  initialPosition: number;
  qualityPreference?: string | null;
  /** When true, remux "original" quality uses codec copy instead of encoding. */
  transcodeAudio?: boolean;
}

interface UseTranscodeQualityResult {
  qualityOptions: QualityOption[];
  activeQualityId: string;
  switchQuality: (qualityId: string, currentPosition: number, forceRestart?: boolean) => void;
  transcodeStreamUrl: string | null;
  playerStartSeconds: number;
  timelineOffsetSeconds: number;
  canSeekAnywhere: boolean;
  durationSeconds: number | null;
  isTranscoding: boolean;
  error: string | null;
  switchedFileId: number | null;
  effectiveVersion: PlayerFileVersion | undefined;
}

function formatBitrate(kbps: number): string {
  if (kbps >= 1000) {
    const mbps = kbps / 1000;
    return mbps % 1 === 0 ? `${mbps} Mbps` : `${mbps.toFixed(1)} Mbps`;
  }
  return `${kbps} kbps`;
}

function playMethodLabel(method: PlayMethod | null): string {
  switch (method) {
    case "direct":
      return "Direct Play";
    case "remux":
      return "Remux";
    case "transcode":
      return "Transcode";
    default:
      return "";
  }
}

function buildQualityOptions(
  version: PlayerFileVersion | undefined,
  playMethod: PlayMethod | null,
): QualityOption[] {
  if (!version) return [];

  const options: QualityOption[] = [];

  // Original quality option.
  const methodLabel = playMethodLabel(playMethod);
  const bitrateLabel = version.bitrate > 0 ? formatBitrate(version.bitrate) : "";
  const sublabelParts = [methodLabel, bitrateLabel].filter(Boolean);

  options.push({
    id: "original",
    label: `Original (${version.resolution === "2160p" ? "4K" : version.resolution})`,
    sublabel: sublabelParts.join(" \u00b7 "),
    resolution: "",
    bitrateKbps: 0,
    isOriginal: true,
  });

  // Auto option — selects best tier at or below screen resolution.
  options.push({
    id: "auto",
    label: "Auto",
    sublabel: "",
    resolution: "",
    bitrateKbps: 0,
    isOriginal: false,
  });

  // Determine the file's native height.
  const nativeHeight = RESOLUTION_HEIGHT[version.resolution] ?? 0;

  // Add transcode options at or below native resolution.
  for (const tier of QUALITY_TIERS) {
    const tierHeight = RESOLUTION_HEIGHT[tier.resolution];
    if (tierHeight == null) continue;
    if (tierHeight >= nativeHeight) continue;

    options.push({
      id: tier.id,
      label: tier.label,
      sublabel: `~${formatBitrate(tier.bitrate)}`,
      resolution: tier.resolution,
      bitrateKbps: tier.bitrate,
      isOriginal: false,
    });
  }

  return options;
}

export function shouldForceVideoTranscodeForOriginalRemux(userAgent: string): boolean {
  const ua = userAgent.toLowerCase();
  const isMacOS = /\bmac os x\b/.test(ua);
  const isChromium = /(chrome|chromium|crios|edg|brave)/.test(ua);
  const isSafari = /safari/.test(ua) && !/(chrome|chromium|crios|edg|brave)/.test(ua);
  return isMacOS && isChromium && !isSafari;
}

export function useTranscodeQuality({
  sessionId,
  selectedVersion,
  versions,
  playMethod,
  initialPosition,
  qualityPreference,
  transcodeAudio,
}: UseTranscodeQualityParams): UseTranscodeQualityResult {
  const config = usePlayerConfig();
  const [activeQualityId, setActiveQualityId] = useState("original");
  const [transcodeStreamUrl, setTranscodeStreamUrl] = useState<string | null>(null);
  const [playerStartSeconds, setPlayerStartSeconds] = useState(0);
  const [timelineOffsetSeconds, setTimelineOffsetSeconds] = useState(0);
  const [canSeekAnywhere, setCanSeekAnywhere] = useState(true);
  const [durationSeconds, setDurationSeconds] = useState<number | null>(null);
  const [isTranscoding, setIsTranscoding] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [switchedFileId, setSwitchedFileId] = useState<number | null>(null);
  const switchAbortRef = useRef<AbortController | null>(null);
  const manifestVersionRef = useRef(0);
  const autoStartKeyRef = useRef<string | null>(null);

  const effectiveVersion = useMemo(() => {
    if (switchedFileId != null) {
      const switched = versions.find((v) => v.file_id === switchedFileId);
      if (switched) return switched;
    }
    return selectedVersion;
  }, [switchedFileId, versions, selectedVersion]);

  const forceVideoTranscodeForOriginalRemux = useMemo(() => {
    if (typeof navigator === "undefined") {
      return false;
    }
    return shouldForceVideoTranscodeForOriginalRemux(navigator.userAgent);
  }, []);

  const qualityOptions = useMemo(
    () => buildQualityOptions(effectiveVersion, playMethod),
    [effectiveVersion, playMethod],
  );

  useEffect(() => {
    switchAbortRef.current?.abort();
    switchAbortRef.current = null;
    autoStartKeyRef.current = null;
    setActiveQualityId("original");
    setTranscodeStreamUrl(null);
    setPlayerStartSeconds(0);
    setTimelineOffsetSeconds(0);
    setCanSeekAnywhere(true);
    setDurationSeconds(null);
    setIsTranscoding(false);
    setError(null);
    setSwitchedFileId(null);
  }, [sessionId, selectedVersion?.file_id, playMethod]);

  const startTranscode = useCallback(
    (qualityId: string, currentPosition: number, forceRestart = false) => {
      if (!sessionId) return;
      if (!forceRestart && qualityId === activeQualityId) return;

      // Abort any in-progress switch (POST + polling).
      switchAbortRef.current?.abort();
      switchAbortRef.current = null;

      // Clear the guard's file switch when reverting to original quality.
      // The backend will set a new switched_file_id in the response if needed.
      if (qualityId === "original") {
        setSwitchedFileId(null);
      }

      // Switching back to the original stream only makes sense when the base
      // playback session was direct/remux. If the base session itself requires
      // transcoding, "original" still means an original-resolution transcode.
      // For direct play, switching to "original" means stopping HLS entirely.
      // For remux and transcode, "original" still needs HLS (with codec copy for remux).
      if (qualityId === "original" && playMethod !== "transcode" && playMethod !== "remux") {
        setActiveQualityId("original");
        setTranscodeStreamUrl(null);
        setPlayerStartSeconds(0);
        setTimelineOffsetSeconds(0);
        setCanSeekAnywhere(true);
        setDurationSeconds(null);
        setIsTranscoding(false);
        setError(null);
        return;
      }

      // Find the quality option to get the bitrate preset.
      const option =
        qualityId === "original"
          ? (qualityOptions.find((o) => o.id === "original") ?? {
              id: "original",
              label: "Original",
              sublabel: "",
              resolution: "",
              bitrateKbps: 0,
              isOriginal: true,
            })
          : qualityOptions.find((o) => o.id === qualityId);
      if (!option) return;

      const abortController = new AbortController();
      switchAbortRef.current = abortController;

      setIsTranscoding(true);
      setActiveQualityId(qualityId);
      setError(null);
      // Immediately clear the old transcode URL so React unmounts the
      // current <media-player> before the backend deletes segments.
      // Without this, the old hls.js instance tries to fetch segments
      // from the deleted output directory and throws bufferAppendErrors.
      setTranscodeStreamUrl(null);

      (async () => {
        try {
          // For remux at "original" quality, use codec copy (no re-encoding).
          const isRemuxOriginal = playMethod === "remux" && option.isOriginal;
          const shouldEncodeRemuxVideo =
            isRemuxOriginal && forceVideoTranscodeForOriginalRemux;
          const body: TranscodeStartRequest = {
            session_id: sessionId,
            seek_seconds: currentPosition,
            target_resolution: isRemuxOriginal ? "" : option.resolution,
            target_codec_video: isRemuxOriginal
              ? (shouldEncodeRemuxVideo ? "h264" : "copy")
              : "h264",
            target_codec_audio: isRemuxOriginal ? (transcodeAudio ? "aac" : "copy") : "aac",
            target_bitrate_kbps: isRemuxOriginal && !shouldEncodeRemuxVideo ? 0 : option.bitrateKbps,
            // Shorter HLS segments reduce startup latency noticeably,
            // especially for remux/copy sessions where a long startup
            // window can delay first frame for several seconds.
            segment_duration: 2,
            subtitle_track_index: -1,
            subtitle_burn_in: false,
          };

          const resp = await playerFetch<TranscodeStartResponse>(
            config,
            "/playback/transcode/start",
            { method: "POST", body: JSON.stringify(body) },
          );

          if (abortController.signal.aborted) return;

          if (resp?.switched_file_id != null) {
            setSwitchedFileId(resp.switched_file_id);
          }

          // Use the manifest URL from the backend response. In distributed mode
          // this points to the proxy node; in integrated mode it's an API-local path.
          const token = config.getAccessToken();
          const params = new URLSearchParams();
          if (token) params.set("token", token);
          manifestVersionRef.current += 1;
          params.set("v", `${Date.now()}-${manifestVersionRef.current}-${qualityId}`);
          const query = params.toString();

          let manifestUrl = resp.manifest_url;
          if (manifestUrl.startsWith("/")) {
            // Relative path — prepend API base URL.
            manifestUrl = `${config.apiBaseUrl}${manifestUrl}`;
          }
          if (query) {
            manifestUrl += (manifestUrl.includes("?") ? "&" : "?") + query;
          }

          setTranscodeStreamUrl(manifestUrl);
          setPlayerStartSeconds(resp.player_start_seconds ?? currentPosition);
          setTimelineOffsetSeconds(resp.timeline_offset_seconds ?? 0);
          setCanSeekAnywhere(resp.can_seek_anywhere ?? true);
          setDurationSeconds(resp.duration_seconds ?? null);
          setError(null);
        } catch (err: unknown) {
          if (abortController.signal.aborted) return;
          // 422 = no alternate file version available for 4K transcode protection.
          if (err instanceof PlayerFetchError && err.status === 422) {
            setActiveQualityId("original");
            setTranscodeStreamUrl(null);
            setPlayerStartSeconds(0);
            setTimelineOffsetSeconds(0);
            setCanSeekAnywhere(true);
            setDurationSeconds(null);
            setError("No lower resolution version available for transcoding");
            return;
          }
          if (playMethod === "transcode") {
            setError(`Couldn't start ${option.label}.`);
          } else {
            setActiveQualityId("original");
            setTranscodeStreamUrl(null);
            setPlayerStartSeconds(0);
            setTimelineOffsetSeconds(0);
            setCanSeekAnywhere(true);
            setDurationSeconds(null);
            setError(`Couldn't switch to ${option.label}.`);
          }
        } finally {
          if (!abortController.signal.aborted) {
            setIsTranscoding(false);
          }
        }
      })();
    },
    [
      sessionId,
      activeQualityId,
      qualityOptions,
      config,
      playMethod,
      transcodeAudio,
      forceVideoTranscodeForOriginalRemux,
    ],
  );

  const switchQuality = useCallback(
    (qualityId: string, currentPosition: number, forceRestart?: boolean) => {
      startTranscode(qualityId, currentPosition, forceRestart ?? false);
    },
    [startTranscode],
  );

  useEffect(() => {
    if (!sessionId || (playMethod !== "transcode" && playMethod !== "remux")) {
      return;
    }

    const autoStartKey = `${sessionId}:${selectedVersion?.file_id ?? "none"}:${initialPosition}`;
    if (autoStartKeyRef.current === autoStartKey) {
      return;
    }

    autoStartKeyRef.current = autoStartKey;

    // If the user has a quality preference, start at that tier if it's a valid
    // option for this file (i.e., lower than the file's native resolution).
    // Otherwise fall back to "original" (e.g., transcode needed for codec reasons).
    let autoStartQuality = "original";
    if (qualityPreference && qualityPreference !== "auto") {
      const prefRes = QUALITY_TO_RESOLUTION[qualityPreference];
      if (prefRes) {
        const match = qualityOptions.find((o) => o.resolution === prefRes);
        if (match) {
          autoStartQuality = match.id;
        }
      }
    }

    startTranscode(autoStartQuality, initialPosition, true);
  }, [
    initialPosition,
    playMethod,
    qualityOptions,
    qualityPreference,
    selectedVersion?.file_id,
    sessionId,
    startTranscode,
  ]);

  return {
    qualityOptions,
    activeQualityId,
    switchQuality,
    transcodeStreamUrl,
    playerStartSeconds,
    timelineOffsetSeconds,
    canSeekAnywhere,
    durationSeconds,
    isTranscoding,
    error,
    switchedFileId,
    effectiveVersion,
  };
}
