import { useEffect, useId, useState, type ReactNode } from "react";
import type { LibraryPlaybackPreference, Profile, UserLibrary } from "@/api/types";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import { useCurrentProfile } from "@/hooks/useCurrentProfile";
import {
  useDeleteLibraryPlaybackPreference,
  useLibraryPlaybackPreferences,
  useSetLibraryPlaybackPreference,
} from "@/hooks/queries/libraryPlaybackPreferences";
import {
  DISABLED_LIBRARY_IDS_SETTING_KEY,
  parseDisabledLibraryIDs,
  serializeDisabledLibraryIDs,
  useAvailableUserLibraries,
} from "@/hooks/queries/libraries";
import { useSetting, useSetSetting } from "@/hooks/queries/settings";
import {
  buildInheritedLanguageLabel,
  buildInheritedShowForcedSubtitlesLabel,
  buildInheritedSubtitleLanguageLabel,
  buildInheritedSubtitleModeLabel,
  buildLibraryPlaybackRequest,
  buildLibraryPlaybackSummaryFromState,
  createLibraryPlaybackEditorState,
  getProfileDefaultForcedSubtitlesHint,
  getProfileDefaultLanguageHint,
  getProfileDefaultSubtitleLanguageHint,
  getProfileDefaultSubtitleModeHint,
  hasLibraryPlaybackOverride,
  INHERIT_VALUE,
  LANGUAGE_OPTIONS,
  type LibraryPlaybackEditorState,
  NONE_VALUE,
  ORIGINAL_LANGUAGE_LABEL,
  ORIGINAL_LANGUAGE_VALUE,
  SUBTITLE_MODE_OPTIONS,
} from "./libraryPlaybackPreferences";
import { toast } from "sonner";
import { ChevronDown, ChevronRight, Eye, EyeOff, RotateCcw } from "lucide-react";

function sortLibrariesByOrder(libraries: UserLibrary[], ids: number[]) {
  const selected = new Set(ids);
  return libraries.filter((library) => selected.has(library.id)).map((library) => library.id);
}

function PlaybackField({
  label,
  value,
  disabled,
  hint,
  onChange,
  children,
}: {
  label: string;
  value: string;
  disabled?: boolean;
  hint?: string;
  onChange: (value: string) => void;
  children: ReactNode;
}) {
  const controlId = useId();

  return (
    <div className="flex flex-col gap-1.5">
      <Label htmlFor={controlId} className="text-muted-foreground text-xs font-medium">
        {label}
      </Label>
      <Select value={value} onValueChange={onChange} disabled={disabled}>
        <SelectTrigger id={controlId} className="w-full">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>{children}</SelectContent>
      </Select>
      {hint && <p className="text-muted-foreground/70 text-[11px] leading-tight">{hint}</p>}
    </div>
  );
}

