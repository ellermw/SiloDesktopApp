import { useCallback, useRef } from "react";
import type { PlayMethod } from "../types";

/**
 * Provides a seek function for direct play streams.
 *
 * Remux streams now use HLS (via the transcode pipeline), so seeking is
 * handled natively by hls.js. This hook only handles direct play seeking
 * via video.currentTime.
 */
export function useRemuxSeeking(
  videoRef: React.RefObject<HTMLVideoElement | null>,
  _playMethod: PlayMethod,
  _streamUrl: string,
  _initialPosition: number,
): {
  handleSeek: (seconds: number) => void;
  getEffectiveTime: () => number;
  seekOffsetRef: React.RefObject<number>;
} {
  const seekOffsetRef = useRef(0);

  const handleSeek = useCallback(
    (seconds: number) => {
      const video = videoRef.current;
      if (!video) return;
      video.currentTime = seconds;
    },
    [videoRef],
  );

  const getEffectiveTime = useCallback(() => {
    const video = videoRef.current;
    if (!video) return 0;
    return video.currentTime;
  }, [videoRef]);

  return { handleSeek, getEffectiveTime, seekOffsetRef };
}
