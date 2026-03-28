import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/client";
import { adminKeys } from "../keys";
import { toast } from "sonner";

type ServerSettings = Record<string, string>;

interface SensitiveStatusResponse {
  configured: string[];
}

export function useAdminServerSettings() {
  return useQuery({
    queryKey: adminKeys.serverSettings(),
    queryFn: () => api<ServerSettings>("/admin/settings").then((d) => d ?? {}),
    staleTime: 30_000,
  });
}

export function useUpdateServerSetting() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ key, value }: { key: string; value: string }) =>
      api(`/admin/settings/${key}`, {
        method: "PUT",
        body: JSON.stringify({ value }),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: adminKeys.serverSettings() });
    },
    onError: (err) => {
      toast.error(err instanceof Error ? err.message : "Failed to update setting");
    },
  });
}

export function useAdminSensitiveStatus() {
  return useQuery({
    queryKey: [...adminKeys.serverSettings(), "sensitive-status"] as const,
    queryFn: () => api<SensitiveStatusResponse>("/admin/settings/sensitive-status"),
    staleTime: 30_000,
  });
}