function LibraryCard({
  library,
  enabled,
  visibilityDisabled = false,
  preference,
  profile,
  onToggleVisibility,
}: {
  library: UserLibrary;
  enabled: boolean;
  visibilityDisabled?: boolean;
  preference: LibraryPlaybackPreference | null;
  profile: Profile;
  onToggleVisibility: (checked: boolean) => void;
}) {
  const controlId = useId();
  const setPlaybackPreference = useSetLibraryPlaybackPreference();
  const deletePlaybackPreference = useDeleteLibraryPlaybackPreference();
  const [expanded, setExpanded] = useState(false);
  const [editorState, setEditorState] = useState(() =>
    createLibraryPlaybackEditorState(preference),
  );

  useEffect(() => {
    // Keep the inline editor aligned with cached mutations and profile switches.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setEditorState(createLibraryPlaybackEditorState(preference));
  }, [preference, profile.id]);

  const playbackPending = setPlaybackPreference.isPending || deletePlaybackPreference.isPending;
  const summaryText = buildLibraryPlaybackSummaryFromState(editorState);
  const hasOverride = hasLibraryPlaybackOverride(editorState);

  function savePlaybackState(
    nextState: LibraryPlaybackEditorState,
    rollbackState: LibraryPlaybackEditorState,
  ) {
    setEditorState(nextState);

    const onError = () => {
      setEditorState(rollbackState);
      toast.error("Failed to update playback defaults");
    };

    if (!hasLibraryPlaybackOverride(nextState)) {
      deletePlaybackPreference.mutate(library.id, {
        onError,
        onSuccess: () => setExpanded(false),
      });
      return;
    }

    setPlaybackPreference.mutate(
      {
        libraryId: library.id,
        body: buildLibraryPlaybackRequest(nextState),
      },
      { onError },
    );
  }

  function handlePlaybackChange(field: keyof LibraryPlaybackEditorState, value: string) {
    const rollbackState = editorState;
    const nextState = { ...editorState, [field]: value };
    savePlaybackState(nextState, rollbackState);
  }

  function handleReset() {
    savePlaybackState(createLibraryPlaybackEditorState(null), editorState);
  }

  return (
    <div className="surface-panel overflow-hidden rounded-[1.5rem] border-0 transition-colors">
      <div className="flex flex-col gap-4 px-4 py-4 sm:px-5">
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-2">
            <span className="truncate text-sm font-medium">{library.name}</span>
            <Badge variant="outline" className="shrink-0 text-[11px] capitalize">
              {library.type}
            </Badge>
            {hasOverride && (
              <Badge variant="secondary" className="shrink-0 text-[11px]">
                Custom
              </Badge>
            )}
          </div>
          <p className="text-muted-foreground mt-0.5 text-xs leading-relaxed">{summaryText}</p>
        </div>

        <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
          <Button
            type="button"
            size="sm"
            variant="ghost"
            className="text-muted-foreground hover:text-foreground justify-start gap-1 self-start text-xs"
            onClick={() => setExpanded((current) => !current)}
          >
            {expanded ? (
              <ChevronDown className="size-3.5" />
            ) : (
              <ChevronRight className="size-3.5" />
            )}
            <span>{expanded ? "Hide playback overrides" : "Edit playback overrides"}</span>
          </Button>
          <div className="surface-panel-subtle flex items-center justify-between rounded-[1rem] px-3 py-2 sm:min-w-[180px]">
            <Label htmlFor={controlId} className="text-xs font-medium">
              Visible in navigation
            </Label>
            <Switch
              id={controlId}
              checked={enabled}
              disabled={visibilityDisabled}
              onCheckedChange={onToggleVisibility}
            />
          </div>
        </div>
      </div>

      {expanded && (
        <div className="border-border/50 bg-muted/20 border-t px-4 py-4 sm:px-5">
          <p className="text-muted-foreground mb-3 text-xs">
            Override your profile&apos;s playback defaults for this library. Changes save
            automatically.
          </p>
          <div className="grid gap-4 sm:grid-cols-2">
            <PlaybackField
              label="Spoken language"
              value={editorState.audioLanguage}
              disabled={playbackPending}
              hint={getProfileDefaultLanguageHint(profile.language)}
              onChange={(value) => handlePlaybackChange("audioLanguage", value)}
            >
              <SelectItem value={INHERIT_VALUE}>
                {buildInheritedLanguageLabel(profile.language)}
              </SelectItem>
              <SelectItem value={ORIGINAL_LANGUAGE_VALUE}>{ORIGINAL_LANGUAGE_LABEL}</SelectItem>
              {LANGUAGE_OPTIONS.map((language) => (
                <SelectItem key={language.code} value={language.code}>
                  {language.label}
                </SelectItem>
              ))}
            </PlaybackField>

            <PlaybackField
              label="Subtitle language"
              value={editorState.subtitleLanguage}
              disabled={playbackPending}
              hint={getProfileDefaultSubtitleLanguageHint(profile.subtitle_language)}
              onChange={(value) => handlePlaybackChange("subtitleLanguage", value)}
            >
              <SelectItem value={INHERIT_VALUE}>
                {buildInheritedSubtitleLanguageLabel(profile.subtitle_language)}
              </SelectItem>
              <SelectItem value={NONE_VALUE}>None</SelectItem>
              {LANGUAGE_OPTIONS.map((language) => (
                <SelectItem key={language.code} value={language.code}>
                  {language.label}
                </SelectItem>
              ))}
            </PlaybackField>

            <PlaybackField
              label="Subtitle behavior"
              value={editorState.subtitleMode}
              disabled={playbackPending}
              hint={getProfileDefaultSubtitleModeHint(profile.subtitle_mode)}
              onChange={(value) => handlePlaybackChange("subtitleMode", value)}
            >
              <SelectItem value={INHERIT_VALUE}>
                {buildInheritedSubtitleModeLabel(profile.subtitle_mode)}
              </SelectItem>
              {SUBTITLE_MODE_OPTIONS.map((mode) => (
                <SelectItem key={mode.value} value={mode.value}>
                  {mode.label}
                </SelectItem>
              ))}
            </PlaybackField>

            <PlaybackField
              label="Forced subtitles"
              value={editorState.showForcedSubtitles}
              disabled={playbackPending}
              hint={getProfileDefaultForcedSubtitlesHint(profile.show_forced_subtitles)}
              onChange={(value) => handlePlaybackChange("showForcedSubtitles", value)}
            >
              <SelectItem value={INHERIT_VALUE}>
                {buildInheritedShowForcedSubtitlesLabel(profile.show_forced_subtitles)}
              </SelectItem>
              <SelectItem value="on">On</SelectItem>
              <SelectItem value="off">Off</SelectItem>
            </PlaybackField>
          </div>

          {hasOverride && (
            <div className="mt-3 flex justify-end">
              <Button
                type="button"
                size="sm"
                variant="ghost"
                className="text-muted-foreground hover:text-foreground gap-1.5 text-xs"
                disabled={playbackPending}
                onClick={handleReset}
              >
                <RotateCcw className="size-3" />
                Reset to profile defaults
              </Button>
            </div>
          )}
        </div>
      )}
    </div>
  );
}

