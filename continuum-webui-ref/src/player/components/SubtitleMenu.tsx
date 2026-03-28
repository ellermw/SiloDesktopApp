import { useState, useCallback, useRef, useMemo } from "react";
import { createPortal } from "react-dom";
import { Captions, CaptionsOff } from "lucide-react";
import type { PlayerSubtitleInfo } from "../types";
import type { PlayerConfig } from "../context/PlayerConfigContext";
import { SubtitleSearchModal } from "./SubtitleSearchModal";
import { getLanguageName } from "../utils/languageNames";
import { sortSubtitlesBySource } from "../utils/subtitleSort";

interface SubtitleMenuProps {
  tracks: PlayerSubtitleInfo[];
  activeIndex: number | null;
  onSelect: (index: number | null) => void;
  mediaFileId?: number;
  playerConfig?: PlayerConfig;
  onRefreshSubtitles?: () => void;
}

const SOURCE_LABELS: Record<string, string> = {
  external: "External",
  embedded: "Embedded",
  downloaded: "Downloaded",
};

export function SubtitleMenu({
  tracks,
  activeIndex,
  onSelect,
  mediaFileId,
  playerConfig,
  onRefreshSubtitles,
}: SubtitleMenuProps) {
  const [open, setOpen] = useState(false);
  const [searchOpen, setSearchOpen] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);

  const sortedTracks = useMemo(() => sortSubtitlesBySource(tracks), [tracks]);

  const handleSelect = useCallback(
    (index: number | null) => {
      onSelect(index);
      setOpen(false);
    },
    [onSelect],
  );

  const handleBlur = useCallback((e: React.FocusEvent) => {
    if (!menuRef.current?.contains(e.relatedTarget as Node)) {
      setOpen(false);
    }
  }, []);

  if (tracks.length === 0 && !mediaFileId) return null;

  return (
    <div ref={menuRef} className="relative" onBlur={handleBlur}>
      <button
        type="button"
        className="flex h-8 w-8 items-center justify-center text-white hover:text-white/80"
        onClick={() => setOpen((v) => !v)}
        aria-label={activeIndex !== null ? "Disable captions" : "Enable captions"}
      >
        {activeIndex !== null ? (
          <Captions className="h-5 w-5" />
        ) : (
          <CaptionsOff className="h-5 w-5" />
        )}
      </button>

      {open && (
        <div className="absolute right-0 bottom-full mb-2 flex min-w-[220px] flex-col rounded-lg bg-black/90 shadow-lg backdrop-blur">
          <div className="shrink-0 py-1">
            <button
              type="button"
              className={`flex w-full items-center gap-2 px-3 py-2 text-left text-sm hover:bg-white/10 ${
                activeIndex === null ? "bg-white/5 text-white" : "text-white/70"
              }`}
              onClick={() => handleSelect(null)}
            >
              <span className="w-4 shrink-0 text-center text-xs">
                {activeIndex === null ? "✓" : ""}
              </span>
              Off
            </button>
          </div>
          <div className="max-h-[60vh] overflow-y-auto py-1">
            {sortedTracks.map((track) => {
              const isActive = track.index === activeIndex;
              const languageName = getLanguageName(track.language);
              const sourceLabel = SOURCE_LABELS[track.source ?? "embedded"] ?? "Embedded";
              const hasDetail =
                track.label && track.label !== track.language && track.label !== languageName;

              return (
                <button
                  key={track.index}
                  type="button"
                  className={`flex w-full items-start gap-2 px-3 py-2 text-left hover:bg-white/10 ${
                    isActive ? "bg-white/5 text-white" : "text-white/70"
                  }`}
                  onClick={() => handleSelect(track.index)}
                >
                  <span className="mt-0.5 w-4 shrink-0 text-center text-xs">
                    {isActive ? "✓" : ""}
                  </span>
                  <span className="flex min-w-0 flex-1 flex-col">
                    <span className="flex w-full items-center justify-between gap-3">
                      <span className="text-sm">{languageName}</span>
                      <span className="shrink-0 rounded bg-white/10 px-1.5 py-0.5 text-[10px] tracking-wide text-white/50 uppercase">
                        {sourceLabel}
                      </span>
                    </span>
                    {hasDetail && (
                      <span className="mt-0.5 truncate text-xs text-white/40">{track.label}</span>
                    )}
                  </span>
                </button>
              );
            })}
          </div>
          {mediaFileId && playerConfig && (
            <div className="shrink-0 border-t border-white/10 py-1">
              <button
                type="button"
                className="flex w-full px-3 py-2 text-left text-sm text-white/70 hover:bg-white/10"
                onClick={() => {
                  setSearchOpen(true);
                  setOpen(false);
                }}
              >
                Search Online...
              </button>
            </div>
          )}
        </div>
      )}

      {searchOpen &&
        mediaFileId &&
        playerConfig &&
        createPortal(
          <SubtitleSearchModal
            mediaFileId={mediaFileId}
            playerConfig={playerConfig}
            isOpen={searchOpen}
            onClose={() => setSearchOpen(false)}
            onSubtitleDownloaded={() => {
              setSearchOpen(false);
              onRefreshSubtitles?.();
            }}
          />,
          document.body,
        )}
    </div>
  );
}
