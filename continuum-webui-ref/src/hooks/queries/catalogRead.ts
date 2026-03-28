import { keepPreviousData, useQuery } from "@tanstack/react-query";

import { api } from "@/api/client";
import type {
  EpisodesResponse,
  FileVersion,
  ItemDetail,
  SeasonDetailResponse,
  SeasonsResponse,
} from "@/api/types";
import { catalogKeys } from "./keys";

export async function fetchCatalogItemDetail(id: string, options?: RequestInit): Promise<ItemDetail> {
  return api<ItemDetail>(`/catalog/items/${id}`, options);
}

export async function fetchCatalogItemVersions(
  id: string,
  options?: RequestInit,
): Promise<FileVersion[]> {
  return api<FileVersion[]>(`/catalog/items/${id}/versions`, options);
}

export async function fetchCatalogItemEpisodes(
  id: string,
  options?: RequestInit,
): Promise<EpisodesResponse> {
  return api<EpisodesResponse>(`/catalog/items/${id}/episodes`, options);
}

export async function fetchCatalogSeriesSeasons(
  seriesId: string,
  options?: RequestInit,
): Promise<SeasonsResponse> {
  return api<SeasonsResponse>(`/catalog/series/${seriesId}/seasons`, options);
}

export async function fetchCatalogSeasonDetail(
  seriesId: string,
  seasonNum: number,
  options?: RequestInit,
): Promise<SeasonDetailResponse> {
  return api<SeasonDetailResponse>(`/catalog/series/${seriesId}/seasons/${seasonNum}`, options);
}

export async function fetchCatalogSeasonEpisodes(
  seriesId: string,
  seasonNum: number,
  options?: RequestInit,
): Promise<EpisodesResponse> {
  return api<EpisodesResponse>(
    `/catalog/series/${seriesId}/seasons/${seasonNum}/episodes`,
    options,
  );
}

export function useCatalogItemDetail(id: string | undefined) {
  return useQuery({
    queryKey: catalogKeys.itemDetail(id!),
    queryFn: () => fetchCatalogItemDetail(id!),
    enabled: !!id,
    placeholderData: keepPreviousData,
  });
}

export function useCatalogItemVersions(id: string | undefined) {
  return useQuery({
    queryKey: catalogKeys.itemVersions(id!),
    queryFn: () => fetchCatalogItemVersions(id!),
    enabled: !!id,
    placeholderData: keepPreviousData,
  });
}

export function useCatalogItemEpisodes(id: string | undefined) {
  return useQuery({
    queryKey: catalogKeys.itemEpisodes(id!),
    queryFn: () => fetchCatalogItemEpisodes(id!),
    enabled: !!id,
    placeholderData: keepPreviousData,
  });
}

export function useCatalogSeriesSeasons(seriesId: string | undefined) {
  return useQuery({
    queryKey: catalogKeys.seriesSeasons(seriesId!),
    queryFn: () => fetchCatalogSeriesSeasons(seriesId!),
    enabled: !!seriesId,
    placeholderData: keepPreviousData,
  });
}

export function useCatalogSeasonDetail(seriesId: string | undefined, seasonNum: number) {
  return useQuery({
    queryKey: catalogKeys.seasonDetail(seriesId!, seasonNum),
    queryFn: () => fetchCatalogSeasonDetail(seriesId!, seasonNum),
    select: (data) => data.season,
    enabled: !!seriesId && seasonNum >= 0,
    placeholderData: keepPreviousData,
  });
}

export function useCatalogSeasonEpisodes(seriesId: string | undefined, seasonNum: number) {
  return useQuery({
    queryKey: catalogKeys.seasonEpisodes(seriesId!, seasonNum),
    queryFn: () => fetchCatalogSeasonEpisodes(seriesId!, seasonNum),
    enabled: !!seriesId && seasonNum >= 0,
    placeholderData: keepPreviousData,
  });
}
