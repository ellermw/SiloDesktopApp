import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { renderToStaticMarkup } from "react-dom/server";
import { MemoryRouter, Route, Routes } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { Profile, WatchDetail } from "@/api/types";
import type { WatchPageProps } from "@/player";

let capturedWatchPageProps: WatchPageProps | null = null;

const mocks = vi.hoisted(() => ({
  useCurrentProfile: vi.fn(),
  useDocumentTitle: vi.fn(),
  useWatchDetail: vi.fn(),
}));

vi.mock("@/hooks/queries/items", () => ({
  useWatchDetail: (...args: unknown[]) => mocks.useWatchDetail(...args),
}));

vi.mock("@/hooks/useCurrentProfile", () => ({
  useCurrentProfile: (...args: unknown[]) => mocks.useCurrentProfile(...args),
}));

vi.mock("@/hooks/useDocumentTitle", () => ({
  useDocumentTitle: (...args: unknown[]) => mocks.useDocumentTitle(...args),
}));

vi.mock("@/hooks/queries/playbackSurfaceRefresh", () => ({
  invalidatePlaybackSurfaceQueries: vi.fn(),
}));

vi.mock("@/player", () => ({
  PlayerConfigProvider: ({ children }: { children: React.ReactNode }) => <>{children}</>,
  WatchPage: (props: WatchPageProps) => {
    capturedWatchPageProps = props;
    return <div>Watch Page</div>;
  },
}));

import WatchRoute from "./WatchRoute";

const profile = {
  id: "profile-1",
  name: "Main",
  avatar: "avatar-1",
  has_pin: false,
  is_child: false,
  max_content_rating: "pg-13",
  quality_preference: "auto",
  language: "en",
  subtitle_language: "fr",
  subtitle_mode: "off",
  show_forced_subtitles: false,
  auto_skip_intro: false,
  auto_skip_credits: false,
  library_restrictions_enabled: false,
  allowed_library_ids: null,
  max_playback_quality: "4k",
  created_at: "2026-03-23T00:00:00Z",
  updated_at: "2026-03-23T00:00:00Z",
} satisfies Profile;

function makeWatchDetail(overrides: Partial<WatchDetail> = {}): WatchDetail {
  return {
    content_id: "movie-1",
    type: "movie",
    title: "Spirited Away",
    overview: "Overview",
    versions: [],
    subtitles: [],
    intro: null,
    credits: null,
    ...overrides,
  };
}

describe("WatchRoute", () => {
  beforeEach(() => {
    capturedWatchPageProps = null;
    mocks.useCurrentProfile.mockReset();
    mocks.useDocumentTitle.mockReset();
    mocks.useWatchDetail.mockReset();

    mocks.useCurrentProfile.mockReturnValue({ profile });
    mocks.useWatchDetail.mockReturnValue({
      data: makeWatchDetail(),
      isLoading: false,
      error: null,
    });
  });

  it("prefers effective subtitle defaults from watch detail over raw profile values", () => {
    mocks.useWatchDetail.mockReturnValue({
      data: makeWatchDetail({
        effective_subtitle_language: "en",
        effective_subtitle_mode: "always",
        effective_show_forced_subtitles: true,
      }),
      isLoading: false,
      error: null,
    });

    renderToStaticMarkup(
      <QueryClientProvider client={new QueryClient()}>
        <MemoryRouter initialEntries={["/watch/movie-1"]}>
          <Routes>
            <Route path="/watch/:id" element={<WatchRoute />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(capturedWatchPageProps).toMatchObject({
      preferredSubtitleLanguage: "en",
      subtitleMode: "always",
      showForcedSubtitles: true,
      profileLanguage: "en",
    });
    expect(mocks.useWatchDetail).toHaveBeenCalledWith("movie-1", undefined);
  });

  it("falls back to the current profile subtitle defaults when watch detail has no effective override", () => {
    renderToStaticMarkup(
      <QueryClientProvider client={new QueryClient()}>
        <MemoryRouter initialEntries={["/watch/movie-1"]}>
          <Routes>
            <Route path="/watch/:id" element={<WatchRoute />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(capturedWatchPageProps).toMatchObject({
      preferredSubtitleLanguage: "fr",
      subtitleMode: "off",
      showForcedSubtitles: false,
      profileLanguage: "en",
    });
  });

  it("passes an explicit empty subtitle override through to the player", () => {
    mocks.useWatchDetail.mockReturnValue({
      data: makeWatchDetail({
        effective_subtitle_language: "",
        effective_subtitle_mode: "auto",
      }),
      isLoading: false,
      error: null,
    });

    renderToStaticMarkup(
      <QueryClientProvider client={new QueryClient()}>
        <MemoryRouter initialEntries={["/watch/movie-1"]}>
          <Routes>
            <Route path="/watch/:id" element={<WatchRoute />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(capturedWatchPageProps).toMatchObject({
      preferredSubtitleLanguage: "",
      subtitleMode: "auto",
      showForcedSubtitles: false,
      profileLanguage: "en",
    });
  });

  it("falls back to the profile forced-subtitle preference when watch detail has no effective override", () => {
    renderToStaticMarkup(
      <QueryClientProvider client={new QueryClient()}>
        <MemoryRouter initialEntries={["/watch/movie-1"]}>
          <Routes>
            <Route path="/watch/:id" element={<WatchRoute />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(capturedWatchPageProps).toMatchObject({
      showForcedSubtitles: false,
    });
  });

  it("defaults showForcedSubtitles to true when both watch detail and profile omit it", () => {
    mocks.useCurrentProfile.mockReturnValue({
      profile: { ...profile, show_forced_subtitles: undefined },
    });
    mocks.useWatchDetail.mockReturnValue({
      data: makeWatchDetail(),
      isLoading: false,
      error: null,
    });

    renderToStaticMarkup(
      <QueryClientProvider client={new QueryClient()}>
        <MemoryRouter initialEntries={["/watch/movie-1"]}>
          <Routes>
            <Route path="/watch/:id" element={<WatchRoute />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(capturedWatchPageProps).toMatchObject({
      showForcedSubtitles: true,
    });
  });

  it("passes the selected file id into the watch detail query", () => {
    renderToStaticMarkup(
      <QueryClientProvider client={new QueryClient()}>
        <MemoryRouter initialEntries={["/watch/movie-1?fileId=42"]}>
          <Routes>
            <Route path="/watch/:id" element={<WatchRoute />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(mocks.useWatchDetail).toHaveBeenCalledWith("movie-1", 42);
  });
});
