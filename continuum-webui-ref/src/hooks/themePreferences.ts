import { storage } from "@/utils/storage";
import type { ThemeId } from "@/lib/themes";
import { DEFAULT_THEME, THEME_IDS } from "@/lib/themes";

export type TextScale = "default" | "large" | "x-large";
export type TextWeight = "default" | "strong";

export function shouldLoadApiTheme({
  loading,
  user,
}: {
  loading: boolean;
  user: { id: number } | null;
}): boolean {
  return !loading && !!user;
}

export function isValidTheme(value: string | null | undefined): value is ThemeId {
  return typeof value === "string" && (THEME_IDS as readonly string[]).includes(value);
}

export function parseTextScale(value: string | null | undefined): TextScale {
  return value === "large" || value === "x-large" ? value : "default";
}

export function parseTextWeight(value: string | null | undefined): TextWeight {
  return value === "strong" ? "strong" : "default";
}

export function parseHighContrast(value: string | null | undefined): boolean {
  return value === "true";
}

export function getInitialTheme(): ThemeId {
  const stored = storage.get(storage.KEYS.THEME);
  return isValidTheme(stored) ? stored : DEFAULT_THEME;
}
