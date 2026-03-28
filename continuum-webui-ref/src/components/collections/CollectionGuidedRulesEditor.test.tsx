import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it, vi } from "vitest";

import type { QueryDefinition } from "@/api/types";
import { createEmptyQueryDefinition } from "@/api/types";

vi.mock("@/hooks/queries/catalog", () => ({
  useCatalogMetadataFilters: () => ({
    data: {
      genres: [],
      studios: [],
      networks: [],
      countries: [],
      content_ratings: [],
      original_languages: ["English", "Japanese"],
    },
    isLoading: false,
  }),
}));

import {
  guidedStateToQueryDefinition,
  queryDefinitionToGuidedState,
  type GuidedFormState,
} from "./CollectionGuidedRulesEditor";
import CollectionGuidedRulesEditor from "./CollectionGuidedRulesEditor";

function emptyState(): GuidedFormState {
  return {
    mediaScope: "all",
    libraryIds: [],
    genres: [],
    yearFrom: "",
    yearTo: "",
    minRating: "",
    contentRating: "",
    studio: "",
    network: "",
    country: "",
    status: "",
    addedInLast: "",
    releasedInLast: "",
    originalLanguage: "",
    sortField: "added_at",
    sortOrder: "desc",
  };
}

describe("queryDefinitionToGuidedState", () => {
  it("returns defaults for an empty query definition", () => {
    const state = queryDefinitionToGuidedState(createEmptyQueryDefinition());
    expect(state).toEqual(emptyState());
  });

  it("extracts fields from a populated query definition", () => {
    const qd: QueryDefinition = {
      library_ids: [1, 2],
      media_scope: "movie",
      match: "all",
      groups: [
        {
          match: "all",
          rules: [
            { field: "genre", op: "is", value: "Action" },
            { field: "genre", op: "is", value: "Thriller" },
            { field: "year", op: "gte", value: 2020 },
            { field: "year", op: "lte", value: 2025 },
            { field: "rating_imdb", op: "gte", value: 7.5 },
            { field: "content_rating", op: "is", value: "R" },
            { field: "studio", op: "is", value: "A24" },
            { field: "original_language", op: "is", value: "English" },
            { field: "status", op: "is", value: "matched" },
            { field: "added_at", op: "in_last", value: "30d" },
          ],
        },
      ],
      sort: { field: "release_date", order: "asc" },
    };

    const state = queryDefinitionToGuidedState(qd);
    expect(state.mediaScope).toBe("movie");
    expect(state.libraryIds).toEqual([1, 2]);
    expect(state.genres).toEqual(["Action", "Thriller"]);
    expect(state.yearFrom).toBe("2020");
    expect(state.yearTo).toBe("2025");
    expect(state.minRating).toBe("7.5");
    expect(state.contentRating).toBe("R");
    expect(state.studio).toBe("A24");
    expect(state.originalLanguage).toBe("English");
    expect(state.status).toBe("matched");
    expect(state.addedInLast).toBe("30d");
    expect(state.sortField).toBe("release_date");
    expect(state.sortOrder).toBe("asc");
  });
});

