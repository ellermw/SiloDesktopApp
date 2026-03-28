import { useState } from "react";
import ViewTransitionLink from "@/components/ViewTransitionLink";
import ContinueWatchingCard from "@/components/ContinueWatchingCard";
import MediaCarousel from "@/components/MediaCarousel";
import MediaItemMenu from "@/components/MediaItemMenu";
import { decodeThumbhash } from "@/lib/thumbhash";
import { useViewTransitionNavigate } from "@/hooks/useViewTransition";
import { useToggleSidebarPin } from "@/hooks/queries/sidebarPins";
import {
  buildSectionCatalogHref,
  isSectionBrowseSupported,
} from "@/pages/catalogSearchParams";
import type { ResolvedSection, SectionItem } from "@/api/types";
import { Pin, PinOff } from "lucide-react";

interface SectionRowProps {
  section: ResolvedSection;
  /** Library ID this section belongs to (enables pin-to-sidebar) */
  libraryId?: number;
}

function SectionItemCard({ item }: { item: SectionItem }) {
  const [loaded, setLoaded] = useState(false);
  const thumbhashUrl = item.poster_thumbhash ? decodeThumbhash(item.poster_thumbhash) : "";

  return (
    <div className="media-card group/card">
      <div className="relative">
        <ViewTransitionLink
          to={`/item/${item.content_id}`}
          className="block overflow-hidden rounded-xl"
        >
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
          {item.year ? `${item.year}` : ""} {item.type === "series" ? "Series" : ""}
        </div>
      </ViewTransitionLink>
    </div>
  );
}

function SectionPinButton({
  sectionId,
  sectionTitle,
  libraryId,
}: {
  sectionId: string;
  sectionTitle: string;
  libraryId: number;
}) {
  const { togglePin, isPinned } = useToggleSidebarPin();
  const pinned = isPinned(libraryId, "section", sectionId);

  return (
    <button
      onClick={() => togglePin(libraryId, { type: "section", id: sectionId, label: sectionTitle })}
      className={`rounded-full p-1.5 transition-colors ${
        pinned
          ? "text-primary hover:text-primary/80"
          : "text-muted-foreground/40 hover:bg-accent/60 hover:text-muted-foreground opacity-0 group-hover/carousel:opacity-100"
      }`}
      title={pinned ? "Unpin from sidebar" : "Pin to sidebar"}
    >
      {pinned ? <PinOff className="h-4 w-4" /> : <Pin className="h-4 w-4" />}
    </button>
  );
}

export default function SectionRow({ section, libraryId }: SectionRowProps) {
  const navigate = useViewTransitionNavigate();
  const browseSupported = isSectionBrowseSupported(section.section_type);

  const handleViewAll = () => {
    if (!browseSupported) {
      return;
    }

    navigate(
      libraryId
        ? buildSectionCatalogHref({
            scope: "library",
            libraryId: libraryId,
            sectionId: section.id,
            title: section.title,
          })
        : buildSectionCatalogHref({
            scope: "home",
            sectionId: section.id,
            title: section.title,
          }),
    );
  };

  if (section.items.length === 0) return null;

  const headerActions =
    libraryId && browseSupported ? (
      <SectionPinButton sectionId={section.id} sectionTitle={section.title} libraryId={libraryId} />
    ) : undefined;

  return (
    <MediaCarousel
      title={section.title}
      onViewAll={
        browseSupported && section.total_count > section.item_limit ? handleViewAll : undefined
      }
      headerActions={headerActions}
    >
      {section.section_type === "continue_watching" || section.section_type === "next_up"
        ? section.items.map((item) => (
            <ContinueWatchingCard key={item.content_id} sectionItem={item} />
          ))
        : section.items.map((item) => (
            <div key={item.content_id} className="w-[140px] shrink-0 sm:w-[160px] lg:w-[185px]">
              <SectionItemCard item={item} />
            </div>
          ))}
    </MediaCarousel>
  );
}
