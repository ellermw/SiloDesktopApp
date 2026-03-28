import type { ReactNode } from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { MemoryRouter, Route, Routes } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

import AppSidebar from "./AppSidebar";
import { getProfileMenuSide, isSidebarExpanded } from "./AppSidebar.logic";

const mockLogout = vi.fn();
const mockClearProfile = vi.fn();
const mockTogglePin = vi.fn();

vi.mock("@/hooks/useAuth", () => ({
  useAuth: () => ({
    user: { id: 1, username: "nathan", role: "admin" },
    logout: mockLogout,
    clearProfile: mockClearProfile,
  }),
}));

vi.mock("@/hooks/useCurrentProfile", () => ({
  useCurrentProfile: () => ({
    profile: { name: "Nathan" },
  }),
}));

vi.mock("@/hooks/queries/libraries", () => ({
  useUserLibraries: () => ({
    data: [{ id: 7, name: "Movies", type: "movies" }],
  }),
}));

vi.mock("@/hooks/queries/sidebarPins", () => ({
  useSidebarPins: () => ({
    pins: {
      "7": [{ type: "section", id: "featured", label: "Featured" }],
    },
  }),
  useToggleSidebarPin: () => ({
    togglePin: mockTogglePin,
  }),
}));

vi.mock("@/hooks/useViewTransition", () => ({
  useViewTransitionNavigate: () => vi.fn(),
}));

vi.mock("@/components/ThemeSwitcher", () => ({
  default: () => <div>Theme switcher</div>,
}));

vi.mock("@/components/ui/avatar", () => ({
  Avatar: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  AvatarFallback: ({ children }: { children: ReactNode }) => <div>{children}</div>,
}));

vi.mock("@/components/ui/dropdown-menu", () => ({
  DropdownMenu: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  DropdownMenuTrigger: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  DropdownMenuContent: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  DropdownMenuItem: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  DropdownMenuLabel: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  DropdownMenuSeparator: () => <hr />,
}));

function renderSidebar(entry: string, { collapsed = false }: { collapsed?: boolean } = {}) {
  return renderToStaticMarkup(
    <MemoryRouter initialEntries={[entry]}>
      <Routes>
        <Route path="*" element={<AppSidebar collapsed={collapsed} />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe("AppSidebar", () => {
  beforeEach(() => {
    mockLogout.mockReset();
    mockClearProfile.mockReset();
    mockTogglePin.mockReset();
  });

  it("uses the cinema highlight text color for active catalog source links", () => {
    const markup = renderSidebar("/catalog?source=query&q=heat");

    expect(markup).toContain("text-sidebar-accent-foreground bg-sidebar-accent");
    expect(markup).not.toContain("text-sidebar-primary-foreground bg-sidebar-accent");
  });

  it("uses the cinema highlight text color for active pinned catalog destinations", () => {
    const markup = renderSidebar(
      "/catalog?source=section&scope=library&library_id=7&section_id=featured&title=Featured",
    );

    expect(markup).toContain("text-sidebar-accent-foreground bg-sidebar-accent");
    expect(markup).not.toContain("text-sidebar-primary-foreground bg-sidebar-accent");
  });

  it("keeps collapsed navigation rows left-anchored instead of centering icons", () => {
    const markup = renderSidebar("/item/42", { collapsed: true });

    expect(markup).toContain(
      'href="/" class="relative flex items-center gap-2.5 rounded-xl px-3 py-3',
    );
  });

  it("preserves section header slots when collapsed so nav groups do not shift upward", () => {
    const markup = renderSidebar("/item/42", { collapsed: true });

    expect(markup).toContain("Libraries");
    expect(markup).toContain("Discover");
    expect(markup).toContain("Your Stuff");
  });

  it("keeps the collapsed sidebar expanded while the profile menu is open without changing menu side", () => {
    expect(isSidebarExpanded(true, false, true)).toBe(true);
    expect(getProfileMenuSide(true)).toBe("right");
  });

  it("centers the profile trigger when the sidebar is collapsed", () => {
    const markup = renderSidebar("/item/42", { collapsed: true });

    expect(markup).toContain("mx-auto h-10 w-10 justify-center px-0");
  });
});
