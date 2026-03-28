import { QueryClient } from "@tanstack/react-query";
import { describe, expect, it } from "vitest";
import { sectionKeys } from "@/hooks/queries/keys";
import { invalidateHomeSurfaceQueries } from "./homeSurfaceRefresh";

describe("invalidateHomeSurfaceQueries", () => {
  it("marks the home layout and section item queries stale", async () => {
    const queryClient = new QueryClient();

    queryClient.setQueryData(sectionKeys.homeLayout(), { sections: [] });
    queryClient.setQueryData(sectionKeys.homeItems("hero"), { section: { id: "hero" } });

    await invalidateHomeSurfaceQueries(queryClient);

    expect(queryClient.getQueryState(sectionKeys.homeLayout())?.isInvalidated).toBe(true);
    expect(queryClient.getQueryState(sectionKeys.homeItems("hero"))?.isInvalidated).toBe(true);
  });
});
