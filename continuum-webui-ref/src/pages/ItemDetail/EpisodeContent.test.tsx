import type { ReactNode } from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { MemoryRouter } from "react-router";
import { describe, expect, it, vi, beforeEach } from "vitest";
import type { ItemDetail, Season } from "@/api/types";
import EpisodeContent from "./EpisodeContent";

const mocks = vi.hoisted(() => {
  let capturedActionBarProps: Record<string, unknown> | null = null;

  return {
    capturedActionBarProps: {
      get value() {
        return capturedActionBarProps;
      },
      set value(value: Record<string, unknown> | null) {
        capturedActionBarProps = value;
      },
    },
    useSeasonDetail: vi.fn(),
    useSeasonEpisodes: vi.fn(),
    useAuth: vi.fn(),
    useCurrentProfile: vi.fn(),
    useRefreshItemMetadata: vi.fn(),
    useWatchedStateMutation: vi.fn(),
    useRating: vi.fn(),
    useSetRating: vi.fn(),
    useDeleteRating: vi.fn(),
    setRatingMutate: vi.fn(),
    deleteRatingMutate: vi.fn(),
  };
});

vi.mock("@/hooks/queries/episodes", () => ({
  useSeasonDetail: mocks.useSeasonDetail,
  useSeasonEpisodes: mocks.useSeasonEpisodes,
}));

vi.mock("@/hooks/useAuth", () => ({
  useAuth: mocks.useAuth,
}));

vi.mock("@/hooks/useCurrentProfile", () => ({
  useCurrentProfile: mocks.useCurrentProfile,
}));

vi.mock("@/hooks/queries/items", () => ({
  useRefreshItemMetadata: mocks.useRefreshItemMetadata,
  useWatchedStateMutation: mocks.useWatchedStateMutation,
}));

vi.mock("@/hooks/queries/ratings", () => ({
  useRating: mocks.useRating,
  useSetRating: mocks.useSetRating,
  useDeleteRating: mocks.useDeleteRating,
}));

vi.mock("@/components/CastCarousel", () => ({
  default: () => <div />,
}));

vi.mock("./DetailHero", () => ({
  default: ({ context, actions }: { context?: ReactNode; actions?: ReactNode }) => (
    <div>
      {context}
      {actions}
    </div>
  ),
}));

vi.mock("./components/MetadataBadges", () => ({
  default: () => <div />,
}));

vi.mock("./components/QualityBadges", () => ({
  default: () => <div />,
}));

vi.mock("./components/ScoreRow", () => ({
  default: () => <div />,
}));

vi.mock("./components/HeroCrewLine", () => ({
  default: () => <div />,
}));

vi.mock("./components/ActionBar", () => ({
  default: (props: Record<string, unknown>) => {
    mocks.capturedActionBarProps.value = props;
    return <div />;
  },
}));

vi.mock("./components/EpisodeCarousel", () => ({
  default: ({
    episodes,
    currentEpisodeNumber,
  }: {
    episodes: { content_id: string; episode_number: number; title: string }[];
    currentEpisodeNumber: number;
  }) => (
    <div data-testid="episode-carousel">
      {episodes.map((ep) => (
        <div
          key={ep.content_id}
          data-episode={ep.episode_number}
          data-current={ep.episode_number === currentEpisodeNumber ? "true" : undefined}
        >
          {ep.title}
        </div>
      ))}
    </div>
  ),
}));

function makeEpisodeItem(
  overrides: Partial<ItemDetail & { type: "episode" }> = {},
): ItemDetail & { type: "episode" } {
  return {
    content_id: "episode-1",
    title: "Pilot",
    year: 2024,
    overview: "Episode overview",
    runtime: 42,
    content_rating: "TV-14",
    genres: [],
    rating_imdb: null,
    rating_tmdb: null,
    rating_rt_critic: null,
    rating_rt_audience: null,
    imdb_id: "",
    tmdb_id: "",
    tvdb_id: "",
    cast: [],
    crew: [],
    studios: [],
    networks: [],
    countries: [],
    first_air_date: null,
    last_air_date: null,
    poster_url: "",
    poster_thumbhash: "",
    backdrop_url: "",
    backdrop_thumbhash: "",
    logo_url: "",
    season_count: null,
    series_id: "series-1",
    series_title: "Example Series",
    season_number: 1,
    episode_number: 1,
    air_date: "2024-01-01",
    versions: [],
    subtitles: [],
    intro: null,
    credits: null,
    ...overrides,
    release_date: overrides.release_date ?? null,
    type: "episode",
  };
}

