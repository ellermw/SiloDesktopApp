import { beforeEach, describe, expect, it, vi } from "vitest";

const mocks = vi.hoisted(() => ({
  api: vi.fn(),
}));

vi.mock("@/api/client", () => ({
  api: mocks.api,
}));

import {
  fetchCatalogItemDetail,
  fetchCatalogItemEpisodes,
  fetchCatalogSeasonDetail,
  fetchCatalogSeasonEpisodes,
  fetchCatalogSeriesSeasons,
} from "./catalogRead";

describe("catalog read helpers", () => {
  beforeEach(() => {
    mocks.api.mockReset();
    mocks.api.mockResolvedValue({});
  });

  it("fetches item detail from the canonical catalog item endpoint", async () => {
    await fetchCatalogItemDetail("movie-123");

    expect(mocks.api).toHaveBeenCalledWith("/catalog/items/movie-123", undefined);
  });

  it("fetches item episodes from the canonical catalog item episodes endpoint", async () => {
    await fetchCatalogItemEpisodes("season-4");

    expect(mocks.api).toHaveBeenCalledWith("/catalog/items/season-4/episodes", undefined);
  });

  it("fetches series seasons from the canonical catalog series endpoint", async () => {
    await fetchCatalogSeriesSeasons("series-9");

    expect(mocks.api).toHaveBeenCalledWith("/catalog/series/series-9/seasons", undefined);
  });

  it("fetches season detail from the canonical catalog season endpoint", async () => {
    await fetchCatalogSeasonDetail("series-9", 2);

    expect(mocks.api).toHaveBeenCalledWith("/catalog/series/series-9/seasons/2", undefined);
  });

  it("fetches season episodes from the canonical catalog season episodes endpoint", async () => {
    await fetchCatalogSeasonEpisodes("series-9", 2);

    expect(mocks.api).toHaveBeenCalledWith(
      "/catalog/series/series-9/seasons/2/episodes",
      undefined,
    );
  });
});
