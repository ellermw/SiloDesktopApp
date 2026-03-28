import type { PlayerSubtitleInfo } from "../types";

function hasPlayableUrl(track: PlayerSubtitleInfo): boolean {
  return track.url.trim().length > 0;
}

export function resolvePlayableSubtitles(
  sessionTracks: PlayerSubtitleInfo[],
  fallbackTracks: PlayerSubtitleInfo[],
): PlayerSubtitleInfo[] {
  const playableSessionTracks = sessionTracks.filter(hasPlayableUrl);
  if (playableSessionTracks.length > 0) {
    return playableSessionTracks;
  }
  return fallbackTracks.filter(hasPlayableUrl);
}
