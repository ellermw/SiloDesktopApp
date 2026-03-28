import { useId, type ReactNode } from "react";

import { useAuth } from "@/hooks/useAuth";
import { useCurrentProfile } from "@/hooks/useCurrentProfile";
import { useUpdateProfile } from "@/hooks/queries/profiles";
import { useSetting, useSetSetting } from "@/hooks/queries/settings";
import { SettingsGroup } from "@/components/settings/SettingsGroup";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { toast } from "sonner";

interface LanguageOption {
  code: string;
  label: string;
}

interface SettingRowProps {
  label: string;
  description: string;
  disabled?: boolean;
  badge?: string;
  control: (props: { id: string; disabled: boolean }) => ReactNode;
}

const QUALITY_OPTIONS = [
  { value: "auto", label: "Auto" },
  { value: "480p", label: "480p" },
  { value: "720p", label: "720p" },
  { value: "1080p", label: "1080p" },
  { value: "4k", label: "4K" },
] as const;

const LANGUAGES: LanguageOption[] = [
  { code: "en", label: "English" },
  { code: "es", label: "Spanish" },
  { code: "fr", label: "French" },
  { code: "de", label: "German" },
  { code: "it", label: "Italian" },
  { code: "pt", label: "Portuguese" },
  { code: "ja", label: "Japanese" },
  { code: "ko", label: "Korean" },
  { code: "zh", label: "Chinese" },
  { code: "ru", label: "Russian" },
  { code: "ar", label: "Arabic" },
  { code: "hi", label: "Hindi" },
];

const SUBTITLE_MODES = [
  { value: "auto", label: "Auto" },
  { value: "always", label: "Always" },
  { value: "off", label: "Off" },
] as const;

function SettingRow({ label, description, disabled = false, badge, control }: SettingRowProps) {
  const controlId = useId();

  return (
    <div className="border-border/50 flex flex-col gap-3 border-t pt-4 first:border-t-0 first:pt-0 sm:flex-row sm:items-center sm:justify-between">
      <div className="min-w-0 space-y-0.5">
        <div className="flex items-center gap-2">
          <Label htmlFor={controlId} className="text-sm font-medium">
            {label}
          </Label>
          {badge ? (
            <span className="bg-muted text-muted-foreground rounded-full px-2 py-0.5 text-[11px] font-medium">
              {badge}
            </span>
          ) : null}
        </div>
        <p className="text-muted-foreground text-[13px] leading-relaxed">{description}</p>
      </div>
      <div className="w-full sm:w-auto">{control({ id: controlId, disabled })}</div>
    </div>
  );
}

function NextUpSetting() {
  const { data: nextUpMode, isLoading } = useSetting("next_up_mode");
  const setSetting = useSetSetting();

  const currentValue = nextUpMode || "combined";

  return (
    <SettingRow
      label="Next up episodes"
      description="Choose whether the next unwatched episode appears with Continue Watching or on its own."
      control={({ id, disabled }) => (
        <Select
          value={currentValue}
          onValueChange={(value) =>
            setSetting.mutate(
              { key: "next_up_mode", value },
              { onSuccess: () => toast.success("Setting saved") },
            )
          }
          disabled={disabled || isLoading || setSetting.isPending}
        >
          <SelectTrigger id={id} className="w-full sm:w-[240px]">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="combined">With Continue Watching</SelectItem>
            <SelectItem value="separate">In a separate section</SelectItem>
          </SelectContent>
        </Select>
      )}
    />
  );
}

