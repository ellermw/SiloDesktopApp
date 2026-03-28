import { useQuery } from "@tanstack/react-query";
import { api } from "@/api/client";
import type { UserLibrary } from "@/api/types";
import { libraryKeys } from "./keys";
import { useSetting } from "./settings";

export const DISABLED_LIBRARY_IDS_SETTING_KEY = "disabled_library_ids";

function normalizeLibraryIDs(ids: number[]) {
  return [...new Set(ids.filter((id) => Number.isInteger(id) && id > 0))];
}

export function parseDisabledLibraryIDs(value: string | null | undefined) {
  if (!value) return [];

  try {
    const parsed = JSON.parse(value);
    if (!Array.isArray(parsed)) return [];
    return normalizeLibraryIDs(
      parsed.map((entry) => (typeof entry === "number" ? entry : Number.NaN)),
    );
  } catch {
    return [];
  }
}

export function serializeDisabledLibraryIDs(ids: number[]) {
  return JSON.stringify(normalizeLibraryIDs(ids));
}

export function filterVisibleLibraries(libraries: UserLibrary[], disabledLibraryIDs: number[]) {
  if (disabledLibraryIDs.length === 0) return libraries;
  const disabled = new Set(disabledLibraryIDs);
  return libraries.filter((library) => !disabled.has(library.id));
}

export function useAvailableUserLibraries() {
  return useQuery({
    queryKey: libraryKeys.user(),
    queryFn: () => api<UserLibrary[]>("/user/libraries"),
    staleTime: 5 * 60 * 1000,
  });
}

export function useUserLibraries() {
  const librariesQuery = useAvailableUserLibraries();
  const disabledSettingQuery = useSetting(DISABLED_LIBRARY_IDS_SETTING_KEY);
  const disabledLibraryIDs = disabledSettingQuery.isLoading
    ? null
    : parseDisabledLibraryIDs(disabledSettingQuery.data);

  return {
    ...librariesQuery,
    data:
      librariesQuery.data == null || disabledLibraryIDs == null
        ? librariesQuery.data
        : filterVisibleLibraries(librariesQuery.data, disabledLibraryIDs),
    isLoading: librariesQuery.isLoading || disabledSettingQuery.isLoading,
    isFetching: librariesQuery.isFetching || disabledSettingQuery.isFetching,
    error: librariesQuery.error ?? disabledSettingQuery.error,
  };
}
