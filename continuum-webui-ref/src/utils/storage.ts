const STORAGE_KEYS = {
  ACCESS_TOKEN: "access_token",
  REFRESH_TOKEN: "refresh_token",
  PROFILE_ID: "profile_id",
  CURRENT_PROFILE: "current_profile",
  VOLUME: "player-volume",
  MUTED: "player-muted",
  THEME: "continuum-theme",
  UI_TEXT_SCALE: "continuum-ui-text-scale",
  UI_TEXT_WEIGHT: "continuum-ui-text-weight",
  UI_HIGH_CONTRAST: "continuum-ui-high-contrast",
} as const;

type StorageKey = (typeof STORAGE_KEYS)[keyof typeof STORAGE_KEYS];

function get(key: StorageKey): string | null {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

function set(key: StorageKey, value: string): void {
  try {
    localStorage.setItem(key, value);
  } catch {
    // Storage full or unavailable
  }
}

function remove(key: StorageKey): void {
  try {
    localStorage.removeItem(key);
  } catch {
    // Storage unavailable
  }
}

export const storage = { KEYS: STORAGE_KEYS, get, set, remove };
