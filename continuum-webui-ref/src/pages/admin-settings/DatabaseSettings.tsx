import { useMemo, useState } from "react";
import { useSettingsForm } from "@/hooks/useSettingsForm";
import { SettingField } from "./SettingField";
import { SaveBar } from "./SaveBar";
import { FieldGroup } from "./FieldGroup";

const KEYS = [
  "database.max_connections",
  "redis.url",
  "userdb.backend",
  "userdb.pool_max_open",
  "userdb.idle_timeout",
  "userdb.litestream_sync",
  "userdb.stale_grace_seconds",
];

export default function DatabaseSettings() {
  const form = useSettingsForm({ keys: useMemo(() => KEYS, []) });
  const redisUrl = form.getValue("redis.url");
  const [redisEnabled, setRedisEnabled] = useState(() => redisUrl.trim() !== "");
  const effectiveRedisEnabled = redisEnabled || redisUrl.trim() !== "";

  if (form.isLoading) return <div>Loading...</div>;

  return (
    <div className="flex h-full flex-col">
      <div className="mb-6 space-y-2">
        <h2 className="text-xl font-semibold tracking-tight">Database</h2>
        <p className="text-muted-foreground text-sm leading-relaxed">
          Configure connection pooling, Redis, and user database replication behavior.
        </p>
      </div>

      <div className="flex-1 space-y-6">
        <FieldGroup label="Main Database">
          <SettingField
            label="Max Connections"
            type="number"
            value={form.getValue("database.max_connections")}
            onChange={(v) => form.setValue("database.max_connections", v)}
          />
        </FieldGroup>

        <FieldGroup label="Redis">
          <SettingField
            label="Enable Redis"
            type="toggle"
            hint="Leave disabled to run without Redis"
            value={effectiveRedisEnabled ? "true" : "false"}
            onChange={(value) => {
              if (value === "true") {
                setRedisEnabled(true);
                return;
              }
              setRedisEnabled(false);
              form.setValue("redis.url", "");
            }}
          />
          {effectiveRedisEnabled && (
            <SettingField
              label="Connection URL"
              type="password"
              hint="redis://host:6379"
              value={redisUrl}
              onChange={(v) => form.setValue("redis.url", v)}
              sensitiveConfigured={form.sensitiveConfigured.includes("redis.url")}
            />
          )}
        </FieldGroup>

        <FieldGroup label="User Database">
          <SettingField
            label="User DB Backend"
            hint="postgres or sqlite"
            value={form.getValue("userdb.backend")}
            onChange={(v) => form.setValue("userdb.backend", v)}
          />
          <SettingField
            label="Pool Max Open"
            type="number"
            value={form.getValue("userdb.pool_max_open")}
            onChange={(v) => form.setValue("userdb.pool_max_open", v)}
          />
          <SettingField
            label="Idle Timeout"
            type="duration"
            hint="e.g. 12h"
            value={form.getValue("userdb.idle_timeout")}
            onChange={(v) => form.setValue("userdb.idle_timeout", v)}
          />
          <SettingField
            label="Litestream Sync Interval"
            type="duration"
            hint="e.g. 1s"
            value={form.getValue("userdb.litestream_sync")}
            onChange={(v) => form.setValue("userdb.litestream_sync", v)}
          />
          <SettingField
            label="Stale Grace Seconds"
            type="number"
            value={form.getValue("userdb.stale_grace_seconds")}
            onChange={(v) => form.setValue("userdb.stale_grace_seconds", v)}
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
