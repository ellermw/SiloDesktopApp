import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/client";
import type { MetadataProvider, CreateProviderRequest } from "@/api/types";
import { adminKeys } from "../keys";
import { toast } from "sonner";

const ADMIN_STALE_TIME = 30_000;

export function useAdminProviders() {
  return useQuery({
    queryKey: adminKeys.providers(),
    queryFn: () => api<MetadataProvider[]>("/admin/providers").then((d) => d ?? []),
    staleTime: ADMIN_STALE_TIME,
  });
}

export function useCreateProvider() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateProviderRequest) =>
      api("/admin/providers", {
        method: "POST",
        body: JSON.stringify(body),
      }),
    onSuccess: () => {
      toast.success("Provider created");
      queryClient.invalidateQueries({ queryKey: adminKeys.providers() });
    },
    onError: (err) => {
      toast.error(err instanceof Error ? err.message : "Failed to save");
    },
  });
}

export function useUpdateProvider() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: number; body: Record<string, unknown> }) =>
      api(`/admin/providers/${id}`, {
        method: "PUT",
        body: JSON.stringify(body),
      }),
    onSuccess: () => {
      toast.success("Provider updated");
      queryClient.invalidateQueries({ queryKey: adminKeys.providers() });
    },
    onError: (err) => {
      toast.error(err instanceof Error ? err.message : "Failed to save");
    },
  });
}

export function useDeleteProvider() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => api(`/admin/providers/${id}`, { method: "DELETE" }),
    onSuccess: () => {
      toast.success("Provider deleted");
      queryClient.invalidateQueries({ queryKey: adminKeys.providers() });
    },
    onError: (err) => {
      toast.error(err instanceof Error ? err.message : "Failed to delete provider");
    },
  });
}
