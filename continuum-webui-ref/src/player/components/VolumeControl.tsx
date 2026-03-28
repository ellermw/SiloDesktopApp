import { useCallback, useRef } from "react";
import { Volume2, VolumeX } from "lucide-react";
import { storage } from "@/utils/storage";

/** Read persisted volume from localStorage. */
export function getPersistedVolume(): { volume: number; muted: boolean } {
  const vol = storage.get(storage.KEYS.VOLUME);
  const muted = storage.get(storage.KEYS.MUTED);
  return {
    volume: vol != null ? Number(vol) : 1,
    muted: muted === "true",
  };
}

/** Persist volume to localStorage. */
export function persistVolume(volume: number, muted: boolean): void {
  storage.set(storage.KEYS.VOLUME, String(volume));
  storage.set(storage.KEYS.MUTED, String(muted));
}

interface VolumeControlProps {
  volume: number;
  muted: boolean;
  onVolumeChange: (volume: number) => void;
  onMutedChange: (muted: boolean) => void;
}

export function VolumeControl({
  volume,
  muted,
  onVolumeChange,
  onMutedChange,
}: VolumeControlProps) {
  const sliderRef = useRef<HTMLDivElement>(null);

  const getVolumeFromEvent = useCallback(
    (e: React.MouseEvent | MouseEvent) => {
      const el = sliderRef.current;
      if (!el) return volume;
      const rect = el.getBoundingClientRect();
      return Math.max(0, Math.min(1, (e.clientX - rect.left) / rect.width));
    },
    [volume],
  );

  const handleMouseDown = useCallback(
    (e: React.MouseEvent) => {
      const v = getVolumeFromEvent(e);
      onVolumeChange(v);

      const handleMouseMove = (ev: MouseEvent) => onVolumeChange(getVolumeFromEvent(ev));
      const handleMouseUp = () => {
        document.removeEventListener("mousemove", handleMouseMove);
        document.removeEventListener("mouseup", handleMouseUp);
      };
      document.addEventListener("mousemove", handleMouseMove);
      document.addEventListener("mouseup", handleMouseUp);
    },
    [getVolumeFromEvent, onVolumeChange],
  );

  const handleWheel = useCallback(
    (e: React.WheelEvent) => {
      e.preventDefault();
      const delta = e.deltaY < 0 ? 0.05 : -0.05;
      onVolumeChange(Math.max(0, Math.min(1, volume + delta)));
    },
    [volume, onVolumeChange],
  );

  const displayVolume = muted ? 0 : volume;

  return (
    <div className="group/vol flex items-center gap-2" onWheel={handleWheel}>
      <button
        type="button"
        className="flex h-8 w-8 items-center justify-center text-white hover:text-white/80"
        onClick={() => onMutedChange(!muted)}
        aria-label={muted ? "Unmute" : "Mute"}
      >
        {muted || volume === 0 ? <VolumeX className="h-5 w-5" /> : <Volume2 className="h-5 w-5" />}
      </button>

      <div
        ref={sliderRef}
        className="relative h-1 w-16 cursor-pointer rounded-full bg-white/20 opacity-0 transition-opacity group-hover/vol:opacity-100"
        onMouseDown={handleMouseDown}
      >
        <div
          className="absolute inset-y-0 left-0 rounded-full bg-white"
          style={{ width: `${displayVolume * 100}%` }}
        />
      </div>
    </div>
  );
}
