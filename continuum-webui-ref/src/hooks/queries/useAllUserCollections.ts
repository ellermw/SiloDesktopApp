import { useQueries } from "@tanstack/react-query";
import { api } from "@/api/client";
import type { LibraryCollection } from "@/api/types";
import { useUserLibraries } from "./libraries";
import { libraryCollectionKeys } from "./keys";
import { useCollections } from "./collections";

export interface CollectionOption {
  id: string;
  title: string;
  source: "library" | "user";
  group: string;
  library_id?: number;
  library_name?: string;
}

export function useAllUserCollections() {
  const { data: libraries } = useUserLibraries();
  const { data: userCollections, isLoading: userCollectionsLoading } = useCollections();

  const libraryQueries = useQueries({
    queries: (libraries ?? []).map((lib) => ({
      queryKey: libraryCollectionKeys.list(lib.id),
      queryFn: () =>
        api<{ collections: LibraryCollection[] }>(`/library/${lib.id}/collections`).then(
          (data) => data.collections ?? [],
        ),
      enabled: Number.isFinite(lib.id) && lib.id > 0,
    })),
  });

  const isLoading = libraryQueries.some((q) => q.isLoading) || userCollectionsLoading;

  const collections: CollectionOption[] = [];

  // Add personal user collections first (grouped under "My Collections").
  if (userCollections) {
    for (const c of userCollections) {
      collections.push({
        id: c.id,
        title: c.name,
        source: "user",
        group: "My Collections",
      });
    }
  }

  // Add library collections grouped by library name.
  if (libraries) {
    for (let i = 0; i < libraries.length; i++) {
      const lib = libraries[i]!;
      const result = libraryQueries[i];
      if (result?.data) {
        for (const c of result.data) {
          collections.push({
            id: c.id,
            title: c.title,
            source: "library",
            group: lib.name,
            library_id: lib.id,
            library_name: lib.name,
          });
        }
      }
    }
  }

  return { collections, isLoading };
}
