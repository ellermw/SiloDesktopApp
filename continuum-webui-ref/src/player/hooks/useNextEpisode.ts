import { useCallback, useEffect, useRef, useState } from "react";
import type { EpisodeRef, PlayerTimeRange, SeriesContext } from "../types";

interface NextEpisodeState {
  showCountdown: boolean;
  secondsRemaining: number;
  nextEpisode: EpisodeRef | null;
  skipToNext: () => void;
  cancelAutoPlay: () => void;
}

/**
 * Detects when the current playback position enters the credits region,
 * starts a 10-second countdown, and provides the next episode reference.
 */
export function useNextEpisode(
  credits: PlayerTimeRange | null,
  seriesContext: SeriesContext | undefined,
  currentTime: number,
  onNavigate: (contentId: string) => void,
): NextEpisodeState {
  const [showCountdown, setShowCountdown] = useState(false);
  const [secondsRemaining, setSecondsRemaining] = useState(10);
  const cancelledRef = useRef(false);
  const countdownRef = useRef<ReturnType<typeof setInterval> | null>(null);

  // Find the next episode.
  const nextEpisode = seriesContext ? findNextEpisode(seriesContext) : null;

  // Detect entry into credits region.
  useEffect(() => {
    if (!credits || !nextEpisode || cancelledRef.current) return;

    if (currentTime >= credits.start && !showCountdown) {
      setShowCountdown(true);
      setSecondsRemaining(10);

      countdownRef.current = setInterval(() => {
        setSecondsRemaining((prev) => {
          if (prev <= 1) {
            if (countdownRef.current) clearInterval(countdownRef.current);
            onNavigate(nextEpisode.contentId);
            return 0;
          }
          return prev - 1;
        });
      }, 1000);
    }
  }, [currentTime, credits, nextEpisode, showCountdown, onNavigate]);

  // Clean up interval on unmount.
  useEffect(() => {
    return () => {
      if (countdownRef.current) clearInterval(countdownRef.current);
    };
  }, []);

  const skipToNext = useCallback(() => {
    if (countdownRef.current) clearInterval(countdownRef.current);
    if (nextEpisode) onNavigate(nextEpisode.contentId);
  }, [nextEpisode, onNavigate]);

  const cancelAutoPlay = useCallback(() => {
    cancelledRef.current = true;
    if (countdownRef.current) clearInterval(countdownRef.current);
    setShowCountdown(false);
  }, []);

  return { showCountdown, secondsRemaining, nextEpisode, skipToNext, cancelAutoPlay };
}

function findNextEpisode(ctx: SeriesContext): EpisodeRef | null {
  const idx = ctx.episodes.findIndex(
    (ep) => ep.seasonNumber === ctx.currentSeason && ep.episodeNumber === ctx.currentEpisode,
  );
  if (idx < 0 || idx >= ctx.episodes.length - 1) return null;
  return ctx.episodes[idx + 1] ?? null;
}
