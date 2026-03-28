import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { api } from "@/api/client";
import type {
  CreateLibraryCollectionRequest,
  ImportMDBListCollectionRequest,
  ImportMDBListCollectionResponse,
  ImportTMDBCollectionRequest,
  ImportTMDBCollectionResponse,
  LibraryCollection,
  LibraryCollectionSyncRun,
  UpdateLibraryCollectionRequest,
} from "@/api/types";
import { adminKeys, libraryCollectionKeys } from "../keys";
import { invalidateLibraryCollectionQueries } from "../collectionSurfaceRefresh";

const ADMIN_STALE_TIME = 30_000;

function buildCollectionFormData(
  data: Record<string, unknown>,
  poster?: File | null,
  backdrop?: File | null,
): FormData | string {
  if (!poster && !backdrop) {
    return JSON.stringify(data);
  }
  const formData = new FormData();
  formData.append("data", JSON.stringify(data));
  if (poster) formData.append("poster", poster);
  if (backdrop) formData.append("backdrop", backdrop);
  return formData;
}

export function useAdminCollections(libraryId?: number) {
  return useQuery({
    queryKey: adminKeys.collections(libraryId),
    queryFn: () => {
      const query = libraryId ? `?library_id=${libraryId}` : "";
      return api<{ collections: LibraryCollection[] }>(`/admin/collections${query}`).then(
        (data) => data.collections ?? [],
      );
    },
    staleTime: ADMIN_STALE_TIME,
  });
}

export function useCreateAdminCollection() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      body,
      poster,
      backdrop,
    }: {
      body: CreateLibraryCollectionRequest;
      poster?: File | null;
      backdrop?: File | null;
    }) => {
      const payload = buildCollectionFormData(
        body as unknown as Record<string, unknown>,
        poster,
        backdrop,
      );
      return api<LibraryCollection>("/admin/collections", {
        method: "POST",
        body: payload,
      });
    },
    onSuccess: (collection) => {
      toast.success("Collection created");
      const libraryIds =
        collection.library_ids.length > 0 ? collection.library_ids : [collection.library_id];
      void invalidateLibraryCollectionQueries(queryClient);
      for (const libraryId of libraryIds) {
        queryClient.invalidateQueries({
          queryKey: adminKeys.collections(libraryId),
        });
        queryClient.invalidateQueries({
          queryKey: libraryCollectionKeys.list(libraryId),
        });
      }
    },
    onError: (error) => {
      toast.error(error instanceof Error ? error.message : "Failed to save");
    },
  });
}

export function useUpdateAdminCollection() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      id,
      body,
      poster,
      backdrop,
    }: {
      id: string;
      body: UpdateLibraryCollectionRequest;
      poster?: File | null;
      backdrop?: File | null;
    }) => {
      const payload = buildCollectionFormData(
        body as unknown as Record<string, unknown>,
        poster,
        backdrop,
      );
      return api<LibraryCollection>(`/admin/collections/${id}`, {
        method: "PUT",
        body: payload,
      });
    },
    onSuccess: (collection) => {
      toast.success("Collection updated");
      const libraryIds =
        collection.library_ids.length > 0 ? collection.library_ids : [collection.library_id];
      void invalidateLibraryCollectionQueries(queryClient);
      for (const libraryId of libraryIds) {
        queryClient.invalidateQueries({
          queryKey: adminKeys.collections(libraryId),
        });
        queryClient.invalidateQueries({
          queryKey: libraryCollectionKeys.list(libraryId),
        });
      }
    },
    onError: (error) => {
      toast.error(error instanceof Error ? error.message : "Failed to save");
    },
  });
}

export function useDeleteAdminCollection() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ id, libraryId }: { id: string; libraryId: number }) =>
      api<void>(`/admin/collections/${id}`, {
        method: "DELETE",
      }).then(() => libraryId),
    onSuccess: (libraryId) => {
      toast.success("Collection deleted");
      void invalidateLibraryCollectionQueries(queryClient);
      queryClient.invalidateQueries({ queryKey: adminKeys.collections(libraryId) });
      queryClient.invalidateQueries({
        queryKey: libraryCollectionKeys.list(libraryId),
      });
    },
    onError: (error) => {
      toast.error(error instanceof Error ? error.message : "Failed to delete");
    },
  });
}

