import { useQuery } from "@tanstack/react-query";
import { api } from "@/api/client";
import type { BrowseItem } from "@/api/types";
import { historyKeys } from "./keys";

export function useHistory() {
  return useQuery({
    queryKey: historyKeys.list(),
    queryFn: () => api<{ items: BrowseItem[] }>("/history").then((d) => d.items ?? []),
  });
}
