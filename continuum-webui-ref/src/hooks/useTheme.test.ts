import { describe, expect, it } from "vitest";
import { shouldLoadApiTheme } from "./themePreferences";

describe("shouldLoadApiTheme", () => {
  it("waits until auth bootstrap finishes before loading the API theme", () => {
    expect(shouldLoadApiTheme({ loading: true, user: null })).toBe(false);
    expect(shouldLoadApiTheme({ loading: true, user: { id: 1 } })).toBe(false);
  });

  it("only loads the API theme for authenticated users after bootstrap", () => {
    expect(shouldLoadApiTheme({ loading: false, user: null })).toBe(false);
    expect(shouldLoadApiTheme({ loading: false, user: { id: 1 } })).toBe(true);
  });
});
