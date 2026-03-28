import type { GuidedFormState } from "@/components/collections/CollectionGuidedRulesEditor";

export interface ActiveFilterBadge {
  key: string;
  label: string;
  clearPatch: Partial<GuidedFormState>;
}

/**
 * Compute displayable badge descriptors for all active "secondary" filters.
 * Skips mediaScope, sortField, sortOrder (shown inline in the bar).
 */
export function getActiveFilterBadges(state: GuidedFormState): ActiveFilterBadge[] {
  const badges: ActiveFilterBadge[] = [];

  // Genres — one badge per selected genre
  for (const genre of state.genres) {
    badges.push({
      key: `genre:${genre}`,
      label: `Genre: ${genre}`,
      clearPatch: { genres: state.genres.filter((g) => g !== genre) },
    });
  }

  // Year range — combine into a single badge
  if (state.yearFrom && state.yearTo) {
    badges.push({
      key: "year",
      label: `Year: ${state.yearFrom}–${state.yearTo}`,
      clearPatch: { yearFrom: "", yearTo: "" },
    });
  } else if (state.yearFrom) {
    badges.push({
      key: "year",
      label: `Year: >= ${state.yearFrom}`,
      clearPatch: { yearFrom: "" },
    });
  } else if (state.yearTo) {
    badges.push({
      key: "year",
      label: `Year: <= ${state.yearTo}`,
      clearPatch: { yearTo: "" },
    });
  }

  // Minimum IMDb rating
  if (state.minRating) {
    badges.push({
      key: "minRating",
      label: `IMDb: >= ${state.minRating}`,
      clearPatch: { minRating: "" },
    });
  }

  // Content rating
  if (state.contentRating) {
    badges.push({
      key: "contentRating",
      label: `Rated: ${state.contentRating}`,
      clearPatch: { contentRating: "" },
    });
  }

  // Studio
  if (state.studio) {
    badges.push({
      key: "studio",
      label: `Studio: ${state.studio}`,
      clearPatch: { studio: "" },
    });
  }

  // Network
  if (state.network) {
    badges.push({
      key: "network",
      label: `Network: ${state.network}`,
      clearPatch: { network: "" },
    });
  }

  // Country
  if (state.country) {
    badges.push({
      key: "country",
      label: `Country: ${state.country}`,
      clearPatch: { country: "" },
    });
  }

  // Status
  if (state.status) {
    badges.push({
      key: "status",
      label: `Status: ${state.status}`,
      clearPatch: { status: "" },
    });
  }

  // Added in the last
  if (state.addedInLast) {
    badges.push({
      key: "addedInLast",
      label: `Added in last: ${state.addedInLast}`,
      clearPatch: { addedInLast: "" },
    });
  }

  // Released in the last
  if (state.releasedInLast) {
    badges.push({
      key: "releasedInLast",
      label: `Released in last: ${state.releasedInLast}`,
      clearPatch: { releasedInLast: "" },
    });
  }

  return badges;
}

/** Count how many secondary filters are active (for the badge count on the Filters button). */
export function countActiveFilters(state: GuidedFormState): number {
  return getActiveFilterBadges(state).length;
}
