import { renderToStaticMarkup } from "react-dom/server";
import { beforeEach, describe, expect, it, vi } from "vitest";

const mocks = vi.hoisted(() => ({
  useQueries: vi.fn(),
  useQuery: vi.fn(),
}));

vi.mock("@tanstack/react-query", () => ({
  keepPreviousData: Symbol("keepPreviousData"),
  useQueries: (...args: unknown[]) => mocks.useQueries(...args),
  useQuery: (...args: unknown[]) => mocks.useQuery(...args),
}));

import type { CatalogResponse } from "@/api/types";
import { createCatalogSearchState, useCatalogWindow } from "./catalog";

function makePage(offset: number, limit = 60): CatalogResponse {
  return {
    total: 1000,
    has_more: true,
    title: "Catalog",
    items: Array.from({ length: limit }, (_, index) => ({
      content_id: `item-${offset + index}`,
      type: "movie" as const,
      title: `Item ${offset + index}`,
      year: 2024,
      genres: [],
      content_rating: "PG",
      status: "matched" as const,
      rating_imdb: null,
      overview: "",
      poster_url: "",
      poster_thumbhash: "",
      backdrop_url: "",
      backdrop_thumbhash: "",
    })),
  };
}

describe("useCatalogWindow", () => {
  beforeEach(() => {
    mocks.useQueries.mockReset();
    mocks.useQuery.mockReset();
  });

  it("does not assign stale placeholder data to newly visible page indices", () => {
    const state = createCatalogSearchState("favorites");
    const limit = 60;

    function Harness({ visibleRange }: { visibleRange: [number, number] }) {
      const result = useCatalogWindow(state, { limit, visibleRange });
      return (
        <div
          data-page6={result.data.pages.get(6)?.[0]?.content_id ?? "missing"}
          data-page7={result.data.pages.get(7)?.[0]?.content_id ?? "missing"}
        />
      );
    }

    mocks.useQueries.mockImplementation(
      ({ queries }: { queries: Array<{ queryKey: [string, string, { offset?: number }]; placeholderData?: unknown }> }) => {
        const offsets = queries.map((query) => query.queryKey[2].offset ?? 0);
        const hasPlaceholderData = queries.some((query) => "placeholderData" in query);

        if (offsets.join(",") === "0,60") {
          return [
            { data: makePage(0, limit), isLoading: false },
            { data: makePage(60, limit), isLoading: false },
          ];
        }

        if (offsets.join(",") === "0,360,420,480") {
          return [
            { data: makePage(0, limit), isLoading: false },
            hasPlaceholderData
              ? { data: makePage(60, limit), isLoading: true, isPlaceholderData: true }
              : { data: undefined, isLoading: true },
            hasPlaceholderData
              ? { data: makePage(120, limit), isLoading: true, isPlaceholderData: true }
              : { data: undefined, isLoading: true },
            { data: undefined, isLoading: true },
          ];
        }

        throw new Error(`Unexpected query offsets: ${offsets.join(",")}`);
      },
    );

    renderToStaticMarkup(<Harness visibleRange={[0, limit - 1]} />);
    const markup = renderToStaticMarkup(<Harness visibleRange={[420, 479]} />);

    expect(markup).toContain('data-page6="missing"');
    expect(markup).toContain('data-page7="missing"');
  });
});
