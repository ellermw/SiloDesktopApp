import { useMemo } from "react";
import { useSettingsForm } from "@/hooks/useSettingsForm";
import { SettingField } from "./SettingField";
import { SaveBar } from "./SaveBar";
import { Tabs, TabsList, TabsTrigger, TabsContent } from "@/components/ui/tabs";

const KEYS = [
  // MetaDB Posters
  "s3.metadata_endpoint",
  "s3.metadata_region",
  "s3.metadata_path_style",
  "s3.metadata_bucket",
  "s3.metadata_access_key",
  "s3.metadata_secret_key",
  "s3.metadata_presign_expiry",
  // General Purpose
  "s3.operational_endpoint",
  "s3.operational_region",
  "s3.operational_path_style",
  "s3.operational_bucket",
  "s3.operational_access_key",
  "s3.operational_secret_key",
  // User DB
  "s3.user_db_endpoint",
  "s3.user_db_region",
  "s3.user_db_path_style",
  "s3.user_db_bucket",
  "s3.user_db_access_key",
  "s3.user_db_secret_key",
];

export default function StorageSettings() {
  const form = useSettingsForm({ keys: useMemo(() => KEYS, []) });

  if (form.isLoading) return <div>Loading...</div>;

  return (
    <div className="flex h-full flex-col">
      <div className="mb-6 space-y-2">
        <h2 className="text-xl font-semibold tracking-tight">Storage</h2>
        <p className="text-muted-foreground text-sm leading-relaxed">
          S3-compatible object storage for artwork, operational exports, and future replicated
          data.
        </p>
      </div>

      <div className="flex-1 space-y-6">
        <Tabs defaultValue="metadata">
          <TabsList className="surface-panel-subtle h-auto gap-1 rounded-[1.1rem] border-0 bg-transparent p-1">
            <TabsTrigger value="metadata">MetaDB Posters</TabsTrigger>
            <TabsTrigger value="operational">General Purpose</TabsTrigger>
            <TabsTrigger value="userdb" disabled>
              User DB
            </TabsTrigger>
          </TabsList>

          <TabsContent value="metadata" className="space-y-1 pt-4">
            <p className="text-muted-foreground mb-4 text-sm">
              Used by MetaDB for storing and serving poster/artwork images.
              Generates presigned URLs for clients to fetch images directly from
              S3.
            </p>
            <SettingField
              label="Endpoint"
              value={form.getValue("s3.metadata_endpoint")}
              onChange={(v) => form.setValue("s3.metadata_endpoint", v)}
            />
            <SettingField
              label="Region"
              value={form.getValue("s3.metadata_region")}
              onChange={(v) => form.setValue("s3.metadata_region", v)}
            />
            <SettingField
              label="Path Style"
              type="toggle"
              value={form.getValue("s3.metadata_path_style")}
              onChange={(v) => form.setValue("s3.metadata_path_style", v)}
            />
            <SettingField
              label="Bucket"
              value={form.getValue("s3.metadata_bucket")}
              onChange={(v) => form.setValue("s3.metadata_bucket", v)}
            />
            <SettingField
              label="Access Key"
              type="password"
              value={form.getValue("s3.metadata_access_key")}
              onChange={(v) => form.setValue("s3.metadata_access_key", v)}
              sensitiveConfigured={form.sensitiveConfigured.includes(
                "s3.metadata_access_key",
              )}
            />
            <SettingField
              label="Secret Key"
              type="password"
              value={form.getValue("s3.metadata_secret_key")}
              onChange={(v) => form.setValue("s3.metadata_secret_key", v)}
              sensitiveConfigured={form.sensitiveConfigured.includes(
                "s3.metadata_secret_key",
              )}
            />
            <SettingField
              label="Presign Expiry"
              type="duration"
              hint="e.g. 4h"
              value={form.getValue("s3.metadata_presign_expiry")}
              onChange={(v) => form.setValue("s3.metadata_presign_expiry", v)}
            />
          </TabsContent>

          <TabsContent value="operational" className="space-y-1 pt-4">
            <p className="text-muted-foreground mb-4 text-sm">
              General-purpose storage for operational tasks such as catalog
              import/export.
            </p>
            <SettingField
              label="Endpoint"
              value={form.getValue("s3.operational_endpoint")}
              onChange={(v) => form.setValue("s3.operational_endpoint", v)}
            />
            <SettingField
              label="Region"
              value={form.getValue("s3.operational_region")}
              onChange={(v) => form.setValue("s3.operational_region", v)}
            />
            <SettingField
              label="Path Style"
              type="toggle"
              value={form.getValue("s3.operational_path_style")}
              onChange={(v) => form.setValue("s3.operational_path_style", v)}
            />
            <SettingField
              label="Bucket"
              value={form.getValue("s3.operational_bucket")}
              onChange={(v) => form.setValue("s3.operational_bucket", v)}
            />
            <SettingField
              label="Access Key"
              type="password"
              value={form.getValue("s3.operational_access_key")}
              onChange={(v) => form.setValue("s3.operational_access_key", v)}
              sensitiveConfigured={form.sensitiveConfigured.includes(
                "s3.operational_access_key",
              )}
            />
            <SettingField
              label="Secret Key"
              type="password"
              value={form.getValue("s3.operational_secret_key")}
              onChange={(v) => form.setValue("s3.operational_secret_key", v)}
              sensitiveConfigured={form.sensitiveConfigured.includes(
                "s3.operational_secret_key",
              )}
            />
          </TabsContent>

          <TabsContent value="userdb" className="space-y-1 pt-4 opacity-50">
            <p className="text-muted-foreground mb-4 text-sm">
              Reserved for Litestream user database replication. Not currently in
              use.
            </p>
            <SettingField
              label="Endpoint"
              value={form.getValue("s3.user_db_endpoint")}
              onChange={(v) => form.setValue("s3.user_db_endpoint", v)}
              disabled
            />
            <SettingField
              label="Region"
              value={form.getValue("s3.user_db_region")}
              onChange={(v) => form.setValue("s3.user_db_region", v)}
              disabled
            />
            <SettingField
              label="Path Style"
              type="toggle"
              value={form.getValue("s3.user_db_path_style")}
              onChange={(v) => form.setValue("s3.user_db_path_style", v)}
              disabled
            />
            <SettingField
              label="Bucket"
              value={form.getValue("s3.user_db_bucket")}
              onChange={(v) => form.setValue("s3.user_db_bucket", v)}
              disabled
            />
            <SettingField
              label="Access Key"
              type="password"
              value={form.getValue("s3.user_db_access_key")}
              onChange={(v) => form.setValue("s3.user_db_access_key", v)}
              sensitiveConfigured={form.sensitiveConfigured.includes(
                "s3.user_db_access_key",
              )}
              disabled
            />
            <SettingField
              label="Secret Key"
              type="password"
              value={form.getValue("s3.user_db_secret_key")}
              onChange={(v) => form.setValue("s3.user_db_secret_key", v)}
              sensitiveConfigured={form.sensitiveConfigured.includes(
                "s3.user_db_secret_key",
              )}
              disabled
            />
          </TabsContent>
        </Tabs>
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
