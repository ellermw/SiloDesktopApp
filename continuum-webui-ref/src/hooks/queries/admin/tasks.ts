import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { api, ApiClientError } from "@/api/client";
import type { ExecutionResult, TaskInfo, TriggerConfig } from "@/api/types";
import { adminKeys } from "@/hooks/queries/keys";

export function useTasks() {
  return useQuery({
    queryKey: adminKeys.tasks(),
    queryFn: () => api<TaskInfo[]>("/admin/tasks"),
    staleTime: 0,
    refetchInterval: (query) => {
      const tasks = query.state.data as TaskInfo[] | undefined;
      return tasks?.some((t) => t.state === "running" || t.state === "cancelling") ? 5_000 : 30_000;
    },
  });
}

export function useTask(key: string) {
  return useQuery({
    queryKey: adminKeys.task(key),
    queryFn: () => api<TaskInfo>(`/admin/tasks/${encodeURIComponent(key)}`),
    staleTime: 0,
    refetchInterval: (query) => {
      const task = query.state.data as TaskInfo | undefined;
      return task?.state === "running" || task?.state === "cancelling" ? 5_000 : 30_000;
    },
  });
}

export function useTaskHistory(key: string) {
  return useQuery({
    queryKey: adminKeys.taskHistory(key),
    queryFn: () =>
      api<ExecutionResult[]>(`/admin/tasks/${encodeURIComponent(key)}/history?limit=20`),
    staleTime: 0,
  });
}

export function useRunTask() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (key: string) =>
      api<{ status: string }>(`/admin/tasks/${encodeURIComponent(key)}/run`, {
        method: "POST",
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: adminKeys.tasks() });
      toast.success("Task started");
    },
    onError: (error: Error) => {
      if (error instanceof ApiClientError && error.status === 409) {
        toast.error("Task is already running");
      } else {
        toast.error("Failed to start task");
      }
    },
  });
}

export function useCancelTask() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (key: string) =>
      api<{ status: string }>(`/admin/tasks/${encodeURIComponent(key)}/cancel`, {
        method: "POST",
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: adminKeys.tasks() });
      toast.success("Cancellation requested");
    },
    onError: () => {
      toast.error("Failed to cancel task");
    },
  });
}

export function useUpdateTriggers() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ key, triggers }: { key: string; triggers: TriggerConfig[] }) =>
      api<TaskInfo>(`/admin/tasks/${encodeURIComponent(key)}/triggers`, {
        method: "PUT",
        body: JSON.stringify(triggers),
      }),
    onSuccess: (_data, { key }) => {
      queryClient.invalidateQueries({ queryKey: adminKeys.task(key) });
      queryClient.invalidateQueries({ queryKey: adminKeys.tasks() });
      toast.success("Schedule updated");
    },
    onError: () => {
      toast.error("Failed to update schedule");
    },
  });
}