describe("guidedStateToQueryDefinition", () => {
  const base = createEmptyQueryDefinition();

  it("produces an empty groups array for an empty state", () => {
    const qd = guidedStateToQueryDefinition(emptyState(), base);
    expect(qd.groups).toEqual([]);
    expect(qd.match).toBe("all");
    expect(qd.media_scope).toBeUndefined();
  });

  it("builds rules from a populated state", () => {
    const state: GuidedFormState = {
      mediaScope: "series",
      libraryIds: [3],
      genres: ["Drama", "Sci-Fi"],
      yearFrom: "2015",
      yearTo: "",
      minRating: "8",
      contentRating: "",
      studio: "",
      originalLanguage: "Japanese",
      network: "HBO",
      country: "US",
      status: "matched",
      addedInLast: "",
      releasedInLast: "1y",
      sortField: "rating_imdb",
      sortOrder: "desc",
    };

    const qd = guidedStateToQueryDefinition(state, base);
    expect(qd.media_scope).toBe("series");
    expect(qd.library_ids).toEqual([3]);
    expect(qd.sort).toEqual({ field: "rating_imdb", order: "desc" });
    expect(qd.groups).toHaveLength(1);

    const rules = qd.groups[0]!.rules;
    expect(rules).toContainEqual({ field: "genre", op: "is", value: "Drama" });
    expect(rules).toContainEqual({ field: "genre", op: "is", value: "Sci-Fi" });
    expect(rules).toContainEqual({ field: "year", op: "gte", value: 2015 });
    expect(rules).toContainEqual({ field: "rating_imdb", op: "gte", value: 8 });
    expect(rules).toContainEqual({ field: "network", op: "is", value: "HBO" });
    expect(rules).toContainEqual({ field: "country", op: "is", value: "US" });
    expect(rules).toContainEqual({ field: "original_language", op: "is", value: "Japanese" });
    expect(rules).toContainEqual({ field: "status", op: "is", value: "matched" });
    expect(rules).toContainEqual({ field: "release_date", op: "in_last", value: "1y" });
  });

  it("round-trips through parse and serialize", () => {
    const original: QueryDefinition = {
      library_ids: [1],
      media_scope: "movie",
      match: "all",
      groups: [
        {
          match: "all",
          rules: [
            { field: "genre", op: "is", value: "Comedy" },
            { field: "year", op: "gte", value: 2000 },
            { field: "year", op: "lte", value: 2020 },
            { field: "rating_imdb", op: "gte", value: 6 },
            { field: "original_language", op: "is", value: "French" },
          ],
        },
      ],
      sort: { field: "title", order: "asc" },
    };

    const state = queryDefinitionToGuidedState(original);
    const rebuilt = guidedStateToQueryDefinition(state, original);

    expect(rebuilt.library_ids).toEqual([1]);
    expect(rebuilt.media_scope).toBe("movie");
    expect(rebuilt.sort).toEqual({ field: "title", order: "asc" });

    const rules = rebuilt.groups[0]!.rules;
    expect(rules).toContainEqual({ field: "genre", op: "is", value: "Comedy" });
    expect(rules).toContainEqual({ field: "year", op: "gte", value: 2000 });
    expect(rules).toContainEqual({ field: "year", op: "lte", value: 2020 });
    expect(rules).toContainEqual({ field: "rating_imdb", op: "gte", value: 6 });
    expect(rules).toContainEqual({ field: "original_language", op: "is", value: "French" });
  });

  it("accepts legacy rating aliases when parsing and emits canonical rating_imdb rules", () => {
    const original: QueryDefinition = {
      library_ids: [],
      match: "all",
      groups: [
        {
          match: "all",
          rules: [{ field: "rating", op: "gte", value: 7.2 }],
        },
      ],
      sort: { field: "rating", order: "desc" },
    };

    const state = queryDefinitionToGuidedState(original);
    const rebuilt = guidedStateToQueryDefinition(state, original);

    expect(state.minRating).toBe("7.2");
    expect(state.sortField).toBe("rating_imdb");
    expect(rebuilt.sort).toEqual({ field: "rating_imdb", order: "desc" });
    expect(rebuilt.groups[0]!.rules).toContainEqual({
      field: "rating_imdb",
      op: "gte",
      value: 7.2,
    });
  });
});

describe("CollectionGuidedRulesEditor original language field", () => {
  it("renders the original language select with the current value", () => {
    const markup = renderToStaticMarkup(
      <CollectionGuidedRulesEditor
        value={{
          ...createEmptyQueryDefinition(),
          groups: [
            {
              match: "all",
              rules: [{ field: "original_language", op: "is", value: "English" }],
            },
          ],
        }}
        onChange={() => {}}
      />,
    );

    expect(markup).toContain("Original Language");
    expect(markup).toContain("English");
  });
});
