import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/client";
import type {
  CreateHistoryImportSourceRequest,
  HistoryImportSource,
  UpdateHistoryImportSourceRequest,
} from "@/api/types";
import { adminKeys, historyImportKeys } from "../keys";
import { toast } from "sonner";

export function useAdminHistoryImportSources() {
  return useQuery({
    queryKey: adminKeys.historyImportSources(),
    queryFn: () => api<HistoryImportSource[]>("/admin/history-import-sources").then((d) => d ?? []),
  });
}

export function useCreateAdminHistoryImportSource() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateHistoryImportSourceRequest) =>
      api<HistoryImportSource>("/admin/history-import-sources", {
        method: "POST",
        body: JSON.stringify(body),
      }),
    onSuccess: () => {
      toast.success("Saved server created");
      queryClient.invalidateQueries({ queryKey: adminKeys.historyImportSources() });
      queryClient.invalidateQueries({ queryKey: historyImportKeys.sources() });
    },
    onError: (err) => {
      toast.error(err instanceof Error ? err.message : "Failed to create saved server");
    },
  });
}

export function useUpdateAdminHistoryImportSource() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: number; body: UpdateHistoryImportSourceRequest }) =>
      api<HistoryImportSource>(`/admin/history-import-sources/${id}`, {
        method: "PUT",
        body: JSON.stringify(body),
      }),
    onSuccess: () => {
      toast.success("Saved server updated");
      queryClient.invalidateQueries({ queryKey: adminKeys.historyImportSources() });
      queryClient.invalidateQueries({ queryKey: historyImportKeys.sources() });
    },
    onError: (err) => {
      toast.error(err instanceof Error ? err.message : "Failed to update saved server");
    },
  });
}

export function useDeleteAdminHistoryImportSource() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => api(`/admin/history-import-sources/${id}`, { method: "DELETE" }),
    onSuccess: () => {
      toast.success("Saved server deleted");
      queryClient.invalidateQueries({ queryKey: adminKeys.historyImportSources() });
      queryClient.invalidateQueries({ queryKey: historyImportKeys.sources() });
    },
    onError: (err) => {
      toast.error(err instanceof Error ? err.message : "Failed to delete saved server");
    },
  });
}
