import type { QueryClient } from "@tanstack/react-query";
import { ratingKeys, recKeys, sectionKeys } from "./keys";

export async function invalidateRatingSurfaceQueries(queryClient: QueryClient, itemId: string) {
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: ratingKeys.item(itemId) }),
    queryClient.invalidateQueries({ queryKey: recKeys.all }),
    queryClient.invalidateQueries({ queryKey: sectionKeys.all }),
  ]);
}
