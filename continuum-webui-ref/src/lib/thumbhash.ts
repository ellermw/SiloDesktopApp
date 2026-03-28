import { thumbHashToDataURL, thumbHashToAverageRGBA } from "thumbhash";

const cache = new Map<string, string>();

/** Decode a base64 thumbhash string to a data URL. Cached per hash. */
export function decodeThumbhash(base64: string): string {
  if (!base64) return "";

  const cached = cache.get(base64);
  if (cached) return cached;

  const binary = atob(base64);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) {
    bytes[i] = binary.charCodeAt(i);
  }

  const dataUrl = thumbHashToDataURL(bytes);
  cache.set(base64, dataUrl);
  return dataUrl;
}

// ── Ambient Color Extraction ────────────────────────────────────

const colorCache = new Map<string, string>();

/**
 * Extract the average color from a base64 thumbhash as a hex string.
 * Returns null if the input is empty or invalid.
 * Adjusts very dark, very light, and desaturated colors so they
 * remain visible as ambient glow on dark surfaces.
 */
export function getAverageColor(base64: string): string | null {
  if (!base64) return null;

  const cached = colorCache.get(base64);
  if (cached) return cached;

  const binary = atob(base64);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) {
    bytes[i] = binary.charCodeAt(i);
  }

  const { r, g, b } = thumbHashToAverageRGBA(bytes);
  let rr = Math.round(r * 255);
  let gg = Math.round(g * 255);
  let bb = Math.round(b * 255);

  // Luminance guards (rec.709)
  const lum = 0.2126 * r + 0.7152 * g + 0.0722 * b;

  if (lum < 0.08) {
    const boost = 0.08 / Math.max(lum, 0.001);
    rr = Math.min(255, Math.round(rr * boost));
    gg = Math.min(255, Math.round(gg * boost));
    bb = Math.min(255, Math.round(bb * boost));
  }

  if (lum > 0.85) {
    const f = 0.85 / lum;
    rr = Math.round(rr * f);
    gg = Math.round(gg * f);
    bb = Math.round(bb * f);
  }

  // Saturation guard — boost desaturated colors
  const maxC = Math.max(rr, gg, bb);
  const minC = Math.min(rr, gg, bb);
  if (maxC > 30 && (maxC - minC) / maxC < 0.15) {
    const avg = (rr + gg + bb) / 3;
    rr = Math.min(255, Math.max(0, Math.round(avg + (rr - avg) * 2)));
    gg = Math.min(255, Math.max(0, Math.round(avg + (gg - avg) * 2)));
    bb = Math.min(255, Math.max(0, Math.round(avg + (bb - avg) * 2)));
  }

  const hex = `#${rr.toString(16).padStart(2, "0")}${gg.toString(16).padStart(2, "0")}${bb.toString(16).padStart(2, "0")}`;
  colorCache.set(base64, hex);
  return hex;
}
