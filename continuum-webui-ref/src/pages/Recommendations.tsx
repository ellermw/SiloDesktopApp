import {
  useForYouMain,
  useForYouRows,
  useSimilarUsers,
  useTasteProfile,
} from "@/hooks/queries/recommendations";
import type { ForYouRow } from "@/hooks/queries/recommendations";
import RecommendationGrid from "@/components/RecommendationGrid";
import { useDocumentTitle } from "@/hooks/useDocumentTitle";

function TasteProfileCard({
  profile,
  isLoading,
}: {
  profile:
    | {
        top_genres: string[];
        favorite_directors: string[];
        signal_counts: Record<string, number>;
      }
    | undefined;
  isLoading: boolean;
}) {
  if (isLoading) {
    return (
      <div className="glass-subtle rounded-xl p-5">
        <div className="bg-muted h-5 w-32 animate-pulse rounded" />
      </div>
    );
  }

  if (!profile || (profile.top_genres.length === 0 && profile.favorite_directors.length === 0)) {
    return (
      <div className="glass-subtle rounded-xl p-5">
        <p className="text-muted-foreground text-sm">
          Rate some items to get personalized recommendations
        </p>
      </div>
    );
  }

  const totalSignals = Object.values(profile.signal_counts).reduce((a, b) => a + b, 0);

  return (
    <div className="glass-subtle space-y-4 rounded-xl p-5">
      <div className="flex items-center justify-between">
        <h2 className="text-base font-semibold">Your Taste Profile</h2>
        {totalSignals > 0 && (
          <span className="text-muted-foreground text-xs">
            {totalSignals} signal{totalSignals !== 1 ? "s" : ""}
          </span>
        )}
      </div>

      {profile.top_genres.length > 0 && (
        <div className="space-y-1.5">
          <p className="text-muted-foreground text-xs font-medium tracking-wide uppercase">
            Top Genres
          </p>
          <div className="flex flex-wrap gap-1.5">
            {profile.top_genres.map((genre) => (
              <span
                key={genre}
                className="bg-accent text-foreground rounded-full px-2.5 py-0.5 text-xs font-medium"
              >
                {genre}
              </span>
            ))}
          </div>
        </div>
      )}

      {profile.favorite_directors.length > 0 && (
        <div className="space-y-1.5">
          <p className="text-muted-foreground text-xs font-medium tracking-wide uppercase">
            Favorite Directors
          </p>
          <div className="flex flex-wrap gap-1.5">
            {profile.favorite_directors.map((director) => (
              <span
                key={director}
                className="border-border text-foreground rounded-full border px-2.5 py-0.5 text-xs"
              >
                {director}
              </span>
            ))}
          </div>
        </div>
      )}

      {Object.keys(profile.signal_counts).length > 0 && (
        <div className="space-y-1.5">
          <p className="text-muted-foreground text-xs font-medium tracking-wide uppercase">
            Activity
          </p>
          <div className="flex flex-wrap gap-3">
            {Object.entries(profile.signal_counts).map(([key, count]) => (
              <div key={key} className="text-sm">
                <span className="font-semibold">{count}</span>{" "}
                <span className="text-muted-foreground capitalize">{key.replace(/_/g, " ")}</span>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}

function RecommendationRow({ row }: { row: ForYouRow }) {
  if (!row.items || row.items.length === 0) return null;

  return (
    <section className="space-y-4">
      <h2 className="text-[20px] font-bold">{row.label}</h2>
      <RecommendationGrid items={row.items} />
    </section>
  );
}

function ForYouSection({ rows, isLoading }: { rows: ForYouRow[]; isLoading: boolean }) {

  if (isLoading) {
    return (
      <section className="space-y-4">
        <h2 className="text-[20px] font-bold">For You</h2>
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6">
          {Array.from({ length: 12 }).map((_, i) => (
            <div key={i} className="bg-muted aspect-[2/3] animate-pulse rounded-xl" />
          ))}
        </div>
      </section>
    );
  }

  if (rows.length === 0) {
    return (
      <section className="space-y-4">
        <h2 className="text-[20px] font-bold">For You</h2>
        <p className="text-muted-foreground text-sm">Not enough data for recommendations yet</p>
      </section>
    );
  }

  return (
    <>
      {rows.map((row, i) => (
        <RecommendationRow key={`${row.type}-${row.cluster_index ?? i}`} row={row} />
      ))}
    </>
  );
}

function SimilarUsersSection({
  items,
  isLoading,
}: {
  items: { media_item_id: string; score: number; reason: string }[] | undefined;
  isLoading: boolean;
}) {
  return (
    <section className="space-y-4">
      <h2 className="text-[20px] font-bold">Users Like You Also Enjoyed</h2>
      {isLoading ? (
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6">
          {Array.from({ length: 12 }).map((_, i) => (
            <div key={i} className="bg-muted aspect-[2/3] animate-pulse rounded-xl" />
          ))}
        </div>
      ) : !items || items.length === 0 ? (
        <p className="text-muted-foreground text-sm">
          No collaborative recommendations available yet
        </p>
      ) : (
        <RecommendationGrid items={items} />
      )}
    </section>
  );
}

export default function Recommendations() {
  useDocumentTitle("Recommendations");

  const tasteProfileQuery = useTasteProfile();
  const forYouMainQuery = useForYouMain();
  const forYouRowsEnabled = !forYouMainQuery.isLoading;
  const forYouRowsQuery = useForYouRows(forYouRowsEnabled);
  const similarUsersEnabled = forYouRowsEnabled && !forYouRowsQuery.isLoading;
  const similarUsersQuery = useSimilarUsers(similarUsersEnabled);

  const forYouRows = [
    ...(forYouMainQuery.data?.row ? [forYouMainQuery.data.row] : []),
    ...(forYouRowsQuery.data?.rows ?? []),
  ];
  const forYouLoading = forYouMainQuery.isLoading || (forYouRowsEnabled && forYouRowsQuery.isLoading);

  return (
    <div className="page-shell space-y-10 py-6 sm:space-y-12">
      <div className="page-header">
        <div className="space-y-3">
          <h1 className="page-title text-[clamp(2rem,5vw,3.5rem)]">Recommendations</h1>
          <p className="page-subtitle text-sm sm:text-base">
            A living view of what fits your taste profile, your activity, and overlapping audience patterns.
          </p>
        </div>
      </div>

      <TasteProfileCard profile={tasteProfileQuery.data} isLoading={tasteProfileQuery.isLoading} />
      <ForYouSection rows={forYouRows} isLoading={forYouLoading} />
      <SimilarUsersSection
        items={similarUsersQuery.data?.items}
        isLoading={!similarUsersEnabled || similarUsersQuery.isLoading}
      />
    </div>
  );
}
