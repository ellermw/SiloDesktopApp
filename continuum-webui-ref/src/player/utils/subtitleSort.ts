import type { PlayerSubtitleInfo, SubtitleMode } from "../types";

const SOURCE_PRIORITY: Record<string, number> = {
  external: 0,
  downloaded: 1,
  embedded: 2,
};

/** Sort subtitle tracks: external first, then downloaded, then embedded. */
export function sortSubtitlesBySource(tracks: PlayerSubtitleInfo[]): PlayerSubtitleInfo[] {
  return [...tracks].sort((a, b) => {
    const pa = SOURCE_PRIORITY[a.source ?? "embedded"] ?? 2;
    const pb = SOURCE_PRIORITY[b.source ?? "embedded"] ?? 2;
    return pa - pb;
  });
}

/**
 * Find the best subtitle track index for a given language,
 * preferring external > downloaded > embedded.
 * Returns the track's backend index (track.index) or -1 if no match.
 */
export function findPreferredSubtitleIndex(tracks: PlayerSubtitleInfo[], language: string): number {
  let bestIdx = -1;
  let bestPriority = Infinity;

  for (const track of tracks) {
    if (!track || track.language !== language) continue;
    const priority = SOURCE_PRIORITY[track.source ?? "embedded"] ?? 2;
    if (priority < bestPriority) {
      bestPriority = priority;
      bestIdx = track.index;
    }
  }

  return bestIdx;
}

export interface SubtitleAutoSelectOptions {
  mode: SubtitleMode;
  tracks: PlayerSubtitleInfo[];
  preferredLanguage: string | null;
  audioLanguage: string | null;
  profileLanguage: string | null;
  showForcedSubtitles: boolean;
}

function findForcedSubtitleIndex(tracks: PlayerSubtitleInfo[], language: string): number | null {
  const match = findPreferredSubtitleIndex(
    tracks.filter((track) => track.forced),
    language,
  );
  return match >= 0 ? match : null;
}

/**
 * Determines which subtitle track to auto-select on playback start.
 * Returns the track's backend index, or null if no track should be selected.
 */
export function resolveSubtitleAutoSelect(options: SubtitleAutoSelectOptions): number | null {
  const { mode, tracks, preferredLanguage, audioLanguage, profileLanguage, showForcedSubtitles } =
    options;

  if (tracks.length === 0) return null;
  const effectiveProfileLang = profileLanguage || "en";
  const effectiveAudioLang = audioLanguage || effectiveProfileLang;

  switch (mode) {
    case "off":
      return showForcedSubtitles ? findForcedSubtitleIndex(tracks, effectiveAudioLang) : null;

    case "always": {
      if (!preferredLanguage) return null;
      const match = findPreferredSubtitleIndex(tracks, preferredLanguage);
      return match >= 0 ? match : null;
    }

    case "auto": {
      if (preferredLanguage === "") return null;
      if (effectiveAudioLang === effectiveProfileLang) {
        return showForcedSubtitles ? findForcedSubtitleIndex(tracks, effectiveAudioLang) : null;
      }
      const lang = preferredLanguage || effectiveProfileLang;
      const match = findPreferredSubtitleIndex(tracks, lang);
      return match >= 0 ? match : null;
    }

    default:
      return null;
  }
}
