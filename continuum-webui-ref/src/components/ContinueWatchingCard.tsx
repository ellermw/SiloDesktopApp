import ViewTransitionLink from "@/components/ViewTransitionLink";
import { Play } from "lucide-react";
import type { ItemDetail, SectionItem } from "@/api/types";
import type { ProgressEntry } from "@/api/types";
import MediaItemMenu from "@/components/MediaItemMenu";

type ContinueWatchingCardProps =
  | {
      detail: ItemDetail;
      progress: ProgressEntry;
      sectionItem?: never;
    }
  | {
      sectionItem: SectionItem;
      detail?: never;
      progress?: never;
    };

export default function ContinueWatchingCard({ ...props }: ContinueWatchingCardProps) {
  const card =
    "sectionItem" in props && props.sectionItem
      ? {
          watchHref: `/watch/${props.sectionItem.content_id}`,
          itemHref: `/item/${props.sectionItem.content_id}`,
          title: props.sectionItem.title,
          seriesTitle: props.sectionItem.series_title,
          seasonNumber: props.sectionItem.season_number,
          episodeNumber: props.sectionItem.episode_number,
          backdropUrl: props.sectionItem.backdrop_url,
          posterUrl: props.sectionItem.poster_url,
          positionSeconds: props.sectionItem.position_seconds ?? 0,
          durationSeconds: props.sectionItem.duration_seconds ?? 0,
        }
      : {
          watchHref: `/watch/${props.detail.content_id}`,
          itemHref: `/item/${props.detail.content_id}`,
          title: props.detail.title,
          seriesTitle: props.detail.series_title,
          seasonNumber: props.detail.season_number,
          episodeNumber: props.detail.episode_number,
          backdropUrl: props.detail.backdrop_url,
          posterUrl: props.detail.poster_url,
          positionSeconds: props.progress.position_seconds,
          durationSeconds: props.progress.duration_seconds,
        };

  const isNextUp = "sectionItem" in props && props.sectionItem?.item_source === "next_up";
  const dismissAction =
    "sectionItem" in props && props.sectionItem
      ? props.sectionItem.item_source === "next_up"
        ? props.sectionItem.series_id
          ? {
              itemId: props.sectionItem.content_id,
              surface: "next_up" as const,
              seriesId: props.sectionItem.series_id,
            }
          : undefined
        : props.sectionItem.progress_updated_at
          ? {
              itemId: props.sectionItem.content_id,
              surface: "continue_watching" as const,
              progressUpdatedAt: props.sectionItem.progress_updated_at,
            }
          : undefined
      : {
          itemId: props.detail.content_id,
          surface: "continue_watching" as const,
          progressUpdatedAt: props.progress.updated_at,
        };
  const progressPercent =
    card.durationSeconds > 0 ? (card.positionSeconds / card.durationSeconds) * 100 : 0;
  const hasEpisodeMeta = card.seasonNumber != null && card.episodeNumber != null;
  const heading = hasEpisodeMeta && card.seriesTitle ? card.seriesTitle : card.title;
  const episodeLabel = hasEpisodeMeta
    ? `Season ${card.seasonNumber} Episode ${card.episodeNumber}`
    : null;
  const episodeMeta = hasEpisodeMeta
    ? card.seriesTitle && card.title
      ? `${episodeLabel} • ${card.title}`
      : episodeLabel
    : null;
  const timeLeftLabel = isNextUp
    ? "Next Episode"
    : card.durationSeconds > 0
      ? `${Math.round((card.durationSeconds - card.positionSeconds) / 60)} min left`
      : "\u00A0";

  return (
    <div className="group/card w-[260px] shrink-0 sm:w-[315px]">
      <div className="relative">
        <ViewTransitionLink to={card.watchHref} className="group/play block">
          <div className="media-card-image relative aspect-video overflow-hidden rounded-xl">
            {card.backdropUrl ? (
              <img
                src={card.backdropUrl}
                alt={heading}
                className="h-full w-full object-cover transition-transform duration-300 group-hover/play:scale-105"
                loading="lazy"
              />
            ) : card.posterUrl ? (
              <img
                src={card.posterUrl}
                alt={heading}
                className="h-full w-full object-cover transition-transform duration-300 group-hover/play:scale-105"
                loading="lazy"
              />
            ) : (
              <div className="text-muted-foreground bg-surface flex h-full w-full items-center justify-center text-sm">
                No Image
              </div>
            )}

            {/* Play overlay */}
            <div className="absolute inset-0 flex items-center justify-center bg-black/0 transition-colors duration-150 group-hover/play:bg-black/30">
              <div className="bg-primary text-primary-foreground flex h-11 w-11 items-center justify-center rounded-full opacity-0 shadow-lg transition-all duration-200 group-hover/play:scale-100 group-hover/play:opacity-100 group-focus-visible/play:opacity-100">
                <Play className="ml-0.5 h-5 w-5" fill="currentColor" />
              </div>
            </div>

            {/* Progress bar */}
            {!isNextUp && progressPercent > 0 && (
              <div className="absolute inset-x-0 bottom-0 h-[3px] bg-black/40">
                <div
                  className="h-full transition-all duration-300"
                  style={{
                    width: `${Math.min(progressPercent, 100)}%`,
                    background: "var(--primary)",
                  }}
                />
              </div>
            )}
          </div>
        </ViewTransitionLink>
        <MediaItemMenu
          contentId={
            "sectionItem" in props && props.sectionItem
              ? props.sectionItem.content_id
              : props.detail.content_id
          }
          mediaType={
            "sectionItem" in props && props.sectionItem ? props.sectionItem.type : props.detail.type
          }
          userState={
            "sectionItem" in props && props.sectionItem ? props.sectionItem.user_state : undefined
          }
          variant="wide"
          dismissAction={dismissAction}
        />
      </div>

      {/* Info */}
      <ViewTransitionLink to={card.itemHref} className="block px-0.5 pt-2.5">
        <div className="truncate text-[13px] font-semibold">{heading}</div>
        {episodeMeta && <div className="text-muted-foreground truncate text-xs">{episodeMeta}</div>}
        <div className="text-muted-foreground text-xs">{timeLeftLabel}</div>
      </ViewTransitionLink>
    </div>
  );
}
