import { useId, useMemo, useState, type ReactNode } from "react";
import { useSetting, useSetSetting } from "@/hooks/queries/settings";
import { SettingsGroup } from "@/components/settings/SettingsGroup";
import { Label } from "@/components/ui/label";
import { Button } from "@/components/ui/button";
import { Switch } from "@/components/ui/switch";
import { Slider } from "@/components/ui/slider";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { toast } from "sonner";
import {
  parseSubtitleAppearance,
  computeSubtitleStyles,
  DEFAULT_SUBTITLE_APPEARANCE,
  FONT_SIZE_OPTIONS,
  FONT_FAMILY_OPTIONS,
  BACKGROUND_STYLE_OPTIONS,
  POSITION_OPTIONS,
  FONT_COLOR_PALETTE,
  BG_COLOR_PALETTE,
} from "@/lib/subtitleAppearance";
import type { SubtitleAppearance } from "@/lib/subtitleAppearance";

const SETTINGS_KEY = "subtitle_appearance";

interface ColorPaletteProps {
  colors: { hex: string; label: string }[];
  selected: string;
  onChange: (hex: string) => void;
  disabled?: boolean;
  labelId?: string;
  descriptionId?: string;
}

function ColorPalette({
  colors,
  selected,
  onChange,
  disabled,
  labelId,
  descriptionId,
}: ColorPaletteProps) {
  return (
    <div
      role="group"
      aria-labelledby={labelId}
      aria-describedby={descriptionId}
      className={`flex flex-wrap gap-2 ${disabled ? "opacity-40" : ""}`}
    >
      {colors.map((c) => (
        <button
          key={c.hex}
          type="button"
          title={c.label}
          aria-label={c.label}
          onClick={() => onChange(c.hex)}
          disabled={disabled}
          className="h-7 w-7 rounded-full border-2 transition-transform hover:scale-110"
          style={{
            backgroundColor: c.hex,
            borderColor: selected === c.hex ? "var(--primary)" : "transparent",
            boxShadow: c.hex === "#000000" ? "inset 0 0 0 1px rgba(255,255,255,0.2)" : undefined,
          }}
        />
      ))}
    </div>
  );
}

interface SettingRowProps {
  label: string;
  description?: string;
  labelForControl?: boolean;
  children: (props: { id: string; labelId: string; descriptionId: string }) => ReactNode;
}

function SettingRow({ label, description, labelForControl = true, children }: SettingRowProps) {
  const controlId = useId();
  const labelId = useId();
  const descriptionId = useId();

  return (
    <div className="grid gap-3 border-t border-border/50 pt-4 first:border-t-0 first:pt-0 md:grid-cols-[minmax(0,1fr)_auto] md:items-center">
      <div className="min-w-0 space-y-0.5">
        <Label
          id={labelId}
          htmlFor={labelForControl ? controlId : undefined}
          className="text-sm font-medium"
        >
          {label}
        </Label>
        {description ? (
          <p id={descriptionId} className="text-muted-foreground text-[13px] leading-relaxed">
            {description}
          </p>
        ) : null}
      </div>
      <div className="flex md:justify-end">
        {children({ id: controlId, labelId, descriptionId })}
      </div>
    </div>
  );
}

