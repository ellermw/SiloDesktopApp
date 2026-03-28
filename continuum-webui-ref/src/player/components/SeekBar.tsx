import { useCallback, useRef, useState } from "react";

interface SeekBarProps {
  currentTime: number;
  duration: number;
  buffered: TimeRanges | null;
  onSeek: (seconds: number) => void;
}

function formatTime(seconds: number): string {
  const s = Math.floor(seconds);
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  const sec = s % 60;
  if (h > 0) {
    return `${h}:${m.toString().padStart(2, "0")}:${sec.toString().padStart(2, "0")}`;
  }
  return `${m}:${sec.toString().padStart(2, "0")}`;
}

export function SeekBar({ currentTime, duration, buffered, onSeek }: SeekBarProps) {
  const barRef = useRef<HTMLDivElement>(null);
  const [hoverTime, setHoverTime] = useState<number | null>(null);
  const [dragging, setDragging] = useState(false);

  const getTimeFromEvent = useCallback(
    (e: React.MouseEvent | MouseEvent) => {
      const bar = barRef.current;
      if (!bar || duration <= 0) return 0;
      const rect = bar.getBoundingClientRect();
      const fraction = Math.max(0, Math.min(1, (e.clientX - rect.left) / rect.width));
      return fraction * duration;
    },
    [duration],
  );

  const [dragTime, setDragTime] = useState<number | null>(null);

  const handleMouseDown = useCallback(
    (e: React.MouseEvent) => {
      setDragging(true);
      const time = getTimeFromEvent(e);
      setDragTime(time);

      const handleMouseMove = (ev: MouseEvent) => {
        setDragTime(getTimeFromEvent(ev));
      };
      const handleMouseUp = (ev: MouseEvent) => {
        const finalTime = getTimeFromEvent(ev);
        onSeek(finalTime);
        setDragging(false);
        setDragTime(null);
        document.removeEventListener("mousemove", handleMouseMove);
        document.removeEventListener("mouseup", handleMouseUp);
      };
      document.addEventListener("mousemove", handleMouseMove);
      document.addEventListener("mouseup", handleMouseUp);
    },
    [getTimeFromEvent, onSeek],
  );

  const handleMouseMove = useCallback(
    (e: React.MouseEvent) => {
      if (!dragging) {
        setHoverTime(getTimeFromEvent(e));
      }
    },
    [dragging, getTimeFromEvent],
  );

  const displayTime = dragTime ?? currentTime;
  const playedPercent = duration > 0 ? (displayTime / duration) * 100 : 0;

  // Calculate all buffered ranges as percentages.
  const bufferedRanges: Array<{ startPercent: number; widthPercent: number }> = [];
  if (buffered && duration > 0) {
    for (let i = 0; i < buffered.length; i++) {
      const startPercent = (buffered.start(i) / duration) * 100;
      const endPercent = (buffered.end(i) / duration) * 100;
      bufferedRanges.push({ startPercent, widthPercent: endPercent - startPercent });
    }
  }

  return (
    <div className="group/seek relative w-full px-2">
      {/* Hover time preview */}
      {hoverTime !== null && !dragging && (
        <div
          className="pointer-events-none absolute -top-8 -translate-x-1/2 rounded bg-black/80 px-2 py-1 text-xs text-white"
          style={{ left: `${(hoverTime / (duration || 1)) * 100}%` }}
        >
          {formatTime(hoverTime)}
        </div>
      )}

      <div
        ref={barRef}
        className="relative h-1 w-full cursor-pointer rounded-full bg-white/20 transition-[height] group-hover/seek:h-2"
        onMouseDown={handleMouseDown}
        onMouseMove={handleMouseMove}
        onMouseLeave={() => setHoverTime(null)}
      >
        {/* Buffered ranges */}
        {bufferedRanges.map((range, i) => (
          <div
            key={i}
            className="absolute inset-y-0 rounded-full bg-white/40"
            style={{ left: `${range.startPercent}%`, width: `${range.widthPercent}%` }}
          />
        ))}
        {/* Played */}
        <div
          className="absolute inset-y-0 left-0 rounded-full bg-white"
          style={{ width: `${playedPercent}%` }}
        />
        {/* Thumb */}
        <div
          className="absolute top-1/2 h-3 w-3 -translate-x-1/2 -translate-y-1/2 rounded-full bg-white opacity-0 transition-opacity group-hover/seek:opacity-100"
          style={{ left: `${playedPercent}%` }}
        />
      </div>
    </div>
  );
}

export { formatTime };
