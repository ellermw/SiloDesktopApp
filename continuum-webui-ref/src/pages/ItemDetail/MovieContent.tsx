import { useNavigate } from "react-router";
import type { ItemDetail } from "@/api/types";
import { useIsFavorite, useToggleFavorite } from "@/hooks/queries/favorites";
import { useIsInWatchlist, useToggleWatchlist } from "@/hooks/queries/watchlist";
import { useRefreshItemMetadata, useWatchedStateMutation } from "@/hooks/queries/items";
import { useRating, useSetRating, useDeleteRating } from "@/hooks/queries/ratings";
import { useSimilarItems } from "@/hooks/queries/recommendations";
import { useAuth } from "@/hooks/useAuth";
import { useAmbientColor } from "@/hooks/useAmbientColor";
import { useCurrentProfile } from "@/hooks/useCurrentProfile";
import CastCarousel from "@/components/CastCarousel";
import CrewList from "@/components/CrewList";
import RecommendationGrid from "@/components/RecommendationGrid";
import DetailHero from "./DetailHero";
import MetadataBadges from "./components/MetadataBadges";
import QualityBadges from "./components/QualityBadges";
import ScoreRow from "./components/ScoreRow";
import HeroCrewLine from "./components/HeroCrewLine";
import ActionBar from "./components/ActionBar";
import { resolveLeafPrimaryAction } from "./itemDetailLayout";
import { getWatchedActionLabel } from "./watchedState";

function formatDuration(minutes: number): string {
  if (minutes <= 0) return "";
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return h > 0 ? `${h}h ${m}m` : `${m}m`;
}

export default function MovieContent({ item }: { item: ItemDetail & { type: "movie" } }) {
  const navigate = useNavigate();
  useAmbientColor(item.backdrop_thumbhash);
  const { user } = useAuth();
  const isAdmin = user?.role === "admin";
  const { profile: currentProfile } = useCurrentProfile();

  const { data: isFavorite = false } = useIsFavorite(item.content_id);
  const { data: inWatchlist = false } = useIsInWatchlist(item.content_id);
  const toggleFavoriteMutation = useToggleFavorite(item.content_id);
  const toggleWatchlistMutation = useToggleWatchlist(item.content_id);
  const refreshMetadataMutation = useRefreshItemMetadata();
  const watchedMutation = useWatchedStateMutation(item);
  const { data: ratingData } = useRating(item.content_id);
  const setRatingMutation = useSetRating(item.content_id);
  const deleteRatingMutation = useDeleteRating(item.content_id);
  const primaryAction = resolveLeafPrimaryAction(item, "Play");
  const { data: similarData } = useSimilarItems(item.content_id);

  const handleRatingChange = (rating: number | null) => {
    if (rating === null) {
      deleteRatingMutation.mutate();
    } else {
      setRatingMutation.mutate(rating);
    }
  };

  const title = item.title ?? "";
  const year = item.year ? String(item.year) : "";
  const firstStudio = (item.studios ?? [])[0];

  return (
    <div>
      <DetailHero
        title={title}
        context="Movie"
        studioLabel={firstStudio}
        backdropUrl={item.backdrop_url}
        backdropThumbhash={item.backdrop_thumbhash}
        posterUrl={item.poster_url}
        posterThumbhash={item.poster_thumbhash}
        logoUrl={item.logo_url}
        tagline={item.tagline || undefined}
        metadata={
          <div className="flex flex-wrap items-center gap-2">
            <MetadataBadges
              year={year || undefined}
              contentRating={item.content_rating || undefined}
              duration={formatDuration(item.runtime ?? 0) || undefined}
            />
            <QualityBadges versions={item.versions} />
          </div>
        }
        scoreRow={
          <ScoreRow
            ratingImdb={item.rating_imdb}
            ratingRtCritic={item.rating_rt_critic}
            ratingRtAudience={item.rating_rt_audience}
          />
        }
        overview={item.overview}
        crewLine={<HeroCrewLine crew={item.crew ?? []} genres={item.genres} />}
        actions={
          <ActionBar
            contentId={item.content_id}
            playHref={item.versions.length > 0 ? `/watch/${item.content_id}` : undefined}
            playLabel={primaryAction.label}
            playProgress={primaryAction.progress}
            resumeResolution={
              item.user_data && "last_resolution" in item.user_data
                ? item.user_data.last_resolution
                : undefined
            }
            resumeHdr={
              item.user_data && "last_hdr" in item.user_data ? item.user_data.last_hdr : undefined
            }
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
            versions={item.versions}
            onPlayVersion={(fileId) => navigate(`/watch/${item.content_id}?fileId=${fileId}`)}
            rating={ratingData?.rating ?? null}
            onRatingChange={handleRatingChange}
            qualityPreference={currentProfile?.quality_preference}
          />
        }
      />

      <div className="page-shell space-y-12 py-10 sm:space-y-14">
        {item.cast && item.cast.length > 0 && (
          <div>
            <h2 className="mb-5 text-xl font-semibold tracking-tight">Cast</h2>
            <CastCarousel cast={item.cast} />
          </div>
        )}

        {item.crew && item.crew.length > 0 && <CrewList crew={item.crew} />}

        {/* More Like This */}
        {similarData?.items && similarData.items.length > 0 && (
          <div>
            <h2 className="mb-5 text-xl font-semibold tracking-tight">More Like This</h2>
            <RecommendationGrid items={similarData.items} />
          </div>
        )}
      </div>
    </div>
  );
}
