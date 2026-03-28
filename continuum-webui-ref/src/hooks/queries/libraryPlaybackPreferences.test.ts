import { QueryClient } from "@tanstack/react-query";
import { beforeEach, describe, expect, it, vi } from "vitest";

const mocks = vi.hoisted(() => ({
  api: vi.fn(),
  storageGet: vi.fn(),
  useMutation: vi.fn(),
  useQuery: vi.fn(),
  useQueryClient: vi.fn(),
}));

vi.mock("@tanstack/react-query", async () => {
  const actual =
    await vi.importActual<typeof import("@tanstack/react-query")>("@tanstack/react-query");

  return {
    ...actual,
    useMutation: (...args: unknown[]) => mocks.useMutation(...args),
    useQuery: (...args: unknown[]) => mocks.useQuery(...args),
    useQueryClient: () => mocks.useQueryClient(),
  };
});

vi.mock("@/api/client", () => ({
  api: (...args: unknown[]) => mocks.api(...args),
}));

vi.mock("@/utils/storage", () => ({
  storage: {
    KEYS: {
      PROFILE_ID: "profile_id",
    },
    get: (...args: unknown[]) => mocks.storageGet(...args),
  },
}));

import type { LibraryPlaybackPreference } from "@/api/types";
import { libraryPlaybackPreferenceKeys } from "./keys";
import {
  type UpdateLibraryPlaybackPreferenceVariables,
  useDeleteLibraryPlaybackPreference,
  useLibraryPlaybackPreferences,
  useSetLibraryPlaybackPreference,
} from "./libraryPlaybackPreferences";

type QueryOptions = {
  queryKey: ReturnType<typeof libraryPlaybackPreferenceKeys.list>;
  queryFn: () => Promise<LibraryPlaybackPreference[]>;
};

type SetMutationOptions = {
  mutationFn: (variables: UpdateLibraryPlaybackPreferenceVariables) => Promise<unknown>;
  onSuccess?: (
    data: unknown,
    variables: UpdateLibraryPlaybackPreferenceVariables,
  ) => Promise<unknown> | unknown;
};

type DeleteMutationOptions = {
  mutationFn: (libraryId: number) => Promise<unknown>;
  onSuccess?: (data: unknown, libraryId: number) => Promise<unknown> | unknown;
};

function latestQueryOptions(): QueryOptions {
  return mocks.useQuery.mock.calls[mocks.useQuery.mock.calls.length - 1]?.[0] as QueryOptions;
}

function latestSetMutationOptions(): SetMutationOptions {
  return mocks.useMutation.mock.calls[
    mocks.useMutation.mock.calls.length - 1
  ]?.[0] as SetMutationOptions;
}

function latestDeleteMutationOptions(): DeleteMutationOptions {
  return mocks.useMutation.mock.calls[
    mocks.useMutation.mock.calls.length - 1
  ]?.[0] as DeleteMutationOptions;
}

