import { useEffect } from "react";
import { Link, useParams, useSearchParams } from "react-router";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { useUserLibraries } from "@/hooks/queries/libraries";
import { useDocumentTitle } from "@/hooks/useDocumentTitle";
import type { AdvancedFilters } from "@/components/advancedFilterOptions";
import LibraryRecommended from "./LibraryRecommended";
import LibraryBrowse from "./LibraryBrowse";
import LibraryCollections from "./LibraryCollections";
import { parseLibraryPageState, updateLibraryPageSearchParams } from "./libraryPageSearchParams";

export default function LibraryPage() {
  const { libraryId } = useParams<{ libraryId: string }>();
  const [searchParams, setSearchParams] = useSearchParams();
  const { data: libraries, isLoading } = useUserLibraries();

  const id = Number(libraryId);
  const library = libraries?.find((l) => l.id === id);
  const libraryType = library?.type ?? "";
  const { activeTab, filters } = parseLibraryPageState(searchParams, libraryType);

  useDocumentTitle(library?.name ?? "Library");

  useEffect(() => {
    const normalizedSearchParams = updateLibraryPageSearchParams(
      searchParams,
      { activeTab, filters },
      libraryType,
    );

    if (normalizedSearchParams.toString() !== searchParams.toString()) {
      setSearchParams(normalizedSearchParams, { replace: true });
    }
  }, [activeTab, filters, libraryType, searchParams, setSearchParams]);

  const handleTabChange = (value: string) => {
    const nextSearchParams = updateLibraryPageSearchParams(
      searchParams,
      {
        activeTab:
          value === "library" ? "library" : value === "collections" ? "collections" : "recommended",
        filters,
      },
      libraryType,
    );
    setSearchParams(nextSearchParams);
  };

  const handleFiltersChange = (nextFilters: AdvancedFilters) => {
    const nextSearchParams = updateLibraryPageSearchParams(
      searchParams,
      {
        activeTab: "library",
        filters: nextFilters,
      },
      libraryType,
    );
    setSearchParams(nextSearchParams);
  };

  if (isLoading) {
    return (
      <div className="flex h-full items-center justify-center">
        <div className="border-primary h-8 w-8 animate-spin rounded-full border-b-2" />
      </div>
    );
  }

  if (!library) {
    return (
      <div className="text-muted-foreground flex h-full flex-col items-center justify-center gap-3 px-6 text-center">
        <p>This library is hidden or unavailable for your account.</p>
        <Link to="/settings/libraries" className="text-primary text-sm font-medium hover:underline">
          Manage library visibility in Settings
        </Link>
      </div>
    );
  }

  return (
    <div className="h-full px-4 py-4 sm:px-6 sm:py-6 lg:px-10 xl:px-12">
      <Tabs value={activeTab} onValueChange={handleTabChange}>
        <div className="mb-5">
          <div className="page-header gap-5">
            <div className="space-y-3">
              <h1 className="page-title text-[clamp(2rem,5vw,3.4rem)]">{library?.name ?? "Library"}</h1>
              <p className="page-subtitle text-sm sm:text-base">
                Move between recommendations, the full archive, and curated collections.
              </p>
            </div>
            <TabsList className="surface-panel-subtle h-auto gap-1 rounded-[1.2rem] border-0 bg-transparent p-1">
              <TabsTrigger
                value="recommended"
                className="rounded-xl border-transparent px-4 py-2.5 text-[13px] font-semibold data-[state=active]:bg-accent data-[state=active]:text-primary data-[state=active]:shadow-none"
              >
                Recommended
              </TabsTrigger>
              <TabsTrigger
                value="library"
                className="rounded-xl border-transparent px-4 py-2.5 text-[13px] font-semibold data-[state=active]:bg-accent data-[state=active]:text-primary data-[state=active]:shadow-none"
              >
                Library
              </TabsTrigger>
              <TabsTrigger
                value="collections"
                className="rounded-xl border-transparent px-4 py-2.5 text-[13px] font-semibold data-[state=active]:bg-accent data-[state=active]:text-primary data-[state=active]:shadow-none"
              >
                Collections
              </TabsTrigger>
            </TabsList>
          </div>
        </div>
        <TabsContent value="recommended" className="mt-0">
          <LibraryRecommended libraryId={id} />
        </TabsContent>
        <TabsContent value="library" className="mt-0">
          <LibraryBrowse
            libraryId={id}
            libraryType={libraryType || "mixed"}
            filters={filters}
            onFiltersChange={handleFiltersChange}
          />
        </TabsContent>
        <TabsContent value="collections" className="mt-0">
          <LibraryCollections libraryId={id} />
        </TabsContent>
      </Tabs>
    </div>
  );
}
