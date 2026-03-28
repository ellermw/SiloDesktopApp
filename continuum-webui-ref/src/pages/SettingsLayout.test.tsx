import { renderToStaticMarkup } from "react-dom/server";
import { MemoryRouter } from "react-router";
import { describe, expect, it } from "vitest";

import SettingsLayout from "./SettingsLayout";

describe("SettingsLayout", () => {
  it("includes a back link to the main app shell", () => {
    const markup = renderToStaticMarkup(
      <MemoryRouter initialEntries={["/settings/playback"]}>
        <SettingsLayout />
      </MemoryRouter>,
    );

    expect(markup).toContain('href="/"');
    expect(markup).toContain(">Back<");
  });
});
