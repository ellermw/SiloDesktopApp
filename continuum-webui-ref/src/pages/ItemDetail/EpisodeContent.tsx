import { useLocation, useNavigate } from "react-router";
import type { ItemDetail } from "@/api/types";
import { useSeasonDetail, useSeasonEpisodes } from "@/hooks/queries/episodes";
import { useAmbientColor } from "@/hooks/useAmbientColor";
import { useAuth } from "@/hooks/useAuth";
import { useCurrentProfile } from "@/hooks/useCurrentProfile";
import { useRefreshItemMetadata, useWatchedStateMutation } from "@/hooks/queries/items";
import CastCarousel from "@/components/CastCarousel";
import CrewList from "@/components/CrewList";
import EpisodeCarousel from "./components/EpisodeCarousel";
import DetailHero from "./DetailHero";
import MetadataBadges from "./components/MetadataBadges";
import QualityBadges from "./components/QualityBadges";
import ScoreRow from "./components/ScoreRow";
import HeroCrewLine from "./components/HeroCrewLine";
import ActionBar from "./components/ActionBar";
import DetailBreadcrumb from "./components/DetailBreadcrumb";
import {
  getSeasonDisplayTitle,
  resolveLeafPrimaryAction,
  resolveEpisodeSiblingSeason,
  type EpisodeNavigationState,
} from "./itemDetailLayout";
import { getWatchedActionLabel } from "./watchedState";

function formatDuration(minutes: number): string {
  if (minutes <= 0) return "";
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return h > 0 ? `${h}h ${m}m` : `${m}m`;
}

export default function EpisodeContent({ item }: { item: ItemDetail & { type: "episode" } }) {
  const navigate = useNavigate();
  const location = useLocation();
  useAmbientColor(item.backdrop_thumbhash);
  const { user } = useAuth();
  const isAdmin = user?.role === "admin";
  const { profile: currentProfile } = useCurrentProfile();
  const refreshMetadataMutation = useRefreshItemMetadata();
  const watchedMutation = useWatchedStateMutation(item);
  const navigationState = location.state as EpisodeNavigationState | null;
  const primaryAction = resolveLeafPrimaryAction(item, "Play Episode");

  const title = item.title ?? "";
  const seriesTitle = item.series_title ?? "Series";
  const seriesId = item.series_id;
  const seasonNum = item.season_number;
  const episodeNum = item.episode_number;
  const siblingSeason = resolveEpisodeSiblingSeason(item);
  const { data: currentSeason } = useSeasonDetail(
    siblingSeason?.seriesId,
    siblingSeason?.seasonNumber ?? -1,
  );

  const ratingImdb = item.rating_imdb;
  const ratingTmdb = item.rating_tmdb;
  const effectiveRating = ratingImdb ?? ratingTmdb;

  // Sibling episodes now come from the season collection, not the current episode ID.
  const { data: episodesData } = useSeasonEpisodes(
    siblingSeason?.seriesId,
    siblingSeason?.seasonNumber ?? -1,
  );
  const siblingEpisodes = episodesData?.episodes ?? [];
  const seasonLabel =
    navigationState?.parentSeasonLabel ??
    (currentSeason
      ? getSeasonDisplayTitle(currentSeason)
      : seasonNum === 0
        ? "Specials"
        : seasonNum != null
          ? `Season ${seasonNum}`
          : "Season");
  const seasonHref =
    navigationState?.parentSeasonHref ??
    (currentSeason ? `/item/${currentSeason.content_id}` : undefined);
  const episodeLinkState =
    seasonHref || seasonLabel
      ? {
          parentSeasonHref: seasonHref,
          parentSeasonLabel: seasonLabel,
        }
      : undefined;

  // Build breadcrumb segments
  const breadcrumbSegments = [
    { label: seriesTitle, href: seriesId ? `/item/${seriesId}` : "/" },
  ] as Array<{ label: string; href?: string }>;
  if (seasonNum != null) {
    breadcrumbSegments.push({ label: seasonLabel, href: seasonHref });
  }
  if (episodeNum != null) {
    breadcrumbSegments.push({ label: `Episode ${episodeNum}` });
  }

  const contextLabel =
    seasonNum != null && episodeNum != null ? `S${seasonNum} \u00B7 E${episodeNum}` : undefined;

  return (
    <div>
      <DetailHero
        title={title}
        context={
          <div className="space-y-3">
            <DetailBreadcrumb segments={breadcrumbSegments} />
            {contextLabel && (
              <div className="text-muted-foreground text-xs font-medium">{contextLabel}</div>
            )}
          </div>
        }
        backdropUrl={item.backdrop_url}
        backdropThumbhash={item.backdrop_thumbhash}
        hidePoster
        logoUrl={item.logo_url}
        metadata={
          <div className="flex flex-wrap items-center gap-2">
            <MetadataBadges duration={formatDuration(item.runtime ?? 0) || undefined} />
            {item.air_date && <span className="metadata-badge">{item.air_date}</span>}
            <QualityBadges versions={item.versions ?? []} />
          </div>
        }
        scoreRow={
          <ScoreRow
            ratingImdb={effectiveRating}
            ratingRtCritic={item.rating_rt_critic}
            ratingRtAudience={item.rating_rt_audience}
          />
        }
        overview={item.overview}
        crewLine={<HeroCrewLine crew={item.crew ?? []} />}
        actions={
          <ActionBar
            contentId={item.content_id}
            playHref={
              item.versions && item.versions.length > 0 ? `/watch/${item.content_id}` : undefined
            }
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
            onRefresh={() => refreshMetadataMutation.mutate(item)}
            isRefreshing={refreshMetadataMutation.isPending}
            isAdmin={isAdmin}
            versions={item.versions ?? []}
            onPlayVersion={(fileId) => navigate(`/watch/${item.content_id}?fileId=${fileId}`)}
            qualityPreference={currentProfile?.quality_preference}
          />
        }
      />

      <div className="page-shell space-y-12 py-10 sm:space-y-14">
        {/* More Episodes carousel — most useful, so show first */}
        {siblingEpisodes.length > 1 && (
          <div>
            <h2 className="mb-5 text-xl font-semibold tracking-tight">More Episodes</h2>
            <EpisodeCarousel
              episodes={siblingEpisodes}
              currentEpisodeNumber={episodeNum ?? -1}
              episodeLinkState={episodeLinkState}
            />
          </div>
        )}

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
