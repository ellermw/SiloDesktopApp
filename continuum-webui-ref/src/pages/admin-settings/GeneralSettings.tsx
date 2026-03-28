import { useMemo } from "react";
import { useSettingsForm } from "@/hooks/useSettingsForm";
import { SettingField } from "./SettingField";
import { SaveBar } from "./SaveBar";
import { FieldGroup } from "./FieldGroup";

const KEYS = [
  "auth.access_token_expiry",
  "auth.refresh_token_expiry",
  "server.log_level",
  "server.log_quiet",
];

export default function GeneralSettings() {
  const form = useSettingsForm({ keys: useMemo(() => KEYS, []) });

  if (form.isLoading) return <div>Loading...</div>;

  return (
    <div className="flex h-full flex-col">
      <div className="mb-6 space-y-2">
        <h2 className="text-xl font-semibold tracking-tight">General</h2>
        <p className="text-muted-foreground text-sm leading-relaxed">
          Authentication, token lifetimes, and server logging behavior.
        </p>
      </div>

      <div className="flex-1 space-y-6">
        <FieldGroup label="Authentication">
          <SettingField
            label="Access Token Expiry"
            type="duration"
            hint="e.g. 1h, 30m"
            value={form.getValue("auth.access_token_expiry")}
            onChange={(v) => form.setValue("auth.access_token_expiry", v)}
          />
          <SettingField
            label="Refresh Token Expiry"
            type="duration"
            hint="e.g. 30d, 720h"
            value={form.getValue("auth.refresh_token_expiry")}
            onChange={(v) => form.setValue("auth.refresh_token_expiry", v)}
          />
        </FieldGroup>

        <FieldGroup label="Logging">
          <SettingField
            label="Log Level"
            type="select"
            value={form.getValue("server.log_level")}
            onChange={(v) => form.setValue("server.log_level", v)}
            options={[
              { value: "debug", label: "Debug" },
              { value: "info", label: "Info" },
              { value: "warn", label: "Warn" },
              { value: "error", label: "Error" },
            ]}
          />
          <SettingField
            label="Quiet Subsystems"
            hint="Comma-separated subsystem prefixes to silence"
            value={form.getValue("server.log_quiet")}
            onChange={(v) => form.setValue("server.log_quiet", v)}
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
