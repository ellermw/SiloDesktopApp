import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/client";
import type { LibraryPlaybackPreference } from "@/api/types";
import { storage } from "@/utils/storage";
import { libraryPlaybackPreferenceKeys } from "./keys";

interface LibraryPlaybackPreferencesResponse {
  preferences: LibraryPlaybackPreference[];
}

export interface UpdateLibraryPlaybackPreferenceRequest {
  audio_language?: string;
  subtitle_language?: string;
  subtitle_mode?: string;
  show_forced_subtitles?: boolean;
}

export interface UpdateLibraryPlaybackPreferenceVariables {
  libraryId: number;
  body: UpdateLibraryPlaybackPreferenceRequest;
}

function hasOwnField<T extends object>(value: T, field: keyof T) {
  return Object.prototype.hasOwnProperty.call(value, field);
}

function isEmptyPreference(body: UpdateLibraryPlaybackPreferenceRequest) {
  return (
    !hasOwnField(body, "audio_language") &&
    !hasOwnField(body, "subtitle_language") &&
    !hasOwnField(body, "subtitle_mode") &&
    !hasOwnField(body, "show_forced_subtitles")
  );
}

function updateCachedPreference(
  preferences: LibraryPlaybackPreference[],
  libraryId: number,
  body: UpdateLibraryPlaybackPreferenceRequest,
) {
  const index = preferences.findIndex((entry) => entry.library_id === libraryId);
  if (index < 0) {
    return null;
  }

  const current = preferences[index]!;
  const next: LibraryPlaybackPreference = {
    ...current,
  };
  if (hasOwnField(body, "audio_language")) {
    next.audio_language = body.audio_language;
  }
  if (hasOwnField(body, "subtitle_language")) {
    next.subtitle_language = body.subtitle_language;
  }
  if (hasOwnField(body, "subtitle_mode")) {
    next.subtitle_mode = body.subtitle_mode;
  }
  if (hasOwnField(body, "show_forced_subtitles")) {
    next.show_forced_subtitles = body.show_forced_subtitles;
  }

  const nextPreferences = [...preferences];
  nextPreferences[index] = next;
  return nextPreferences;
}

function removeCachedPreference(preferences: LibraryPlaybackPreference[], libraryId: number) {
  return preferences.filter((entry) => entry.library_id !== libraryId);
}

export function useLibraryPlaybackPreferences(options?: { enabled?: boolean }) {
  const profileId = storage.get(storage.KEYS.PROFILE_ID);

  return useQuery({
    queryKey: libraryPlaybackPreferenceKeys.list(profileId),
    queryFn: async () => {
      const result = await api<LibraryPlaybackPreferencesResponse>("/library-playback-prefs");
      return result.preferences ?? [];
    },
    enabled: (options?.enabled ?? true) && profileId != null,
    staleTime: 5 * 60 * 1000,
  });
}

export function useSetLibraryPlaybackPreference() {
  const queryClient = useQueryClient();
  const profileId = storage.get(storage.KEYS.PROFILE_ID);
  const key = libraryPlaybackPreferenceKeys.list(profileId);

  return useMutation({
    mutationFn: ({ libraryId, body }: UpdateLibraryPlaybackPreferenceVariables) =>
      api(`/library-playback-prefs/${libraryId}`, {
        method: "PUT",
        body: JSON.stringify(body),
      }),
    onSuccess: (_data, { libraryId, body }) => {
      const current = queryClient.getQueryData<LibraryPlaybackPreference[]>(key);

      if (!current) {
        return queryClient.invalidateQueries({ queryKey: key });
      }

      if (isEmptyPreference(body)) {
        queryClient.setQueryData(key, removeCachedPreference(current, libraryId));
        return;
      }

      const next = updateCachedPreference(current, libraryId, body);
      if (!next) {
        return queryClient.invalidateQueries({ queryKey: key });
      }

      queryClient.setQueryData(key, next);
    },
  });
}

export function useDeleteLibraryPlaybackPreference() {
  const queryClient = useQueryClient();
  const profileId = storage.get(storage.KEYS.PROFILE_ID);
  const key = libraryPlaybackPreferenceKeys.list(profileId);

  return useMutation({
    mutationFn: (libraryId: number) =>
      api(`/library-playback-prefs/${libraryId}`, {
        method: "DELETE",
      }),
    onSuccess: (_data, libraryId) => {
      const current = queryClient.getQueryData<LibraryPlaybackPreference[]>(key);

      if (!current) {
        return queryClient.invalidateQueries({ queryKey: key });
      }

      queryClient.setQueryData(key, removeCachedPreference(current, libraryId));
    },
  });
}
