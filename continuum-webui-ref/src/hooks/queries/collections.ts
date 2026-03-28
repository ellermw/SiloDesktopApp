import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/client";
import type {
  BrowseItem,
  Collection,
  CreateCollectionRequest,
  UpdateCollectionRequest,
} from "@/api/types";
import { collectionKeys } from "./keys";
import { toast } from "sonner";
import { invalidateUserCollectionQueries } from "./collectionSurfaceRefresh";

export function useCollections() {
  return useQuery({
    queryKey: collectionKeys.list(),
    queryFn: () =>
      api<{ collections: Collection[] }>("/collections").then((d) => d.collections ?? []),
  });
}

export function useCollectionItems(collectionId: string) {
  return useQuery({
    queryKey: collectionKeys.items(collectionId),
    queryFn: () =>
      api<{ items: BrowseItem[] }>(`/collections/${collectionId}/items`).then((d) => d.items ?? []),
  });
}

export function useCreateCollection() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateCollectionRequest) =>
      api("/collections", {
        method: "POST",
        body: JSON.stringify(body),
      }),
    onSuccess: () => {
      toast.success("Collection created");
      return invalidateUserCollectionQueries(queryClient);
    },
    onError: (err) => {
      toast.error(err instanceof Error ? err.message : "Failed to save");
    },
  });
}

export function useUpdateCollection() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateCollectionRequest }) =>
      api(`/collections/${id}`, {
        method: "PUT",
        body: JSON.stringify(body),
      }),
    onSuccess: (_data, { id }) => {
      toast.success("Collection updated");
      return invalidateUserCollectionQueries(queryClient, id);
    },
    onError: (err) => {
      toast.error(err instanceof Error ? err.message : "Failed to save");
    },
  });
}

export function useDeleteCollection() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api(`/collections/${id}`, { method: "DELETE" }),
    onSuccess: (_data, id) => {
      toast.success("Collection deleted");
      return invalidateUserCollectionQueries(queryClient, id);
    },
    onError: (err) => {
      toast.error(err instanceof Error ? err.message : "Failed to delete");
    },
  });
}
