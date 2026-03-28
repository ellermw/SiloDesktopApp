import type { FileVersion } from "@/api/types";

export function mapAudioLabel(codec: string): string {
  const lower = codec.toLowerCase();
  if (lower.includes("atmos")) return "Atmos";
  if (lower.includes("truehd")) return "TrueHD";
  if (lower.includes("dts-hd") || lower.includes("dts:x")) return "DTS-HD";
  if (lower.includes("dts")) return "DTS";
  if (lower.includes("eac3") || lower.includes("e-ac-3")) return "EAC3";
  if (lower.includes("aac")) return "AAC";
  if (lower.includes("flac")) return "FLAC";
  return codec.toUpperCase();
}

export const RESOLUTION_RANK: Record<string, number> = {
  "4k": 4,
  "2160p": 4,
  "1440p": 3,
  "1080p": 2,
  "720p": 1,
  "480p": 0,
};

export const AUDIO_RANK: Record<string, number> = {
  atmos: 6,
  truehd: 5,
  "dts-hd": 4,
  "dts:x": 4,
  dts: 3,
  flac: 2,
  eac3: 1,
  "e-ac-3": 1,
  aac: 0,
};

export function resolutionScore(res: string): number {
  return RESOLUTION_RANK[res.toLowerCase()] ?? -1;
}

export function audioScore(codec: string): number {
  const lower = codec.toLowerCase();
  for (const [key, score] of Object.entries(AUDIO_RANK)) {
    if (lower.includes(key)) return score;
  }
  return -1;
}

export function pickBestAttributes(
  versions: FileVersion[],
  qualityPreference?: string | null,
): {
  resolution: string;
  hdr: boolean;
  audioLabel: string;
} | null {
  if (versions.length === 0) return null;

  // When the user has a quality preference, only consider versions at or below that cap.
  let candidates = versions;
  if (qualityPreference && qualityPreference !== "auto") {
    const maxRank = RESOLUTION_RANK[qualityPreference.toLowerCase()] ?? -1;
    if (maxRank >= 0) {
      const filtered = versions.filter((v) => resolutionScore(v.resolution) <= maxRank);
      if (filtered.length > 0) candidates = filtered;
    }
  }

  let bestRes = "";
  let bestResScore = -1;
  let hdr = false;
  let bestAudioCodec = "";
  let bestAudioScore = -1;

  for (const v of candidates) {
    const rs = resolutionScore(v.resolution);
    if (rs > bestResScore) {
      bestResScore = rs;
      bestRes = v.resolution;
    }
    if (v.hdr) hdr = true;

    const as_ = audioScore(v.codec_audio);
    if (as_ > bestAudioScore) {
      bestAudioScore = as_;
      bestAudioCodec = v.codec_audio;
    }

    // Also check individual audio tracks for higher-quality codecs
    if (v.audio_tracks) {
      for (const t of v.audio_tracks) {
        if (t.codec) {
          const ts = audioScore(t.codec);
          if (ts > bestAudioScore) {
            bestAudioScore = ts;
            bestAudioCodec = t.codec;
          }
        }
      }
    }
  }

  return {
    resolution: bestRes,
    hdr,
    audioLabel: bestAudioCodec ? mapAudioLabel(bestAudioCodec) : "",
  };
}
