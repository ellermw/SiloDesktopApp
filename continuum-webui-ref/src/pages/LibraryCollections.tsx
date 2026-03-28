import { useState } from "react";
import type { LibraryCollection } from "@/api/types";
import { useLibraryCollections } from "@/hooks/queries/libraryCollections";
import { useToggleSidebarPin } from "@/hooks/queries/sidebarPins";
import { useViewTransitionNavigate } from "@/hooks/useViewTransition";
import { buildLibraryCollectionCatalogHref } from "@/pages/catalogSearchParams";
import { Card, CardContent } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { Pin, PinOff } from "lucide-react";

interface LibraryCollectionsProps {
  libraryId: number;
}

const GRID_CLASSES =
  "grid grid-cols-3 sm:grid-cols-4 md:grid-cols-5 lg:grid-cols-7 xl:grid-cols-8 gap-3";

export default function LibraryCollections({ libraryId }: LibraryCollectionsProps) {
  const { data: collections = [], isLoading } = useLibraryCollections(libraryId);

  if (isLoading) {
    return (
      <div className="page-shell py-6 sm:py-8">
        <div className={GRID_CLASSES}>
          {Array.from({ length: 24 }, (_, i) => (
            <div key={i}>
              <Skeleton className="aspect-[2/3] rounded-lg" />
              <Skeleton className="mt-2 h-4 w-3/4" />
            </div>
          ))}
        </div>
      </div>
    );
  }

  if (collections.length === 0) {
    return (
      <div className="page-shell py-6 sm:py-8">
        <Card className="surface-panel overflow-hidden rounded-[2rem] border-0 shadow-none">
          <CardContent className="py-10 text-center">
            <p className="text-lg font-semibold">No collections yet</p>
            <p className="text-muted-foreground mt-2 text-sm">
              Create library collections from the admin area to feature curated shelves here.
            </p>
          </CardContent>
        </Card>
      </div>
    );
  }

  return (
    <div className="page-shell space-y-6 py-6 sm:py-8">
      <div className="page-header gap-5">
        <div className="space-y-3">
          <h1 className="page-title text-[clamp(2rem,4vw,3rem)]">Collections</h1>
          <p className="page-subtitle text-sm sm:text-base">
            Browse hand-picked shelves and smart lists created for this library.
          </p>
        </div>
      </div>
      <div className={GRID_CLASSES}>
        {collections.map((collection) => (
          <CollectionPosterCard key={collection.id} collection={collection} libraryId={libraryId} />
        ))}
      </div>
    </div>
  );
}

function CollectionPosterCard({
  collection,
  libraryId,
}: {
  collection: LibraryCollection;
  libraryId: number;
}) {
  const [loaded, setLoaded] = useState(false);
  const navigate = useViewTransitionNavigate();
  const { togglePin, isPinned } = useToggleSidebarPin();
  const pinned = isPinned(libraryId, "collection", collection.id);

  return (
    <div className="group/card relative w-full text-left">
      <button
        type="button"
        onClick={() => navigate(buildLibraryCollectionCatalogHref(collection.id, collection.title))}
        className="block w-full text-left"
      >
        <div className="media-card-image relative aspect-[2/3] overflow-hidden rounded-xl">
          {collection.poster_url ? (
            <img
              src={collection.poster_url}
              alt={collection.title}
              className={`h-full w-full object-cover transition-opacity duration-300 ${
                loaded ? "opacity-100" : "opacity-0"
              }`}
              loading="lazy"
              onLoad={() => setLoaded(true)}
            />
          ) : (
            <div className="text-muted-foreground flex h-full w-full flex-col items-center justify-center gap-1 p-3 text-center text-sm">
              <span className="line-clamp-3 font-medium">{collection.title}</span>
            </div>
          )}
          <span className="absolute right-2 bottom-2 rounded-md bg-black/60 px-2 py-0.5 text-[11px] font-bold text-foreground backdrop-blur-sm">
            {collection.collection_type === "smart" ? "\u2014" : collection.item_count}
          </span>
        </div>
        <div className="px-0.5 pt-2.5">
          <div className="truncate text-[13px] font-semibold">{collection.title}</div>
          <div className="text-muted-foreground text-xs">{collection.collection_type}</div>
        </div>
      </button>
      {/* Pin to sidebar button */}
      <button
        type="button"
        onClick={(e) => {
          e.stopPropagation();
          togglePin(libraryId, {
            type: "collection",
            id: collection.id,
            label: collection.title,
          });
        }}
        className={`absolute top-2 left-2 rounded-lg p-1.5 backdrop-blur-sm transition-all ${
          pinned
            ? "bg-primary/90 text-primary-foreground"
            : "bg-black/50 text-white opacity-0 group-hover/card:opacity-100"
        }`}
        title={pinned ? "Unpin from sidebar" : "Pin to sidebar"}
      >
        {pinned ? <PinOff className="h-3.5 w-3.5" /> : <Pin className="h-3.5 w-3.5" />}
      </button>
    </div>
  );
}
