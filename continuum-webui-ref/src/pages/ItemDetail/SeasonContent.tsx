import { Link } from "react-router";
import { Check, Play } from "lucide-react";
import type { ItemDetail } from "@/api/types";
import { useItemEpisodes } from "@/hooks/queries/episodes";
import { useRefreshItemMetadata, useWatchedStateMutation } from "@/hooks/queries/items";
import { useAmbientColor } from "@/hooks/useAmbientColor";
import { useAuth } from "@/hooks/useAuth";
import CastCarousel from "@/components/CastCarousel";
import CrewList from "@/components/CrewList";
import MediaItemMenu from "@/components/MediaItemMenu";
import { Skeleton } from "@/components/ui/skeleton";
import DetailHero from "./DetailHero";
import MetadataBadges from "./components/MetadataBadges";
import ActionBar from "./components/ActionBar";
import DetailBreadcrumb from "./components/DetailBreadcrumb";
import type { EpisodeNavigationState } from "./itemDetailLayout";
import { getWatchedActionLabel } from "./watchedState";

function seasonLabel(seasonNumber: number, title?: string) {
  if (title) return title;
  if (seasonNumber === 0) return "Specials";
  return `Season ${seasonNumber}`;
}

export default function SeasonContent({ item }: { item: ItemDetail & { type: "season" } }) {
  useAmbientColor(item.backdrop_thumbhash);
  const { user } = useAuth();
  const isAdmin = user?.role === "admin";
  const watchedMutation = useWatchedStateMutation(item);
  const refreshMetadataMutation = useRefreshItemMetadata();

  const {
    data: episodesData,
    isLoading: episodesLoading,
    error: episodesError,
  } = useItemEpisodes(item.content_id);

  const episodes = episodesData?.episodes ?? [];
  const seasonNumber = item.season_number ?? 0;
  const label = item.is_specials ? "Specials" : seasonLabel(seasonNumber, item.title);
  const seriesTitle = item.series_title ?? "Series";
  const seriesId = item.series_id;
  const firstEpisode = episodes[0];
  const episodeLinkState: EpisodeNavigationState = {
    parentSeasonHref: `/item/${item.content_id}`,
    parentSeasonLabel: label,
  };

  const displayTitle = `${seriesTitle}: ${label}`;

  const breadcrumb = (
    <DetailBreadcrumb
      segments={[
        { label: seriesTitle, href: seriesId ? `/item/${seriesId}` : "/" },
        { label: label },
      ]}
    />
  );

  const yearStr = item.air_date?.slice(0, 4);

  if (episodesError) {
    return (
      <div className="px-4 py-6 sm:px-6 sm:py-10 lg:px-12">
        <Link
          to={seriesId ? `/item/${seriesId}` : "/"}
          className="text-muted-foreground hover:text-foreground text-sm"
        >
          &larr; Back to {seriesTitle}
        </Link>
        <p className="text-muted-foreground mt-6 text-sm">
          {episodesError instanceof Error ? episodesError.message : "Season not found"}
        </p>
      </div>
    );
  }

  return (
    <div>
      <DetailHero
        variant="compact"
        title={displayTitle}
        context={breadcrumb}
        backdropUrl={item.backdrop_url}
        backdropThumbhash={item.backdrop_thumbhash}
        posterUrl={item.poster_url}
        posterThumbhash={item.poster_thumbhash}
        logoUrl={item.logo_url}
        metadata={
          <MetadataBadges
            year={yearStr || undefined}
            episodeCount={item.episode_count ?? episodes.length}
          />
        }
        overview={item.overview}
        actions={
          <ActionBar
            contentId={item.content_id}
            playHref={firstEpisode ? `/watch/${firstEpisode.content_id}` : undefined}
            playLabel="Play First Episode"
            watchedLabel={getWatchedActionLabel(item)}
            onToggleWatched={() => watchedMutation.mutate(!(item.user_data?.played ?? false))}
            isUpdatingWatched={watchedMutation.isPending}
            onRefresh={() => refreshMetadataMutation.mutate(item)}
            isRefreshing={refreshMetadataMutation.isPending}
            isAdmin={isAdmin}
          />
        }
      />

      <div className="page-shell py-8 sm:py-10">
        <div className="mb-5 flex items-center justify-between gap-3">
          <h2 className="text-xl font-semibold tracking-tight">Episodes</h2>
          <span className="text-muted-foreground text-sm">
            {item.episode_count ?? episodes.length} total
          </span>
        </div>

        {episodesLoading ? (
          <div className="grid grid-cols-1 gap-4 min-[460px]:grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5">
            {Array.from({ length: 10 }, (_, i) => i).map((i) => (
              <div key={i}>
                <Skeleton className="aspect-video w-full rounded-lg" />
                <Skeleton className="mt-2 h-3 w-16" />
                <Skeleton className="mt-1 h-4 w-24" />
              </div>
            ))}
          </div>
        ) : episodes.length > 0 ? (
          <div className="grid grid-cols-1 gap-4 min-[460px]:grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5">
            {episodes.map((episode) => (
              <div key={episode.content_id} className="group/card media-card">
                <div className="relative">
                  <Link
                    to={`/item/${episode.content_id}`}
                    state={episodeLinkState}
                    className="group block"
                  >
                    <div className="media-card-image relative aspect-video">
                      {episode.still_url ? (
                        <img
                          src={episode.still_url}
                          alt={episode.title || `Episode ${episode.episode_number}`}
                          className="h-full w-full object-cover transition-transform duration-300 group-hover:scale-[1.03]"
                          loading="lazy"
                        />
                      ) : (
                        <div className="flex h-full w-full items-center justify-center">
                          <Play size={32} className="text-muted-foreground/30" />
                        </div>
                      )}
                      {episode.user_data?.played && (
                        <div className="watched-badge">
                          <Check className="size-4" />
                        </div>
                      )}
                      {!episode.user_data?.played &&
                        (episode.user_data?.position_seconds ?? 0) > 0 &&
                        (episode.user_data?.duration_seconds ?? 0) > 0 && (
                          <div className="absolute inset-x-0 bottom-0 h-[3px] bg-black/40">
                            <div
                              className="progress-fill h-full rounded-r-sm"
                              style={{
                                width: `${Math.max(
                                  0,
                                  Math.min(
                                    100,
                                    ((episode.user_data?.position_seconds ?? 0) /
                                      (episode.user_data?.duration_seconds ?? 1)) *
                                      100,
                                  ),
                                )}%`,
                                background: "var(--primary)",
                              }}
                            />
                          </div>
                        )}
                    </div>
                  </Link>
                  <MediaItemMenu
                    contentId={episode.content_id}
                    mediaType="episode"
                    userState={
                      episode.user_data
                        ? {
                            played: episode.user_data.played,
                            is_favorite: false,
                            in_watchlist: false,
                          }
                        : undefined
                    }
                    variant="wide"
                    showCollectionActions={false}
                  />
                </div>
                <Link
                  to={`/item/${episode.content_id}`}
                  state={episodeLinkState}
                  className="block"
                >
                  <p className="text-muted-foreground mt-2 text-xs">
                    Episode {episode.episode_number}
                  </p>
                  <p className="text-foreground truncate text-sm font-semibold">
                    {episode.title || `Episode ${episode.episode_number}`}
                  </p>
                </Link>
              </div>
            ))}
          </div>
        ) : (
          <div className="border-border text-muted-foreground bg-surface rounded-lg border p-5 text-sm">
            No episodes are available for this season yet.
          </div>
        )}

        {item.cast && item.cast.length > 0 && (
          <div className="mt-10">
            <h2 className="mb-4 text-xl font-semibold tracking-tight">Cast</h2>
            <CastCarousel cast={item.cast} />
          </div>
        )}

        {item.crew && item.crew.length > 0 && (
          <div className="mt-10">
            <CrewList crew={item.crew} />
          </div>
        )}
      </div>
    </div>
  );
}
