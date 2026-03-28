import { renderToStaticMarkup } from "react-dom/server";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

const mocks = vi.hoisted(() => ({
  useAdminLibraries: vi.fn(),
  useSkippedLibraryRoots: vi.fn(),
  useCheckLibraryMount: vi.fn(),
  useCreateLibrary: vi.fn(),
  useUpdateLibrary: vi.fn(),
  useDeleteLibrary: vi.fn(),
  useScanLibrary: vi.fn(),
  useScanAllLibraries: vi.fn(),
  useRefreshLibraryMetadata: vi.fn(),
  useConfirmEmptyRootCleanup: vi.fn(),
  useLibraryProviders: vi.fn(),
  useSetLibraryProviders: vi.fn(),
  useUploadLibraryPoster: vi.fn(),
  useDeleteLibraryPoster: vi.fn(),
  useAdminProviders: vi.fn(),
}));

vi.mock("@/hooks/queries/admin/libraries", () => ({
  useAdminLibraries: (...args: unknown[]) => mocks.useAdminLibraries(...args),
  useSkippedLibraryRoots: (...args: unknown[]) => mocks.useSkippedLibraryRoots(...args),
  useCheckLibraryMount: (...args: unknown[]) => mocks.useCheckLibraryMount(...args),
  useCreateLibrary: (...args: unknown[]) => mocks.useCreateLibrary(...args),
  useUpdateLibrary: (...args: unknown[]) => mocks.useUpdateLibrary(...args),
  useDeleteLibrary: (...args: unknown[]) => mocks.useDeleteLibrary(...args),
  useScanLibrary: (...args: unknown[]) => mocks.useScanLibrary(...args),
  useScanAllLibraries: (...args: unknown[]) => mocks.useScanAllLibraries(...args),
  useRefreshLibraryMetadata: (...args: unknown[]) => mocks.useRefreshLibraryMetadata(...args),
  useConfirmEmptyRootCleanup: (...args: unknown[]) => mocks.useConfirmEmptyRootCleanup(...args),
  useLibraryProviders: (...args: unknown[]) => mocks.useLibraryProviders(...args),
  useSetLibraryProviders: (...args: unknown[]) => mocks.useSetLibraryProviders(...args),
  useUploadLibraryPoster: (...args: unknown[]) => mocks.useUploadLibraryPoster(...args),
  useDeleteLibraryPoster: (...args: unknown[]) => mocks.useDeleteLibraryPoster(...args),
}));

vi.mock("@/hooks/queries/admin/providers", () => ({
  useAdminProviders: (...args: unknown[]) => mocks.useAdminProviders(...args),
}));

import AdminLibraries from "./AdminLibraries";

describe("AdminLibraries", () => {
  beforeEach(() => {
    const mutate = vi.fn();
    const queryState = {
      mutate,
      isPending: false,
      variables: undefined,
    };

    mocks.useAdminLibraries.mockReturnValue({
      data: [
        {
          id: 1,
          name: "Movies",
          paths: ["/media/movies"],
          type: "movies",
          enabled: true,
          last_scanned_at: null,
          scan_warning_code: "empty_root",
          scan_warning_at: null,
          scan_warning_message: null,
        },
      ],
      isLoading: false,
    });
    mocks.useCheckLibraryMount.mockReturnValue(queryState);
    mocks.useSkippedLibraryRoots.mockReturnValue({
      data: [],
      isLoading: false,
    });
    mocks.useCreateLibrary.mockReturnValue(queryState);
    mocks.useUpdateLibrary.mockReturnValue(queryState);
    mocks.useDeleteLibrary.mockReturnValue(queryState);
    mocks.useScanLibrary.mockReturnValue(queryState);
    mocks.useScanAllLibraries.mockReturnValue(queryState);
    mocks.useRefreshLibraryMetadata.mockReturnValue(queryState);
    mocks.useConfirmEmptyRootCleanup.mockReturnValue(queryState);
    mocks.useLibraryProviders.mockReturnValue({
      data: [],
      isLoading: false,
    });
    mocks.useSetLibraryProviders.mockReturnValue(queryState);
    mocks.useUploadLibraryPoster.mockReturnValue(queryState);
    mocks.useDeleteLibraryPoster.mockReturnValue(queryState);
    mocks.useAdminProviders.mockReturnValue({
      data: [],
      isLoading: false,
    });
  });

  it("uses scan language instead of metadata refresh language on the admin libraries page", () => {
    const markup = renderToStaticMarkup(
      <MemoryRouter>
        <AdminLibraries />
      </MemoryRouter>,
    );

    expect(markup).toContain("Manage library roots and scans. Catalog import/export now lives under Maintenance.");
    expect(markup).toContain('title="Scan Library"');
    expect(markup).toContain("Scan All");
    expect(markup).toContain('title="Refresh metadata"');
    expect(markup).toContain(
      "Run another scan after storage returns, or confirm deletion before the next empty-root scan.",
    );
  });

  it("renders a low-key troubleshooting section only when skipped roots exist", () => {
    mocks.useSkippedLibraryRoots.mockReturnValue({
      data: [
        {
          library_id: 1,
          library_name: "Movies",
          root_path: "/media/movies/Inception (2010)",
          reason: "missing_folder_ids",
          sample_file_path: "/media/movies/Inception (2010)/Inception (2010).mkv",
          file_count: 1,
          first_seen_at: "2026-03-23T20:00:00Z",
          last_seen_at: "2026-03-23T21:00:00Z",
        },
      ],
      isLoading: false,
    });

    const markup = renderToStaticMarkup(
      <MemoryRouter>
        <AdminLibraries />
      </MemoryRouter>,
    );

    expect(markup).toContain("Troubleshooting");
    expect(markup).toContain("Skipped roots");
    expect(markup).toContain("Movies");
    expect(markup).toContain("/media/movies/Inception (2010)");
    expect(markup).toContain("missing_folder_ids");
    expect(markup).toContain("First seen");
    expect(markup).toContain("Last seen");
  });

  it("hides the troubleshooting section when no skipped roots exist", () => {
    const markup = renderToStaticMarkup(
      <MemoryRouter>
        <AdminLibraries />
      </MemoryRouter>,
    );

    expect(markup).not.toContain("Skipped roots");
  });
});
