import { useMemo } from "react";
import { useSettingsForm } from "@/hooks/useSettingsForm";
import { SettingField } from "./SettingField";
import { SaveBar } from "./SaveBar";
import { FieldGroup } from "./FieldGroup";

const KEYS = [
  "playback.ffmpeg_path",
  "playback.transcode_dir",
  "playback.hw_accel",
  "playback.transcode_enabled",
  "playback.allow_hevc_encoding",
  "allow_4k_transcode",
  "enable_transcode_throttle",
  "transcode_throttle_seconds",
  "playback.transcode_ahead_segments",
  "playback.segment_duration",
  "playback.watched_threshold",
];

export default function PlaybackSettings() {
  const form = useSettingsForm({ keys: useMemo(() => KEYS, []) });

  if (form.isLoading) return <div>Loading...</div>;

  return (
    <div className="flex h-full flex-col">
      <div className="mb-6 space-y-2">
        <h2 className="text-xl font-semibold tracking-tight">Playback</h2>
        <p className="text-muted-foreground text-sm leading-relaxed">
          Configure transcoding, segment generation, and watched-state behavior.
        </p>
      </div>

      <div className="flex-1 space-y-6">
        <FieldGroup label="Transcoding">
          <SettingField
            label="FFmpeg Path"
            value={form.getValue("playback.ffmpeg_path")}
            onChange={(v) => form.setValue("playback.ffmpeg_path", v)}
          />
          <SettingField
            label="Transcode Directory"
            value={form.getValue("playback.transcode_dir")}
            onChange={(v) => form.setValue("playback.transcode_dir", v)}
          />
          <SettingField
            label="Hardware Acceleration"
            hint="auto, vaapi, nvenc, qsv, none"
            value={form.getValue("playback.hw_accel")}
            onChange={(v) => form.setValue("playback.hw_accel", v)}
          />
          <SettingField
            label="Transcoding Enabled"
            type="toggle"
            value={form.getValue("playback.transcode_enabled")}
            onChange={(v) => form.setValue("playback.transcode_enabled", v)}
          />
          <SettingField
            label="Allow HEVC Encoding"
            type="toggle"
            value={form.getValue("playback.allow_hevc_encoding")}
            onChange={(v) => form.setValue("playback.allow_hevc_encoding", v)}
          />
          <SettingField
            label="Allow 4K Transcoding"
            type="toggle"
            value={form.getValue("allow_4k_transcode")}
            onChange={(v) => form.setValue("allow_4k_transcode", v)}
          />
          <SettingField
            label="Enable Transcode Throttling"
            type="toggle"
            value={form.getValue("enable_transcode_throttle")}
            onChange={(v) => form.setValue("enable_transcode_throttle", v)}
          />
          {form.getValue("enable_transcode_throttle") === "true" && (
            <SettingField
              label="Throttle Buffer (seconds)"
              type="number"
              hint="How many seconds ahead FFmpeg transcodes before pausing. Minimum: 60."
              value={form.getValue("transcode_throttle_seconds")}
              onChange={(v) => form.setValue("transcode_throttle_seconds", v)}
            />
          )}
        </FieldGroup>

        <FieldGroup label="Segments">
          <SettingField
            label="Transcode Ahead Segments"
            type="number"
            value={form.getValue("playback.transcode_ahead_segments")}
            onChange={(v) => form.setValue("playback.transcode_ahead_segments", v)}
          />
          <SettingField
            label="Segment Duration"
            type="number"
            value={form.getValue("playback.segment_duration")}
            onChange={(v) => form.setValue("playback.segment_duration", v)}
          />
        </FieldGroup>

        <FieldGroup label="Behavior">
          <SettingField
            label="Watched Threshold (%)"
            type="number"
            hint="Mark as watched after this % is played (default: 90)"
            value={form.getValue("playback.watched_threshold")}
            onChange={(v) => form.setValue("playback.watched_threshold", v)}
          />
        </FieldGroup>
      </div>

      <SaveBar
        dirtyCount={form.dirtyCount}
        onSave={form.save}
        onDiscard={form.discard}
        isSaving={form.isSaving}
        restartRequired={form.restartRequired}
      />
    </div>
  );
}
