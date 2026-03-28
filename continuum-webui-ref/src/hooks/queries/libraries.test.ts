import { describe, expect, it } from "vitest";
import type { UserLibrary } from "@/api/types";
import {
  filterVisibleLibraries,
  parseDisabledLibraryIDs,
  serializeDisabledLibraryIDs,
} from "./libraries";

const libraries: UserLibrary[] = [
  { id: 1, name: "Movies", type: "movies" },
  { id: 2, name: "Shows", type: "series" },
  { id: 3, name: "Anime", type: "series" },
];

describe("parseDisabledLibraryIDs", () => {
  it("returns an empty list when the setting is missing or invalid", () => {
    expect(parseDisabledLibraryIDs(null)).toEqual([]);
    expect(parseDisabledLibraryIDs("")).toEqual([]);
    expect(parseDisabledLibraryIDs("nope")).toEqual([]);
    expect(parseDisabledLibraryIDs('{"ids":[1,2]}')).toEqual([]);
  });

  it("keeps only positive integer library ids", () => {
    expect(parseDisabledLibraryIDs('[1,2,2,0,-1,3.5,"4"]')).toEqual([1, 2]);
  });
});

describe("serializeDisabledLibraryIDs", () => {
  it("stores a normalized id list", () => {
    expect(serializeDisabledLibraryIDs([3, 1, 3, -1, 0])).toBe("[3,1]");
  });
});

describe("filterVisibleLibraries", () => {
  it("returns all libraries when nothing is disabled", () => {
    expect(filterVisibleLibraries(libraries, [])).toEqual(libraries);
  });

  it("filters out disabled libraries", () => {
    expect(filterVisibleLibraries(libraries, [2, 99]).map((library) => library.id)).toEqual([1, 3]);
  });
});