export default function SubtitleAppearanceSettings() {
  const { data: settingJson } = useSetting(SETTINGS_KEY);
  const saveMutation = useSetSetting();
  const serverSettings = useMemo(
    () => parseSubtitleAppearance(settingJson ?? null),
    [settingJson],
  );
  const serverKey = settingJson ?? "__default__";
  const [settingsState, setSettingsState] = useState<{
    key: string;
    settings: SubtitleAppearance;
  }>({
    key: serverKey,
    settings: serverSettings,
  });
  const settings = settingsState.key === serverKey ? settingsState.settings : serverSettings;

  function update<K extends keyof SubtitleAppearance>(key: K, value: SubtitleAppearance[K]) {
    setSettingsState((prev) => {
      const base = prev.key === serverKey ? prev.settings : serverSettings;
      return {
        key: serverKey,
        settings: { ...base, [key]: value },
      };
    });
  }

  function handleSave() {
    saveMutation.mutate(
      { key: SETTINGS_KEY, value: JSON.stringify(settings) },
      {
        onSuccess: () => toast.success("Subtitle settings saved"),
        onError: () => toast.error("Failed to save subtitle settings"),
      },
    );
  }

  function handleReset() {
    setSettingsState({
      key: serverKey,
      settings: { ...DEFAULT_SUBTITLE_APPEARANCE },
    });
  }

  const isBoxStyle = settings.backgroundStyle === "box";
  const { containerStyle, cueStyle } = computeSubtitleStyles(settings);

  return (
    <div className="space-y-6">
      <div className="space-y-3">
        <h2 className="text-2xl font-semibold tracking-tight sm:text-3xl">Subtitles</h2>
        <p className="text-muted-foreground max-w-2xl text-sm leading-relaxed">
          Preview changes as you work, then save when the style feels right.
        </p>
      </div>

      <SettingsGroup
        title="Preview"
        description="This sample reflects the current subtitle size, color, and positioning."
      >
        <div
          className="surface-panel-subtle relative overflow-hidden rounded-[1.3rem]"
          style={{ aspectRatio: "16 / 9", background: "linear-gradient(135deg, #0f0f1a, #1a1a3e)" }}
        >
          <div
            className="absolute inset-x-0 z-10 flex flex-col items-center gap-1"
            style={containerStyle}
          >
            <span
              className="inline-block rounded px-3 py-1 text-center leading-snug"
              style={{ ...cueStyle, whiteSpace: "pre-line" }}
            >
              Sample subtitle text
            </span>
            <span
              className="inline-block rounded px-3 py-1 text-center leading-snug"
              style={{ ...cueStyle, whiteSpace: "pre-line" }}
            >
              with multiple lines
            </span>
          </div>
        </div>
      </SettingsGroup>

      <SettingsGroup
        title="Text"
        description="Adjust the size, typeface, color, and outline treatment for subtitle text."
      >
        <SettingRow label="Font size">
          {({ id, descriptionId }) => (
            <Select
              value={settings.fontSize}
              onValueChange={(v) => update("fontSize", v as SubtitleAppearance["fontSize"])}
            >
              <SelectTrigger id={id} aria-describedby={descriptionId} className="w-full sm:w-48">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {FONT_SIZE_OPTIONS.map((opt) => (
                  <SelectItem key={opt.value} value={opt.value}>
                    {opt.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        </SettingRow>

        <SettingRow label="Font family">
          {({ id, descriptionId }) => (
            <Select
              value={settings.fontFamily}
              onValueChange={(v) => update("fontFamily", v as SubtitleAppearance["fontFamily"])}
            >
              <SelectTrigger id={id} aria-describedby={descriptionId} className="w-full sm:w-48">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {FONT_FAMILY_OPTIONS.map((opt) => (
                  <SelectItem key={opt.value} value={opt.value}>
                    {opt.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        </SettingRow>

        <SettingRow label="Font color" labelForControl={false}>
          {({ labelId, descriptionId }) => (
            <ColorPalette
              colors={FONT_COLOR_PALETTE}
              selected={settings.fontColor}
              onChange={(hex) => update("fontColor", hex)}
              labelId={labelId}
              descriptionId={descriptionId}
            />
          )}
        </SettingRow>

        <SettingRow label="Text outline">
          {({ id, descriptionId }) => (
            <Switch
              id={id}
              aria-describedby={descriptionId}
              checked={settings.textOutline}
              onCheckedChange={(v) => update("textOutline", v)}
            />
          )}
        </SettingRow>
      </SettingsGroup>

      <SettingsGroup
        title="Background"
        description="Change the backing used for boxed subtitles and how strongly it appears."
      >
        <SettingRow
          label="Background style"
          description="Only the boxed style uses a fill, opacity, and background color."
        >
          {({ id, descriptionId }) => (
            <Select
              value={settings.backgroundStyle}
              onValueChange={(v) =>
                update("backgroundStyle", v as SubtitleAppearance["backgroundStyle"])
              }
            >
              <SelectTrigger id={id} aria-describedby={descriptionId} className="w-full sm:w-48">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {BACKGROUND_STYLE_OPTIONS.map((opt) => (
                  <SelectItem key={opt.value} value={opt.value}>
                    {opt.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        </SettingRow>

        <SettingRow
          label="Background opacity"
          description="Increase this to make the subtitle box more solid."
        >
          {({ descriptionId }) => (
            <div
              className={`flex w-full items-center gap-3 sm:w-56 ${!isBoxStyle ? "opacity-40" : ""}`}
            >
              <Slider
                value={[settings.backgroundOpacity]}
                onValueChange={(vals) =>
                  update("backgroundOpacity", vals[0] ?? settings.backgroundOpacity)
                }
                min={0}
                max={100}
                step={5}
                disabled={!isBoxStyle}
                aria-describedby={descriptionId}
                className="flex-1"
              />
              <span className="text-muted-foreground w-10 text-right text-sm tabular-nums">
                {settings.backgroundOpacity}%
              </span>
            </div>
          )}
        </SettingRow>

        <SettingRow
          label="Background color"
          description="Pick the fill color used behind boxed subtitles."
          labelForControl={false}
        >
          {({ labelId, descriptionId }) => (
            <ColorPalette
              colors={BG_COLOR_PALETTE}
              selected={settings.backgroundColor}
              onChange={(hex) => update("backgroundColor", hex)}
              disabled={!isBoxStyle}
              labelId={labelId}
              descriptionId={descriptionId}
            />
          )}
        </SettingRow>
      </SettingsGroup>

      <SettingsGroup
        title="Position"
        description="Place subtitles where they read best on the video frame."
      >
        <SettingRow label="Position" description="Choose where subtitles sit on the video frame.">
          {({ id, descriptionId }) => (
            <Select
              value={settings.position}
              onValueChange={(v) => update("position", v as SubtitleAppearance["position"])}
            >
              <SelectTrigger id={id} aria-describedby={descriptionId} className="w-full sm:w-48">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {POSITION_OPTIONS.map((opt) => (
                  <SelectItem key={opt.value} value={opt.value}>
                    {opt.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        </SettingRow>
      </SettingsGroup>

      {/* ── Actions ──────────────────────────────────────── */}
      <div className="flex flex-col gap-2 pt-1 sm:flex-row">
        <Button onClick={handleSave} disabled={saveMutation.isPending}>
          Save
        </Button>
        <Button variant="outline" onClick={handleReset}>
          Reset to Defaults
        </Button>
      </div>
    </div>
  );
}
