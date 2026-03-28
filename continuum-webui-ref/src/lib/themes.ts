export const THEME_IDS = [
  "midnight-cinema",
  "cinema-light",
  "cobalt-studio",
  "oxblood-noir",
  "ember-slate",
  "evergreen-studio",
  "verdant-ink",
  "catppuccin",
  "gruvbox",
  "void-space",
  "charcoal-studio",
  "graphite-pro",
  "obsidian-depth",
] as const;

export type ThemeId = (typeof THEME_IDS)[number];

export interface ThemeDefinition {
  id: ThemeId;
  label: string;
  fontFamily: string;
  /** Accent/primary color shown in the theme picker preview */
  previewAccent: string;
  /** Background color shown in the theme picker preview */
  previewBg: string;
  /** Short description of the theme's aesthetic */
  description?: string;
  /** Whether this theme should appear in the curated picker */
  curated?: boolean;
}

export const THEMES: Record<ThemeId, ThemeDefinition> = {
  catppuccin: {
    id: "catppuccin",
    label: "Catppuccin",
    fontFamily: "Outfit",
    previewAccent: "#cba6f7",
    previewBg: "#1e1e2e",
    description: "Pastel purple on warm dark blue",
  },
  gruvbox: {
    id: "gruvbox",
    label: "Gruvbox",
    fontFamily: "Manrope",
    previewAccent: "#fabd2f",
    previewBg: "#282828",
    description: "Warm retro with golden accent",
  },
  "void-space": {
    id: "void-space",
    label: "Void Space",
    fontFamily: "Manrope",
    previewAccent: "#58a6ff",
    previewBg: "#0d1117",
    description: "Cool blue on deep space black",
  },
  "charcoal-studio": {
    id: "charcoal-studio",
    label: "Charcoal",
    fontFamily: "Outfit",
    previewAccent: "#0a84ff",
    previewBg: "#1c1c1e",
    description: "Apple-inspired blue on dark gray",
  },
  "graphite-pro": {
    id: "graphite-pro",
    label: "Graphite",
    fontFamily: "Sora",
    previewAccent: "#a855f7",
    previewBg: "#18181b",
    description: "Vibrant purple on zinc",
  },
  "obsidian-depth": {
    id: "obsidian-depth",
    label: "Obsidian",
    fontFamily: "Urbanist",
    previewAccent: "#00d4aa",
    previewBg: "#0f0f0f",
    description: "Cyan accent on true dark",
  },
  "midnight-cinema": {
    id: "midnight-cinema",
    label: "Cinema Dark",
    fontFamily: "Outfit",
    previewAccent: "#e8e8ec",
    previewBg: "#141417",
    description: "Monochromatic cinema — content is the color",
    curated: true,
  },
  "cinema-light": {
    id: "cinema-light",
    label: "Cinema Light",
    fontFamily: "Outfit",
    previewAccent: "#1a1a1e",
    previewBg: "#f4f4f6",
    description: "Light monochromatic cinema — content is the color",
    curated: true,
  },
  "cobalt-studio": {
    id: "cobalt-studio",
    label: "Cobalt",
    fontFamily: "Outfit",
    previewAccent: "#78aefc",
    previewBg: "#101722",
    description: "Cool blue graphite with crisp contrast",
    curated: true,
  },
  "oxblood-noir": {
    id: "oxblood-noir",
    label: "Oxblood",
    fontFamily: "Outfit",
    previewAccent: "#d16a78",
    previewBg: "#171113",
    description: "Deep red-black with restrained luxury warmth",
    curated: true,
  },
  "ember-slate": {
    id: "ember-slate",
    label: "Ember",
    fontFamily: "Urbanist",
    previewAccent: "#f07b62",
    previewBg: "#151213",
    description: "Smoked charcoal with ember-red accents",
  },
  "evergreen-studio": {
    id: "evergreen-studio",
    label: "Evergreen",
    fontFamily: "Outfit",
    previewAccent: "#5bc39d",
    previewBg: "#101715",
    description: "Refined evergreen accents on dense graphite",
    curated: true,
  },
  "verdant-ink": {
    id: "verdant-ink",
    label: "Verdant Ink",
    fontFamily: "Urbanist",
    previewAccent: "#86d4b6",
    previewBg: "#0d1513",
    description: "Cool green-black with softer luminous contrast",
  },
};

export const DEFAULT_THEME: ThemeId = "midnight-cinema";

export const CURATED_THEME_IDS = THEME_IDS.filter((id) => THEMES[id].curated);