export function useDeleteCollectionImage() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      id,
      type,
      libraryId,
    }: {
      id: string;
      type: "poster" | "backdrop";
      libraryId: number;
    }) =>
      api<void>(`/admin/collections/${id}/image?type=${type}`, {
        method: "DELETE",
      }).then(() => libraryId),
    onSuccess: (libraryId) => {
      toast.success("Image removed");
      void invalidateLibraryCollectionQueries(queryClient);
      queryClient.invalidateQueries({ queryKey: adminKeys.collections(libraryId) });
      queryClient.invalidateQueries({
        queryKey: libraryCollectionKeys.list(libraryId),
      });
    },
    onError: (error) => {
      toast.error(error instanceof Error ? error.message : "Failed to remove image");
    },
  });
}

export function useSyncAdminCollection() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ id, libraryId }: { id: string; libraryId: number }) =>
      api<LibraryCollectionSyncRun>(`/admin/collections/${id}/sync`, {
        method: "POST",
      }).then((data) => ({ data, libraryId })),
    onSuccess: ({ data, libraryId }) => {
      toast.success(
        data.status === "warning" ? "Collection synced with warnings" : "Collection synced",
      );
      void invalidateLibraryCollectionQueries(queryClient);
      queryClient.invalidateQueries({ queryKey: adminKeys.collections(libraryId) });
      queryClient.invalidateQueries({
        queryKey: libraryCollectionKeys.list(libraryId),
      });
    },
    onError: (error) => {
      toast.error(error instanceof Error ? error.message : "Sync failed");
    },
  });
}

export function useImportMDBListCollection() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      body,
      poster,
      backdrop,
    }: {
      body: ImportMDBListCollectionRequest;
      poster?: File | null;
      backdrop?: File | null;
    }) => {
      const payload = buildCollectionFormData(
        body as unknown as Record<string, unknown>,
        poster,
        backdrop,
      );
      return api<ImportMDBListCollectionResponse>("/admin/collections/import/mdblist", {
        method: "POST",
        body: payload,
      });
    },
    onSuccess: (result) => {
      toast.success(
        result.sync_run?.status === "warning"
          ? "MDBList imported with warnings"
          : "MDBList imported",
      );
      void invalidateLibraryCollectionQueries(queryClient);
      queryClient.invalidateQueries({
        queryKey: adminKeys.collections(result.collection.library_id),
      });
      queryClient.invalidateQueries({
        queryKey: libraryCollectionKeys.list(result.collection.library_id),
      });
    },
    onError: (error) => {
      toast.error(error instanceof Error ? error.message : "Import failed");
    },
  });
}

export function useImportTMDBCollection() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      body,
      poster,
      backdrop,
    }: {
      body: ImportTMDBCollectionRequest;
      poster?: File | null;
      backdrop?: File | null;
    }) => {
      const payload = buildCollectionFormData(
        body as unknown as Record<string, unknown>,
        poster,
        backdrop,
      );
      return api<ImportTMDBCollectionResponse>("/admin/collections/import/tmdb", {
        method: "POST",
        body: payload,
      });
    },
    onSuccess: (result) => {
      toast.success(
        result.sync_run?.status === "warning"
          ? "TMDB collection imported with warnings"
          : "TMDB collection imported",
      );
      void invalidateLibraryCollectionQueries(queryClient);
      queryClient.invalidateQueries({
        queryKey: adminKeys.collections(result.collection.library_id),
      });
      queryClient.invalidateQueries({
        queryKey: libraryCollectionKeys.list(result.collection.library_id),
      });
    },
    onError: (error) => {
      toast.error(error instanceof Error ? error.message : "Import failed");
    },
  });
}
