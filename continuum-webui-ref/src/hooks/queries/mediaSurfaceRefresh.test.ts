import { QueryClient } from "@tanstack/react-query";
import { describe, expect, it } from "vitest";
import {
  catalogKeys,
  favoriteKeys,
  historyKeys,
  itemKeys,
  personKeys,
  progressKeys,
  recKeys,
  sectionKeys,
  watchlistKeys,
} from "./keys";
import { invalidateMediaSurfaceQueries } from "./mediaSurfaceRefresh";

describe("invalidateMediaSurfaceQueries", () => {
  it("marks item, section, progress, history, favorites, and watchlist queries stale", async () => {
    const queryClient = new QueryClient();
    const browseKey = itemKeys.browse({
      q: "",
      type: "all",
      sort: "created_at",
      order: "desc",
      offset: 0,
      limit: 20,
    });

    queryClient.setQueryData(browseKey, { items: [] });
    queryClient.setQueryData(sectionKeys.homeItems("hero"), { section: { id: "hero" } });
    queryClient.setQueryData(sectionKeys.library(1), { sections: [] });
    queryClient.setQueryData(
      catalogKeys.list({
        source: "query",
        q: "star",
        limit: 20,
        offset: 0,
      }),
      { items: [] },
    );
    queryClient.setQueryData(recKeys.forYouMain(), { row: null });
    queryClient.setQueryData(recKeys.forYouRows(), { rows: [] });
    queryClient.setQueryData(personKeys.catalog("person-1", { limit: 24, offset: 0 }), {
      total: 0,
      has_more: false,
      items: [],
    });
    queryClient.setQueryData(progressKeys.list(), { progress: [] });
    queryClient.setQueryData(historyKeys.list(), { items: [] });
    queryClient.setQueryData(favoriteKeys.list(), { items: [] });
    queryClient.setQueryData(watchlistKeys.list(), { items: [] });

    await invalidateMediaSurfaceQueries(queryClient);

    expect(queryClient.getQueryState(browseKey)?.isInvalidated).toBe(true);
    expect(queryClient.getQueryState(sectionKeys.homeItems("hero"))?.isInvalidated).toBe(true);
    expect(queryClient.getQueryState(sectionKeys.library(1))?.isInvalidated).toBe(true);
    expect(
      queryClient.getQueryState(
        catalogKeys.list({
          source: "query",
          q: "star",
          limit: 20,
          offset: 0,
        }),
      )?.isInvalidated,
    ).toBe(true);
    expect(queryClient.getQueryState(recKeys.forYouMain())?.isInvalidated).toBe(true);
    expect(queryClient.getQueryState(recKeys.forYouRows())?.isInvalidated).toBe(true);
    expect(
      queryClient.getQueryState(personKeys.catalog("person-1", { limit: 24, offset: 0 }))
        ?.isInvalidated,
    ).toBe(true);
    expect(queryClient.getQueryState(progressKeys.list())?.isInvalidated).toBe(true);
    expect(queryClient.getQueryState(historyKeys.list())?.isInvalidated).toBe(true);
    expect(queryClient.getQueryState(favoriteKeys.list())?.isInvalidated).toBe(true);
    expect(queryClient.getQueryState(watchlistKeys.list())?.isInvalidated).toBe(true);
  });

  it("marks the active catalog detail query stale for the mutated item", async () => {
    const queryClient = new QueryClient();

    queryClient.setQueryData(catalogKeys.itemDetail("item-1"), { content_id: "item-1" });

    await invalidateMediaSurfaceQueries(queryClient, { itemId: "item-1" });

    expect(queryClient.getQueryState(catalogKeys.itemDetail("item-1"))?.isInvalidated).toBe(true);
  });
});
