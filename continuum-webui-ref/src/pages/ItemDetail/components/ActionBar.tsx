import { Link, useNavigate } from "react-router";
import {
  Heart,
  Plus,
  Check,
  Download,
  MoreVertical,
  Play,
  RefreshCw,
  ChevronDown,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import type { FileVersion } from "@/api/types";
import { pickBestAttributes } from "./versionRankingUtils";
import VersionFlyoutItems from "./VersionFlyout";
import StarRating from "@/components/StarRating";

interface ActionBarProps {
  contentId?: string;
  playHref?: string;
  playLabel?: string;
  playProgress?: number;
  resumeResolution?: string;
  resumeHdr?: boolean;
  watchedLabel?: string;
  onToggleWatched?: () => void;
  isUpdatingWatched?: boolean;
  onToggleFavorite?: () => void;
  isFavorite?: boolean;
  onToggleWatchlist?: () => void;
  inWatchlist?: boolean;
  onRefresh?: () => void;
  isRefreshing?: boolean;
  isAdmin?: boolean;
  versions?: FileVersion[];
  onPlayVersion?: (fileId: number) => void;
  rating?: number | null;
  onRatingChange?: (rating: number | null) => void;
  qualityPreference?: string | null;
}

export default function ActionBar({
  contentId,
  playHref,
  playLabel = "Play",
  playProgress,
  resumeResolution,
  resumeHdr,
  watchedLabel,
  onToggleWatched,
  isUpdatingWatched = false,
  onToggleFavorite,
  isFavorite = false,
  onToggleWatchlist,
  inWatchlist = false,
  onRefresh,
  isRefreshing = false,
  isAdmin = false,
  versions,
  onPlayVersion,
  rating,
  onRatingChange,
  qualityPreference,
}: ActionBarProps) {
  const navigate = useNavigate();
  const hasMultipleVersions = (versions?.length ?? 0) >= 2;
  const best =
    hasMultipleVersions && versions ? pickBestAttributes(versions, qualityPreference) : null;
  const qualityLabel = resumeResolution
    ? [resumeResolution, resumeHdr ? "HDR" : ""].filter(Boolean).join(" ")
    : best
      ? [best.resolution, best.hdr ? "HDR" : ""].filter(Boolean).join(" ")
      : "";

  const progressOverlay =
    playProgress != null && playProgress > 0 && playProgress < 100 ? (
      <span
        className="pointer-events-none absolute inset-y-0 left-0 border-r-2 border-primary-foreground/40 bg-primary-foreground/20"
        style={{ width: `${playProgress}%` }}
      />
    ) : null;

  return (
    <div className="flex flex-wrap items-center gap-3">
      {/* ── Play button ─────────────────────────────────────── */}
      {playHref ? (
        hasMultipleVersions ? (
          <DropdownMenu>
            <div className="relative flex overflow-hidden rounded-full shadow-md">
              <Button
                asChild
                className="relative h-11 gap-2.5 rounded-none rounded-l-full px-7 text-[15px] font-bold tracking-wide"
              >
                <Link to={playHref}>
                  <Play className="size-[18px] fill-current" />
                  {playLabel}
                  {qualityLabel && (
                    <span className="ml-1 text-primary-foreground/60">&middot; {qualityLabel}</span>
                  )}
                </Link>
              </Button>
              <DropdownMenuTrigger asChild>
                <button
                  type="button"
                  className="flex h-11 items-center border-l border-primary-foreground/20 bg-primary px-3 text-primary-foreground transition-colors hover:bg-primary/90"
                  aria-label="Choose version"
                >
                  <ChevronDown className="size-4" />
                </button>
              </DropdownMenuTrigger>
              {progressOverlay}
            </div>
            <DropdownMenuContent align="start" className="min-w-[340px]">
              {versions && onPlayVersion && (
                <VersionFlyoutItems versions={versions} onPlayVersion={onPlayVersion} />
              )}
            </DropdownMenuContent>
          </DropdownMenu>
        ) : (
          <Button
            asChild
            className="relative h-11 gap-2.5 overflow-hidden rounded-full px-8 text-[15px] font-bold tracking-wide shadow-md"
          >
            <Link to={playHref}>
              <Play className="size-[18px] fill-current" />
              {playLabel}
              {progressOverlay}
            </Link>
          </Button>
        )
      ) : (
        <Button disabled className="h-11 gap-2.5 rounded-full px-8 text-[15px] font-bold tracking-wide">
          <Play className="size-[18px] fill-current" />
          {playLabel}
        </Button>
      )}

      {/* ── Watched toggle ──────────────────────────────────── */}
      {watchedLabel && onToggleWatched && (
        <Button
          variant="glass"
          onClick={onToggleWatched}
          disabled={isUpdatingWatched}
          className="h-11 rounded-full px-5 text-[14px] font-semibold"
        >
          <Check className="size-[18px]" />
          {watchedLabel}
        </Button>
      )}

      {/* ── Icon action buttons ─────────────────────────────── */}
      {onToggleFavorite && (
        <Button
          variant="glass"
          size="icon-lg"
          onClick={onToggleFavorite}
          title={isFavorite ? "Unfavorite" : "Favorite"}
          className="size-11 rounded-full"
        >
          <Heart className={`size-[18px] transition-colors ${isFavorite ? "text-red-400 fill-current" : ""}`} />
        </Button>
      )}

      {onRatingChange && (
        <StarRating value={rating ?? null} onChange={onRatingChange} size={18} />
      )}

      <DropdownMenu modal={false}>
        <DropdownMenuTrigger asChild>
          <Button variant="glass" size="icon-lg" title="More" className="size-11 rounded-full">
            <MoreVertical className="size-[18px]" />
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end" className="w-56">
          {watchedLabel && onToggleWatched && (
            <DropdownMenuItem disabled={isUpdatingWatched} onSelect={onToggleWatched}>
              <Check className="size-4" />
              {watchedLabel}
            </DropdownMenuItem>
          )}
          {onToggleFavorite && (
            <DropdownMenuItem onSelect={onToggleFavorite}>
              <Heart className="size-4" />
              {isFavorite ? "Remove from Favorites" : "Add to Favorites"}
            </DropdownMenuItem>
          )}
          {onToggleWatchlist && (
            <DropdownMenuItem onSelect={onToggleWatchlist}>
              {inWatchlist ? <Check className="size-4" /> : <Plus className="size-4" />}
              {inWatchlist ? "Remove from Watchlist" : "Add to Watchlist"}
            </DropdownMenuItem>
          )}
          <DropdownMenuItem disabled>
            <Download className="size-4" />
            Download (Coming soon)
          </DropdownMenuItem>
          {isAdmin && (
            <>
              <DropdownMenuSeparator />
              {contentId && (
                <DropdownMenuItem
                  onSelect={() =>
                    navigate(`/admin/history?media_item_id=${encodeURIComponent(contentId)}`)
                  }
                >
                  View Play History
                </DropdownMenuItem>
              )}
              {onRefresh && (
                <DropdownMenuItem disabled={isRefreshing} onSelect={onRefresh}>
                  {isRefreshing && <RefreshCw className="size-4 animate-spin" />}
                  Refresh Metadata
                </DropdownMenuItem>
              )}
            </>
          )}
        </DropdownMenuContent>
      </DropdownMenu>
    </div>
  );
}
