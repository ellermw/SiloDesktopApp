import { describe, expect, it } from "vitest";
import {
  createSidebarPinsOptimisticMutation,
  parseSidebarPins,
  rollbackSidebarPinsOptimisticMutation,
  toggleSidebarPins,
} from "./sidebarPins";

describe("sidebar pin helpers", () => {
  it("parses invalid values as an empty pin map", () => {
    expect(parseSidebarPins(null)).toEqual({});
    expect(parseSidebarPins("not-json")).toEqual({});
    expect(parseSidebarPins("[]")).toEqual({});
  });

  it("adds a new pin to the target library", () => {
    expect(
      toggleSidebarPins({}, 42, { type: "collection", id: "col-1", label: "Pinned Horror" }),
    ).toEqual({
      "42": [{ type: "collection", id: "col-1", label: "Pinned Horror" }],
    });
  });

  it("removes an existing pin from the target library only", () => {
    expect(
      toggleSidebarPins(
        {
          "42": [
            { type: "collection", id: "col-1", label: "Pinned Horror" },
            { type: "section", id: "sec-1", label: "Recently Added" },
          ],
          "99": [{ type: "collection", id: "col-2", label: "Other Library" }],
        },
        42,
        { type: "collection", id: "col-1", label: "Pinned Horror" },
      ),
    ).toEqual({
      "42": [{ type: "section", id: "sec-1", label: "Recently Added" }],
      "99": [{ type: "collection", id: "col-2", label: "Other Library" }],
    });
  });

  it("rolls back the latest optimistic mutation when its revision is still current", () => {
    const mutation = createSidebarPinsOptimisticMutation({
      currentRaw: null,
      currentRevision: null,
      libraryId: 42,
      pin: { type: "collection", id: "col-1", label: "Pinned Horror" },
      revision: 1,
    });

    expect(
      rollbackSidebarPinsOptimisticMutation({
        currentRevision: 1,
        mutation,
      }),
    ).toEqual({
      raw: null,
      revision: null,
    });
  });

  it("does not roll back over a newer optimistic mutation revision", () => {
    const firstMutation = createSidebarPinsOptimisticMutation({
      currentRaw: null,
      currentRevision: null,
      libraryId: 42,
      pin: { type: "collection", id: "col-1", label: "Pinned Horror" },
      revision: 1,
    });
    createSidebarPinsOptimisticMutation({
      currentRaw: firstMutation.optimisticRaw,
      currentRevision: 1,
      libraryId: 42,
      pin: { type: "section", id: "sec-1", label: "Recently Added" },
      revision: 2,
    });

    expect(
      rollbackSidebarPinsOptimisticMutation({
        currentRevision: 2,
        mutation: firstMutation,
      }),
    ).toBeNull();
  });
});
