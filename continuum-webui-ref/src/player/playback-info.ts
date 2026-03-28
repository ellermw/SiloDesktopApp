import type {
  PlaybackSessionPlaybackInfo,
  PlayMethod,
  PlayerAudioTrack,
  PlayerFileVersion,
  PlayerVideoTrack,
} from "./types";

export interface RuntimePlaybackStats {
  playerWidth?: number;
  playerHeight?: number;
  videoWidth?: number;
  videoHeight?: number;
  droppedFrames?: number | null;
  corruptedFrames?: number | null;
}

export interface PlaybackInfoRow {
  label: string;
  value: string;
}

export interface PlaybackInfoSection {
  title: string;
  rows: PlaybackInfoRow[];
}

interface DeriveDisplayedPlaybackStateInput {
  playMethod: PlayMethod;
  playbackInfo: PlaybackSessionPlaybackInfo | null;
  selectedVersion?: PlayerFileVersion;
  transcodeStreamUrl: string | null;
  activeQualityId: string;
}

interface BuildPlaybackInfoSectionsInput {
  streamUrl: string;
  playMethod: PlayMethod;
  playbackInfo: PlaybackSessionPlaybackInfo | null;
  selectedVersion?: PlayerFileVersion;
  runtimeStats: RuntimePlaybackStats;
}

const CODEC_LABELS: Record<string, string> = {
  aac: "AAC",
  ac3: "AC3",
  av1: "AV1",
  dts: "DTS",
  dtshd: "DTS-HD",
  eac3: "EAC3",
  flac: "FLAC",
  h264: "H.264",
  hevc: "HEVC",
  mp3: "MP3",
  opus: "Opus",
  truehd: "TrueHD",
};

export function buildPlaybackInfoSections({
  streamUrl,
  playMethod,
  playbackInfo,
  selectedVersion,
  runtimeStats,
}: BuildPlaybackInfoSectionsInput): PlaybackInfoSection[] {
  const videoTrack = selectedVersion ? pickVideoTrack(selectedVersion) : undefined;
  const audioTrack = selectedVersion ? pickAudioTrack(selectedVersion) : undefined;

  return [
    {
      title: "Player",
      rows: [
        { label: "Player", value: "HTML Video Player" },
        { label: "Play method", value: formatPlayMethod(playMethod) },
        { label: "Protocol", value: formatProtocol(streamUrl) },
        { label: "Stream type", value: formatStreamType(playbackInfo, streamUrl) },
      ],
    },
    {
      title: "Video Info",
      rows: [
        {
          label: "Player dimensions",
          value: formatDimensions(runtimeStats.playerWidth, runtimeStats.playerHeight),
        },
        {
          label: "Video resolution",
          value: formatDimensions(runtimeStats.videoWidth, runtimeStats.videoHeight),
        },
        {
          label: "Dropped frames",
          value: formatFrameCount(runtimeStats.droppedFrames),
        },
        {
          label: "Corrupted frames",
          value: formatFrameCount(runtimeStats.corruptedFrames),
        },
      ],
    },
    {
      title: "Playback Stream Info",
      rows: [
        {
          label: "Video codec",
          value: formatDeliveredVideoCodec(playbackInfo?.video_codec, playMethod),
        },
        {
          label: "Audio codec",
          value: formatDeliveredAudioCodec(
            playbackInfo?.audio_codec,
            playMethod,
            playbackInfo?.transcode_audio ?? false,
          ),
        },
      ],
    },
    {
      title: "Original Media Info",
      rows: [
        {
          label: "Container",
          value: displayValue(selectedVersion?.container),
        },
        {
          label: "Size",
          value: formatFileSize(selectedVersion?.file_size),
        },
        {
          label: "Bitrate",
          value: formatMbpsFromKbps(selectedVersion?.bitrate),
        },
        {
          label: "Video codec",
          value: formatOriginalVideoCodec(selectedVersion, videoTrack),
        },
        {
          label: "Video bitrate",
          value: formatMbpsFromKbps(videoTrack?.bitrate),
        },
        {
          label: "Video range type",
          value: formatVideoRangeType(selectedVersion, videoTrack),
        },
        {
          label: "Audio codec",
          value: formatOriginalAudioCodec(selectedVersion, audioTrack),
        },
        {
          label: "Audio bitrate",
          value: formatKbps(audioTrack?.bitrate),
        },
        {
          label: "Audio channels",
          value: formatAudioChannels(selectedVersion, audioTrack),
        },
        {
          label: "Audio sample rate",
          value: formatSampleRate(audioTrack?.sample_rate),
        },
      ],
    },
  ];
}

export function deriveDisplayedPlaybackState({
  playMethod,
  playbackInfo,
  selectedVersion,
  transcodeStreamUrl,
  activeQualityId,
}: DeriveDisplayedPlaybackStateInput): {
  playMethod: PlayMethod;
  playbackInfo: PlaybackSessionPlaybackInfo | null;
} {
  if (!transcodeStreamUrl) {
    return { playMethod, playbackInfo };
  }

  const sourceVideoCodec =
    (selectedVersion ? pickVideoTrack(selectedVersion)?.codec : "") ||
    selectedVersion?.codec_video ||
    "";
  const sourceAudioCodec =
    (selectedVersion ? pickAudioTrack(selectedVersion)?.codec : "") ||
    selectedVersion?.codec_audio ||
    "";
  const remuxOriginal = playMethod === "remux" && activeQualityId === "original";

  if (remuxOriginal) {
    const transcodeAudio = playbackInfo?.transcode_audio ?? false;
    return {
      playMethod: "remux",
      playbackInfo: {
        stream_type: "hls",
        transcode_audio: transcodeAudio,
        video_codec: sourceVideoCodec || playbackInfo?.video_codec || "",
        audio_codec: transcodeAudio ? "aac" : sourceAudioCodec || playbackInfo?.audio_codec || "",
      },
    };
  }

  return {
    playMethod: "transcode",
    playbackInfo: {
      stream_type: "hls",
      transcode_audio: true,
      video_codec: "h264",
      audio_codec: "aac",
    },
  };
}