function makeSeason(overrides: Partial<Season> = {}): Season {
  return {
    content_id: "season-1",
    season_number: 1,
    is_specials: false,
    title: "Season 1",
    overview: "",
    air_date: null,
    episode_count: 8,
    poster_url: "",
    poster_thumbhash: "",
    ...overrides,
  };
}

function countOccurrences(markup: string, fragment: string): number {
  return markup.split(fragment).length - 1;
}

describe("EpisodeContent", () => {
  beforeEach(() => {
    mocks.capturedActionBarProps.value = null;
    mocks.useAuth.mockReturnValue({ user: null });
    mocks.useCurrentProfile.mockReturnValue({ profile: null });
    mocks.useRefreshItemMetadata.mockReturnValue({
      mutate: vi.fn(),
      isPending: false,
    });
    mocks.useWatchedStateMutation.mockReturnValue({
      mutate: vi.fn(),
      isPending: false,
    });
    mocks.useSeasonEpisodes.mockReturnValue({
      data: { episodes: [] },
    });
    mocks.useSeasonDetail.mockReturnValue({
      data: makeSeason(),
    });
    mocks.useRating.mockReturnValue({ data: { rating: 3, rated_at: "2026-03-22T00:00:00Z" } });
  });

  it("links the season breadcrumb and back button to the resolved season page", () => {
    const markup = renderToStaticMarkup(
      <MemoryRouter initialEntries={["/item/episode-1"]}>
        <EpisodeContent item={makeEpisodeItem()} />
      </MemoryRouter>,
    );

    expect(countOccurrences(markup, 'href="/item/series-1"')).toBe(1);
    expect(countOccurrences(markup, 'href="/item/season-1"')).toBe(2);
    expect(markup).toContain(">Season 1<");
  });

  it("uses season navigation state before season detail finishes loading", () => {
    mocks.useSeasonDetail.mockReturnValue({
      data: undefined,
    });

    const markup = renderToStaticMarkup(
      <MemoryRouter
        initialEntries={[
          {
            pathname: "/item/episode-1",
            state: {
              parentSeasonHref: "/item/season-99",
              parentSeasonLabel: "Season 99",
            },
          },
        ]}
      >
        <EpisodeContent item={makeEpisodeItem()} />
      </MemoryRouter>,
    );

    expect(countOccurrences(markup, 'href="/item/season-99"')).toBe(2);
    expect(markup).toContain(">Season 99<");
  });

  it("shows all season episodes in the carousel, not just nearby ones", () => {
    const allEpisodes = Array.from({ length: 10 }, (_, i) => ({
      content_id: `ep-${i + 1}`,
      season_number: 1,
      episode_number: i + 1,
      title: `Episode ${i + 1} Title`,
      overview: "",
      air_date: null,
      runtime: 42,
      still_url: "",
      still_thumbhash: "",
      files: [],
    }));

    mocks.useSeasonEpisodes.mockReturnValue({
      data: { episodes: allEpisodes },
    });

    const markup = renderToStaticMarkup(
      <MemoryRouter initialEntries={["/item/episode-1"]}>
        <EpisodeContent item={makeEpisodeItem({ episode_number: 5 })} />
      </MemoryRouter>,
    );

    // All 10 episodes should be rendered
    for (let i = 1; i <= 10; i++) {
      expect(markup).toContain(`Episode ${i} Title`);
    }

    // Current episode (5) should be marked
    expect(markup).toContain('data-current="true"');
    expect(countOccurrences(markup, 'data-current="true"')).toBe(1);
  });

  it("hides the carousel when only one episode exists", () => {
    mocks.useSeasonEpisodes.mockReturnValue({
      data: {
        episodes: [
          {
            content_id: "ep-1",
            season_number: 1,
            episode_number: 1,
            title: "Only Episode",
            overview: "",
            air_date: null,
            runtime: 42,
            still_url: "",
            still_thumbhash: "",
            files: [],
          },
        ],
      },
    });

    const markup = renderToStaticMarkup(
      <MemoryRouter initialEntries={["/item/episode-1"]}>
        <EpisodeContent item={makeEpisodeItem({ episode_number: 1 })} />
      </MemoryRouter>,
    );

    expect(markup).not.toContain("More Episodes");
    expect(markup).not.toContain("episode-carousel");
  });

  it("does not pass rating props to ActionBar", () => {
    renderToStaticMarkup(
      <MemoryRouter initialEntries={["/item/episode-1"]}>
        <EpisodeContent item={makeEpisodeItem()} />
      </MemoryRouter>,
    );

    expect(mocks.capturedActionBarProps.value).not.toHaveProperty("rating");
    expect(mocks.capturedActionBarProps.value).not.toHaveProperty("onRatingChange");
  });
});
