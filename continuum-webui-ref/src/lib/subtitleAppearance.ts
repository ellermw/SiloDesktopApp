import type { CSSProperties } from "react";

// ─── Types ──────────────────────────────────────────────────────────────────

export interface SubtitleAppearance {
  fontSize: "small" | "medium" | "large" | "xlarge";
  fontFamily: "sans-serif" | "serif" | "monospace";
  fontColor: string;
  backgroundColor: string;
  backgroundStyle: "box" | "shadow" | "outline" | "none";
  backgroundOpacity: number;
  textOutline: boolean;
  position: "bottom" | "lower-third" | "top";
}

// ─── Defaults ───────────────────────────────────────────────────────────────

export const DEFAULT_SUBTITLE_APPEARANCE: SubtitleAppearance = {
  fontSize: "medium",
  fontFamily: "sans-serif",
  fontColor: "#ffffff",
  backgroundColor: "#000000",
  backgroundStyle: "box",
  backgroundOpacity: 75,
  textOutline: false,
  position: "bottom",
};

// ─── Option Arrays ──────────────────────────────────────────────────────────

export const FONT_SIZE_OPTIONS = [
  { value: "small" as const, label: "Small" },
  { value: "medium" as const, label: "Medium" },
  { value: "large" as const, label: "Large" },
  { value: "xlarge" as const, label: "Extra Large" },
];

export const FONT_FAMILY_OPTIONS = [
  { value: "sans-serif" as const, label: "Sans-serif" },
  { value: "serif" as const, label: "Serif" },
  { value: "monospace" as const, label: "Monospace" },
];

export const BACKGROUND_STYLE_OPTIONS = [
  { value: "box" as const, label: "Box" },
  { value: "shadow" as const, label: "Drop Shadow" },
  { value: "outline" as const, label: "Outline" },
  { value: "none" as const, label: "None" },
];

export const POSITION_OPTIONS = [
  { value: "bottom" as const, label: "Bottom" },
  { value: "lower-third" as const, label: "Lower Third" },
  { value: "top" as const, label: "Top" },
];

// ─── Color Palettes ─────────────────────────────────────────────────────────

interface ColorSwatch {
  hex: string;
  label: string;
}

export const FONT_COLOR_PALETTE: ColorSwatch[] = [
  { hex: "#ffffff", label: "White" },
  { hex: "#facc15", label: "Yellow" },
  { hex: "#22c55e", label: "Green" },
  { hex: "#06b6d4", label: "Cyan" },
  { hex: "#d946ef", label: "Magenta" },
  { hex: "#ef4444", label: "Red" },
  { hex: "#3b82f6", label: "Blue" },
  { hex: "#000000", label: "Black" },
];

export const BG_COLOR_PALETTE: ColorSwatch[] = [
  { hex: "#000000", label: "Black" },
  { hex: "#374151", label: "Dark Gray" },
  { hex: "#1e3a5f", label: "Navy" },
  { hex: "#7f1d1d", label: "Dark Red" },
  { hex: "#14532d", label: "Dark Green" },
];

// ─── Parser ─────────────────────────────────────────────────────────────────

const VALID_FONT_SIZES: Set<string> = new Set(FONT_SIZE_OPTIONS.map((o) => o.value));
const VALID_FONT_FAMILIES: Set<string> = new Set(FONT_FAMILY_OPTIONS.map((o) => o.value));
const VALID_BG_STYLES: Set<string> = new Set(BACKGROUND_STYLE_OPTIONS.map((o) => o.value));
const VALID_POSITIONS: Set<string> = new Set(POSITION_OPTIONS.map((o) => o.value));

