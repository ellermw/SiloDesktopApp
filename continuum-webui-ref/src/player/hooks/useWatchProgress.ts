import { useCallback, useEffect, useRef } from "react";
import { usePlayerConfig } from "../context/PlayerConfigContext";
import { playerFetch } from "../player-fetch";

/**
 * Reports watch progress to the server every 10 seconds.
 * Reads currentTime and paused state from the video element.
 * An optional timeOffsetRef adds a transcode seek offset so the
 * reported position reflects the original media timeline.
 *
 * On cleanup (player unmount), sends one final progress report with
 * keepalive so the backend records the last known position even when
 * the user seeks and closes quickly.
 */
export function useWatchProgress(
  sessionId: string | null,
  videoRef: React.RefObject<HTMLVideoElement | null>,
  timeOffsetRef?: React.RefObject<number>,
) {
  const config = usePlayerConfig();
  const configRef = useRef(config);
  configRef.current = config;
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const skipCleanupReportRef = useRef(false);

  const getProgressSnapshot = useCallback(() => {
    const video = videoRef.current;
    if (!video) return null;

    const offset = timeOffsetRef?.current ?? 0;
    return {
      position: (video.currentTime ?? 0) + offset,
      isPaused: video.paused ?? true,
    };
  }, [timeOffsetRef, videoRef]);

  const reportProgress = useCallback(
    async (options?: { keepalive?: boolean; isPaused?: boolean }) => {
      if (!sessionId) return;

      const snapshot = getProgressSnapshot();
      if (!snapshot) return;

      const body = JSON.stringify({
        position: snapshot.position,
        is_paused: options?.isPaused ?? snapshot.isPaused,
      });

      if (options?.keepalive) {
        const cfg = configRef.current;
        const headers: Record<string, string> = {
          "Content-Type": "application/json",
        };
        const token = cfg.getAccessToken();
        if (token) headers["Authorization"] = `Bearer ${token}`;
        const profileId = cfg.getProfileId();
        if (profileId) headers["X-Profile-Id"] = profileId;

        await fetch(`${cfg.apiBaseUrl}/playback/${sessionId}/progress`, {
          method: "POST",
          headers,
          body,
          keepalive: true,
        });
        return;
      }

      await playerFetch(configRef.current, `/playback/${sessionId}/progress`, {
        method: "POST",
        body,
      });
    },
    [getProgressSnapshot, sessionId],
  );

  const flushProgress = useCallback(async () => {
    if (!sessionId) return;

    await reportProgress({ isPaused: true });
    skipCleanupReportRef.current = true;
  }, [reportProgress, sessionId]);

  useEffect(() => {
    if (!sessionId) return;
    skipCleanupReportRef.current = false;

    intervalRef.current = setInterval(() => {
      reportProgress().catch(() => {
        // Best effort — don't disrupt playback on progress report failure.
      });
    }, 10_000);

    return () => {
      if (intervalRef.current) clearInterval(intervalRef.current);
      intervalRef.current = null;

      if (skipCleanupReportRef.current) return;

      // Send one final progress report so the backend has the latest
      // position before the session is deleted (e.g. after a seek).
      reportProgress({ keepalive: true, isPaused: true }).catch(() => {});
    };
  }, [reportProgress, sessionId]);

  return flushProgress;
}
