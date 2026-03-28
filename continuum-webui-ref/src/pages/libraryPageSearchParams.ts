import {
  ADVANCED_FILTER_CONTENT_RATINGS,
  ADVANCED_FILTER_GENRES,
  DEFAULT_LIBRARY_BROWSE_FILTERS,
} from "@/components/advancedFilterOptions";
import type { AdvancedFilters } from "@/components/advancedFilterOptions";

export type LibraryPageTab = "recommended" | "library" | "collections";

export interface LibraryPageState {
  activeTab: LibraryPageTab;
  filters: AdvancedFilters;
}

const LIBRARY_FILTER_KEYS = [
  "type",
  "sort",
  "order",
  "genre",
  "year_min",
  "year_max",
  "content_rating",
] as const;

const VALID_TYPES = new Set(["all", "movie", "series"]);
const VALID_SORTS = new Set(["recently_added", "sort_title", "year", "rating_imdb"]);
const VALID_ORDERS = new Set(["asc", "desc"]);
const VALID_GENRES = new Set(["all", ...ADVANCED_FILTER_GENRES]);
const VALID_CONTENT_RATINGS = new Set(["all", ...ADVANCED_FILTER_CONTENT_RATINGS]);

function sanitizeEnum<T extends string>(
  value: string | null,
  allowed: Set<string>,
  fallback: T,
): T {
  if (!value || !allowed.has(value)) {
    return fallback;
  }
  return value as T;
}

function sanitizeYear(value: string | null): string {
  if (!value) {
    return "";
  }

  const trimmed = value.trim();
  if (!/^\d+$/.test(trimmed)) {
    return "";
  }

  const numericValue = Number(trimmed);
  return Number.isInteger(numericValue) && numericValue > 0 ? String(numericValue) : "";
}

function normalizeFilters(filters: AdvancedFilters, libraryType: string): AdvancedFilters {
  const type =
    libraryType === "mixed"
      ? sanitizeEnum(filters.type, VALID_TYPES, DEFAULT_LIBRARY_BROWSE_FILTERS.type)
      : DEFAULT_LIBRARY_BROWSE_FILTERS.type;

  return {
    type,
    sort: sanitizeEnum(filters.sort, VALID_SORTS, DEFAULT_LIBRARY_BROWSE_FILTERS.sort),
    order: sanitizeEnum(filters.order, VALID_ORDERS, DEFAULT_LIBRARY_BROWSE_FILTERS.order),
    genre: sanitizeEnum(filters.genre, VALID_GENRES, DEFAULT_LIBRARY_BROWSE_FILTERS.genre),
    year_min: sanitizeYear(filters.year_min),
    year_max: sanitizeYear(filters.year_max),
    content_rating: sanitizeEnum(
      filters.content_rating,
      VALID_CONTENT_RATINGS,
      DEFAULT_LIBRARY_BROWSE_FILTERS.content_rating,
    ),
  };
}

export function parseLibraryPageState(
  searchParams: URLSearchParams,
  libraryType: string,
): LibraryPageState {
  const activeTab: LibraryPageTab =
    searchParams.get("tab") === "library"
      ? "library"
      : searchParams.get("tab") === "collections"
        ? "collections"
        : "recommended";

  if (activeTab !== "library") {
    return {
      activeTab,
      filters: { ...DEFAULT_LIBRARY_BROWSE_FILTERS },
    };
  }

  return {
    activeTab,
    filters: normalizeFilters(
      {
        type: searchParams.get("type") ?? DEFAULT_LIBRARY_BROWSE_FILTERS.type,
        sort: searchParams.get("sort") ?? DEFAULT_LIBRARY_BROWSE_FILTERS.sort,
        order: searchParams.get("order") ?? DEFAULT_LIBRARY_BROWSE_FILTERS.order,
        genre: searchParams.get("genre") ?? DEFAULT_LIBRARY_BROWSE_FILTERS.genre,
        year_min: searchParams.get("year_min") ?? DEFAULT_LIBRARY_BROWSE_FILTERS.year_min,
        year_max: searchParams.get("year_max") ?? DEFAULT_LIBRARY_BROWSE_FILTERS.year_max,
        content_rating:
          searchParams.get("content_rating") ?? DEFAULT_LIBRARY_BROWSE_FILTERS.content_rating,
      },
      libraryType,
    ),
  };
}

export function updateLibraryPageSearchParams(
  currentSearchParams: URLSearchParams,
  state: LibraryPageState,
  libraryType: string,
): URLSearchParams {
  const nextSearchParams = new URLSearchParams(currentSearchParams);

  nextSearchParams.delete("tab");
  for (const key of LIBRARY_FILTER_KEYS) {
    nextSearchParams.delete(key);
  }

  if (state.activeTab !== "library") {
    if (state.activeTab === "collections") {
      nextSearchParams.set("tab", "collections");
    }
    return nextSearchParams;
  }

  const filters = normalizeFilters(state.filters, libraryType);

  nextSearchParams.set("tab", "library");

  if (libraryType === "mixed" && filters.type !== DEFAULT_LIBRARY_BROWSE_FILTERS.type) {
    nextSearchParams.set("type", filters.type);
  }
  if (filters.sort !== DEFAULT_LIBRARY_BROWSE_FILTERS.sort) {
    nextSearchParams.set("sort", filters.sort);
  }
  if (filters.order !== DEFAULT_LIBRARY_BROWSE_FILTERS.order) {
    nextSearchParams.set("order", filters.order);
  }
  if (filters.genre !== DEFAULT_LIBRARY_BROWSE_FILTERS.genre) {
    nextSearchParams.set("genre", filters.genre);
  }
  if (filters.year_min) {
    nextSearchParams.set("year_min", filters.year_min);
  }
  if (filters.year_max) {
    nextSearchParams.set("year_max", filters.year_max);
  }
  if (filters.content_rating !== DEFAULT_LIBRARY_BROWSE_FILTERS.content_rating) {
    nextSearchParams.set("content_rating", filters.content_rating);
  }

  return nextSearchParams;
}
