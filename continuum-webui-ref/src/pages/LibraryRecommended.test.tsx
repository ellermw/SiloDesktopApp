import type { ReactNode } from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { renderToStaticMarkup } from "react-dom/server";
import { beforeEach, describe, expect, it, vi } from "vitest";

import LibraryRecommended from "./LibraryRecommended";

const mockUseLibrarySections = vi.fn();
const mockUseSidebarPins = vi.fn();
const mockUseLibraryCollectionItems = vi.fn();

vi.mock("@/hooks/queries/sections", () => ({
  useLibrarySections: (...args: unknown[]) => mockUseLibrarySections(...args),
}));

vi.mock("@/hooks/queries/sidebarPins", () => ({
  useSidebarPins: (...args: unknown[]) => mockUseSidebarPins(...args),
}));

vi.mock("@/hooks/queries/libraryCollections", () => ({
  useLibraryCollectionItems: (...args: unknown[]) => mockUseLibraryCollectionItems(...args),
}));

vi.mock("@/components/MediaCarousel", () => ({
  default: ({ title, children }: { title: string; children: ReactNode }) => (
    <section data-kind="carousel">
      <h2>{title}</h2>
      {children}
    </section>
  ),
}));

vi.mock("@/components/HeroBanner", () => ({
  default: ({ items }: { items: Array<{ title: string }> }) => (
    <div data-kind="hero">{items.map((item) => item.title).join(",")}</div>
  ),
}));

vi.mock("@/components/SectionRow", () => ({
  default: ({ section }: { section: { title: string; section_type: string } }) => (
    <div data-kind="section-row" data-section-type={section.section_type}>
      {section.title}
    </div>
  ),
}));

vi.mock("@/components/ItemCard", () => ({
  default: ({ item }: { item: { title: string } }) => <div>{item.title}</div>,
}));

function makeSection(
  overrides: Partial<{
    id: string;
    section_type: string;
    title: string;
    featured: boolean;
  }> = {},
) {
  return {
    id: overrides.id ?? "section-1",
    section_type: overrides.section_type ?? "recently_added",
    title: overrides.title ?? "Recently Added",
    featured: overrides.featured ?? false,
    item_limit: 20,
    total_count: 1,
    is_custom: false,
    customized: false,
    items: [
      {
        content_id: "item-1",
        type: "movie",
        title: "Item One",
        year: 2024,
        genres: [],
        status: "matched",
        rating_imdb: null,
        overview: "",
        poster_url: "",
        poster_thumbhash: "",
        backdrop_url: "",
        backdrop_thumbhash: "",
        logo_url: "",
      },
    ],
  };
}

function renderWithQueryClient(node: ReactNode) {
  const queryClient = new QueryClient();
  return renderToStaticMarkup(
    <QueryClientProvider client={queryClient}>{node}</QueryClientProvider>,
  );
}

describe("LibraryRecommended", () => {
  beforeEach(() => {
    mockUseLibrarySections.mockReturnValue({
      data: {
        sections: [
          makeSection({
            id: "cw",
            section_type: "continue_watching",
            title: "Continue Watching",
          }),
          makeSection({
            id: "recent",
            title: "Recently Added",
          }),
        ],
      },
      isLoading: false,
    });
    mockUseSidebarPins.mockReturnValue({ pins: {} });
    mockUseLibraryCollectionItems.mockReturnValue({ data: [], isLoading: false });
  });

  it("renders sections from the sections API", () => {
    const markup = renderWithQueryClient(<LibraryRecommended libraryId={42} />);

    expect(markup).toContain("Continue Watching");
    expect(markup).toContain("Recently Added");
  });

  it("renders hero banner for featured sections", () => {
    mockUseLibrarySections.mockReturnValue({
      data: {
        sections: [
          makeSection({ id: "hero", title: "Featured", featured: true }),
          makeSection({ id: "recent", title: "Recently Added" }),
        ],
      },
      isLoading: false,
    });

    const markup = renderWithQueryClient(<LibraryRecommended libraryId={42} />);

    expect(markup).toContain('data-kind="hero"');
    expect(markup).toContain("Recently Added");
  });

  it("renders nothing while loading with no data", () => {
    mockUseLibrarySections.mockReturnValue({
      data: undefined,
      isLoading: true,
    });

    const markup = renderWithQueryClient(<LibraryRecommended libraryId={42} />);
    expect(markup).toBe("");
  });

  it("renders pinned collection rows from sidebar_pins for the current library", () => {
    mockUseSidebarPins.mockReturnValue({
      pins: {
        "42": [
          { type: "collection", id: "col-1", label: "Pinned Horror" },
          { type: "section", id: "sec-1", label: "Recently Added" },
        ],
        "99": [{ type: "collection", id: "col-2", label: "Other Library Collection" }],
      },
    });
    mockUseLibraryCollectionItems.mockImplementation((libraryId: number, collectionId: string) => {
      if (libraryId === 42 && collectionId === "col-1") {
        return {
          data: [
            {
              content_id: "item-1",
              title: "Scream",
            },
          ],
          isLoading: false,
        };
      }

      return { data: [], isLoading: false };
    });

    const markup = renderWithQueryClient(<LibraryRecommended libraryId={42} />);

    expect(markup).toContain("Pinned Horror");
    expect(markup).toContain("Scream");
    expect(markup).not.toContain("Other Library Collection");
    expect(mockUseLibraryCollectionItems).toHaveBeenCalledWith(42, "col-1");
  });
});
