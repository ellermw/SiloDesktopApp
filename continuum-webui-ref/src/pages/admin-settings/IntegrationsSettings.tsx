import { useState, useEffect, useMemo } from "react";
import type { FormEvent } from "react";
import { Tabs, TabsList, TabsTrigger, TabsContent } from "@/components/ui/tabs";
import { useSettingsForm } from "@/hooks/useSettingsForm";
import { SettingField } from "./SettingField";
import { SaveBar } from "./SaveBar";
import { FieldGroup } from "./FieldGroup";

// --- Subtitles imports ---
import {
  useSubtitleProviders,
  useUpdateSubtitleProvider,
  useTestSubtitleProvider,
} from "@/hooks/queries/admin/subtitles";
import type { SubtitleProviderConfig } from "@/api/types";

// --- Providers imports ---
import type { MetadataProvider, CreateProviderRequest } from "@/api/types";
import {
  useAdminProviders,
  useCreateProvider,
  useUpdateProvider,
  useDeleteProvider,
} from "@/hooks/queries/admin/providers";
import { ConfirmDialog } from "@/components/ConfirmDialog";

// --- Recommendations imports ---
import { Button } from "@/components/ui/button";
import { Switch } from "@/components/ui/switch";
import { Label } from "@/components/ui/label";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Eye,
  EyeOff,
  CircleCheck,
  CircleAlert,
  Plus,
  Pencil,
  Trash2,
} from "lucide-react";

// ============================================================================
// Tab 1: Services
// ============================================================================

const SERVICES_KEYS = ["metadb.url", "metadb.api_key", "tmdb.api_key"];

