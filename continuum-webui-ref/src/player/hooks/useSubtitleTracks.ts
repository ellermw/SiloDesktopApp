import { useEffect, useRef, useState } from "react";
import { parseVTT, type ParsedCue } from "../utils/parseVTT";
import type { PlayerSubtitleInfo } from "../types";

/** Strip VTT formatting tags, keeping only the text content. */
function stripVTTTags(text: string): string {
  return text.replace(/<[^>]+>/g, "");
}

export function findActiveCueTexts(
  cues: ParsedCue[],
  currentTime: number,
  timeOffsetSeconds = 0,
): string[] {
  const time = currentTime + timeOffsetSeconds;
  const active: string[] = [];
  for (const cue of cues) {
    if (time >= cue.start && time < cue.end) {
      active.push(stripVTTTags(cue.text));
    }
  }
  return active;
}

/**
 * Manages subtitle display with custom rendering.
 *
 * Instead of relying on the browser's native TextTrack cue renderer (which has
 * bugs with programmatically-added tracks not clearing stale cues on seek),
 * this hook:
 *
 * 1. Fetches VTT data on demand and caches parsed cues.
 * 2. Matches cues against video.currentTime on every timeupdate / seeking event.
 * 3. Returns the active cue texts for the caller to render in a DOM overlay.
 */
export function useSubtitleTracks(
  videoRef: React.RefObject<HTMLVideoElement | null>,
  subtitleUrls: PlayerSubtitleInfo[],
  activeSubtitleIndex: number | null,
  timeOffsetRef: React.RefObject<number>,
): string[] {
  // Cache parsed cues by URL to avoid re-fetching.
  const cuesCacheRef = useRef<Map<string, ParsedCue[]>>(new Map());

  // The parsed cues for the currently active subtitle track.
  const parsedCuesRef = useRef<ParsedCue[] | null>(null);

  // Active cue texts returned to the caller for rendering.
  const [activeCueTexts, setActiveCueTexts] = useState<string[]>([]);

  // Dedup key to avoid unnecessary state updates.
  const lastKeyRef = useRef("");

  // Resolve the active subtitle info object.
  const activeSub =
    activeSubtitleIndex !== null
      ? (subtitleUrls.find((s) => s.index === activeSubtitleIndex) ?? null)
      : null;
  const activeUrl = activeSub?.url ?? null;

  // Fetch & parse VTT when the active subtitle changes.
  useEffect(() => {
    if (!activeUrl) {
      parsedCuesRef.current = null;
      lastKeyRef.current = "";
      setActiveCueTexts([]);
      return;
    }

    let cancelled = false;

    async function fetchCues() {
      if (!activeUrl) return;

      let cues = cuesCacheRef.current.get(activeUrl);
      if (!cues) {
        try {
          const resp = await fetch(activeUrl);
          if (!resp.ok) {
            console.error(`[useSubtitleTracks] Failed to fetch ${activeUrl}: ${resp.status}`);
            return;
          }
          cues = parseVTT(await resp.text());
          cuesCacheRef.current.set(activeUrl, cues);
        } catch (err) {
          console.error("[useSubtitleTracks] Fetch error:", err);
          return;
        }
      }

      if (!cancelled) {
        parsedCuesRef.current = cues;
      }
    }

    fetchCues();
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeUrl]);

  // Compute active cues on timeupdate / seeked, clear on seeking.
  //
  // We deliberately avoid showing cues on the `seeking` event because
  // video.currentTime already reflects the *target* position while the
  // actual video frame hasn't loaded yet (HLS segment buffering can take
  // 1-2 s). Showing cues at the target makes subtitles appear ahead of
  // the video. Instead we:
  //   seeking  → clear displayed cues (avoids stale AND premature text)
  //   seeked   → show cues once the seek lands
  //   timeupdate → normal playback updates
  useEffect(() => {
    const video = videoRef.current;
    if (!video) return;
    const videoEl = video;
    let rafId: number | null = null;

    function updateActiveCues() {
      const cues = parsedCuesRef.current;
      if (!cues) {
        if (lastKeyRef.current !== "") {
          lastKeyRef.current = "";
          setActiveCueTexts([]);
        }
        return;
      }

      const active = findActiveCueTexts(cues, videoEl.currentTime, timeOffsetRef.current ?? 0);

      // Only update state when the displayed text actually changes.
      const key = active.join("\0");
      if (key !== lastKeyRef.current) {
        lastKeyRef.current = key;
        setActiveCueTexts(active);
      }
    }

    function clearCues() {
      if (lastKeyRef.current !== "") {
        lastKeyRef.current = "";
        setActiveCueTexts([]);
      }
    }

    function stopFrameUpdates() {
      if (rafId !== null) {
        cancelAnimationFrame(rafId);
        rafId = null;
      }
    }

    function frameTick() {
      updateActiveCues();
      if (!videoEl.paused && !videoEl.ended) {
        rafId = requestAnimationFrame(frameTick);
      } else {
        rafId = null;
      }
    }

    function startFrameUpdates() {
      if (rafId === null) {
        rafId = requestAnimationFrame(frameTick);
      }
    }

    videoEl.addEventListener("timeupdate", updateActiveCues);
    videoEl.addEventListener("seeked", updateActiveCues);
    videoEl.addEventListener("seeking", clearCues);
    videoEl.addEventListener("play", startFrameUpdates);
    videoEl.addEventListener("playing", startFrameUpdates);
    videoEl.addEventListener("pause", stopFrameUpdates);
    videoEl.addEventListener("waiting", stopFrameUpdates);
    videoEl.addEventListener("ended", stopFrameUpdates);
    if (!videoEl.paused && !videoEl.ended) {
      startFrameUpdates();
    }
    return () => {
      stopFrameUpdates();
      videoEl.removeEventListener("timeupdate", updateActiveCues);
      videoEl.removeEventListener("seeked", updateActiveCues);
      videoEl.removeEventListener("seeking", clearCues);
      videoEl.removeEventListener("play", startFrameUpdates);
      videoEl.removeEventListener("playing", startFrameUpdates);
      videoEl.removeEventListener("pause", stopFrameUpdates);
      videoEl.removeEventListener("waiting", stopFrameUpdates);
      videoEl.removeEventListener("ended", stopFrameUpdates);
    };
  }, [timeOffsetRef, videoRef]);

  return activeCueTexts;
}
