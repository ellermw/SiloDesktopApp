import { useEffect } from "react";

/**
 * Registers keyboard shortcuts for the video player.
 * Space/K = play/pause, F = fullscreen, M = mute, C = toggle captions,
 * ArrowLeft/Right = seek ±10s, ArrowUp/Down = volume ±5%.
 */
export function useKeyboardShortcuts(
  videoRef: React.RefObject<HTMLVideoElement | null>,
  containerRef: React.RefObject<HTMLElement | null>,
  handleSeek: (time: number) => void,
  toggleCaptions: () => void,
) {
  useEffect(() => {
    function handleKeyDown(e: KeyboardEvent) {
      // Don't intercept keys when typing in inputs.
      const target = e.target as HTMLElement;
      if (target.tagName === "INPUT" || target.tagName === "TEXTAREA" || target.isContentEditable) {
        return;
      }

      const video = videoRef.current;
      if (!video) return;

      switch (e.key) {
        case " ":
        case "k":
        case "K":
          e.preventDefault();
          if (video.paused) video.play().catch(() => {});
          else video.pause();
          break;

        case "f":
        case "F":
          e.preventDefault();
          if (document.fullscreenElement) {
            document.exitFullscreen().catch(() => {});
          } else {
            containerRef.current?.requestFullscreen().catch(() => {});
          }
          break;

        case "m":
        case "M":
          e.preventDefault();
          video.muted = !video.muted;
          break;

        case "c":
        case "C":
          e.preventDefault();
          toggleCaptions();
          break;

        case "ArrowLeft":
          e.preventDefault();
          handleSeek(Math.max(0, video.currentTime - 10));
          break;

        case "ArrowRight":
          e.preventDefault();
          handleSeek(Math.min(video.duration || 0, video.currentTime + 10));
          break;

        case "ArrowUp":
          e.preventDefault();
          video.volume = Math.min(1, video.volume + 0.05);
          break;

        case "ArrowDown":
          e.preventDefault();
          video.volume = Math.max(0, video.volume - 0.05);
          break;
      }
    }

    document.addEventListener("keydown", handleKeyDown);
    return () => document.removeEventListener("keydown", handleKeyDown);
  }, [videoRef, containerRef, handleSeek, toggleCaptions]);
}