export default function PlaybackSettings() {
  const { selectProfile } = useAuth();
  const { profile } = useCurrentProfile();
  const updateMutation = useUpdateProfile();

  // RequireProfile route guard guarantees profile is non-null
  if (!profile) return null;

  return (
    <div className="space-y-6">
      <div className="space-y-3">
        <h2 className="text-2xl font-semibold tracking-tight sm:text-3xl">Playback</h2>
        <p className="text-muted-foreground max-w-2xl text-sm leading-relaxed">
          Defaults for quality, language, and playback shortcuts.
        </p>
      </div>

      <SettingsGroup
        title="Quality and language"
        description="Choose the defaults this profile should use when playback starts."
      >
        <SettingRow
          label="Video quality"
          description="Pick the resolution to use when the app can match your connection."
          disabled={updateMutation.isPending}
          control={({ id, disabled }) => (
            <Select
              value={profile.quality_preference || "auto"}
              onValueChange={(value) =>
                updateMutation.mutate(
                  { id: profile.id, body: { quality_preference: value } },
                  {
                    onSuccess: (updatedProfile) => selectProfile(updatedProfile),
                    onError: () => toast.error("Failed to save setting"),
                  },
                )
              }
            >
              <SelectTrigger id={id} className="w-full sm:w-[220px]" disabled={disabled}>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {QUALITY_OPTIONS.map((opt) => (
                  <SelectItem key={opt.value} value={opt.value}>
                    {opt.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        />

        <SettingRow
          label="Spoken language"
          description="Choose the audio language you want first when more than one is available."
          disabled={updateMutation.isPending}
          control={({ id, disabled }) => (
            <Select
              value={profile.language || "en"}
              onValueChange={(value) =>
                updateMutation.mutate(
                  { id: profile.id, body: { language: value } },
                  {
                    onSuccess: (updatedProfile) => selectProfile(updatedProfile),
                    onError: () => toast.error("Failed to save setting"),
                  },
                )
              }
            >
              <SelectTrigger id={id} className="w-full sm:w-[220px]" disabled={disabled}>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="original">Original Language</SelectItem>
                {LANGUAGES.map((lang) => (
                  <SelectItem key={lang.code} value={lang.code}>
                    {lang.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        />

        <SettingRow
          label="Subtitle language"
          description="Choose a subtitle language to prefer. Leave it off if you do not have one."
          disabled={updateMutation.isPending}
          control={({ id, disabled }) => (
            <Select
              value={profile.subtitle_language || "none"}
              onValueChange={(value) =>
                updateMutation.mutate(
                  {
                    id: profile.id,
                    body: { subtitle_language: value === "none" ? "" : value },
                  },
                  {
                    onSuccess: (updatedProfile) => selectProfile(updatedProfile),
                    onError: () => toast.error("Failed to save setting"),
                  },
                )
              }
            >
              <SelectTrigger id={id} className="w-full sm:w-[220px]" disabled={disabled}>
                <SelectValue placeholder="None" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="none">None</SelectItem>
                {LANGUAGES.map((lang) => (
                  <SelectItem key={lang.code} value={lang.code}>
                    {lang.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        />

        <SettingRow
          label="Subtitle behavior"
          description="Decide when subtitles should appear if the app has a match."
          disabled={updateMutation.isPending}
          control={({ id, disabled }) => (
            <Select
              value={profile.subtitle_mode || "auto"}
              onValueChange={(value) =>
                updateMutation.mutate(
                  { id: profile.id, body: { subtitle_mode: value } },
                  {
                    onSuccess: (updatedProfile) => selectProfile(updatedProfile),
                    onError: () => toast.error("Failed to save setting"),
                  },
                )
              }
            >
              <SelectTrigger id={id} className="w-full sm:w-[220px]" disabled={disabled}>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {SUBTITLE_MODES.map((mode) => (
                  <SelectItem key={mode.value} value={mode.value}>
                    {mode.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        />

        <SettingRow
          label="Show forced subtitles"
          description="Display subtitles for foreign-language dialogue even when subtitles are off or set to auto."
          disabled={updateMutation.isPending}
          control={({ id, disabled }) => (
            <Switch
              id={id}
              checked={profile.show_forced_subtitles ?? true}
              disabled={disabled}
              onCheckedChange={(checked) =>
                updateMutation.mutate(
                  { id: profile.id, body: { show_forced_subtitles: checked } },
                  {
                    onSuccess: (updatedProfile) => selectProfile(updatedProfile),
                    onError: () => toast.error("Failed to save setting"),
                  },
                )
              }
            />
          )}
        />
      </SettingsGroup>

      <SettingsGroup
        title="Skipping and next up"
        description="Control the small playback shortcuts that can save you a few taps."
      >
        <SettingRow
          label="Skip intros automatically"
          description="Jump past opening credits when the app can detect them."
          disabled
          badge="Coming soon"
          control={({ id, disabled }) => <Switch id={id} checked={false} disabled={disabled} />}
        />

        <SettingRow
          label="Skip end credits automatically"
          description="Move on after the episode ends when credits can be skipped."
          disabled
          badge="Coming soon"
          control={({ id, disabled }) => <Switch id={id} checked={false} disabled={disabled} />}
        />

        <NextUpSetting />
      </SettingsGroup>
    </div>
  );
}
