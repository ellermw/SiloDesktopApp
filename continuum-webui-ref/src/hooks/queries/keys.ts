export interface BrowseParams {
  q: string;
  type: string;
  sort: string;
  order: string;
  offset: number;
  limit: number;
  library_id?: number;
  genre?: string;
  year_min?: number;
  year_max?: number;
  content_rating?: string;
}

export interface InfiniteBrowseParams {
  q: string;
  type: string;
  sort: string;
  order: string;
  limit: number;
  library_id?: number;
  genre?: string;
  year_min?: number;
  year_max?: number;
  content_rating?: string;
}

export interface CatalogParams {
  source: string;
  q?: string;
  title?: string;
  scope?: string;
  section_id?: string;
  library_id?: number;
  collection_id?: string;
  person_id?: string;
  query_fingerprint?: string;
  include_technical?: boolean;
  limit: number;
  offset?: number;
}

export const itemKeys = {
  all: ["items"] as const,
  details: () => ["items", "detail"] as const,
  detail: (id: string) => ["items", "detail", id] as const,
  watchDetail: (id: string, fileId?: number) =>
    ["items", "watchDetail", id, fileId ?? "default"] as const,
  browse: (params: BrowseParams) => ["items", "browse", params] as const,
  infiniteBrowse: (params: InfiniteBrowseParams) => ["items", "infiniteBrowse", params] as const,
  filters: () => ["items", "filters"] as const,
};

export const catalogKeys = {
  all: ["catalog"] as const,
  list: (params: CatalogParams) => ["catalog", "list", params] as const,
  filters: (params: Omit<CatalogParams, "limit">) => ["catalog", "filters", params] as const,
  itemDetail: (id: string) => ["catalog", "items", id, "detail"] as const,
  itemVersions: (id: string) => ["catalog", "items", id, "versions"] as const,
  itemEpisodes: (id: string) => ["catalog", "items", id, "episodes"] as const,
  seriesSeasons: (seriesId: string) => ["catalog", "series", seriesId, "seasons"] as const,
  seasonDetail: (seriesId: string, seasonNum: number) =>
    ["catalog", "series", seriesId, "seasons", seasonNum, "detail"] as const,
  seasonEpisodes: (seriesId: string, seasonNum: number) =>
    ["catalog", "series", seriesId, "seasons", seasonNum, "episodes"] as const,
};

export const favoriteKeys = {
  all: ["favorites"] as const,
  list: () => ["favorites", "list"] as const,
  check: (itemId: string) => ["favorites", "check", itemId] as const,
};

export const watchlistKeys = {
  all: ["watchlist"] as const,
  list: () => ["watchlist", "list"] as const,
  check: (itemId: string) => ["watchlist", "check", itemId] as const,
};

export const historyKeys = {
  all: ["history"] as const,
  list: () => ["history", "list"] as const,
};

export const collectionKeys = {
  all: ["collections"] as const,
  list: () => ["collections", "list"] as const,
  items: (collectionId: string) => ["collections", "items", collectionId] as const,
  preview: (scope: "user" | "admin", fingerprint: string) =>
    ["collections", "preview", scope, fingerprint] as const,
};

export const libraryCollectionKeys = {
  all: ["libraryCollections"] as const,
  list: (libraryId: number) => ["libraryCollections", "list", libraryId] as const,
  items: (libraryId: number, collectionId: string) =>
    ["libraryCollections", "items", libraryId, collectionId] as const,
};

export const profileKeys = {
  all: ["profiles"] as const,
  list: () => ["profiles", "list"] as const,
};

export const personKeys = {
  all: ["people"] as const,
  detail: (id: string) => ["people", "detail", id] as const,
  catalog: (
    id: string,
    options: {
      type?: string;
      limit?: number;
      offset?: number;
    } = {},
  ) =>
    [
      "people",
      "catalog",
      id,
      options.type ?? "all",
      options.limit ?? 24,
      options.offset ?? 0,
    ] as const,
};

export const episodeKeys = {
  all: ["episodes"] as const,
  seasons: (seriesId: string) => ["episodes", "seasons", seriesId] as const,
  seasonDetail: (seriesId: string, seasonNum: number) =>
    ["episodes", "seasons", seriesId, seasonNum, "detail"] as const,
  bySeason: (seriesId: string, seasonNum: number) => ["episodes", seriesId, seasonNum] as const,
  byItem: (itemId: string) => ["episodes", "item", itemId] as const,
};

export const libraryKeys = {
  all: ["libraries"] as const,
  user: () => ["libraries", "user"] as const,
};

