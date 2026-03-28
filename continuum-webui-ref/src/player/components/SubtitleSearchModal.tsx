import { useState, useEffect, useCallback } from "react";
import { createPortal } from "react-dom";
import type { PlayerConfig } from "../context/PlayerConfigContext";
import { playerFetch } from "../player-fetch";
import type { SubtitleSearchResponse, SubtitleResult } from "@/api/types";
import { LANGUAGES } from "../utils/languageNames";

interface SubtitleSearchModalProps {
  mediaFileId: number;
  playerConfig: PlayerConfig;
  isOpen: boolean;
  onClose: () => void;
  onSubtitleDownloaded: () => void;
}

interface ProviderInfo {
  abbr: string;
  color: string;
}

const providerInfo: Record<string, ProviderInfo> = {
  opensubtitles: { abbr: "OS", color: "#eab308" },
  subdl: { abbr: "SDL", color: "#3b82f6" },
  subsource: { abbr: "SS", color: "#ef4444" },
};

function scoreColor(score: number): string {
  if (score >= 70) return "#22c55e";
  if (score >= 40) return "#eab308";
  return "#ef4444";
}

export function SubtitleSearchModal({
  mediaFileId,
  playerConfig,
  isOpen,
  onClose,
  onSubtitleDownloaded,
}: SubtitleSearchModalProps) {
  const [selectedLang, setSelectedLang] = useState("en");
  const [results, setResults] = useState<SubtitleResult[]>([]);
  const [warnings, setWarnings] = useState<string[]>([]);
  const [searching, setSearching] = useState(false);
  const [downloading, setDownloading] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const handleClose = useCallback(() => {
    onClose();
  }, [onClose]);

  useEffect(() => {
    if (!isOpen) return;

    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        handleClose();
      }
    };

    document.addEventListener("keydown", handleKeyDown);
    return () => {
      document.removeEventListener("keydown", handleKeyDown);
    };
  }, [isOpen, handleClose]);

  const handleSearch = useCallback(async () => {
    setSearching(true);
    setError(null);
    setResults([]);
    setWarnings([]);

    try {
      const response = await playerFetch<SubtitleSearchResponse>(
        playerConfig,
        "/subtitles/search",
        {
          method: "POST",
          body: JSON.stringify({
            media_file_id: mediaFileId,
            languages: [selectedLang],
          }),
        },
      );
      setResults(response.results ?? []);
      setWarnings(response.warnings ?? []);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Search failed");
    } finally {
      setSearching(false);
    }
  }, [playerConfig, mediaFileId, selectedLang]);

  const handleDownload = useCallback(
    async (result: SubtitleResult) => {
      const key = `${result.provider}:${result.id}`;
      setDownloading(key);
      setError(null);

      try {
        await playerFetch<unknown>(playerConfig, "/subtitles/download", {
          method: "POST",
          body: JSON.stringify({
            media_file_id: mediaFileId,
            provider: result.provider,
            subtitle_id: result.id,
            language: result.language,
            release_name: result.release_name,
            format: result.format,
            score: result.score,
            hearing_impaired: result.hearing_impaired,
          }),
        });
        onSubtitleDownloaded();
        handleClose();
      } catch (err) {
        setError(err instanceof Error ? err.message : "Download failed");
      } finally {
        setDownloading(null);
      }
    },
    [playerConfig, mediaFileId, onSubtitleDownloaded, handleClose],
  );

  if (!isOpen) return null;

  const modal = (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/80"
      onClick={handleClose}
      role="dialog"
      aria-modal="true"
      aria-label="Subtitle Search"
    >
      <div
        className="w-full max-w-[480px] rounded-lg bg-neutral-900 text-white shadow-xl"
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div className="flex items-center justify-between border-b border-white/10 px-4 py-3">
          <h2 className="text-sm font-semibold">Search Subtitles</h2>
          <button
            type="button"
            className="text-white/60 hover:text-white"
            onClick={handleClose}
            aria-label="Close"
          >
            ✕
          </button>
        </div>

        {/* Search controls */}
        <div className="flex gap-2 px-4 py-3">
          <select
            className="flex-1 rounded bg-neutral-800 px-2 py-1.5 text-sm text-white focus:outline-none"
            value={selectedLang}
            onChange={(e) => setSelectedLang(e.target.value)}
          >
            {LANGUAGES.map((lang) => (
              <option key={lang.code} value={lang.code}>
                {lang.label}
              </option>
            ))}
          </select>
          <button
            type="button"
            className="rounded bg-white/10 px-3 py-1.5 text-sm font-medium hover:bg-white/20 disabled:opacity-50"
            onClick={handleSearch}
            disabled={searching}
          >
            {searching ? "Searching…" : "Search"}
          </button>
        </div>

        {/* Error */}
        {error && (
          <div className="mx-4 mb-3 rounded bg-red-900/40 px-3 py-2 text-xs text-red-300">
            {error}
          </div>
        )}

        {/* Warnings */}
        {warnings.length > 0 && (
          <div className="mx-4 mb-2 space-y-1">
            {warnings.map((w, i) => (
              <div key={i} className="rounded bg-yellow-900/40 px-3 py-1.5 text-xs text-yellow-300">
                {w}
              </div>
            ))}
          </div>
        )}

        {/* Results */}
        <div className="max-h-[60vh] overflow-y-auto px-4 pb-4">
          {results.length === 0 && !searching && !error && (
            <p className="py-6 text-center text-xs text-white/40">
              Select a language and press Search.
            </p>
          )}

          {results.map((result) => {
            const key = `${result.provider}:${result.id}`;
            const isDownloading = downloading === key;
            const info = providerInfo[result.provider];

            return (
              <button
                key={key}
                type="button"
                className="mb-1.5 flex w-full items-center gap-2 rounded bg-white/5 px-3 py-2 text-left hover:bg-white/10 disabled:opacity-50"
                onClick={() => handleDownload(result)}
                disabled={downloading !== null}
              >
                {/* Score badge */}
                <span
                  className="shrink-0 rounded px-1.5 py-0.5 text-xs font-bold tabular-nums"
                  style={{
                    backgroundColor: `${scoreColor(result.score)}22`,
                    color: scoreColor(result.score),
                    border: `1px solid ${scoreColor(result.score)}55`,
                  }}
                >
                  {result.score}
                </span>

                {/* Release name */}
                <span className="min-w-0 flex-1 truncate text-xs">{result.release_name}</span>

                {/* HI badge */}
                {result.hearing_impaired && (
                  <span className="shrink-0 rounded bg-white/10 px-1 py-0.5 text-[10px] text-white/60">
                    HI
                  </span>
                )}

                {/* Download count */}
                <span className="shrink-0 text-[10px] text-white/40">
                  ↓{result.downloads.toLocaleString()}
                </span>

                {/* Provider badge */}
                <span
                  className="shrink-0 rounded px-1.5 py-0.5 text-[10px] font-semibold"
                  style={{
                    backgroundColor: `${info?.color ?? "#6b7280"}22`,
                    color: info?.color ?? "#9ca3af",
                    border: `1px solid ${info?.color ?? "#6b7280"}55`,
                  }}
                >
                  {info?.abbr ?? result.provider}
                </span>

                {/* Download spinner */}
                {isDownloading && (
                  <span className="shrink-0 text-xs text-white/60" aria-label="Downloading">
                    ⟳
                  </span>
                )}
              </button>
            );
          })}
        </div>
      </div>
    </div>
  );

  return createPortal(modal, document.body);
}
