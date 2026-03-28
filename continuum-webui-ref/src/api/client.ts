import type { ApiError, RefreshResponse } from "./types";
import { storage } from "../utils/storage";

type ProfileUnverifiedListener = () => void;
let profileUnverifiedListener: ProfileUnverifiedListener | null = null;

export function onProfileUnverified(listener: ProfileUnverifiedListener | null) {
  profileUnverifiedListener = listener;
}

let accessToken: string | null = null;
let refreshPromise: Promise<boolean> | null = null;

export function setAccessToken(token: string | null) {
  accessToken = token;
}

export function getAccessToken(): string | null {
  return accessToken;
}

function getRefreshToken(): string | null {
  return storage.get(storage.KEYS.REFRESH_TOKEN);
}

export function setRefreshToken(token: string | null) {
  if (token) {
    storage.set(storage.KEYS.REFRESH_TOKEN, token);
  } else {
    storage.remove(storage.KEYS.REFRESH_TOKEN);
  }
}

function getProfileId(): string | null {
  return storage.get(storage.KEYS.PROFILE_ID);
}

export function setProfileId(id: string | null) {
  if (id) {
    storage.set(storage.KEYS.PROFILE_ID, id);
  } else {
    storage.remove(storage.KEYS.PROFILE_ID);
  }
}

let profileToken: string | null = null;

export function setProfileToken(token: string | null) {
  profileToken = token;
  if (token) {
    sessionStorage.setItem("profile_token", token);
  } else {
    sessionStorage.removeItem("profile_token");
  }
}

export function getProfileToken(): string | null {
  if (!profileToken) {
    profileToken = sessionStorage.getItem("profile_token");
  }
  return profileToken;
}

async function attemptRefresh(): Promise<boolean> {
  const rt = getRefreshToken();
  if (!rt) return false;

  try {
    const data = await refreshAccessToken(rt, fetch);
    if (!data) return false;
    setAccessToken(data.access_token);
    setRefreshToken(data.refresh_token);
    return true;
  } catch {
    return false;
  }
}

export async function bootstrapAccessToken(fetchImpl: typeof fetch = fetch): Promise<boolean> {
  if (accessToken) {
    return true;
  }

  const rt = getRefreshToken();
  if (!rt) {
    return false;
  }

  try {
    const data = await refreshAccessToken(rt, fetchImpl);
    if (!data) {
      return false;
    }
    setAccessToken(data.access_token);
    setRefreshToken(data.refresh_token);
    return true;
  } catch {
    return false;
  }
}

async function refreshAccessToken(
  refreshToken: string,
  fetchImpl: typeof fetch,
): Promise<RefreshResponse | null> {
  const res = await fetchImpl("/api/v1/auth/refresh", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ refresh_token: refreshToken }),
  });
  if (!res.ok) {
    return null;
  }
  return res.json();
}

export class ApiClientError extends Error {
  constructor(
    public status: number,
    public code: string,
    message: string,
  ) {
    super(message);
    this.name = "ApiClientError";
  }
}

async function parseApiError(res: Response): Promise<ApiError> {
  let apiErr: ApiError = { error: "unknown", message: res.statusText };
  try {
    apiErr = await res.json();
  } catch {
    // response wasn't JSON
  }
  return apiErr;
}

export interface RestoredUserSession<TUser> {
  user: TUser;
  accessToken: string;
  refreshToken: string;
}

export async function restoreUserSession<TUser>({
  accessToken,
  refreshToken,
  fetchImpl = fetch,
}: {
  accessToken: string;
  refreshToken: string;
  fetchImpl?: typeof fetch;
}): Promise<RestoredUserSession<TUser>> {
  let restoredAccessToken = accessToken;
  let restoredRefreshToken = refreshToken;

  const requestUser = (token: string) =>
    fetchImpl("/api/v1/auth/me", {
      headers: {
        Authorization: `Bearer ${token}`,
      },
    });

  let res = await requestUser(restoredAccessToken);

  if (res.status === 401) {
    const refreshed = await refreshAccessToken(restoredRefreshToken, fetchImpl);
    if (refreshed) {
      restoredAccessToken = refreshed.access_token;
      restoredRefreshToken = refreshed.refresh_token;
      res = await requestUser(restoredAccessToken);
    }
  }

  if (!res.ok) {
    const apiErr = await parseApiError(res);
    throw new ApiClientError(res.status, apiErr.error, apiErr.message);
  }

  return {
    user: (await res.json()) as TUser,
    accessToken: restoredAccessToken,
    refreshToken: restoredRefreshToken,
  };
}

export async function api<T>(path: string, options: RequestInit = {}): Promise<T> {
  const headers: Record<string, string> = {
    ...(options.headers as Record<string, string>),
  };
  if (!(options.body instanceof FormData)) {
    headers["Content-Type"] = "application/json";
  }

  if (accessToken) {
    headers["Authorization"] = `Bearer ${accessToken}`;
  }

  const profileId = getProfileId();
  if (profileId) {
    headers["X-Profile-Id"] = profileId;
  }

  const profToken = getProfileToken();
  if (profToken) {
    headers["X-Profile-Token"] = profToken;
  }

  let res = await fetch(`/api/v1${path}`, { ...options, headers });

  // Auto-refresh on 401
  if (res.status === 401 && getRefreshToken()) {
    if (!refreshPromise) {
      refreshPromise = attemptRefresh().finally(() => {
        refreshPromise = null;
      });
    }
    const refreshed = await refreshPromise;
    if (refreshed) {
      headers["Authorization"] = `Bearer ${accessToken}`;
      res = await fetch(`/api/v1${path}`, { ...options, headers });
    }
  }

  if (!res.ok) {
    const apiErr = await parseApiError(res);
    if (res.status === 403 && apiErr.error === "profile_unverified") {
      setProfileToken(null);
      profileUnverifiedListener?.();
    }
    throw new ApiClientError(res.status, apiErr.error, apiErr.message);
  }

  // Handle 204 No Content
  if (res.status === 204) {
    return undefined as T;
  }

  return res.json();
}

// People API
export async function searchPeople(query: string, limit = 20): Promise<import("./types").Person[]> {
  const params = new URLSearchParams({ q: query, limit: String(limit) });
  return api<import("./types").Person[]>(`/people?${params}`);
}

export async function getPerson(id: string): Promise<import("./types").Person> {
  return api<import("./types").Person>(`/people/${id}`);
}

export async function getPersonCatalogItems(
  id: string,
  type?: string,
  limit = 24,
  offset = 0,
): Promise<import("./types").BrowseResponse> {
  const params = new URLSearchParams({
    source: "person",
    person_id: id,
    limit: String(limit),
    offset: String(offset),
  });
  if (type) params.set("type", type);
  return api<import("./types").BrowseResponse>(`/catalog?${params}`);
}