export const libraryPlaybackPreferenceKeys = {
  all: ["libraryPlaybackPreferences"] as const,
  list: (profileId?: string | null) =>
    ["libraryPlaybackPreferences", "list", profileId ?? "none"] as const,
  library: (profileId: string | null | undefined, libraryId: number) =>
    ["libraryPlaybackPreferences", "library", profileId ?? "none", libraryId] as const,
};

export const progressKeys = {
  all: ["progress"] as const,
  list: (status?: string, libraryId?: number) => ["progress", "list", status, libraryId] as const,
};

export const settingsKeys = {
  all: ["settings"] as const,
  detail: (key: string) => ["settings", key] as const,
};

export const historyImportKeys = {
  all: ["history-imports"] as const,
  sources: () => ["history-imports", "sources"] as const,
  runs: (limit = 10) => ["history-imports", "runs", limit] as const,
  run: (id?: string) => ["history-imports", "run", id] as const,
};

export const sectionKeys = {
  all: ["sections"] as const,
  home: () => ["sections", "home"] as const,
  homeLayout: () => ["sections", "home", "layout"] as const,
  homeItemsRoot: () => ["sections", "home", "items"] as const,
  homeItems: (sectionId: string) => ["sections", "home", "items", sectionId] as const,
  libraryRoot: () => ["sections", "library"] as const,
  library: (libraryId: number) => ["sections", "library", libraryId] as const,
  adminList: (scope: string, libraryId?: number) =>
    ["sections", "admin", scope, libraryId] as const,
  profileOverrides: (scope: string, libraryId?: string) =>
    ["sections", "profile", scope, libraryId] as const,
  profileOverridesRaw: (scope: string, libraryId?: string) =>
    ["sections", "profile", scope, libraryId, "raw"] as const,
};

export const ratingKeys = {
  all: ["ratings"] as const,
  item: (itemId: string) => ["ratings", itemId] as const,
  list: () => ["ratings", "list"] as const,
};

export const recKeys = {
  all: ["recommendations"] as const,
  forYouMain: () => [...recKeys.all, "for-you", "main"] as const,
  forYouRows: () => [...recKeys.all, "for-you", "rows"] as const,
  similar: (itemId: string) => [...recKeys.all, "similar", itemId] as const,
  becauseWatched: (itemId: string) => [...recKeys.all, "because-watched", itemId] as const,
  similarUsers: () => [...recKeys.all, "similar-users"] as const,
  tasteProfile: () => [...recKeys.all, "taste-profile"] as const,
};

export const adminKeys = {
  users: () => ["admin", "users"] as const,
  userDetail: (userId: number) => ["admin", "users", userId] as const,
  userProfiles: (userId?: number) => ["admin", "users", userId, "profiles"] as const,
  libraries: () => ["admin", "libraries"] as const,
  librarySkippedRoots: () => ["admin", "libraries", "skippedRoots"] as const,
  jobs: (jobType?: string) => ["admin", "jobs", jobType] as const,
  catalogImportSources: () => ["admin", "catalog", "importSources"] as const,
  localImportSources: () => ["admin", "catalog", "localImportSources"] as const,
  collections: (libraryId?: number) => ["admin", "collections", libraryId] as const,
  libraryProviders: (id: number) => ["admin", "libraries", id, "providers"] as const,
  providers: () => ["admin", "providers"] as const,
  nodes: () => ["admin", "nodes"] as const,
  stats: () => ["admin", "stats"] as const,
  sessions: () => ["admin", "sessions"] as const,
  serverSettings: () => ["admin", "serverSettings"] as const,
  recommendationsStatus: () => ["admin", "recommendationsStatus"] as const,
  inviteCodes: () => ["admin", "inviteCodes"] as const,
  apiKeys: () => ["admin", "apiKeys"] as const,
  rateLimitConfig: () => ["admin", "rateLimitConfig"] as const,
  playbackHistory: (params: {
    userId?: number;
    profileId?: string;
    mediaItemId?: string;
    completed?: string;
    limit?: number;
  }) => ["admin", "playbackHistory", params] as const,
  userIPs: (userId: number, days?: number) => ["admin", "users", userId, "ips", days] as const,
  ipUsers: (ip: string, days?: number) => ["admin", "ips", ip, days] as const,
  operationalLogs: (params: Record<string, unknown>) => ["admin", "logs", "app", params] as const,
  auditLogs: (params: Record<string, unknown>) => ["admin", "logs", "audit", params] as const,
  subtitleProviders: () => ["admin", "subtitleProviders"] as const,
  historyImportSources: () => ["admin", "historyImportSources"] as const,
  tasks: () => ["admin", "tasks"] as const,
  task: (key: string) => ["admin", "tasks", key] as const,
  taskHistory: (key: string) => ["admin", "tasks", key, "history"] as const,
};
