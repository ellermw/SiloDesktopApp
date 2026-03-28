import type { QueryClient } from "@tanstack/react-query";
import { sectionKeys } from "@/hooks/queries/keys";

export async function invalidateHomeSurfaceQueries(queryClient: QueryClient) {
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: sectionKeys.homeLayout() }),
    queryClient.invalidateQueries({ queryKey: sectionKeys.homeItemsRoot() }),
  ]);
}
