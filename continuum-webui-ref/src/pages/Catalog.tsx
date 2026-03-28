import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useSearchParams } from "react-router";
import { Search } from "lucide-react";

import ItemGrid from "@/components/ItemGrid";
import CatalogFiltersPanel from "@/components/catalog/CatalogFiltersPanel";
import { useCatalogWindow } from "@/hooks/queries/catalog";
import { useDocumentTitle } from "@/hooks/useDocumentTitle";
import SearchBar from "@/components/SearchBar";

import { buildCatalogApiSearchParams, parseCatalogSearchParams } from "./catalogSearchParams";

function defaultCatalogTitle(source: string, searchQuery?: string) {
  if (source === "favorites") return "Favorites";
  if (source === "watchlist") return "Watchlist";
  if (source === "history") return "History";
  if (searchQuery) return `Results for "${searchQuery}"`;
  return "Catalog";
}

export default function Catalog() {
  const [searchParams, setSearchParams] = useSearchParams();
  const state = useMemo(() => parseCatalogSearchParams(searchParams), [searchParams]);
  const emptySearchTitle =
    state.source === "query" && !state.q ? "Search" : defaultCatalogTitle(state.source, state.q);

  useDocumentTitle(emptySearchTitle);

  if (state.source === "query" && !state.q) {
    return (
      <section className="page-shell flex min-h-[calc(100dvh-10rem)] flex-col items-center justify-center py-16 text-center">
        <div className="text-muted-foreground mb-6">
          <Search className="h-10 w-10" strokeWidth={1.5} />
        </div>
        <h1 className="page-title mb-4">Search</h1>
        <p className="page-subtitle mb-8 max-w-xl text-sm sm:text-base">
          Find films, series, performances, and rediscover things you forgot you saved.
        </p>
        <SearchBar autoFocus prominent />
      </section>
    );
  }

  return (
    <CatalogResults searchParams={searchParams} setSearchParams={setSearchParams} state={state} />
  );
}

function CatalogResults({
  searchParams,
  setSearchParams,
  state,
}: {
  searchParams: URLSearchParams;
  setSearchParams: (nextInit: URLSearchParams) => void;
  state: ReturnType<typeof parseCatalogSearchParams>;
}) {
  const limit = 60;
  const searchKey = searchParams.toString();
  const [visibleRangeState, setVisibleRangeState] = useState<{
    key: string;
    range: [number, number];
  }>({
    key: searchKey,
    range: [0, limit - 1],
  });
  const visibleRange =
    visibleRangeState.key === searchKey ? visibleRangeState.range : ([0, limit - 1] as [number, number]);
  const debounceRef = useRef<ReturnType<typeof setTimeout>>(undefined);
  const handleVisibleRangeChange = useCallback((start: number, end: number) => {
    clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => {
      setVisibleRangeState({ key: searchKey, range: [start, end] });
    }, 50);
  }, [searchKey]);

  useEffect(() => () => clearTimeout(debounceRef.current), []);

  const catalogQuery = useCatalogWindow(state, { limit, visibleRange });
  const title =
    catalogQuery.data?.title ?? state.title ?? defaultCatalogTitle(state.source, state.q);

  useDocumentTitle(title);

  return (
    <div className="page-shell space-y-6 py-4 sm:py-6">
      <header className="page-header">
        <div className="space-y-3">
          <h1 className="page-title text-[clamp(2rem,5vw,3.5rem)]">{title}</h1>
          <p className="page-subtitle text-sm sm:text-base">
            Refine the archive by type, era, rating, or genre.
          </p>
        </div>
        <div className="surface-panel-subtle min-w-[180px] rounded-[1.35rem] px-5 py-4 text-right">
          <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-muted-foreground">
            Available now
          </p>
          <p className="mt-2 text-2xl font-semibold tracking-tight">
            {catalogQuery.data?.totalItems ?? 0}
          </p>
          <p className="text-muted-foreground text-xs">
            item{(catalogQuery.data?.totalItems ?? 0) === 1 ? "" : "s"}
          </p>
        </div>
      </header>

      <CatalogFiltersPanel
        state={state}
        onStateChange={(nextState) => {
          const nextSearchParams = buildCatalogApiSearchParams(nextState);
          if (nextSearchParams.toString() !== searchParams.toString()) {
            setSearchParams(nextSearchParams);
          }
        }}
      />

      <ItemGrid
        totalItems={catalogQuery.data?.totalItems ?? 0}
        pages={catalogQuery.data?.pages ?? new Map()}
        pageSize={limit}
        loading={catalogQuery.isLoading}
        onVisibleRangeChange={handleVisibleRangeChange}
      />
    </div>
  );
}
