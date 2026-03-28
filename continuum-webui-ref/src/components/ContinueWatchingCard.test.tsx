import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { renderToStaticMarkup } from "react-dom/server";
import { MemoryRouter } from "react-router";
import { describe, expect, it } from "vitest";
import ContinueWatchingCard from "./ContinueWatchingCard";

describe("ContinueWatchingCard", () => {
  it("renders separate watch and item links alongside episodic metadata", () => {
    const queryClient = new QueryClient();
    const markup = renderToStaticMarkup(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <ContinueWatchingCard
            detail={{
              content_id: "ep-001",
              type: "episode",
              title: "Pilot",
              overview: "Episode overview",
              versions: [],
              subtitles: [],
              intro: null,
              credits: null,
              genres: [],
              cast: [],
              crew: [],
              studios: [],
              networks: [],
              countries: [],
              poster_url: "",
              poster_thumbhash: "",
              backdrop_url: "/episode-backdrop.jpg",
              backdrop_thumbhash: "",
              logo_url: "",
              release_date: null,
              runtime: 42,
              year: 0,
              content_rating: "",
              status: "matched",
              rating_imdb: null,
              rating_tmdb: null,
              rating_rt_critic: null,
              rating_rt_audience: null,
              imdb_id: "",
              tmdb_id: "",
              tvdb_id: "",
              first_air_date: null,
              last_air_date: null,
              season_count: null,
              series_id: "series-1",
              series_title: "Breaking Bad",
              season_number: 1,
              episode_number: 1,
            }}
            progress={{
              media_item_id: "ep-001",
              position_seconds: 120,
              duration_seconds: 3600,
              completed: false,
              updated_at: "2026-03-07T00:00:00Z",
            }}
          />
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(markup).toContain('href="/watch/ep-001"');
    expect(markup).toContain('href="/item/ep-001"');
    expect(markup).toContain("Breaking Bad");
    expect(markup).toContain("Season 1 Episode 1");
    expect(markup).toContain("Pilot");
    expect(markup).toContain("58 min left");
    expect(markup).toContain("More actions");
  });
});
