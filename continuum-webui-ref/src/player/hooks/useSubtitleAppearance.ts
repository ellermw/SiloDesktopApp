import { useMemo } from "react";
import { useSetting } from "@/hooks/queries/settings";
import { parseSubtitleAppearance, computeSubtitleStyles } from "@/lib/subtitleAppearance";
import type { SubtitleAppearance, SubtitleStyles } from "@/lib/subtitleAppearance";

export function useSubtitleAppearance(): SubtitleStyles & { settings: SubtitleAppearance } {
  const { data } = useSetting("subtitle_appearance");

  const settings = useMemo(() => parseSubtitleAppearance(data ?? null), [data]);

  const styles = useMemo(() => computeSubtitleStyles(settings), [settings]);

  return { settings, ...styles };
}
