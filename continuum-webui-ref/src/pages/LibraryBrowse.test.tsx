import { renderToStaticMarkup } from "react-dom/server";
import { beforeEach, describe, expect, it, vi } from "vitest";

const mocks = vi.hoisted(() => ({
  useCatalogWindow: vi.fn(),
}));

vi.mock("@/hooks/queries/catalog", () => ({
  useCatalogWindow: (...args: unknown[]) => mocks.useCatalogWindow(...args),
}));

vi.mock("@/components/ItemGrid", () => ({
  default: () => <div>Grid</div>,
}));

vi.mock("@/components/ScrollToTopButton", () => ({
  default: () => <div>Scroll</div>,
}));

vi.mock("@/components/AdvancedFilterBar", () => ({
  default: () => <div>Filters</div>,
}));

import LibraryBrowse from "./LibraryBrowse";

describe("LibraryBrowse", () => {
  beforeEach(() => {
    mocks.useCatalogWindow.mockReset();
    mocks.useCatalogWindow.mockReturnValue({
      totalItems: 1,
      pages: new Map([[0, [{ content_id: "movie-1", title: "Heat", type: "movie" }]]]),
      isLoading: false,
    });
  });

  it("loads library browse data through the catalog query hook", () => {
    renderToStaticMarkup(
      <LibraryBrowse
        libraryId={7}
        libraryType="mixed"
        filters={{
          type: "movie",
          sort: "title",
          order: "asc",
          genre: "all",
          year_min: "",
          year_max: "",
          content_rating: "all",
        }}
        onFiltersChange={() => {}}
      />,
    );

    expect(mocks.useCatalogWindow).toHaveBeenCalled();
  });
});