function ServicesContent() {
  const form = useSettingsForm({ keys: useMemo(() => SERVICES_KEYS, []) });

  if (form.isLoading) return <div>Loading...</div>;

  return (
    <div className="flex h-full flex-col">
      <div className="flex-1 space-y-6">
        <FieldGroup label="MetaDB">
          <SettingField
            label="URL"
            value={form.getValue("metadb.url")}
            onChange={(v) => form.setValue("metadb.url", v)}
          />
          <SettingField
            label="API Key"
            type="password"
            value={form.getValue("metadb.api_key")}
            onChange={(v) => form.setValue("metadb.api_key", v)}
            sensitiveConfigured={form.sensitiveConfigured.includes("metadb.api_key")}
          />
        </FieldGroup>

        <FieldGroup label="TMDB">
          <SettingField
            label="API Key"
            type="password"
            hint="Shared by TMDB metadata providers and TMDB collection/trending features."
            value={form.getValue("tmdb.api_key")}
            onChange={(v) => form.setValue("tmdb.api_key", v)}
            sensitiveConfigured={form.sensitiveConfigured.includes("tmdb.api_key")}
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

// ============================================================================
// Tab 2: Subtitles
// ============================================================================

const SUBTITLE_PROVIDER_NAMES: Record<string, string> = {
  opensubtitles: "OpenSubtitles",
  subdl: "SubDL",
  subsource: "SubSource",
};

interface SubtitleProviderFormState {
  enabled: boolean;
  api_key: string;
  username: string;
  password: string;
  showApiKey: boolean;
}

interface SubtitleTestResult {
  success: boolean;
  error?: string;
}

function defaultSubtitleFormState(config: SubtitleProviderConfig): SubtitleProviderFormState {
  return {
    enabled: config.enabled,
    api_key: "",
    username: "",
    password: "",
    showApiKey: false,
  };
}

function SubtitleCredentialStatus({ configured }: { configured: boolean }) {
  if (configured) {
    return (
      <span className="text-muted-foreground inline-flex items-center gap-1 text-xs">
        <CircleCheck className="h-3.5 w-3.5 text-green-500" />
        Configured
      </span>
    );
  }
  return (
    <span className="text-muted-foreground inline-flex items-center gap-1 text-xs">
      <CircleAlert className="h-3.5 w-3.5 text-yellow-500" />
      Not configured
    </span>
  );
}

function SubtitleProviderCard({ config }: { config: SubtitleProviderConfig }) {
  const [form, setForm] = useState<SubtitleProviderFormState>(() =>
    defaultSubtitleFormState(config),
  );
  const [testResult, setTestResult] = useState<SubtitleTestResult | null>(null);

  const updateProvider = useUpdateSubtitleProvider();
  const testProvider = useTestSubtitleProvider();

  useEffect(() => {
    setForm((prev) => ({
      ...prev,
      enabled: config.enabled,
    }));
  }, [config.enabled]);

  const providerName = config.provider_name;
  const displayName = SUBTITLE_PROVIDER_NAMES[providerName] ?? providerName;
  const isOpenSubtitles = providerName === "opensubtitles";

  function handleSave() {
    updateProvider.mutate({
      provider: providerName,
      config: {
        enabled: form.enabled,
        ...(isOpenSubtitles
          ? { username: form.username, password: form.password }
          : { api_key: form.api_key }),
      },
    });
  }

  function handleTest() {
    setTestResult(null);
    testProvider.mutate(providerName, {
      onSuccess: (result) => {
        setTestResult({ success: result.success, error: result.error });
      },
      onError: (err) => {
        setTestResult({
          success: false,
          error: err instanceof Error ? err.message : "Test failed",
        });
      },
    });
  }

  return (
    <div className="border-border bg-surface space-y-4 rounded-lg border px-5 py-4">
      {/* Header row */}
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <span className="text-sm font-semibold">{displayName}</span>
          <SubtitleCredentialStatus
            configured={isOpenSubtitles ? config.has_credentials : config.has_api_key}
          />
        </div>
        <div className="flex items-center gap-2">
          <Label htmlFor={`${providerName}-enabled`} className="text-sm font-medium">
            {form.enabled ? "Enabled" : "Disabled"}
          </Label>
          <Switch
            id={`${providerName}-enabled`}
            checked={form.enabled}
            onCheckedChange={(checked) => setForm((prev) => ({ ...prev, enabled: checked }))}
          />
        </div>
      </div>

      {/* Credentials: username/password for OpenSubtitles, API key for others */}
      {isOpenSubtitles ? (
        <>
          <div className="space-y-1">
            <Label htmlFor={`${providerName}-username`} className="text-sm font-medium">
              Username
            </Label>
            <Input
              id={`${providerName}-username`}
              type="text"
              placeholder={
                config.has_credentials ? "Leave blank to keep current" : "OpenSubtitles username"
              }
              value={form.username}
              onChange={(e) => setForm((prev) => ({ ...prev, username: e.target.value }))}
            />
          </div>
          <div className="space-y-1">
            <Label htmlFor={`${providerName}-password`} className="text-sm font-medium">
              Password
            </Label>
            <Input
              id={`${providerName}-password`}
              type="password"
              placeholder={
                config.has_credentials ? "Leave blank to keep current" : "OpenSubtitles password"
              }
              value={form.password}
              onChange={(e) => setForm((prev) => ({ ...prev, password: e.target.value }))}
            />
          </div>
        </>
      ) : (
        <div className="space-y-1">
          <Label htmlFor={`${providerName}-api-key`} className="text-sm font-medium">
            API Key
          </Label>
          <div className="flex items-center gap-2">
            <Input
              id={`${providerName}-api-key`}
              type={form.showApiKey ? "text" : "password"}
              placeholder={config.has_api_key ? "Leave blank to keep current" : "Enter API key"}
              value={form.api_key}
              onChange={(e) => setForm((prev) => ({ ...prev, api_key: e.target.value }))}
              className="flex-1"
            />
            <Button
              variant="ghost"
              size="icon"
              type="button"
              onClick={() => setForm((prev) => ({ ...prev, showApiKey: !prev.showApiKey }))}
            >
              {form.showApiKey ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
            </Button>
          </div>
        </div>
      )}

      {/* Actions */}
      <div className="flex items-center gap-3 pt-1">
        <Button variant="outline" onClick={handleTest} disabled={testProvider.isPending}>
          {testProvider.isPending ? "Testing..." : "Test Connection"}
        </Button>
        <Button onClick={handleSave} disabled={updateProvider.isPending}>
          {updateProvider.isPending ? "Saving..." : "Save"}
        </Button>
        {testResult !== null && (
          <span className={`text-sm ${testResult.success ? "text-green-500" : "text-red-500"}`}>
            {testResult.success
              ? "Connection successful"
              : (testResult.error ?? "Connection failed")}
          </span>
        )}
      </div>
    </div>
  );
}

const SUBTITLE_PROVIDER_ORDER = ["opensubtitles", "subdl", "subsource"];

function SubtitlesContent() {
  const { data, isLoading } = useSubtitleProviders();

  if (isLoading) return <div>Loading...</div>;

  const providers = data?.providers ?? [];

  // Sort by known order, putting unknown providers at end
  const sorted = [...providers].sort((a, b) => {
    const ai = SUBTITLE_PROVIDER_ORDER.indexOf(a.provider_name);
    const bi = SUBTITLE_PROVIDER_ORDER.indexOf(b.provider_name);
    if (ai === -1 && bi === -1) return 0;
    if (ai === -1) return 1;
    if (bi === -1) return -1;
    return ai - bi;
  });

  return (
    <div className="space-y-4">
      <p className="text-muted-foreground max-w-3xl text-sm">
        Configure external subtitle search providers. Credentials are stored securely and never
        returned by the API.
      </p>

      <div className="max-w-2xl space-y-4">
        {sorted.map((provider) => (
          <SubtitleProviderCard key={provider.provider_name} config={provider} />
        ))}
        {sorted.length === 0 && (
          <div className="border-border bg-surface rounded-lg border px-5 py-4">
            <p className="text-muted-foreground text-sm">No subtitle providers configured.</p>
          </div>
        )}
      </div>
    </div>
  );
}

// ============================================================================
// Tab 3: Providers
// ============================================================================

interface MetadataSettingsFields {
  [key: string]: { label: string; placeholder: string; sensitive?: boolean };
}

const metadataSettingsFieldsByType: Record<string, MetadataSettingsFields> = {
  metadb: {
    url: { label: "URL", placeholder: "https://mdp.zenterprise.org" },
    api_key: {
      label: "API Key",
      placeholder: "mdb_...",
      sensitive: true,
    },
  },
  s3: {
    bucket: { label: "Bucket", placeholder: "metadata" },
  },
  tmdb: {},
  tvdb: {
    api_key: {
      label: "API Key",
      placeholder: "TVDB v4 API key",
      sensitive: true,
    },
    pin: {
      label: "Subscriber PIN",
      placeholder: "Your TVDB subscriber PIN",
      sensitive: true,
    },
  },
};

function MetadataProviderForm({
  provider,
  onClose,
}: {
  provider: MetadataProvider | null;
  onClose: () => void;
}) {
  const [slug, setSlug] = useState(provider?.slug ?? "");
  const [providerType, setProviderType] = useState(provider?.provider_type ?? "metadb");
  const [enabled, setEnabled] = useState(provider?.enabled ?? true);
  const [settings, setSettings] = useState<Record<string, string>>(provider?.settings ?? {});
  const createMutation = useCreateProvider();
  const updateMutation = useUpdateProvider();
  const isPending = createMutation.isPending || updateMutation.isPending;

  const fields = metadataSettingsFieldsByType[providerType] ?? {};

  function updateSetting(key: string, value: string) {
    setSettings((prev) => ({ ...prev, [key]: value }));
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (provider) {
      const cleanSettings: Record<string, string> = {};
      for (const [k, v] of Object.entries(settings)) {
        if (!v.includes("****")) {
          cleanSettings[k] = v;
        }
      }
      updateMutation.mutate(
        {
          id: provider.id,
          body: {
            slug,
            enabled,
            settings: Object.keys(cleanSettings).length > 0 ? cleanSettings : undefined,
          },
        },
        { onSuccess: onClose },
      );
    } else {
      const body: CreateProviderRequest = {
        slug,
        provider_type: providerType,
        settings,
      };
      createMutation.mutate(body, { onSuccess: onClose });
    }
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      <div className="space-y-2">
        <Label>Slug</Label>
        <Input
          value={slug}
          onChange={(e) => setSlug(e.target.value)}
          placeholder="metadb"
          required
        />
      </div>
      <div className="space-y-2">
        <Label>Type</Label>
        <Select
          value={providerType}
          onValueChange={(v) => {
            setProviderType(v);
            setSettings({});
          }}
          disabled={!!provider}
        >
          <SelectTrigger>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="metadb">MetaDB</SelectItem>
            <SelectItem value="s3">S3</SelectItem>
            <SelectItem value="tmdb">TMDB</SelectItem>
            <SelectItem value="tvdb">TVDB</SelectItem>
          </SelectContent>
        </Select>
      </div>
      {provider && (
        <div className="flex items-center gap-2">
          <Switch checked={enabled} onCheckedChange={setEnabled} />
          <Label>Enabled</Label>
        </div>
      )}
      {providerType === "tmdb" && (
        <p className="text-muted-foreground text-sm">
          TMDB providers use the shared API key from Integrations.
        </p>
      )}
      {Object.entries(fields).map(([key, field]) => (
        <div key={key} className="space-y-2">
          <Label>{field.label}</Label>
          <Input
            type={field.sensitive ? "password" : "text"}
            value={settings[key] ?? ""}
            onChange={(e) => updateSetting(key, e.target.value)}
            placeholder={field.placeholder}
          />
        </div>
      ))}
      <Button type="submit" className="w-full" disabled={isPending}>
        {isPending ? "Saving..." : "Save"}
      </Button>
    </form>
  );
}

function ProvidersContent() {
  const { data: providers = [], isLoading } = useAdminProviders();
  const [dialogOpen, setDialogOpen] = useState(false);
  const [editingProvider, setEditingProvider] = useState<MetadataProvider | null>(null);
  const [confirmDeleteProvider, setConfirmDeleteProvider] = useState<MetadataProvider | null>(null);
  const deleteMutation = useDeleteProvider();

  function handleDelete(provider: MetadataProvider) {
    setConfirmDeleteProvider(provider);
  }

  if (isLoading) return <div>Loading providers...</div>;

  return (
    <div className="space-y-6">
      <ConfirmDialog
        open={confirmDeleteProvider !== null}
        onOpenChange={(open) => {
          if (!open) setConfirmDeleteProvider(null);
        }}
        title="Delete provider"
        description={`Delete provider "${confirmDeleteProvider?.slug}"? This action cannot be undone.`}
        confirmLabel="Delete"
        variant="destructive"
        onConfirm={() => {
          if (confirmDeleteProvider) deleteMutation.mutate(confirmDeleteProvider.id);
          setConfirmDeleteProvider(null);
        }}
      />
      <div className="flex items-center justify-between">
        <p className="text-muted-foreground max-w-3xl text-sm">
          Manage metadata providers for matching and enriching media.
        </p>
        <Dialog
          open={dialogOpen}
          onOpenChange={(open) => {
            setDialogOpen(open);
            if (!open) setEditingProvider(null);
          }}
        >
          <DialogTrigger asChild>
            <Button size="sm">
              <Plus className="mr-1 h-4 w-4" /> Add Provider
            </Button>
          </DialogTrigger>
          <DialogContent>
            <DialogHeader>
              <DialogTitle>{editingProvider ? "Edit Provider" : "Add Provider"}</DialogTitle>
            </DialogHeader>
            <MetadataProviderForm
              provider={editingProvider}
              onClose={() => {
                setDialogOpen(false);
                setEditingProvider(null);
              }}
            />
          </DialogContent>
        </Dialog>
      </div>

      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Slug</TableHead>
            <TableHead>Type</TableHead>
            <TableHead>Status</TableHead>
            <TableHead>Settings</TableHead>
            <TableHead>Created</TableHead>
            <TableHead className="w-24">Actions</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {providers.length === 0 ? (
            <TableRow>
              <TableCell colSpan={6} className="text-muted-foreground py-8 text-center">
                No providers configured. Add a provider to enable metadata matching.
              </TableCell>
            </TableRow>
          ) : (
            providers.map((p) => (
              <TableRow key={p.id}>
                <TableCell className="font-mono font-medium">{p.slug}</TableCell>
                <TableCell>
                  <Badge variant="secondary">{p.provider_type}</Badge>
                </TableCell>
                <TableCell>
                  <Badge variant={p.enabled ? "outline" : "destructive"}>
                    {p.enabled ? "Enabled" : "Disabled"}
                  </Badge>
                </TableCell>
                <TableCell className="text-muted-foreground max-w-48 truncate font-mono text-xs">
                  {Object.entries(p.settings)
                    .map(([k, v]) => `${k}=${v}`)
                    .join(", ")}
                </TableCell>
                <TableCell className="text-muted-foreground text-xs">
                  {new Date(p.created_at).toLocaleDateString()}
                </TableCell>
                <TableCell>
                  <div className="flex gap-1">
                    <Button
                      variant="ghost"
                      size="icon"
                      className="h-7 w-7"
                      onClick={() => {
                        setEditingProvider(p);
                        setDialogOpen(true);
                      }}
                    >
                      <Pencil className="h-3 w-3" />
                    </Button>
                    <Button
                      variant="ghost"
                      size="icon"
                      className="h-7 w-7"
                      onClick={() => handleDelete(p)}
                    >
                      <Trash2 className="h-3 w-3" />
                    </Button>
                  </div>
                </TableCell>
              </TableRow>
            ))
          )}
        </TableBody>
      </Table>
    </div>
  );
}

export default function IntegrationsSettings() {
  return (
    <div className="flex h-full flex-col">
      <div className="mb-6">
        <h2 className="text-lg font-semibold">Integrations</h2>
        <p className="text-muted-foreground text-sm">
          External services, subtitle providers, and metadata providers
        </p>
      </div>

      <Tabs defaultValue="services" className="flex-1">
        <TabsList>
          <TabsTrigger value="services">Services</TabsTrigger>
          <TabsTrigger value="subtitles">Subtitles</TabsTrigger>
          <TabsTrigger value="providers">Providers</TabsTrigger>
        </TabsList>
        <TabsContent value="services">
          <ServicesContent />
        </TabsContent>
        <TabsContent value="subtitles">
          <SubtitlesContent />
        </TabsContent>
        <TabsContent value="providers">
          <ProvidersContent />
        </TabsContent>
      </Tabs>
    </div>
  );
}