describe("library playback preference query hooks", () => {
  let queryClient: QueryClient;

  beforeEach(() => {
    queryClient = new QueryClient({
      defaultOptions: {
        queries: {
          retry: false,
        },
      },
    });

    mocks.api.mockReset();
    mocks.storageGet.mockReset();
    mocks.useMutation.mockReset();
    mocks.useQuery.mockReset();
    mocks.useQueryClient.mockReset();

    mocks.storageGet.mockReturnValue("profile-1");
    mocks.useQuery.mockImplementation((options: unknown) => options);
    mocks.useMutation.mockImplementation((options: unknown) => options);
    mocks.useQueryClient.mockReturnValue(queryClient);
  });

  it("loads the active profile playback preference list", async () => {
    const row: LibraryPlaybackPreference = {
      profile_id: "profile-1",
      library_id: 7,
      audio_language: "ja",
      subtitle_language: "en",
      subtitle_mode: "always",
      show_forced_subtitles: true,
      updated_at: "2026-03-23T00:00:00.000Z",
    };
    mocks.api.mockResolvedValueOnce({ preferences: [row] });

    useLibraryPlaybackPreferences();
    const query = latestQueryOptions();

    expect(query.queryKey).toEqual(libraryPlaybackPreferenceKeys.list("profile-1"));
    await expect(query.queryFn()).resolves.toEqual([row]);
    expect(mocks.api).toHaveBeenCalledWith("/library-playback-prefs");
  });

  it("updates an existing preference row in cache after PUT", async () => {
    const initial: LibraryPlaybackPreference = {
      profile_id: "profile-1",
      library_id: 7,
      audio_language: "en",
      subtitle_language: "en",
      subtitle_mode: "auto",
      show_forced_subtitles: true,
      updated_at: "2026-03-23T00:00:00.000Z",
    };
    const key = libraryPlaybackPreferenceKeys.list("profile-1");
    queryClient.setQueryData(key, [initial]);

    useSetLibraryPlaybackPreference();
    const mutation = latestSetMutationOptions();
    const body = { audio_language: "ja", subtitle_mode: "always", show_forced_subtitles: false };

    await mutation.mutationFn({ libraryId: 7, body });
    expect(mocks.api).toHaveBeenCalledWith("/library-playback-prefs/7", {
      method: "PUT",
      body: JSON.stringify(body),
    });

    await mutation.onSuccess?.(undefined, { libraryId: 7, body });

    expect(queryClient.getQueryData<LibraryPlaybackPreference[]>(key)).toEqual([
      {
        ...initial,
        audio_language: "ja",
        subtitle_language: "en",
        subtitle_mode: "always",
        show_forced_subtitles: false,
      },
    ]);
  });

  it("removes an existing preference row from cache when PUT clears all fields", async () => {
    const initial: LibraryPlaybackPreference = {
      profile_id: "profile-1",
      library_id: 7,
      audio_language: "ja",
      subtitle_language: "en",
      subtitle_mode: "always",
      show_forced_subtitles: true,
      updated_at: "2026-03-23T00:00:00.000Z",
    };
    const key = libraryPlaybackPreferenceKeys.list("profile-1");
    queryClient.setQueryData(key, [initial]);

    useSetLibraryPlaybackPreference();
    const mutation = latestSetMutationOptions();
    const body = {};

    await mutation.mutationFn({ libraryId: 7, body });
    await mutation.onSuccess?.(undefined, { libraryId: 7, body });

    expect(queryClient.getQueryData<LibraryPlaybackPreference[]>(key)).toEqual([]);
  });

  it("preserves an explicit empty subtitle language override in cache", async () => {
    const initial: LibraryPlaybackPreference = {
      profile_id: "profile-1",
      library_id: 7,
      audio_language: "ja",
      subtitle_language: "en",
      subtitle_mode: "auto",
      show_forced_subtitles: true,
      updated_at: "2026-03-23T00:00:00.000Z",
    };
    const key = libraryPlaybackPreferenceKeys.list("profile-1");
    queryClient.setQueryData(key, [initial]);

    useSetLibraryPlaybackPreference();
    const mutation = latestSetMutationOptions();
    const body = { subtitle_language: "" };

    await mutation.onSuccess?.(undefined, { libraryId: 7, body });

    expect(queryClient.getQueryData<LibraryPlaybackPreference[]>(key)).toEqual([
      {
        ...initial,
        subtitle_language: "",
      },
    ]);
  });

  it("removes an existing preference row from cache after DELETE", async () => {
    const initial: LibraryPlaybackPreference = {
      profile_id: "profile-1",
      library_id: 7,
      audio_language: "ja",
      subtitle_language: "en",
      subtitle_mode: "always",
      show_forced_subtitles: true,
      updated_at: "2026-03-23T00:00:00.000Z",
    };
    const key = libraryPlaybackPreferenceKeys.list("profile-1");
    queryClient.setQueryData(key, [initial]);

    useDeleteLibraryPlaybackPreference();
    const mutation = latestDeleteMutationOptions();

    await mutation.mutationFn(7);
    expect(mocks.api).toHaveBeenCalledWith("/library-playback-prefs/7", {
      method: "DELETE",
    });

    await mutation.onSuccess?.(undefined, 7);

    expect(queryClient.getQueryData<LibraryPlaybackPreference[]>(key)).toEqual([]);
  });

  it("scopes the query key to the active profile id", () => {
    mocks.storageGet.mockReturnValue("profile-2");

    useLibraryPlaybackPreferences();
    const query = latestQueryOptions();

    expect(query.queryKey).toEqual(libraryPlaybackPreferenceKeys.list("profile-2"));
  });

  it("invalidates the active profile list when PUT succeeds without cached data", async () => {
    const invalidateQueries = vi.spyOn(queryClient, "invalidateQueries");
    useSetLibraryPlaybackPreference();
    const mutation = latestSetMutationOptions();
    const body = { audio_language: "ja" };

    await mutation.onSuccess?.(undefined, { libraryId: 7, body });

    expect(invalidateQueries).toHaveBeenCalledWith({
      queryKey: libraryPlaybackPreferenceKeys.list("profile-1"),
    });
  });
});
