/**
 * Minimal fetch wrapper for the player module.
 * Uses PlayerConfig for API base URL and auth — never imports app-specific code.
 */

import type { PlayerConfig } from "./context/PlayerConfigContext";

export class PlayerFetchError extends Error {
  constructor(
    public status: number,
    message: string,
  ) {
    super(message);
    this.name = "PlayerFetchError";
  }
}

/**
 * Performs an authenticated fetch against the configured API.
 * Returns the parsed JSON body for 2xx responses, undefined for 204.
 * Throws PlayerFetchError for non-2xx responses.
 */
export async function playerFetch<T>(
  config: PlayerConfig,
  path: string,
  options: RequestInit = {},
): Promise<T> {
  const headers: Record<string, string> = {
    "Content-Type": "application/json",
    ...(options.headers as Record<string, string>),
  };

  const token = config.getAccessToken();
  if (token) {
    headers["Authorization"] = `Bearer ${token}`;
  }

  const profileId = config.getProfileId();
  if (profileId) {
    headers["X-Profile-Id"] = profileId;
  }

  const res = await fetch(`${config.apiBaseUrl}${path}`, {
    ...options,
    headers,
  });

  if (!res.ok) {
    const text = await res.text().catch(() => res.statusText);
    throw new PlayerFetchError(res.status, text);
  }

  if (res.status === 204) {
    return undefined as T;
  }

  return res.json();
}
