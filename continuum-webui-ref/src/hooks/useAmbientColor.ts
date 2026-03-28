import { useEffect, useRef } from "react";
import { getAverageColor } from "@/lib/thumbhash";

const PROPERTY = "--ambient";

/**
 * Sets the --ambient CSS custom property on <html> from a thumbhash.
 *
 * When the thumbhash changes, the ambient color updates and the
 * .ambient-glow CSS class handles the smooth visual transition.
 * On unmount or when thumbhash becomes empty, resets --ambient so the
 * CSS fallback `var(--ambient, var(--primary))` takes over.
 */
export function useAmbientColor(thumbhash: string | undefined | null): void {
  const prevRef = useRef<string | null>(null);

  useEffect(() => {
    const root = document.documentElement;

    if (!thumbhash) {
      if (prevRef.current !== null) {
        root.style.removeProperty(PROPERTY);
        prevRef.current = null;
      }
      return;
    }

    const color = getAverageColor(thumbhash);
    if (!color) {
      root.style.removeProperty(PROPERTY);
      prevRef.current = null;
      return;
    }

    // Only write to DOM if value changed
    if (color !== prevRef.current) {
      root.style.setProperty(PROPERTY, color);
      prevRef.current = color;
    }

    return () => {
      root.style.removeProperty(PROPERTY);
      prevRef.current = null;
    };
  }, [thumbhash]);
}