export function parseSubtitleAppearance(json: string | null): SubtitleAppearance {
  if (!json) return { ...DEFAULT_SUBTITLE_APPEARANCE };
  try {
    const p = JSON.parse(json) as Record<string, unknown>;
    return {
      fontSize: VALID_FONT_SIZES.has(p.fontSize as string)
        ? (p.fontSize as SubtitleAppearance["fontSize"])
        : DEFAULT_SUBTITLE_APPEARANCE.fontSize,
      fontFamily: VALID_FONT_FAMILIES.has(p.fontFamily as string)
        ? (p.fontFamily as SubtitleAppearance["fontFamily"])
        : DEFAULT_SUBTITLE_APPEARANCE.fontFamily,
      fontColor:
        typeof p.fontColor === "string" && /^#[0-9a-fA-F]{6}$/.test(p.fontColor)
          ? p.fontColor
          : DEFAULT_SUBTITLE_APPEARANCE.fontColor,
      backgroundColor:
        typeof p.backgroundColor === "string" && /^#[0-9a-fA-F]{6}$/.test(p.backgroundColor)
          ? p.backgroundColor
          : DEFAULT_SUBTITLE_APPEARANCE.backgroundColor,
      backgroundStyle: VALID_BG_STYLES.has(p.backgroundStyle as string)
        ? (p.backgroundStyle as SubtitleAppearance["backgroundStyle"])
        : DEFAULT_SUBTITLE_APPEARANCE.backgroundStyle,
      backgroundOpacity:
        typeof p.backgroundOpacity === "number" &&
        p.backgroundOpacity >= 0 &&
        p.backgroundOpacity <= 100
          ? p.backgroundOpacity
          : DEFAULT_SUBTITLE_APPEARANCE.backgroundOpacity,
      textOutline:
        typeof p.textOutline === "boolean"
          ? p.textOutline
          : DEFAULT_SUBTITLE_APPEARANCE.textOutline,
      position: VALID_POSITIONS.has(p.position as string)
        ? (p.position as SubtitleAppearance["position"])
        : DEFAULT_SUBTITLE_APPEARANCE.position,
    };
  } catch {
    return { ...DEFAULT_SUBTITLE_APPEARANCE };
  }
}

// ─── Style Computation ──────────────────────────────────────────────────────

const FONT_SIZE_MAP: Record<SubtitleAppearance["fontSize"], string> = {
  small: "1rem",
  medium: "1.35rem",
  large: "1.75rem",
  xlarge: "2.25rem",
};

function hexToRgb(hex: string): { r: number; g: number; b: number } {
  const clean = hex.replace("#", "");
  return {
    r: parseInt(clean.substring(0, 2), 16),
    g: parseInt(clean.substring(2, 4), 16),
    b: parseInt(clean.substring(4, 6), 16),
  };
}

function buildTextShadow(settings: SubtitleAppearance): string | undefined {
  const shadows: string[] = [];

  if (settings.backgroundStyle === "shadow") {
    shadows.push("2px 2px 4px rgba(0,0,0,0.9)");
  }

  if (settings.backgroundStyle === "outline") {
    // 1px cardinal + 2px diagonal for a visible, rounded outline
    const offsets = [
      [-1, 0],
      [1, 0],
      [0, -1],
      [0, 1],
      [-2, -2],
      [-2, 2],
      [2, -2],
      [2, 2],
    ];
    for (const [x, y] of offsets) {
      shadows.push(`${x}px ${y}px 0 #000`);
    }
  }

  if (settings.textOutline) {
    shadows.push("0 0 3px #000", "0 0 3px #000");
  }

  return shadows.length > 0 ? shadows.join(", ") : undefined;
}

export interface SubtitleStyles {
  containerStyle: CSSProperties;
  cueStyle: CSSProperties;
}

export function computeSubtitleStyles(settings: SubtitleAppearance): SubtitleStyles {
  const containerStyle: CSSProperties = {};
  const cueStyle: CSSProperties = {};

  // Position
  if (settings.position === "top") {
    containerStyle.top = "8%";
    containerStyle.bottom = "auto";
  } else if (settings.position === "lower-third") {
    containerStyle.bottom = "25%";
  } else {
    containerStyle.bottom = "12%";
  }

  // Font
  cueStyle.fontSize = FONT_SIZE_MAP[settings.fontSize];
  cueStyle.fontFamily = settings.fontFamily;
  cueStyle.color = settings.fontColor;

  // Background
  if (settings.backgroundStyle === "box") {
    const { r, g, b } = hexToRgb(settings.backgroundColor);
    cueStyle.backgroundColor = `rgba(${r}, ${g}, ${b}, ${settings.backgroundOpacity / 100})`;
  }

  // Text shadow (handles shadow, outline, and textOutline — concatenated)
  const textShadow = buildTextShadow(settings);
  if (textShadow) {
    cueStyle.textShadow = textShadow;
  }

  return { containerStyle, cueStyle };
}
