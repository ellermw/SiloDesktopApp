import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/client";
import type {
  SubtitleProviderConfig,
  SubtitleProviderUpdateRequest,
  SubtitleProviderTestResponse,
} from "@/api/types";
import { adminKeys } from "../keys";
import { toast } from "sonner";

const ADMIN_STALE_TIME = 30_000;

export function useSubtitleProviders() {
  return useQuery({
    queryKey: adminKeys.subtitleProviders(),
    queryFn: () => api<{ providers: SubtitleProviderConfig[] }>("/admin/subtitle-providers"),
    staleTime: ADMIN_STALE_TIME,
  });
}

export function useUpdateSubtitleProvider() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      provider,
      config,
    }: {
      provider: string;
      config: SubtitleProviderUpdateRequest;
    }) =>
      api<{ status: string }>(`/admin/subtitle-providers/${provider}`, {
        method: "PUT",
        body: JSON.stringify(config),
      }),
    onSuccess: () => {
      toast.success("Provider settings saved");
      queryClient.invalidateQueries({
        queryKey: adminKeys.subtitleProviders(),
      });
    },
    onError: (err) => {
      toast.error(err instanceof Error ? err.message : "Failed to save provider settings");
    },
  });
}

export function useTestSubtitleProvider() {
  return useMutation({
    mutationFn: (provider: string) =>
      api<SubtitleProviderTestResponse>(`/admin/subtitle-providers/${provider}/test`, {
        method: "POST",
      }),
  });
}
