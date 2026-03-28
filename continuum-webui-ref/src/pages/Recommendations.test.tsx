import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { renderToStaticMarkup } from "react-dom/server";
import { beforeEach, describe, expect, it, vi } from "vitest";

import Recommendations from "./Recommendations";

const mockUseTasteProfile = vi.fn();
const mockUseForYouMain = vi.fn();
const mockUseForYouRows = vi.fn();
const mockUseSimilarUsers = vi.fn();

vi.mock("@/hooks/queries/recommendations", () => ({
  useTasteProfile: (...args: unknown[]) => mockUseTasteProfile(...args),
  useForYouMain: (...args: unknown[]) => mockUseForYouMain(...args),
  useForYouRows: (...args: unknown[]) => mockUseForYouRows(...args),
  useSimilarUsers: (...args: unknown[]) => mockUseSimilarUsers(...args),
}));

vi.mock("@/hooks/useDocumentTitle", () => ({
  useDocumentTitle: () => undefined,
}));

vi.mock("@/components/RecommendationGrid", () => ({
  default: ({ items }: { items: Array<{ media_item_id: string }> }) => (
    <div data-kind="recommendation-grid">{items.map((item) => item.media_item_id).join(",")}</div>
  ),
}));

function renderPage() {
  const queryClient = new QueryClient();
  return renderToStaticMarkup(
    <QueryClientProvider client={queryClient}>
      <Recommendations />
    </QueryClientProvider>,
  );
}

describe("Recommendations", () => {
  beforeEach(() => {
    mockUseTasteProfile.mockReturnValue({
      data: {
        top_genres: ["Drama"],
        favorite_directors: ["Jane Doe"],
        signal_counts: { rated_high: 3 },
      },
      isLoading: false,
    });
    mockUseForYouMain.mockReturnValue({ data: undefined, isLoading: true });
    mockUseForYouRows.mockReturnValue({ data: undefined, isLoading: false });
    mockUseSimilarUsers.mockReturnValue({ data: undefined, isLoading: false });
  });

  it("defers secondary recommendations until the main row settles", () => {
    renderPage();

    expect(mockUseForYouRows).toHaveBeenCalledWith(false);
    expect(mockUseSimilarUsers).toHaveBeenCalledWith(false);
  });

  it("defers collaborative recommendations until secondary rows settle", () => {
    mockUseForYouMain.mockReturnValue({
      data: {
        row: {
          type: "cluster",
          label: "For You",
          items: [{ media_item_id: "main-item", score: 0.9, reason: "main" }],
        },
      },
      isLoading: false,
    });
    mockUseForYouRows.mockReturnValue({ data: undefined, isLoading: true });

    renderPage();

    expect(mockUseForYouRows).toHaveBeenCalledWith(true);
    expect(mockUseSimilarUsers).toHaveBeenCalledWith(false);
  });

  it("renders main and secondary rows once both recommendation queries resolve", () => {
    mockUseForYouMain.mockReturnValue({
      data: {
        row: {
          type: "cluster",
          label: "For You",
          items: [{ media_item_id: "main-item", score: 0.9, reason: "main" }],
        },
      },
      isLoading: false,
    });
    mockUseForYouRows.mockReturnValue({
      data: {
        rows: [
          {
            type: "cluster",
            label: "Because you enjoy Drama",
            items: [{ media_item_id: "row-item", score: 0.8, reason: "row" }],
          },
        ],
      },
      isLoading: false,
    });
    mockUseSimilarUsers.mockReturnValue({
      data: {
        items: [{ media_item_id: "similar-item", score: 0.7, reason: "similar" }],
      },
      isLoading: false,
    });

    const markup = renderPage();

    expect(mockUseSimilarUsers).toHaveBeenCalledWith(true);
    expect(markup).toContain("For You");
    expect(markup).toContain("Because you enjoy Drama");
    expect(markup).toContain("main-item");
    expect(markup).toContain("row-item");
    expect(markup).toContain("similar-item");
  });
});