export function formatPlayMethod(method: PlayMethod): string {
  switch (method) {
    case "direct":
      return "Direct Play";
    case "remux":
      return "Direct Streaming";
    case "transcode":
      return "Transcode";
  }
}

export function formatProtocol(streamUrl: string): string {
  try {
    const base = typeof window !== "undefined" ? window.location.href : "http://localhost";
    return new URL(streamUrl, base).protocol.replace(":", "");
  } catch {
    return "—";
  }
}

export function formatStreamType(
  playbackInfo: PlaybackSessionPlaybackInfo | null,
  streamUrl: string,
): string {
  if (playbackInfo?.stream_type === "hls" || /\.m3u8(?:$|\?)/i.test(streamUrl)) {
    return "HLS";
  }
  if (playbackInfo?.stream_type === "progressive") {
    return "Progressive";
  }
  return "Progressive";
}

export function formatDimensions(width?: number, height?: number): string {
  if (!isPositive(width) || !isPositive(height)) {
    return "—";
  }
  return `${Math.round(width)}x${Math.round(height)}`;
}

export function formatFrameCount(value?: number | null): string {
  if (!Number.isFinite(value) || value == null || value < 0) {
    return "—";
  }
  return String(Math.round(value));
}

export function formatDeliveredVideoCodec(codec?: string, playMethod?: PlayMethod): string {
  const base = formatCodecLabel(codec);
  if (base === "—") return base;

  switch (playMethod) {
    case "direct":
      return `${base} (direct)`;
    case "remux":
      return `${base} (copy)`;
    case "transcode":
      return `${base} (transcoded)`;
    default:
      return base;
  }
}

export function formatDeliveredAudioCodec(
  codec?: string,
  playMethod?: PlayMethod,
  transcodeAudio = false,
): string {
  const base = formatCodecLabel(codec);
  if (base === "—") return base;

  if (playMethod === "transcode") {
    return `${base} (transcoded)`;
  }
  if (playMethod === "remux") {
    return `${base} (${transcodeAudio ? "transcoded" : "copy"})`;
  }
  if (playMethod === "direct") {
    return `${base} (direct)`;
  }
  return base;
}

export function formatOriginalVideoCodec(
  version?: PlayerFileVersion,
  track?: PlayerVideoTrack,
): string {
  const codec = formatCodecLabel(track?.codec || version?.codec_video);
  if (codec === "—") {
    return "—";
  }
  return [codec, track?.profile].filter(Boolean).join(" ");
}

export function formatVideoRangeType(
  version?: PlayerFileVersion,
  track?: PlayerVideoTrack,
): string {
  if (track?.dolby_vision) {
    const dolbyVision = track.dolby_vision.toLowerCase().startsWith("dolby vision")
      ? track.dolby_vision
      : `Dolby Vision ${track.dolby_vision}`;
    return track.video_range ? `${dolbyVision} (${track.video_range})` : dolbyVision;
  }
  if (track?.video_range) {
    return track.video_range;
  }
  if (version?.hdr) {
    return "HDR";
  }
  if (version) {
    return "SDR";
  }
  return "—";
}

export function formatOriginalAudioCodec(
  version?: PlayerFileVersion,
  track?: PlayerAudioTrack,
): string {
  const title = track?.title || track?.embedded_title;
  if (title) {
    return title;
  }
  return formatCodecLabel(track?.codec || version?.codec_audio);
}

export function formatAudioChannels(version?: PlayerFileVersion, track?: PlayerAudioTrack): string {
  const channels = track?.channels ?? version?.audio_channels;
  return isPositive(channels) ? String(channels) : "—";
}

export function formatSampleRate(sampleRate?: number): string {
  return isPositive(sampleRate) ? `${sampleRate.toLocaleString()} Hz` : "—";
}

export function formatKbps(kbps?: number): string {
  return isPositive(kbps) ? `${Math.round(kbps).toLocaleString()} kbps` : "—";
}

export function formatMbpsFromKbps(kbps?: number): string {
  return isPositive(kbps) ? `${(kbps / 1000).toFixed(1)} Mbps` : "—";
}

export function formatFileSize(bytes?: number): string {
  if (!isPositive(bytes)) {
    return "—";
  }
  if (bytes >= 1024 ** 3) {
    return `${(bytes / 1024 ** 3).toFixed(1)} GiB`;
  }
  if (bytes >= 1024 ** 2) {
    return `${(bytes / 1024 ** 2).toFixed(1)} MiB`;
  }
  if (bytes >= 1024) {
    return `${(bytes / 1024).toFixed(1)} KiB`;
  }
  return `${bytes} B`;
}

export function formatCodecLabel(codec?: string): string {
  if (!codec) {
    return "—";
  }
  const normalized = codec.trim();
  if (!normalized) {
    return "—";
  }
  return CODEC_LABELS[normalized.toLowerCase()] ?? normalized.toUpperCase();
}

function pickVideoTrack(version: PlayerFileVersion): PlayerVideoTrack | undefined {
  return version.video_tracks?.[0];
}

function pickAudioTrack(version: PlayerFileVersion): PlayerAudioTrack | undefined {
  return version.audio_tracks?.find((track) => track.default) ?? version.audio_tracks?.[0];
}

function displayValue(value?: string): string {
  return value && value.trim() ? value : "—";
}

function isPositive(value?: number): value is number {
  return Number.isFinite(value) && value != null && value > 0;
}
