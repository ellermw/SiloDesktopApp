import { useEffect } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useLibraryCollectionItems } from "@/hooks/queries/libraryCollections";
import { useLibrarySections } from "@/hooks/queries/sections";
import { useSidebarPins } from "@/hooks/queries/sidebarPins";
import MediaCarousel from "@/components/MediaCarousel";
import ItemCard from "@/components/ItemCard";
import HeroBanner from "@/components/HeroBanner";
import SectionRow from "@/components/SectionRow";
import { splitLibrarySections } from "./librarySectionLayout";
import { sectionKeys } from "@/hooks/queries/keys";

interface LibraryRecommendedProps {
  libraryId: number;
}

export default function LibraryRecommended({ libraryId }: LibraryRecommendedProps) {
  const queryClient = useQueryClient();
  const { data: librarySectionsData, isLoading } = useLibrarySections(libraryId);

  useEffect(() => {
    void queryClient.invalidateQueries({ queryKey: sectionKeys.library(libraryId) });
  }, [libraryId, queryClient]);

  const sections = librarySectionsData?.sections ?? [];
  const { hero, rows } = splitLibrarySections(sections);

  if (isLoading && sections.length === 0) {
    return null;
  }

  return (
    <div className="space-y-10 py-2 sm:space-y-12">
      {hero ? <HeroBanner items={hero.items} maxSlides={hero.item_limit} /> : null}
      {rows.map((section) => (
        <SectionRow key={section.id} section={section} libraryId={libraryId} />
      ))}

      {/* Pinned Collections */}
      <PinnedCollections libraryId={libraryId} />
    </div>
  );
}

function PinnedCollections({ libraryId }: { libraryId: number }) {
  const { pins } = useSidebarPins();
  const pinnedCollections = (pins[String(libraryId)] ?? []).filter(
    (pin) => pin.type === "collection",
  );

  return (
    <>
      {pinnedCollections.map((collection) => (
        <PinnedCollectionCarousel
          key={collection.id}
          libraryId={libraryId}
          collectionId={collection.id}
          name={collection.label}
        />
      ))}
    </>
  );
}

function PinnedCollectionCarousel({
  libraryId,
  collectionId,
  name,
}: {
  libraryId: number;
  collectionId: string;
  name: string;
}) {
  const { data: items, isLoading } = useLibraryCollectionItems(libraryId, collectionId);

  if (!isLoading && (!items || items.length === 0)) return null;

  return (
    <MediaCarousel title={name} loading={isLoading}>
      {(items ?? []).map((item) => (
        <div key={item.content_id} className="w-[140px] shrink-0 sm:w-[160px] lg:w-[184px]">
          <ItemCard item={item} />
        </div>
      ))}
    </MediaCarousel>
  );
}
