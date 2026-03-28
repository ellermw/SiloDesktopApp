import { useMemo } from "react";
import { useQueries, useQuery } from "@tanstack/react-query";

import { api } from "@/api/client";
import type { CatalogFiltersResponse, CatalogResponse } from "@/api/types";
import type { CatalogParams } from "@/hooks/queries/keys";
import { catalogKeys } from "@/hooks/queries/keys";
import { createEmptyQueryDefinition, type CatalogSource } from "@/api/types";
import {
  buildCatalogApiSearchParams,
  catalogSourceAllowsOverlay,
  type CatalogSearchState,
} from "@/pages/catalogSearchParams";

function catalogParamsForKey(state: CatalogSearchState, limit: number): CatalogParams {
  return {
    source: state.source,
    q: state.q,
    title: state.title,
    scope: state.scope,
    section_id: state.section_id,
    library_id: state.library_id,
    collection_id: state.collection_id,
    person_id: state.person_id,
    query_fingerprint: JSON.stringify(state.query_definition),
    limit,
  };
}

function buildCatalogUrl(state: CatalogSearchState, limit: number, offset: number): string {
  const params = buildCatalogApiSearchParams(state);
  params.set("limit", String(limit));
  params.set("offset", String(offset));
  return `/catalog?${params.toString()}`;
}

function buildCatalogFiltersUrlWithOptions(
  state: CatalogSearchState,
  options: { includeTechnical?: boolean } = {},
): string {
  const params = buildCatalogApiSearchParams(state);
  if (options.includeTechnical === false) {
    params.set("include_technical", "false");
  }
  return `/catalog/filters?${params.toString()}`;
}

export async function fetchCatalogPage(
  state: CatalogSearchState,
  limit: number,
  offset: number,
  options?: RequestInit,
): Promise<CatalogResponse> {
  return api<CatalogResponse>(buildCatalogUrl(state, limit, offset), options);
}

export async function fetchCatalogFilters(
  state: CatalogSearchState,
  options?: RequestInit,
  requestOptions: { includeTechnical?: boolean } = {},
): Promise<CatalogFiltersResponse> {
  return api<CatalogFiltersResponse>(
    buildCatalogFiltersUrlWithOptions(state, requestOptions),
    options,
  );
}

export function createCatalogSearchState(
  source: CatalogSource,
  patch: Partial<CatalogSearchState> = {},
): CatalogSearchState {
  return {
    source,
    query_definition: createEmptyQueryDefinition(),
    ...patch,
  };
}

export function useCatalogWindow(
  state: CatalogSearchState,
  options: { limit?: number; visibleRange?: [number, number] } = {},
) {
  const limit = options.limit ?? 60;
  const params = catalogParamsForKey(state, limit);
  const visibleRange = options.visibleRange ?? [0, limit - 1];
  const bufferPages = state.source === "query" && state.q ? 0 : 1;
  const startPage = Math.max(0, Math.floor(visibleRange[0] / limit) - bufferPages);
  const endPage = Math.floor(visibleRange[1] / limit) + bufferPages;

  const pageIndices = useMemo(() => {
    const indices = new Set<number>();
    indices.add(0);
    for (let page = startPage; page <= endPage; page++) {
      indices.add(page);
    }
    return Array.from(indices).sort((a, b) => a - b);
  }, [endPage, startPage]);

  const queryResults = useQueries({
    queries: pageIndices.map((pageIndex) => {
      const offset = pageIndex * limit;
      return {
        queryKey: catalogKeys.list({
          ...params,
          limit,
          offset,
        }),
        queryFn: ({ signal }: { signal: AbortSignal }) =>
          fetchCatalogPage(state, limit, offset, { signal }),
        staleTime: 10 * 60 * 1000,
      };
    }),
  });

  const page0Result = queryResults[0];
  const title =
    page0Result?.data?.title ??
    queryResults.find((result) => result.data?.title)?.data?.title ??
    state.title;
  const totalItems = page0Result?.data?.total ?? 0;
  const isLoading = page0Result?.isLoading ?? true;

  const pages = useMemo(() => {
    const map = new Map<number, CatalogResponse["items"]>();
    pageIndices.forEach((pageIndex, queryIndex) => {
      const data = queryResults[queryIndex]?.data;
      if (data) {
        map.set(pageIndex, data.items);
      }
    });
    return map;
  }, [pageIndices, queryResults]);

  return {
    data: {
      title,
      totalItems,
      pages,
    },
    isLoading,
  };
}

export function useCatalogFilters(
  state: CatalogSearchState,
  options: { enabled?: boolean; includeTechnical?: boolean } = {},
) {
  const params = catalogParamsForKey(state, 0);
  const enabled = options.enabled ?? true;
  const includeTechnical = options.includeTechnical ?? true;

  return useQuery({
    queryKey: catalogKeys.filters({
      source: params.source,
      q: params.q,
      title: params.title,
      scope: params.scope,
      section_id: params.section_id,
      library_id: params.library_id,
      collection_id: params.collection_id,
      person_id: state.person_id,
      query_fingerprint: params.query_fingerprint,
      include_technical: includeTechnical,
    }),
    queryFn: ({ signal }) => fetchCatalogFilters(state, { signal }, { includeTechnical }),
    enabled: enabled && catalogSourceAllowsOverlay(state.source),
    staleTime: 5 * 60 * 1000,
  });
}

export function useCatalogMetadataFilters() {
  return useCatalogFilters(createCatalogSearchState("query"), { includeTechnical: false });
}
