import { useMemo } from "react";
import type { ItemDetail } from "@/api/types";
import { useIsFavorite, useToggleFavorite } from "@/hooks/queries/favorites";
import { useIsInWatchlist, useToggleWatchlist } from "@/hooks/queries/watchlist";
import { useRefreshItemMetadata, useWatchedStateMutation } from "@/hooks/queries/items";
import { useItemEpisodes, useSeasons } from "@/hooks/queries/episodes";
import { useContinueWatching } from "@/hooks/queries/progress";
import { useRating, useSetRating, useDeleteRating } from "@/hooks/queries/ratings";
import { useAmbientColor } from "@/hooks/useAmbientColor";
import { useAuth } from "@/hooks/useAuth";
import CastCarousel from "@/components/CastCarousel";
import CrewList from "@/components/CrewList";
import DetailHero from "./DetailHero";
import SeasonCarousel from "./SeasonCarousel";
import MetadataBadges from "./components/MetadataBadges";
import ScoreRow from "./components/ScoreRow";
import HeroCrewLine from "./components/HeroCrewLine";
import ActionBar from "./components/ActionBar";
import { resolveSeriesPrimaryAction } from "./itemDetailLayout";
import { getWatchedActionLabel } from "./watchedState";

export default function SeriesContent({ item }: { item: ItemDetail & { type: "series" } }) {
  useAmbientColor(item.backdrop_thumbhash);
  const { user } = useAuth();
  const isAdmin = user?.role === "admin";

  const { data: isFavorite = false } = useIsFavorite(item.content_id);
  const { data: inWatchlist = false } = useIsInWatchlist(item.content_id);
  const toggleFavoriteMutation = useToggleFavorite(item.content_id);
  const toggleWatchlistMutation = useToggleWatchlist(item.content_id);
  const refreshMetadataMutation = useRefreshItemMetadata();
  const watchedMutation = useWatchedStateMutation(item);
  const { data: ratingData } = useRating(item.content_id);
  const setRatingMutation = useSetRating(item.content_id);
  const deleteRatingMutation = useDeleteRating(item.content_id);

  const { data: seasonsData } = useSeasons(item.content_id);
  const seasons = useMemo(() => seasonsData?.seasons ?? [], [seasonsData?.seasons]);
  const { items: continueWatchingItems } = useContinueWatching();

  const handleRatingChange = (rating: number | null) => {
    if (rating === null) {
      deleteRatingMutation.mutate();
    } else {
      setRatingMutation.mutate(rating);
    }
  };

  const title = item.title ?? "";
  const firstYear = item.first_air_date?.slice(0, 4);
  const lastYear = item.last_air_date?.slice(0, 4);
  const yearDisplay = firstYear
    ? lastYear && lastYear !== firstYear
      ? `${firstYear}\u2013${lastYear}`
      : firstYear
    : "";

  const firstNetwork = (item.networks ?? [])[0];
  const episodeCount = seasons.reduce((sum, s) => sum + s.episode_count, 0);

  const primaryAction = useMemo(
    () =>
      resolveSeriesPrimaryAction({
        seriesId: item.content_id,
        seasons,
        continueWatching: continueWatchingItems
          .filter((entry) => entry.detail?.series_id === item.content_id)
          .map((entry) => ({
            contentId: entry.detail?.content_id ?? entry.progress.media_item_id,
            seriesId: entry.detail?.series_id,
            title: entry.detail?.title ?? "",
          })),
      }),
    [continueWatchingItems, item.content_id, seasons],
  );
  const { data: primaryActionEpisodes } = useItemEpisodes(primaryAction.targetSeasonId);

  const resolvedPrimaryHref = useMemo(() => {
    if (primaryAction.directHref) return primaryAction.directHref;

    const episodes = primaryActionEpisodes?.episodes ?? [];
    if (episodes.length === 0 || primaryAction.targetEpisodeNumber == null) {
      return undefined;
    }

    const targetIndex = Math.max(
      0,
      Math.min(primaryAction.targetEpisodeNumber - 1, episodes.length - 1),
    );
    const targetEpisode = episodes[targetIndex];
    return targetEpisode ? `/watch/${targetEpisode.content_id}` : undefined;
  }, [primaryAction.directHref, primaryAction.targetEpisodeNumber, primaryActionEpisodes]);

  return (
    <div>
      <DetailHero
        title={title}
        context="Series"
        studioLabel={firstNetwork}
        backdropUrl={item.backdrop_url}
        backdropThumbhash={item.backdrop_thumbhash}
        posterUrl={item.poster_url}
        posterThumbhash={item.poster_thumbhash}
        logoUrl={item.logo_url}
        tagline={item.tagline || undefined}
        metadata={
          <MetadataBadges
            year={yearDisplay || undefined}
            contentRating={item.content_rating || undefined}
            seasonCount={seasons.length || undefined}
            episodeCount={episodeCount || undefined}
          />
        }
        scoreRow={
          <ScoreRow
            ratingImdb={item.rating_imdb}
            ratingRtCritic={item.rating_rt_critic}
            ratingRtAudience={item.rating_rt_audience}
          />
        }
        overview={item.overview}
        crewLine={
          <HeroCrewLine crew={item.crew ?? []} genres={item.genres} jobLabel="Created by" />
        }
        actions={
          <ActionBar
            contentId={item.content_id}
            playHref={resolvedPrimaryHref}
            playLabel={primaryAction.label}
            watchedLabel={getWatchedActionLabel(item)}
            onToggleWatched={() => watchedMutation.mutate(!(item.user_data?.played ?? false))}
            isUpdatingWatched={watchedMutation.isPending}
            onToggleFavorite={() => toggleFavoriteMutation.mutate(isFavorite)}
            isFavorite={isFavorite}
            onToggleWatchlist={() => toggleWatchlistMutation.mutate(inWatchlist)}
            inWatchlist={inWatchlist}
            onRefresh={() => refreshMetadataMutation.mutate(item)}
            isRefreshing={refreshMetadataMutation.isPending}
            isAdmin={isAdmin}
            rating={ratingData?.rating ?? null}
            onRatingChange={handleRatingChange}
          />
        }
      />

      <div className="page-shell space-y-12 py-10 sm:space-y-14">
        {seasons.length > 0 && <SeasonCarousel seasons={seasons} />}
        {item.cast && item.cast.length > 0 && (
          <div>
            <h2 className="mb-5 text-xl font-semibold tracking-tight">Cast</h2>
            <CastCarousel cast={item.cast} />
          </div>
        )}
        {item.crew && item.crew.length > 0 && <CrewList crew={item.crew} />}
      </div>
    </div>
  );
}
