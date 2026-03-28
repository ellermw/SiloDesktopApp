import { useCallback, useRef, useState } from "react";
import { createEmptyQueryDefinition, type QueryDefinition } from "@/api/types";
import { useCatalogWindow } from "@/hooks/queries/catalog";
import ItemGrid from "@/components/ItemGrid";
import ScrollToTopButton from "@/components/ScrollToTopButton";
import AdvancedFilterBar from "@/components/AdvancedFilterBar";
import type { AdvancedFilters } from "@/components/advancedFilterOptions";

function normalizeCatalogMediaScope(type: string): QueryDefinition["media_scope"] {
  return type === "movie" || type === "series" ? type : undefined;
}

function normalizeCatalogSort(sort: string): QueryDefinition["sort"]["field"] {
  switch (sort) {
    case "sort_title":
      return "title";
    case "year":
    case "rating_imdb":
    case "release_date":
    case "relevance":
      return sort;
    default:
      return "added_at";
  }
}

interface LibraryBrowseProps {
  libraryId: number;
  libraryType: string;
  filters: AdvancedFilters;
  onFiltersChange: (filters: AdvancedFilters) => void;
}

export default function LibraryBrowse({
  libraryId,
  libraryType,
  filters,
  onFiltersChange,
}: LibraryBrowseProps) {
  const limit = 60;
  const filtersKey = JSON.stringify({
    content_rating: filters.content_rating,
    genre: filters.genre,
    order: filters.order,
    sort: filters.sort,
    type: filters.type,
    year_max: filters.year_max,
    year_min: filters.year_min,
  });

  const [visibleRangeState, setVisibleRangeState] = useState<{
    key: string;
    range: [number, number];
  }>({
    key: filtersKey,
    range: [0, limit - 1],
  });
  const visibleRange =
    visibleRangeState.key === filtersKey ? visibleRangeState.range : ([0, limit - 1] as [number, number]);

  const debounceRef = useRef<ReturnType<typeof setTimeout>>(undefined);
  const handleVisibleRangeChange = useCallback((start: number, end: number) => {
    clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => {
      setVisibleRangeState({ key: filtersKey, range: [start, end] });
    }, 50);
  }, [filtersKey]);

  const query_definition = createEmptyQueryDefinition();
  query_definition.library_ids = [libraryId];
  query_definition.media_scope = normalizeCatalogMediaScope(filters.type);
  query_definition.sort = {
    field: normalizeCatalogSort(filters.sort),
    order: filters.order as "asc" | "desc",
  };
  query_definition.groups = [
    ...(filters.genre !== "all"
      ? [{ match: "all" as const, rules: [{ field: "genre", op: "contains", value: filters.genre }] }]
      : []),
    ...((filters.year_min || filters.year_max)
      ? [
          {
            match: "all" as const,
            rules: [
              ...(filters.year_min
                ? [{ field: "year", op: "gte", value: Number(filters.year_min) }]
                : []),
              ...(filters.year_max
                ? [{ field: "year", op: "lte", value: Number(filters.year_max) }]
                : []),
            ],
          },
        ]
      : []),
    ...(filters.content_rating !== "all"
      ? [
          {
            match: "all" as const,
            rules: [{ field: "content_rating", op: "is", value: filters.content_rating }],
          },
        ]
      : []),
  ];

  const catalogQuery = useCatalogWindow(
    {
      source: "query",
      library_id: libraryId,
      query_definition,
    },
    {
      limit,
      visibleRange,
    },
  );
  const totalItems = catalogQuery.data?.totalItems ?? 0;
  const pages = catalogQuery.data?.pages ?? new Map();
  const isLoading = catalogQuery.isLoading;

  const showTypeFilter = libraryType === "mixed";

  return (
    <div className="space-y-5 py-2 sm:space-y-6">
      <AdvancedFilterBar
        filters={filters}
        onChange={onFiltersChange}
        showTypeFilter={showTypeFilter}
      />
      <ItemGrid
        totalItems={totalItems}
        pages={pages}
        pageSize={limit}
        loading={isLoading}
        onVisibleRangeChange={handleVisibleRangeChange}
        sortField={filters.sort}
      />
      <ScrollToTopButton />
    </div>
  );
}
