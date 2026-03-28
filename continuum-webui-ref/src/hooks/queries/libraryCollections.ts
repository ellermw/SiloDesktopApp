import { useQuery } from "@tanstack/react-query";
import { api } from "@/api/client";
import type { BrowseItem, LibraryCollection } from "@/api/types";
import { libraryCollectionKeys } from "./keys";

export function useLibraryCollections(libraryId: number) {
  return useQuery({
    queryKey: libraryCollectionKeys.list(libraryId),
    queryFn: () =>
      api<{ collections: LibraryCollection[] }>(`/library/${libraryId}/collections`).then(
        (data) => data.collections ?? [],
      ),
    enabled: Number.isFinite(libraryId) && libraryId > 0,
  });
}

export function useLibraryCollectionItems(libraryId: number, collectionId: string | null) {
  return useQuery({
    queryKey: libraryCollectionKeys.items(libraryId, collectionId ?? ""),
    queryFn: () =>
      api<{ items: BrowseItem[] }>(`/library/${libraryId}/collections/${collectionId}/items`).then(
        (data) => data.items ?? [],
      ),
    enabled:
      Number.isFinite(libraryId) &&
      libraryId > 0 &&
      collectionId !== null &&
      collectionId.length > 0,
  });
}
