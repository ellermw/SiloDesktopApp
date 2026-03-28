import { useState } from "react";
import ViewTransitionLink from "@/components/ViewTransitionLink";
import type { BrowseItem } from "@/api/types";
import { decodeThumbhash } from "@/lib/thumbhash";
import { timeAgo } from "@/lib/timeAgo";
import MediaItemMenu from "@/components/MediaItemMenu";

function SortMeta({ item, sortField }: { item: BrowseItem; sortField?: string }) {
  const defaultLabel = [item.year || "", item.type === "series" ? "Series" : ""].filter(Boolean).join(" · ");

  switch (sortField) {
    case "recently_added": {
      const ago = item.added_at ? timeAgo(item.added_at) : null;
      return <>{ago ?? defaultLabel}</>;
    }
    case "rating_imdb":
      return item.rating_imdb != null
        ? <><span className="not-uppercase">★</span> {item.rating_imdb.toFixed(1)} / 10</>
        : <>{defaultLabel}</>;
    case "release_date":
      return item.release_date
        ? <>{new Date(item.release_date).toLocaleDateString(undefined, { year: "numeric", month: "short", day: "numeric" })}</>
        : <>{defaultLabel}</>;
    default:
      return <>{defaultLabel}</>;
  }
}

export default function ItemCard({ item, sortField }: { item: BrowseItem; sortField?: string }) {
  const [loaded, setLoaded] = useState(false);
  const thumbhashUrl = item.poster_thumbhash ? decodeThumbhash(item.poster_thumbhash) : "";

  return (
    <div className="media-card group/card">
      <div className="relative">
        <ViewTransitionLink to={`/item/${item.content_id}`} className="block overflow-hidden rounded-xl">
          <div
            className="media-card-image relative aspect-[2/3]"
            style={
              thumbhashUrl
                ? {
                    backgroundImage: `url(${thumbhashUrl})`,
                    backgroundSize: "cover",
                    backgroundPosition: "center",
                  }
                : undefined
            }
          >
            {item.poster_url ? (
              <img
                src={item.poster_url}
                alt={item.title}
                className={`h-full w-full object-cover transition-opacity duration-300 ${loaded ? "opacity-100" : "opacity-0"}`}
                loading="lazy"
                onLoad={() => setLoaded(true)}
              />
            ) : (
              <div className="text-muted-foreground flex h-full w-full flex-col items-center justify-center gap-1 p-3 text-center text-sm">
                <span className="line-clamp-3 font-medium">{item.title || "No Poster"}</span>
              </div>
            )}
            <div className="pointer-events-none absolute inset-x-0 bottom-0 h-24 bg-gradient-to-t from-black/55 to-transparent opacity-90" />
            {item.status === "pending" && (
              <span className="glass-subtle text-foreground absolute top-2.5 left-2.5 rounded-full border border-white/15 px-2.5 py-1 text-[10px] font-semibold uppercase tracking-[0.14em]">
                Scanning
              </span>
            )}
            {item.status === "unmatched" && (
              <span className="glass-subtle absolute top-2.5 left-2.5 rounded-full border border-red-500/25 px-2.5 py-1 text-[10px] font-semibold uppercase tracking-[0.14em] text-red-300">
                Unmatched
              </span>
            )}
          </div>
        </ViewTransitionLink>
        <MediaItemMenu
          contentId={item.content_id}
          mediaType={item.type}
          userState={item.user_state}
          variant="poster"
        />
      </div>
      <ViewTransitionLink to={`/item/${item.content_id}`} className="block px-1 pt-3">
        <div className="truncate text-[14px] font-semibold tracking-tight">{item.title}</div>
        <div className="mt-1 text-[11px] font-medium uppercase tracking-[0.14em] text-muted-foreground">
          <SortMeta item={item} sortField={sortField} />
        </div>
      </ViewTransitionLink>
    </div>
  );
}
