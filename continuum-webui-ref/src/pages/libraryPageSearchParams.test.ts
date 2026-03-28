import { describe, expect, it } from "vitest";
import { parseLibraryPageState, updateLibraryPageSearchParams } from "./libraryPageSearchParams";

function params(search: string) {
  return new URLSearchParams(search);
}

function asObject(searchParams: URLSearchParams) {
  return Object.fromEntries(searchParams.entries());
}

describe("parseLibraryPageState", () => {
  it("uses recommended tab and title ascending defaults when params are absent", () => {
    const state = parseLibraryPageState(params(""), "mixed");

    expect(state).toEqual({
      activeTab: "recommended",
      filters: {
        type: "all",
        sort: "sort_title",
        order: "asc",
        genre: "all",
        year_min: "",
        year_max: "",
        content_rating: "all",
      },
    });
  });

  it("ignores library filters unless tab=library is set", () => {
    const state = parseLibraryPageState(
      params("genre=Crime&sort=year&order=desc&year_min=1990"),
      "mixed",
    );

    expect(state).toEqual({
      activeTab: "recommended",
      filters: {
        type: "all",
        sort: "sort_title",
        order: "asc",
        genre: "all",
        year_min: "",
        year_max: "",
        content_rating: "all",
      },
    });
  });

  it("treats tab=collections as a non-browse tab with default filters", () => {
    const state = parseLibraryPageState(params("tab=collections&genre=Crime&sort=year"), "mixed");

    expect(state).toEqual({
      activeTab: "collections",
      filters: {
        type: "all",
        sort: "sort_title",
        order: "asc",
        genre: "all",
        year_min: "",
        year_max: "",
        content_rating: "all",
      },
    });
  });

  it("sanitizes invalid values and ignores type filters for non-mixed libraries", () => {
    const state = parseLibraryPageState(
      params(
        "tab=library&type=series&sort=nope&order=sideways&genre=Unknown&year_min=abc&year_max=2005&content_rating=wat",
      ),
      "movies",
    );

    expect(state).toEqual({
      activeTab: "library",
      filters: {
        type: "all",
        sort: "sort_title",
        order: "asc",
        genre: "all",
        year_min: "",
        year_max: "2005",
        content_rating: "all",
      },
    });
  });

  it("does not honor type filters until the library type is known", () => {
    const state = parseLibraryPageState(params("tab=library&type=movie&genre=Crime"), "");

    expect(state).toEqual({
      activeTab: "library",
      filters: {
        type: "all",
        sort: "sort_title",
        order: "asc",
        genre: "Crime",
        year_min: "",
        year_max: "",
        content_rating: "all",
      },
    });
  });

  it("parses valid library filters for mixed libraries", () => {
    const state = parseLibraryPageState(
      params(
        "tab=library&type=movie&sort=year&order=desc&genre=Crime&year_min=1970&year_max=2010&content_rating=R",
      ),
      "mixed",
    );

    expect(state).toEqual({
      activeTab: "library",
      filters: {
        type: "movie",
        sort: "year",
        order: "desc",
        genre: "Crime",
        year_min: "1970",
        year_max: "2010",
        content_rating: "R",
      },
    });
  });
});

describe("updateLibraryPageSearchParams", () => {
  it("writes only non-default library filters and preserves unrelated params", () => {
    const next = updateLibraryPageSearchParams(
      params("foo=bar"),
      {
        activeTab: "library",
        filters: {
          type: "movie",
          sort: "sort_title",
          order: "asc",
          genre: "Crime",
          year_min: "",
          year_max: "",
          content_rating: "all",
        },
      },
      "mixed",
    );

    expect(asObject(next)).toEqual({
      foo: "bar",
      tab: "library",
      type: "movie",
      genre: "Crime",
    });
  });

  it("drops zero years because they do not affect the browse query", () => {
    const next = updateLibraryPageSearchParams(
      params("tab=library"),
      {
        activeTab: "library",
        filters: {
          type: "all",
          sort: "sort_title",
          order: "asc",
          genre: "all",
          year_min: "0",
          year_max: "0000",
          content_rating: "all",
        },
      },
      "mixed",
    );

    expect(asObject(next)).toEqual({ tab: "library" });
  });

  it("removes library params when switching back to recommended", () => {
    const next = updateLibraryPageSearchParams(
      params("foo=bar&tab=library&type=movie&genre=Crime&year_min=2000"),
      {
        activeTab: "recommended",
        filters: {
          type: "movie",
          sort: "year",
          order: "desc",
          genre: "Crime",
          year_min: "2000",
          year_max: "",
          content_rating: "PG-13",
        },
      },
      "mixed",
    );

    expect(asObject(next)).toEqual({ foo: "bar" });
  });

  it("writes only the collections tab marker and drops browse-only filters", () => {
    const next = updateLibraryPageSearchParams(
      params("foo=bar&tab=library&type=movie&genre=Crime"),
      {
        activeTab: "collections",
        filters: {
          type: "movie",
          sort: "year",
          order: "desc",
          genre: "Crime",
          year_min: "2000",
          year_max: "",
          content_rating: "PG-13",
        },
      },
      "mixed",
    );

    expect(asObject(next)).toEqual({
      foo: "bar",
      tab: "collections",
    });
  });
});
