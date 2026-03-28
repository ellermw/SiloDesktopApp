import { MemoryRouter } from "react-router";
import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it, vi } from "vitest";

import HeroBanner from "./HeroBanner";

vi.mock("@/hooks/useAmbientColor", () => ({
  useAmbientColor: () => undefined,
}));

vi.mock("@/lib/thumbhash", () => ({
  decodeThumbhash: () => "",
}));

describe("HeroBanner", () => {
  it("does not render the desktop spotlighting card", () => {
    const markup = renderToStaticMarkup(
      <MemoryRouter>
        <HeroBanner
          items={[
            {
              content_id: "movie-1",
              type: "movie",
              title: "Featured Movie",
              year: 2025,
              genres: ["Drama"],
              status: "matched",
              rating_imdb: 8.1,
              overview: "Overview",
              poster_url: "",
              poster_thumbhash: "",
              backdrop_url: "",
              backdrop_thumbhash: "",
              logo_url: "",
            },
          ]}
        />
      </MemoryRouter>,
    );

    expect(markup).not.toContain("Now spotlighting");
    expect(markup).toContain("Featured Movie");
    expect(markup).toContain("Open details");
  });
});
