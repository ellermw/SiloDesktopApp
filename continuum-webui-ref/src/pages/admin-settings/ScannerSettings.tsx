import { useMemo } from "react";
import { useSettingsForm } from "@/hooks/useSettingsForm";
import { SettingField } from "./SettingField";
import { SaveBar } from "./SaveBar";
import { FieldGroup } from "./FieldGroup";

const KEYS = [
  "scanner.schedule",
  "scanner.workers",
  "scanner.file_removal_grace",
  "matcher.workers",
  "matcher.batch_size",
];

export default function ScannerSettings() {
  const form = useSettingsForm({ keys: useMemo(() => KEYS, []) });

  if (form.isLoading) return <div>Loading...</div>;

  return (
    <div className="flex h-full flex-col">
      <div className="mb-6 space-y-2">
        <h2 className="text-xl font-semibold tracking-tight">Scanner & Matcher</h2>
        <p className="text-muted-foreground text-sm leading-relaxed">
          Schedule library scans and control how aggressively metadata matching runs.
        </p>
      </div>

      <div className="flex-1 space-y-6">
        <FieldGroup label="Scanner">
          <SettingField
            label="Scanner Schedule"
            hint="Cron expression, e.g. */15 * * * *"
            value={form.getValue("scanner.schedule")}
            onChange={(v) => form.setValue("scanner.schedule", v)}
          />
          <SettingField
            label="Scanner Workers"
            type="number"
            value={form.getValue("scanner.workers")}
            onChange={(v) => form.setValue("scanner.workers", v)}
          />
          <SettingField
            label="File Removal Grace"
            type="duration"
            hint="e.g. 24h"
            value={form.getValue("scanner.file_removal_grace")}
            onChange={(v) => form.setValue("scanner.file_removal_grace", v)}
          />
        </FieldGroup>

        <FieldGroup label="Matcher">
          <SettingField
            label="Matcher Workers"
            type="number"
            value={form.getValue("matcher.workers")}
            onChange={(v) => form.setValue("matcher.workers", v)}
          />
          <SettingField
            label="Matcher Batch Size"
            type="number"
            value={form.getValue("matcher.batch_size")}
            onChange={(v) => form.setValue("matcher.batch_size", v)}
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
