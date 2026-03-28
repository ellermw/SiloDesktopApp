import { useQuery } from "@tanstack/react-query";
import { api } from "@/api/client";
import type { AdminStats, AdminSession } from "@/api/types";
import { adminKeys } from "../keys";

const ADMIN_STALE_TIME = 30_000;

export function useAdminStats() {
  return useQuery({
    queryKey: adminKeys.stats(),
    queryFn: () => api<AdminStats>("/admin/stats"),
    staleTime: ADMIN_STALE_TIME,
    refetchInterval: ADMIN_STALE_TIME,
    refetchIntervalInBackground: false,
  });
}

export function useAdminSessions() {
  return useQuery({
    queryKey: adminKeys.sessions(),
    queryFn: () => api<AdminSession[]>("/admin/sessions").then((d) => d ?? []),
    staleTime: ADMIN_STALE_TIME,
  });
}
