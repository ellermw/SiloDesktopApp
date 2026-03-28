import { useState, useCallback, useRef } from "react";
import { Volume2 } from "lucide-react";
import type { PlayerAudioTrack } from "../types";
import { getLanguageName } from "../utils/languageNames";

interface AudioTrackMenuProps {
  tracks: PlayerAudioTrack[];
  activeIndex: number;
  onSelect: (index: number, currentPosition: number) => void;
  currentPosition: number;
}

function formatTrackLabel(track: PlayerAudioTrack, index: number): string {
  const parts: string[] = [];

  const lang = track.language ? getLanguageName(track.language) : undefined;
  if (lang) parts.push(lang);

  if (track.layout) {
    parts.push(track.layout);
  } else if (track.channels) {
    parts.push(`${track.channels}ch`);
  }

  if (track.codec) {
    parts.push(track.codec.toUpperCase());
  }

  return parts.length > 0 ? parts.join(" \u00B7 ") : `Track ${index + 1}`;
}

export function AudioTrackMenu({
  tracks,
  activeIndex,
  onSelect,
  currentPosition,
}: AudioTrackMenuProps) {
  const [open, setOpen] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);

  const handleSelect = useCallback(
    (index: number) => {
      onSelect(index, currentPosition);
      setOpen(false);
    },
    [currentPosition, onSelect],
  );

  const handleBlur = useCallback((e: React.FocusEvent) => {
    if (!menuRef.current?.contains(e.relatedTarget as Node)) {
      setOpen(false);
    }
  }, []);

  if (tracks.length === 0) return null;

  const disabled = tracks.length <= 1;

  return (
    <div ref={menuRef} className="relative" onBlur={handleBlur}>
      <button
        type="button"
        className={`flex h-8 w-8 items-center justify-center ${
          disabled ? "cursor-default text-white/30" : "text-white hover:text-white/80"
        }`}
        onClick={disabled ? undefined : () => setOpen((v) => !v)}
        aria-label="Audio tracks"
        aria-disabled={disabled}
      >
        <Volume2 className="h-5 w-5" />
      </button>

      {open && (
        <div className="absolute right-0 bottom-full mb-2 min-w-[220px] rounded-lg bg-black/90 py-1.5 shadow-xl backdrop-blur-sm">
          <div className="px-3 py-1.5 text-xs font-medium tracking-wide text-white/50 uppercase">
            Audio
          </div>
          {tracks.map((track, index) => (
            <button
              key={index}
              type="button"
              className={`flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm transition-colors hover:bg-white/10 ${
                index === activeIndex ? "text-blue-400" : "text-white/80"
              }`}
              onClick={() => handleSelect(index)}
            >
              <span className="w-4 text-center">{index === activeIndex ? "\u2713" : ""}</span>
              <span className="flex-1">{formatTrackLabel(track, index)}</span>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
