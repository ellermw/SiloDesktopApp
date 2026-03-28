import { useCallback, useRef, useState } from "react";
import { Settings } from "lucide-react";
import type { QualityOption } from "../types";

export interface VersionInfo {
  fileId: number;
  label: string;
  isActive: boolean;
}

interface QualityMenuProps {
  options: QualityOption[];
  activeId: string;
  isTranscoding: boolean;
  error: string | null;
  onSelect: (id: string) => void;
  versions?: VersionInfo[];
  onSwitchVersion?: (fileId: number) => void;
}

export function QualityMenu({
  options,
  activeId,
  isTranscoding,
  error,
  onSelect,
  versions,
  onSwitchVersion,
}: QualityMenuProps) {
  const [open, setOpen] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);

  const handleSelect = useCallback(
    (id: string) => {
      onSelect(id);
      setOpen(false);
    },
    [onSelect],
  );

  // Close on outside click.
  const handleBlur = useCallback((e: React.FocusEvent) => {
    if (!menuRef.current?.contains(e.relatedTarget as Node)) {
      setOpen(false);
    }
  }, []);

  if (options.length === 0) return null;

  const activeOption = options.find((o) => o.id === activeId);

  return (
    <div ref={menuRef} className="relative" onBlur={handleBlur}>
      <button
        type="button"
        className="flex h-8 items-center gap-1 rounded px-2 text-xs text-white hover:bg-white/10"
        onClick={() => setOpen((v) => !v)}
        aria-label="Quality"
      >
        <Settings className="h-4 w-4" />
        <span className="hidden sm:inline">
          {isTranscoding ? "..." : (activeOption?.label ?? "Quality")}
        </span>
      </button>

      {open && (
        <div className="absolute right-0 bottom-full mb-2 min-w-[200px] rounded-lg bg-black/90 py-1 shadow-lg backdrop-blur">
          {error && <div className="px-3 py-1 text-xs text-red-400">{error}</div>}
          {/* Version switching (multiple file versions) */}
          {versions && versions.length > 1 && onSwitchVersion && (
            <>
              <div className="px-3 py-1 text-xs tracking-wider text-white/40 uppercase">
                Version
              </div>
              {versions.map((v) => (
                <button
                  key={v.fileId}
                  type="button"
                  className={`flex w-full px-3 py-2 text-left text-sm hover:bg-white/10 ${
                    v.isActive ? "text-white" : "text-white/70"
                  }`}
                  onClick={() => {
                    onSwitchVersion(v.fileId);
                    setOpen(false);
                  }}
                >
                  {v.label}
                </button>
              ))}
              <div className="my-1 border-t border-white/10" />
              <div className="px-3 py-1 text-xs tracking-wider text-white/40 uppercase">
                Quality
              </div>
            </>
          )}
          {options.map((opt) => (
            <button
              key={opt.id}
              type="button"
              className={`flex w-full items-center justify-between px-3 py-2 text-left text-sm hover:bg-white/10 ${
                opt.id === activeId ? "text-white" : "text-white/70"
              }`}
              onClick={() => handleSelect(opt.id)}
            >
              <span>{opt.label}</span>
              <span className="text-xs text-white/40">{opt.sublabel}</span>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
