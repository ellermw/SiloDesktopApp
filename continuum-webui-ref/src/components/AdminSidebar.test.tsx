import { renderToStaticMarkup } from "react-dom/server";
import { MemoryRouter } from "react-router";
import { describe, expect, it } from "vitest";
import AdminSidebar from "./AdminSidebar";

describe("AdminSidebar", () => {
  it("includes a Sections link in the manage navigation", () => {
    const markup = renderToStaticMarkup(
      <MemoryRouter initialEntries={["/admin"]}>
        <AdminSidebar />
      </MemoryRouter>,
    );

    expect(markup).toContain('href="/admin/sections"');
    expect(markup).toContain(">Sections<");
  });

  it("includes a Maintenance link in the server navigation", () => {
    const markup = renderToStaticMarkup(
      <MemoryRouter initialEntries={["/admin"]}>
        <AdminSidebar />
      </MemoryRouter>,
    );

    expect(markup).toContain('href="/admin/maintenance"');
    expect(markup).toContain(">Maintenance<");
  });

  it("includes a Recommendations link in the server navigation", () => {
    const markup = renderToStaticMarkup(
      <MemoryRouter initialEntries={["/admin"]}>
        <AdminSidebar />
      </MemoryRouter>,
    );

    expect(markup).toContain('href="/admin/recommendations"');
    expect(markup).toContain(">Recommendations<");
  });
});