export default function LibrarySettings() {
  const { data: libraries, isLoading: librariesLoading } = useAvailableUserLibraries();
  const { data: disabledSetting, isLoading: disabledSettingLoading } = useSetting(
    DISABLED_LIBRARY_IDS_SETTING_KEY,
  );
  const { profile: currentProfile, isLoading: profileLoading } = useCurrentProfile();
  const { data: playbackPreferences, isLoading: playbackPrefsLoading } =
    useLibraryPlaybackPreferences({
      enabled: !!currentProfile,
    });
  const setSetting = useSetSetting();
  const [disabledLibraryIDs, setDisabledLibraryIDs] = useState<number[]>([]);

  useEffect(() => {
    const parsedIDs = parseDisabledLibraryIDs(disabledSetting);
    // Keep the editable local order in sync with the latest saved setting.
    // This supports optimistic updates and rollback without changing behavior.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setDisabledLibraryIDs(libraries ? sortLibrariesByOrder(libraries, parsedIDs) : parsedIDs);
  }, [disabledSetting, libraries]);

  function saveDisabledLibraries(nextDisabledLibraryIDs: number[], rollbackIDs: number[]) {
    setDisabledLibraryIDs(nextDisabledLibraryIDs);
    setSetting.mutate(
      {
        key: DISABLED_LIBRARY_IDS_SETTING_KEY,
        value: serializeDisabledLibraryIDs(nextDisabledLibraryIDs),
      },
      {
        onError: () => {
          setDisabledLibraryIDs(rollbackIDs);
          toast.error("Failed to update library visibility");
        },
      },
    );
  }

  function handleLibraryToggle(libraryId: number, enabled: boolean) {
    if (!libraries) return;

    const current = disabledLibraryIDs;
    const next = enabled
      ? current.filter((id) => id !== libraryId)
      : sortLibrariesByOrder(libraries, [...current, libraryId]);

    saveDisabledLibraries(next, current);
  }

  function handleEnableAll() {
    saveDisabledLibraries([], disabledLibraryIDs);
  }

  function handleDisableAll() {
    if (!libraries) return;
    saveDisabledLibraries(
      libraries.map((library) => library.id),
      disabledLibraryIDs,
    );
  }

  if (librariesLoading || disabledSettingLoading || profileLoading || playbackPrefsLoading) {
    return <div className="text-muted-foreground pt-4">Loading libraries...</div>;
  }

  if (!libraries || libraries.length === 0) {
    return (
      <div className="text-muted-foreground pt-4">
        No libraries are available for this account right now.
      </div>
    );
  }

  if (!currentProfile) {
    return <div className="text-muted-foreground pt-4">Choose a profile to manage libraries.</div>;
  }

  const visibleCount = libraries.filter(
    (library) => !disabledLibraryIDs.includes(library.id),
  ).length;
  const preferencesByLibraryId = new Map(
    (playbackPreferences ?? []).map((preference) => [preference.library_id, preference]),
  );

  return (
    <div className="space-y-6">
      <div className="space-y-3">
        <h2 className="text-2xl font-semibold tracking-tight sm:text-3xl">Libraries</h2>
        <p className="text-muted-foreground max-w-2xl text-sm leading-relaxed">
          Toggle which libraries appear in your navigation and customize playback defaults per
          library.
        </p>
      </div>

      <div className="surface-panel-subtle flex flex-col gap-4 rounded-[1.4rem] p-4 sm:flex-row sm:items-center sm:justify-between">
        <p className="text-muted-foreground text-sm leading-relaxed">
          <span className="text-foreground font-medium">{visibleCount}</span> of {libraries.length}{" "}
          libraries visible for this profile.
        </p>
        <div className="flex flex-col gap-2 sm:flex-row">
          <Button
            size="sm"
            variant="outline"
            className="gap-1.5 sm:min-w-[120px]"
            onClick={handleEnableAll}
            disabled={setSetting.isPending || visibleCount === libraries.length}
          >
            <Eye className="size-3.5" />
            Show all
          </Button>
          <Button
            size="sm"
            variant="outline"
            className="gap-1.5 sm:min-w-[120px]"
            onClick={handleDisableAll}
            disabled={setSetting.isPending || visibleCount === 0}
          >
            <EyeOff className="size-3.5" />
            Hide all
          </Button>
        </div>
      </div>

      <div className="space-y-3">
        {libraries.map((library) => {
          const enabled = !disabledLibraryIDs.includes(library.id);
          return (
            <LibraryCard
              key={`${currentProfile.id}:${library.id}`}
              library={library}
              enabled={enabled}
              visibilityDisabled={setSetting.isPending}
              preference={preferencesByLibraryId.get(library.id) ?? null}
              profile={currentProfile}
              onToggleVisibility={(checked) => handleLibraryToggle(library.id, checked)}
            />
          );
        })}
      </div>
    </div>
  );
}
