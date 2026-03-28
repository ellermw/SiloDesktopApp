import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/client";
import { settingsKeys } from "./keys";

interface SettingEntry {
  key: string;
  value: string;
}

export function useSetting(key: string, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: settingsKeys.detail(key),
    queryFn: async () => {
      try {
        const result = await api<SettingEntry>(`/settings/${key}`);
        return result.value;
      } catch {
        return null;
      }
    },
    enabled: options?.enabled ?? true,
    staleTime: 5 * 60 * 1000,
  });
}

export function useSetSetting() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: ({ key, value }: { key: string; value: string }) =>
      api(`/settings/${key}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ value }),
      }),
    onSuccess: (_, { key }) => {
      qc.invalidateQueries({ queryKey: settingsKeys.detail(key) });
    },
  });
}
