import { describe, expect, it } from "vitest";
import { sectionKeys } from "./keys";

describe("sectionKeys", () => {
  it("builds stable home progressive keys", () => {
    expect(sectionKeys.homeLayout()).toEqual(["sections", "home", "layout"]);
    expect(sectionKeys.homeItems("hero")).toEqual(["sections", "home", "items", "hero"]);
  });
});
