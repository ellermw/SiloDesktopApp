import { useMemo } from "react";
import { useSettingsForm } from "@/hooks/useSettingsForm";
import { SettingField } from "./SettingField";
import { SaveBar } from "./SaveBar";
import { FieldGroup } from "./FieldGroup";

const KEYS = [
  "jellyfin_compat.public_url",
  "jellyfin_compat.server_name",
  "jellyfin_compat.server_id",
  "jellyfin_compat.emulated_server_version",
  "jellyfin_compat.session_ttl",
  "jellyfin_compat.playback_session_ttl",
];

export default function JellyfinSettings() {
  const form = useSettingsForm({ keys: useMemo(() => KEYS, []) });

  if (form.isLoading) return <div>Loading...</div>;

  return (
    <div className="flex h-full flex-col">
      <div className="mb-6 space-y-2">
        <h2 className="text-xl font-semibold tracking-tight">Jellyfin Compat</h2>
        <p className="text-muted-foreground text-sm leading-relaxed">
          Tune the compatibility layer exposed to Jellyfin-compatible clients.
        </p>
      </div>

      <div className="flex-1">
        <FieldGroup label="Server Identity">
          <SettingField
            label="Public URL"
            value={form.getValue("jellyfin_compat.public_url")}
            onChange={(v) => form.setValue("jellyfin_compat.public_url", v)}
          />
          <SettingField
            label="Server Name"
            value={form.getValue("jellyfin_compat.server_name")}
            onChange={(v) => form.setValue("jellyfin_compat.server_name", v)}
          />
          <SettingField
            label="Server ID"
            value={form.getValue("jellyfin_compat.server_id")}
            onChange={(v) => form.setValue("jellyfin_compat.server_id", v)}
          />
          <SettingField
            label="Emulated Server Version"
            value={form.getValue("jellyfin_compat.emulated_server_version")}
            onChange={(v) => form.setValue("jellyfin_compat.emulated_server_version", v)}
          />
        </FieldGroup>

        <div className="mt-6">
          <FieldGroup label="Session Lifetimes">
            <SettingField
              label="Session TTL"
              type="duration"
              hint="e.g. 24h"
              value={form.getValue("jellyfin_compat.session_ttl")}
              onChange={(v) => form.setValue("jellyfin_compat.session_ttl", v)}
            />
            <SettingField
              label="Playback Session TTL"
              type="duration"
              hint="e.g. 6h"
              value={form.getValue("jellyfin_compat.playback_session_ttl")}
              onChange={(v) => form.setValue("jellyfin_compat.playback_session_ttl", v)}
            />
          </FieldGroup>
        </div>
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
