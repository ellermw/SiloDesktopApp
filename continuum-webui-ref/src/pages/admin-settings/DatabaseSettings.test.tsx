import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it, vi } from "vitest";

import DatabaseSettings from "./DatabaseSettings";

const useSettingsFormMock = vi.fn();

vi.mock("@/hooks/useSettingsForm", () => ({
  useSettingsForm: (...args: unknown[]) => useSettingsFormMock(...args),
}));

function makeForm(redisUrl: string) {
  return {
    isLoading: false,
    getValue: (key: string) => {
      if (key === "redis.url") return redisUrl;
      return "";
    },
    setValue: vi.fn(),
    dirtyCount: 0,
    save: vi.fn(),
    discard: vi.fn(),
    isSaving: false,
    restartRequired: false,
    sensitiveConfigured: redisUrl ? ["redis.url"] : [],
  };
}

describe("DatabaseSettings", () => {
  it("shows Redis controls in the Database tab", () => {
    useSettingsFormMock.mockReturnValue(makeForm(""));

    const markup = renderToStaticMarkup(<DatabaseSettings />);

    expect(markup).toContain("Redis");
    expect(markup).toContain("Enable Redis");
    expect(markup).not.toContain("Connection URL");
  });

  it("shows the Redis connection URL when Redis is enabled", () => {
    useSettingsFormMock.mockReturnValue(makeForm("redis://cache:6379"));

    const markup = renderToStaticMarkup(<DatabaseSettings />);

    expect(markup).toContain("Enable Redis");
    expect(markup).toContain("Connection URL");
  });
});
