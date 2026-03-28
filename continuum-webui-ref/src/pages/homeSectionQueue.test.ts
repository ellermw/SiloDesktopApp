import { describe, expect, it } from "vitest";
import {
  getPrioritizedHomeSectionIds,
  planNextHomeSectionBatch,
  planNextHomeSectionRequests,
} from "./homeSectionQueue";

describe("planNextHomeSectionRequests", () => {
  it("returns at most five prioritized section ids", () => {
    expect(
      planNextHomeSectionRequests({
        prioritizedIds: ["hero", "a", "b", "c", "d", "e", "f"],
        loadedIds: new Set(["a"]),
        inFlightIds: new Set(["b"]),
        limit: 5,
      }),
    ).toEqual(["hero", "c", "d", "e", "f"]);
  });

  it("prioritizes the featured section first and respects remaining concurrency slots", () => {
    expect(
      planNextHomeSectionBatch({
        layout: [
          {
            id: "hero",
            section_type: "recently_added",
            title: "Hero",
            featured: true,
            item_limit: 5,
            is_custom: false,
            customized: false,
          },
          {
            id: "row-1",
            section_type: "recently_added",
            title: "Row 1",
            featured: false,
            item_limit: 5,
            is_custom: false,
            customized: false,
          },
          {
            id: "row-2",
            section_type: "recently_added",
            title: "Row 2",
            featured: false,
            item_limit: 5,
            is_custom: false,
            customized: false,
          },
          {
            id: "row-3",
            section_type: "recently_added",
            title: "Row 3",
            featured: false,
            item_limit: 5,
            is_custom: false,
            customized: false,
          },
        ],
        loadedIds: new Set(),
        inFlightIds: new Set(["already-1", "already-2"]),
        maxConcurrentRequests: 5,
      }),
    ).toEqual(["hero", "row-1", "row-2"]);
  });

  it("derives prioritized ids with the featured section first", () => {
    expect(
      getPrioritizedHomeSectionIds([
        {
          id: "row-1",
          section_type: "recently_added",
          title: "Row 1",
          featured: false,
          item_limit: 5,
          is_custom: false,
          customized: false,
        },
        {
          id: "hero",
          section_type: "recently_added",
          title: "Hero",
          featured: true,
          item_limit: 5,
          is_custom: false,
          customized: false,
        },
      ]),
    ).toEqual(["hero", "row-1"]);
  });
});
