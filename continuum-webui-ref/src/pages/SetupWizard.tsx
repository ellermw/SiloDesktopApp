import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import { Navigate, useNavigate } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { Check } from "lucide-react";
import { ApiClientError, api } from "@/api/client";
import type {
  CreateLibraryRequest,
  CreateProfileRequest,
  CreateProviderRequest,
  Library,
  MetadataProvider,
  Profile,
} from "@/api/types";
import { useAuth } from "@/hooks/useAuth";
import { useDocumentTitle } from "@/hooks/useDocumentTitle";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
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

const SKIP_LIBRARY_STORAGE_KEY = "setup_wizard_skip_library";
const SERVER_DONE_STORAGE_KEY = "setup_wizard_server_done";

// Keep these aligned with internal/config/config.go setDefaults().
const METADB_PROVIDER_DEFAULTS = {
  url: "https://mdp.zenterprise.org",
  api_key: "mdb_efaeba41a93ee1c7113dc2878900f922f69758d1fde9d4ac20f41a9383d049c5",
};

const METADB_S3_DEFAULTS = {
  s3_endpoint: "https://s3.zenterprise.org",
  s3_region: "",
  s3_bucket: "metadb-v3",
  s3_access_key: "7H8ALF7JPRFVC78997RH",
  s3_secret_key: "D2b19ebbzSAI8K5OTfAi5YvIgXS8teqR0urNBIz9",
};

const PROVIDER_DEFAULTS: Record<string, Record<string, string>> = {
  metadb: { ...METADB_PROVIDER_DEFAULTS, ...METADB_S3_DEFAULTS },
  s3: { bucket: "metadata" },
  tmdb: {},
  tvdb: {},
};

const providerFieldMap: Record<
  string,
  Array<{ key: string; label: string; placeholder: string; sensitive?: boolean }>
> = {
  metadb: [
    { key: "url", label: "URL", placeholder: "https://mdp.zenterprise.org" },
    { key: "api_key", label: "API Key", placeholder: "mdb_...", sensitive: true },
  ],
  s3: [{ key: "bucket", label: "Bucket", placeholder: "metadata" }],
  tmdb: [],
  tvdb: [
    { key: "api_key", label: "API Key", placeholder: "TVDB v4 API key", sensitive: true },
    { key: "pin", label: "Subscriber PIN", placeholder: "TVDB PIN", sensitive: true },
  ],
};

interface MetadbS3Field {
  key: string;
  label: string;
  placeholder: string;
  sensitive?: boolean;
  settingsKey: string;
}

const metadbS3Fields: MetadbS3Field[] = [
  {
    key: "s3_endpoint",
    label: "S3 Endpoint",
    placeholder: "https://s3.example.com",
    settingsKey: "s3.metadata_endpoint",
  },
  {
    key: "s3_region",
    label: "S3 Region",
    placeholder: "us-east-1",
    settingsKey: "s3.metadata_region",
  },
  {
    key: "s3_bucket",
    label: "S3 Metadata Bucket",
    placeholder: "metadb-v3",
    settingsKey: "s3.metadata_bucket",
  },
  {
    key: "s3_access_key",
    label: "S3 Access Key",
    placeholder: "access key",
    sensitive: true,
    settingsKey: "s3.metadata_access_key",
  },
  {
    key: "s3_secret_key",
    label: "S3 Secret Key",
    placeholder: "secret key",
    sensitive: true,
    settingsKey: "s3.metadata_secret_key",
  },
];

interface SensitiveStatusResponse {
  configured: string[];
}

interface ServerSettingEntry {
  key: string;
  value: string;
}

function getProviderDefaults(providerType: string) {
  return { ...(PROVIDER_DEFAULTS[providerType] ?? {}) };
}

interface StepDef {
  id: string;
  label: string;
  complete: boolean;
  active: boolean;
}

function StepIndicator({ steps }: { steps: StepDef[] }) {
  return (
    <div className="flex items-center gap-1">
      {steps.map((step, i) => (
        <div key={step.id} className="flex items-center gap-1">
          {i > 0 && (
            <div
              className={`h-px w-6 ${step.complete || step.active ? "bg-primary/40" : "bg-border"}`}
            />
          )}
          <div
            className={`flex h-6 w-6 items-center justify-center rounded-full text-xs font-medium ${
              step.complete
                ? "bg-primary text-primary-foreground"
                : step.active
                  ? "border-primary text-primary border"
                  : "border-border text-muted-foreground border"
            }`}
          >
            {step.complete ? <Check className="h-3 w-3" /> : i + 1}
          </div>
          <span
            className={`text-sm ${
              step.active
                ? "text-foreground font-medium"
                : step.complete
                  ? "text-muted-foreground"
                  : "text-muted-foreground/60"
            }`}
          >
            {step.label}
          </span>
        </div>
      ))}
    </div>
  );
}

function shouldRetrySetupQuery(failureCount: number, error: unknown) {
  if (error instanceof ApiClientError && error.status === 429) {
    return false;
  }
  return failureCount < 1;
}

function getQueryMessage(error: unknown, fallback: string) {
  if (error instanceof Error && error.message) {
    return error.message;
  }
  return fallback;
}

function CurrentValueHint({ value }: { value: string }) {
  if (!value) {
    return null;
  }

  return (
    <p className="text-muted-foreground text-xs break-all">
      Current value: <span className="font-mono">{value}</span>
    </p>
  );
}

async function getAdminSettingValue(key: string) {
  try {
    return await api<ServerSettingEntry>(`/admin/settings/${encodeURIComponent(key)}`);
  } catch (err) {
    if (err instanceof ApiClientError && err.status === 404) {
      return null;
    }
    throw err;
  }
}

export default function SetupWizard() {
  const navigate = useNavigate();
  const { user, profile, loading, setupLoading, setupRequired, setupInitialUser, selectProfile } =
    useAuth();

  const isAdmin = user?.role === "admin";

  useDocumentTitle("Setup");

  const [username, setUsername] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [profileName, setProfileName] = useState("");
  const [libraryName, setLibraryName] = useState("Main Library");
  const [libraryPath, setLibraryPath] = useState("");
  const [libraryType, setLibraryType] = useState("movies");
  const [scanAfterCreate, setScanAfterCreate] = useState(true);
  const [libraryStepSkipped, setLibraryStepSkipped] = useState(() => {
    if (typeof window === "undefined") {
      return false;
    }
    return window.localStorage.getItem(SKIP_LIBRARY_STORAGE_KEY) === "true";
  });
  const [serverStepDone, setServerStepDone] = useState(() => {
    if (typeof window === "undefined") {
      return false;
    }
    return window.localStorage.getItem(SERVER_DONE_STORAGE_KEY) === "true";
  });
  const [redisUrl, setRedisUrl] = useState("");
  const [ffmpegPath, setFfmpegPath] = useState("");
  const [transcodeDir, setTranscodeDir] = useState("");
  const [hwAccel, setHwAccel] = useState("auto");
  const [transcodeEnabled, setTranscodeEnabled] = useState(true);
  const [jellyfinPublicUrl, setJellyfinPublicUrl] = useState("");
  const [jellyfinServerName, setJellyfinServerName] = useState("");
  const [submittingServer, setSubmittingServer] = useState(false);
  const [providerType, setProviderType] = useState("metadb");
  const [providerSlug, setProviderSlug] = useState("metadb");
  const [providerSettings, setProviderSettings] = useState<Record<string, string>>(() =>
    getProviderDefaults("metadb"),
  );
  const [sharedTmdbAPIKey, setSharedTmdbAPIKey] = useState("");
  const [selectedProviderId, setSelectedProviderId] = useState("");
  const [attachingProvider, setAttachingProvider] = useState(true);
  const [submittingAccount, setSubmittingAccount] = useState(false);
  const [submittingProfile, setSubmittingProfile] = useState(false);
  const [submittingLibrary, setSubmittingLibrary] = useState(false);
  const [submittingProvider, setSubmittingProvider] = useState(false);
  const [finishing, setFinishing] = useState(false);
  const [serverSettingsHydrated, setServerSettingsHydrated] = useState(false);

  const serverRequired = !serverStepDone;

  const profilesQuery = useQuery({
    queryKey: ["setup-wizard", "profiles"],
    queryFn: () => api<{ profiles: Profile[] }>("/profiles").then((data) => data.profiles ?? []),
    enabled: !!user,
    retry: shouldRetrySetupQuery,
  });

  const profiles = profilesQuery.data ?? [];
  const accountComplete = !!user;
  const profileComplete = profiles.length > 0;

  const librariesQuery = useQuery({
    queryKey: ["setup-wizard", "libraries"],
    queryFn: () => api<Library[]>("/libraries").then((data) => data ?? []),
    enabled: isAdmin && profileComplete,
    retry: shouldRetrySetupQuery,
  });

  const libraries = librariesQuery.data ?? [];
  const libraryComplete = libraries.length > 0;
  const libraryRequired = !libraryComplete && !libraryStepSkipped;
  const currentStep = !accountComplete
    ? "account"
    : !profileComplete
      ? "profile"
      : libraryRequired
        ? "library"
        : serverRequired
          ? "server"
          : "metadata";

  const providersQuery = useQuery({
    queryKey: ["setup-wizard", "providers"],
    queryFn: () => api<MetadataProvider[]>("/admin/providers").then((data) => data ?? []),
    enabled: isAdmin && currentStep === "metadata",
    retry: shouldRetrySetupQuery,
  });

  const settingsQuery = useQuery({
    queryKey: ["setup-wizard", "server-settings"],
    queryFn: () => api<Record<string, string>>("/admin/settings"),
    enabled: isAdmin && (currentStep === "server" || currentStep === "metadata"),
    retry: shouldRetrySetupQuery,
  });

  const sensitiveStatusQuery = useQuery({
    queryKey: ["setup-wizard", "sensitive-status"],
    queryFn: () => api<SensitiveStatusResponse>("/admin/settings/sensitive-status"),
    enabled: isAdmin && (currentStep === "server" || currentStep === "metadata"),
    retry: shouldRetrySetupQuery,
  });

  const redisSettingQuery = useQuery({
    queryKey: ["setup-wizard", "setting", "redis.url"],
    queryFn: () => getAdminSettingValue("redis.url"),
    enabled: isAdmin && currentStep === "server",
    retry: shouldRetrySetupQuery,
  });

  const metadbApiKeyQuery = useQuery({
    queryKey: ["setup-wizard", "setting", "metadb.api_key"],
    queryFn: () => getAdminSettingValue("metadb.api_key"),
    enabled: isAdmin && currentStep === "metadata" && providerType === "metadb",
    retry: shouldRetrySetupQuery,
  });

  const tmdbApiKeyQuery = useQuery({
    queryKey: ["setup-wizard", "setting", "tmdb.api_key"],
    queryFn: () => getAdminSettingValue("tmdb.api_key"),
    enabled: isAdmin && currentStep === "metadata" && providerType === "tmdb",
    retry: shouldRetrySetupQuery,
  });

  const metadbS3AccessKeyQuery = useQuery({
    queryKey: ["setup-wizard", "setting", "s3.metadata_access_key"],
    queryFn: () => getAdminSettingValue("s3.metadata_access_key"),
    enabled: isAdmin && currentStep === "metadata" && providerType === "metadb",
    retry: shouldRetrySetupQuery,
  });

  const metadbS3SecretKeyQuery = useQuery({
    queryKey: ["setup-wizard", "setting", "s3.metadata_secret_key"],
    queryFn: () => getAdminSettingValue("s3.metadata_secret_key"),
    enabled: isAdmin && currentStep === "metadata" && providerType === "metadb",
    retry: shouldRetrySetupQuery,
  });

  const providers = providersQuery.data ?? [];
  const primaryLibrary = libraries[0] ?? null;

  const serverSettings = settingsQuery.data;

  useEffect(() => {
    if (!serverSettings || serverSettingsHydrated) return;
    setServerSettingsHydrated(true);

    const s = serverSettings;
    if (s["playback.ffmpeg_path"]) setFfmpegPath(s["playback.ffmpeg_path"]);
    if (s["playback.transcode_dir"]) setTranscodeDir(s["playback.transcode_dir"]);
    if (s["playback.hw_accel"]) setHwAccel(s["playback.hw_accel"]);
    if (s["playback.transcode_enabled"] !== undefined && s["playback.transcode_enabled"] !== "") {
      setTranscodeEnabled(s["playback.transcode_enabled"] === "true");
    }
    if (s["jellyfin_compat.public_url"]) setJellyfinPublicUrl(s["jellyfin_compat.public_url"]);
    if (s["jellyfin_compat.server_name"]) setJellyfinServerName(s["jellyfin_compat.server_name"]);
    const metadbUrl = s["metadb.url"];
    if (metadbUrl) {
      setProviderSettings((current) => ({ ...current, url: metadbUrl }));
    }
    const metadataEndpoint = s["s3.metadata_endpoint"];
    if (metadataEndpoint) {
      setProviderSettings((current) => ({ ...current, s3_endpoint: metadataEndpoint }));
    }
    const metadataRegion = s["s3.metadata_region"];
    if (metadataRegion) {
      setProviderSettings((current) => ({ ...current, s3_region: metadataRegion }));
    }
    const metadataBucket = s["s3.metadata_bucket"];
    if (metadataBucket) {
      setProviderSettings((current) => ({ ...current, s3_bucket: metadataBucket }));
    }
  }, [serverSettings, serverSettingsHydrated]);

  useEffect(() => {
    const currentRedisValue = redisSettingQuery.data?.value;
    if (!currentRedisValue) {
      return;
    }
    setRedisUrl((current) => (current === "" ? currentRedisValue : current));
  }, [redisSettingQuery.data?.value]);

  useEffect(() => {
    if (providerType !== "metadb") {
      return;
    }

    const configuredValues = {
      api_key: metadbApiKeyQuery.data?.value ?? "",
      s3_access_key: metadbS3AccessKeyQuery.data?.value ?? "",
      s3_secret_key: metadbS3SecretKeyQuery.data?.value ?? "",
    };
    const metadbDefaults = PROVIDER_DEFAULTS.metadb ?? {};

    setProviderSettings((current) => {
      let updated = false;
      const next = { ...current };

      for (const [key, value] of Object.entries(configuredValues)) {
        if (!value) {
          continue;
        }
        if (current[key] !== metadbDefaults[key]) {
          continue;
        }
        next[key] = value;
        updated = true;
      }

      return updated ? next : current;
    });
  }, [
    providerType,
    metadbApiKeyQuery.data?.value,
    metadbS3AccessKeyQuery.data?.value,
    metadbS3SecretKeyQuery.data?.value,
  ]);

  useEffect(() => {
    if (providerType !== "tmdb") {
      return;
    }
    const configuredValue = tmdbApiKeyQuery.data?.value ?? "";
    if (!configuredValue) {
      return;
    }
    setSharedTmdbAPIKey((current) => (current === "" ? configuredValue : current));
  }, [providerType, tmdbApiKeyQuery.data?.value]);

  if (loading || setupLoading) {
    return <div className="text-muted-foreground p-8 text-sm">Loading...</div>;
  }

  if (!setupRequired && !user) {
    return <Navigate to="/login" replace />;
  }

  if (user && user.role !== "admin") {
    return <Navigate to="/profiles" replace />;
  }

  if (user && profilesQuery.isPending) {
    return <div className="text-muted-foreground p-8 text-sm">Loading...</div>;
  }

  if (user && profileComplete && isAdmin && librariesQuery.isPending) {
    return <div className="text-muted-foreground p-8 text-sm">Loading...</div>;
  }

  const serverDataRateLimited =
    currentStep === "server" &&
    ((settingsQuery.error instanceof ApiClientError && settingsQuery.error.status === 429) ||
      (sensitiveStatusQuery.error instanceof ApiClientError &&
        sensitiveStatusQuery.error.status === 429) ||
      (redisSettingQuery.error instanceof ApiClientError &&
        redisSettingQuery.error.status === 429));
  const serverSettingsLoadError =
    settingsQuery.isError || sensitiveStatusQuery.isError || redisSettingQuery.isError;
  const metadataDataRateLimited =
    currentStep === "metadata" &&
    ((providersQuery.error instanceof ApiClientError && providersQuery.error.status === 429) ||
      (settingsQuery.error instanceof ApiClientError && settingsQuery.error.status === 429) ||
      (sensitiveStatusQuery.error instanceof ApiClientError &&
        sensitiveStatusQuery.error.status === 429) ||
      (metadbApiKeyQuery.error instanceof ApiClientError &&
        metadbApiKeyQuery.error.status === 429) ||
      (tmdbApiKeyQuery.error instanceof ApiClientError &&
        tmdbApiKeyQuery.error.status === 429) ||
      (metadbS3AccessKeyQuery.error instanceof ApiClientError &&
        metadbS3AccessKeyQuery.error.status === 429) ||
      (metadbS3SecretKeyQuery.error instanceof ApiClientError &&
        metadbS3SecretKeyQuery.error.status === 429));
  const metadataSettingsLoadError =
    providersQuery.isError ||
    settingsQuery.isError ||
    sensitiveStatusQuery.isError ||
    metadbApiKeyQuery.isError ||
    tmdbApiKeyQuery.isError ||
    metadbS3AccessKeyQuery.isError ||
    metadbS3SecretKeyQuery.isError;

  const steps: StepDef[] = [
    {
      id: "account",
      label: "Account",
      complete: accountComplete,
      active: currentStep === "account",
    },
    {
      id: "profile",
      label: "Profile",
      complete: profileComplete,
      active: currentStep === "profile",
    },
    {
      id: "library",
      label: "Library",
      complete: libraryComplete,
      active: currentStep === "library",
    },
    {
      id: "server",
      label: "Server",
      complete: serverStepDone,
      active: currentStep === "server",
    },
    { id: "metadata", label: "Metadata", complete: false, active: currentStep === "metadata" },
  ];

  async function handleAccountSubmit(e: FormEvent) {
    e.preventDefault();
    if (password !== confirmPassword) {
      toast.error("Passwords do not match");
      return;
    }

    setSubmittingAccount(true);
    try {
      await setupInitialUser(username, email, password);
      toast.success("Admin account created");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to create admin account");
    } finally {
      setSubmittingAccount(false);
    }
  }

  async function handleProfileSubmit(e: FormEvent) {
    e.preventDefault();
    setSubmittingProfile(true);

    try {
      const body: CreateProfileRequest = { name: profileName };
      const created = await api<Profile>("/profiles", {
        method: "POST",
        body: JSON.stringify(body),
      });
      selectProfile(created);
      await profilesQuery.refetch();
      toast.success("Profile created");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to create profile");
    } finally {
      setSubmittingProfile(false);
    }
  }

  async function handleLibrarySubmit(e: FormEvent) {
    e.preventDefault();
    setSubmittingLibrary(true);

    try {
      const body: CreateLibraryRequest = {
        name: libraryName,
        type: libraryType,
        paths: [libraryPath],
      };
      const created = await api<Library>("/libraries", {
        method: "POST",
        body: JSON.stringify(body),
      });

      setLibraryStepSkipped(false);
      window.localStorage.removeItem(SKIP_LIBRARY_STORAGE_KEY);

      if (scanAfterCreate) {
        await api("/scan", {
          method: "POST",
          body: JSON.stringify({ library_id: created.id }),
        });
      }

      await librariesQuery.refetch();
      toast.success(scanAfterCreate ? "Library created and scan started" : "Library created");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to create library");
    } finally {
      setSubmittingLibrary(false);
    }
  }

  function handleSkipLibraryStep() {
    setLibraryStepSkipped(true);
    window.localStorage.setItem(SKIP_LIBRARY_STORAGE_KEY, "true");
    toast.success("Library setup skipped for now");
  }

  async function handleServerSubmit(e: FormEvent) {
    e.preventDefault();
    setSubmittingServer(true);

    const entries: ServerSettingEntry[] = [];

    if (redisUrl.trim()) {
      entries.push({ key: "redis.url", value: redisUrl.trim() });
    }
    if (ffmpegPath.trim()) {
      entries.push({ key: "playback.ffmpeg_path", value: ffmpegPath.trim() });
    }
    if (transcodeDir.trim()) {
      entries.push({ key: "playback.transcode_dir", value: transcodeDir.trim() });
    }
    entries.push({ key: "playback.hw_accel", value: hwAccel });
    entries.push({ key: "playback.transcode_enabled", value: transcodeEnabled ? "true" : "false" });
    if (jellyfinPublicUrl.trim()) {
      entries.push({ key: "jellyfin_compat.public_url", value: jellyfinPublicUrl.trim() });
    }
    if (jellyfinServerName.trim()) {
      entries.push({ key: "jellyfin_compat.server_name", value: jellyfinServerName.trim() });
    }

    try {
      for (const entry of entries) {
        await api(`/admin/settings/${encodeURIComponent(entry.key)}`, {
          method: "PUT",
          body: JSON.stringify({ value: entry.value }),
        });
      }
      setServerStepDone(true);
      window.localStorage.setItem(SERVER_DONE_STORAGE_KEY, "true");
      toast.success("Server settings saved");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to save server settings");
    } finally {
      setSubmittingServer(false);
    }
  }

  function handleSkipServerStep() {
    setServerStepDone(true);
    window.localStorage.setItem(SERVER_DONE_STORAGE_KEY, "true");
    toast.success("Server configuration skipped for now");
  }

  function updateProviderSetting(key: string, value: string) {
    setProviderSettings((current) => ({ ...current, [key]: value }));
  }

  async function attachProviderToPrimaryLibrary(providerId: number) {
    if (!primaryLibrary) {
      return;
    }

    await api(`/libraries/${primaryLibrary.id}/providers`, {
      method: "PUT",
      body: JSON.stringify({
        entries: [{ provider_id: providerId, priority: 0 }],
      }),
    });
  }

  async function handleCreateProvider(e: FormEvent) {
    e.preventDefault();
    setSubmittingProvider(true);

    try {
      const body: CreateProviderRequest = {
        slug: providerSlug.trim() || providerType,
        provider_type: providerType,
        settings: providerSettings,
      };
      const created = await api<MetadataProvider>("/admin/providers", {
        method: "POST",
        body: JSON.stringify(body),
      });

      if (attachingProvider) {
        await attachProviderToPrimaryLibrary(created.id);
      }

      await providersQuery.refetch();
      setSelectedProviderId(String(created.id));
      toast.success(attachingProvider ? "Provider created and attached" : "Provider created");

      // If MetaDB, also persist credentials to server_settings
      if (providerType === "metadb") {
        const settingsToSave: Record<string, string> = {};
        for (const field of metadbS3Fields) {
          const val = providerSettings[field.key];
          if (val) {
            settingsToSave[field.settingsKey] = val;
          }
        }
        // Also save MetaDB URL and API key to server_settings
        if (providerSettings["url"]) {
          settingsToSave["metadb.url"] = providerSettings["url"];
        }
        if (providerSettings["api_key"]) {
          settingsToSave["metadb.api_key"] = providerSettings["api_key"];
        }
        for (const [key, value] of Object.entries(settingsToSave)) {
          try {
            await api(`/admin/settings/${encodeURIComponent(key)}`, {
              method: "PUT",
              body: JSON.stringify({ value }),
            });
          } catch {
            // Non-fatal: settings can be configured later from admin
          }
        }
      } else if (providerType === "tmdb" && sharedTmdbAPIKey.trim()) {
        try {
          await api(`/admin/settings/${encodeURIComponent("tmdb.api_key")}`, {
            method: "PUT",
            body: JSON.stringify({ value: sharedTmdbAPIKey.trim() }),
          });
        } catch {
          // Non-fatal: the provider can still be configured later from admin settings.
        }
      }
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to create provider");
    } finally {
      setSubmittingProvider(false);
    }
  }

  async function handleAttachExistingProvider() {
    if (!selectedProviderId) {
      toast.error("Choose a provider first");
      return;
    }

    setSubmittingProvider(true);
    try {
      await attachProviderToPrimaryLibrary(Number(selectedProviderId));
      toast.success("Provider attached to library");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to attach provider");
    } finally {
      setSubmittingProvider(false);
    }
  }

  async function handleFinish() {
    setFinishing(true);
    try {
      const chosenProfile = profile ?? profiles[0] ?? null;
      if (chosenProfile && !profile) {
        selectProfile(chosenProfile);
      }
      navigate("/");
    } finally {
      setFinishing(false);
    }
  }

  const providerFields = providerFieldMap[providerType] ?? [];

  return (
    <div className="auth-shell items-start py-10 sm:py-14">
      <div className="auth-card glass panel-border w-full max-w-3xl border-0">
        <div className="mb-8">
          <h1 className="text-foreground text-3xl font-extrabold tracking-[-0.04em]">Setup</h1>
          <p className="page-subtitle mt-3 text-sm sm:text-base">
            Configure the essentials now. Everything here can still be changed later from admin.
          </p>
          <div className="mt-6 overflow-x-auto pb-1">
            <StepIndicator steps={steps} />
          </div>
        </div>

        <div className="surface-panel-subtle rounded-[1.6rem] p-5 sm:p-6">
          {currentStep === "account" && (
            <form onSubmit={handleAccountSubmit} className="space-y-4">
              <h2 className="text-foreground text-lg font-semibold tracking-tight">Admin account</h2>
              <div className="space-y-1.5">
                <Label htmlFor="setup-email">Email</Label>
                <Input
                  id="setup-email"
                  type="email"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  autoComplete="email"
                  required
                />
              </div>
              <div className="grid gap-4 sm:grid-cols-2">
                <div className="space-y-1.5">
                  <Label htmlFor="setup-username">Username</Label>
                  <Input
                    id="setup-username"
                    value={username}
                    onChange={(e) => setUsername(e.target.value)}
                    autoComplete="username"
                    required
                  />
                </div>
                <div className="space-y-1.5">
                  <Label htmlFor="setup-password">Password</Label>
                  <Input
                    id="setup-password"
                    type="password"
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                    autoComplete="new-password"
                    required
                  />
                </div>
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="setup-confirm-password">Confirm password</Label>
                <Input
                  id="setup-confirm-password"
                  type="password"
                  value={confirmPassword}
                  onChange={(e) => setConfirmPassword(e.target.value)}
                  autoComplete="new-password"
                  required
                />
              </div>
              <Button type="submit" disabled={submittingAccount}>
                {submittingAccount ? "Creating..." : "Create account"}
              </Button>
            </form>
          )}

          {currentStep === "profile" && (
            <form onSubmit={handleProfileSubmit} className="space-y-4">
              <h2 className="text-foreground text-lg font-semibold tracking-tight">First profile</h2>
              <div className="space-y-1.5">
                <Label htmlFor="setup-profile-name">Name</Label>
                <Input
                  id="setup-profile-name"
                  value={profileName}
                  onChange={(e) => setProfileName(e.target.value)}
                  placeholder="Alex"
                  required
                />
              </div>
              <Button type="submit" disabled={submittingProfile}>
                {submittingProfile ? "Creating..." : "Create profile"}
              </Button>
            </form>
          )}

          {currentStep === "library" && (
            <form onSubmit={handleLibrarySubmit} className="space-y-4">
              <h2 className="text-foreground text-lg font-semibold tracking-tight">Media library</h2>
              <div className="grid gap-4 sm:grid-cols-2">
                <div className="space-y-1.5">
                  <Label htmlFor="setup-library-name">Name</Label>
                  <Input
                    id="setup-library-name"
                    value={libraryName}
                    onChange={(e) => setLibraryName(e.target.value)}
                    required
                  />
                </div>
                <div className="space-y-1.5">
                  <Label htmlFor="setup-library-type">Type</Label>
                  <Select value={libraryType} onValueChange={setLibraryType}>
                    <SelectTrigger id="setup-library-type">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="movies">Movies</SelectItem>
                      <SelectItem value="series">Series</SelectItem>
                      <SelectItem value="mixed">Mixed</SelectItem>
                    </SelectContent>
                  </Select>
                </div>
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="setup-library-path">Path</Label>
                <Input
                  id="setup-library-path"
                  value={libraryPath}
                  onChange={(e) => setLibraryPath(e.target.value)}
                  placeholder="/media/movies"
                  required
                />
              </div>
              <div className="flex items-center gap-2">
                <Switch
                  id="setup-library-scan"
                  checked={scanAfterCreate}
                  onCheckedChange={setScanAfterCreate}
                />
                <Label htmlFor="setup-library-scan">Scan after creating</Label>
              </div>
              <div className="flex flex-col gap-3 sm:flex-row">
                <Button type="submit" disabled={submittingLibrary}>
                  {submittingLibrary ? "Creating..." : "Create library"}
                </Button>
                <Button
                  type="button"
                  variant="outline"
                  onClick={handleSkipLibraryStep}
                  disabled={submittingLibrary}
                >
                  Skip for now
                </Button>
              </div>
              <p className="text-muted-foreground text-sm">
                You can add a library later from the admin area.
              </p>
            </form>
          )}

          {currentStep === "server" && (
            <form onSubmit={handleServerSubmit} className="space-y-6">
              {serverDataRateLimited && (
                <div className="surface-panel-subtle rounded-2xl p-4 text-sm">
                  <p className="text-foreground">
                    Server settings are being rate limited right now, so existing values may not be
                    visible yet.
                  </p>
                  <p className="text-muted-foreground mt-1">
                    Retry loading before saving if you want to preserve env-backed settings like
                    Redis.
                  </p>
                  <div className="mt-3 flex gap-2">
                    <Button
                      type="button"
                      variant="outline"
                      onClick={() => {
                        void settingsQuery.refetch();
                        void sensitiveStatusQuery.refetch();
                      }}
                    >
                      Retry loading
                    </Button>
                  </div>
                </div>
              )}
              {!serverDataRateLimited && serverSettingsLoadError && (
                <div className="surface-panel-subtle rounded-2xl p-4 text-sm">
                  <p className="text-foreground">Existing server settings could not be loaded.</p>
                  <p className="text-muted-foreground mt-1">
                    {getQueryMessage(
                      settingsQuery.error ?? sensitiveStatusQuery.error,
                      "Please retry loading or continue and configure these values manually.",
                    )}
                  </p>
                </div>
              )}
              <div>
                <h2 className="text-foreground text-lg font-semibold tracking-tight">Server configuration</h2>
                <p className="text-muted-foreground mt-1 text-sm">
                  Optional. You can configure these later in admin settings.
                </p>
              </div>

              {/* Redis */}
              <div className="space-y-3">
                <h3 className="text-foreground text-sm font-semibold uppercase tracking-[0.16em]">Redis</h3>
                <p className="text-muted-foreground text-sm">
                  Required for multi-node deployments. Leave empty for single-instance setups.
                </p>
                <div className="space-y-1.5">
                  <Label htmlFor="setup-redis-url">URL</Label>
                  <Input
                    id="setup-redis-url"
                    type="password"
                    value={redisUrl}
                    onChange={(e) => setRedisUrl(e.target.value)}
                    placeholder="redis://localhost:6379"
                  />
                  <CurrentValueHint value={redisSettingQuery.data?.value ?? redisUrl} />
                </div>
              </div>

              {/* Playback */}
              <div className="border-border space-y-3 border-t pt-4">
                <h3 className="text-foreground text-sm font-semibold uppercase tracking-[0.16em]">Playback</h3>
                <div className="space-y-1.5">
                  <Label htmlFor="setup-ffmpeg-path">FFmpeg Path</Label>
                  <Input
                    id="setup-ffmpeg-path"
                    value={ffmpegPath}
                    onChange={(e) => setFfmpegPath(e.target.value)}
                    placeholder="/usr/lib/jellyfin-ffmpeg/ffmpeg"
                  />
                </div>
                <div className="space-y-1.5">
                  <Label htmlFor="setup-transcode-dir">Transcode Directory</Label>
                  <Input
                    id="setup-transcode-dir"
                    value={transcodeDir}
                    onChange={(e) => setTranscodeDir(e.target.value)}
                    placeholder="/tmp/continuum-transcode"
                  />
                </div>
                <div className="space-y-1.5">
                  <Label htmlFor="setup-hw-accel">Hardware Acceleration</Label>
                  <Select value={hwAccel} onValueChange={setHwAccel}>
                    <SelectTrigger id="setup-hw-accel">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="auto">Auto</SelectItem>
                      <SelectItem value="vaapi">VAAPI</SelectItem>
                      <SelectItem value="nvenc">NVENC</SelectItem>
                      <SelectItem value="qsv">QSV</SelectItem>
                      <SelectItem value="none">None</SelectItem>
                    </SelectContent>
                  </Select>
                </div>
                <div className="flex items-center gap-2">
                  <Switch
                    id="setup-transcode-enabled"
                    checked={transcodeEnabled}
                    onCheckedChange={setTranscodeEnabled}
                  />
                  <Label htmlFor="setup-transcode-enabled">Transcoding enabled</Label>
                </div>
              </div>

              {/* Jellyfin Compatibility */}
              <div className="border-border space-y-3 border-t pt-4">
                <h3 className="text-foreground text-sm font-semibold uppercase tracking-[0.16em]">Jellyfin compatibility</h3>
                <p className="text-muted-foreground text-sm">
                  Required for 3rd-party clients like VidHub, Findroid, and Infuse.
                </p>
                <div className="space-y-1.5">
                  <Label htmlFor="setup-jellyfin-url">Public URL</Label>
                  <Input
                    id="setup-jellyfin-url"
                    value={jellyfinPublicUrl}
                    onChange={(e) => setJellyfinPublicUrl(e.target.value)}
                    placeholder="http://your-server:8096"
                  />
                </div>
                <div className="space-y-1.5">
                  <Label htmlFor="setup-jellyfin-name">Server Name</Label>
                  <Input
                    id="setup-jellyfin-name"
                    value={jellyfinServerName}
                    onChange={(e) => setJellyfinServerName(e.target.value)}
                    placeholder="Continuum"
                  />
                </div>
              </div>

              <div className="flex flex-col gap-3 sm:flex-row">
                <Button type="submit" disabled={submittingServer}>
                  {submittingServer ? "Saving..." : "Save & continue"}
                </Button>
                <Button
                  type="button"
                  variant="outline"
                  onClick={handleSkipServerStep}
                  disabled={submittingServer}
                >
                  Skip for now
                </Button>
              </div>
              <p className="text-muted-foreground text-sm">
                You can change these later in admin settings.
              </p>
            </form>
          )}

          {currentStep === "metadata" && (
            <div className="space-y-6">
              {metadataDataRateLimited && (
                <div className="surface-panel-subtle rounded-2xl p-4 text-sm">
                  <p className="text-foreground">
                    Metadata setup requests are being rate limited, so provider data may be
                    incomplete.
                  </p>
                  <div className="mt-3 flex gap-2">
                    <Button
                      type="button"
                      variant="outline"
                      onClick={() => {
                        void providersQuery.refetch();
                        void settingsQuery.refetch();
                        void sensitiveStatusQuery.refetch();
                      }}
                    >
                      Retry loading
                    </Button>
                  </div>
                </div>
              )}
              {!metadataDataRateLimited && metadataSettingsLoadError && (
                <div className="surface-panel-subtle rounded-2xl p-4 text-sm">
                  <p className="text-foreground">Existing providers could not be loaded.</p>
                  <p className="text-muted-foreground mt-1">
                    {getQueryMessage(
                      providersQuery.error ??
                        settingsQuery.error ??
                        sensitiveStatusQuery.error ??
                        metadbApiKeyQuery.error ??
                        tmdbApiKeyQuery.error ??
                        metadbS3AccessKeyQuery.error ??
                        metadbS3SecretKeyQuery.error,
                      "You can still create a provider manually below.",
                    )}
                  </p>
                </div>
              )}
              <div>
                <h2 className="text-foreground text-lg font-semibold tracking-tight">Metadata provider</h2>
                <p className="text-muted-foreground mt-1 text-sm">
                  Optional. You can configure this later in settings.
                </p>
              </div>

              <form onSubmit={handleCreateProvider} className="space-y-4">
                <div className="grid gap-4 sm:grid-cols-2">
                  <div className="space-y-1.5">
                    <Label htmlFor="setup-provider-type">Provider</Label>
                    <Select
                      value={providerType}
                      onValueChange={(value) => {
                        setProviderType(value);
                        setProviderSlug(value);
                        setProviderSettings(getProviderDefaults(value));
                      }}
                    >
                      <SelectTrigger id="setup-provider-type">
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
                  <div className="space-y-1.5">
                    <Label htmlFor="setup-provider-slug">Slug</Label>
                    <Input
                      id="setup-provider-slug"
                      value={providerSlug}
                      onChange={(e) => setProviderSlug(e.target.value)}
                      required
                    />
                  </div>
                </div>
                {providerFields.map((field) => (
                  <div key={field.key} className="space-y-1.5">
                    <Label htmlFor={`setup-provider-${field.key}`}>{field.label}</Label>
                    <Input
                      id={`setup-provider-${field.key}`}
                      type={field.sensitive ? "password" : "text"}
                      value={providerSettings[field.key] ?? ""}
                      onChange={(e) => updateProviderSetting(field.key, e.target.value)}
                      placeholder={field.placeholder}
                    />
                    <CurrentValueHint value={providerSettings[field.key] ?? ""} />
                  </div>
                ))}
                {providerType === "tmdb" && (
                  <div className="space-y-1.5">
                    <Label htmlFor="setup-provider-tmdb-api-key">Shared TMDB API Key</Label>
                    <Input
                      id="setup-provider-tmdb-api-key"
                      type="password"
                      value={sharedTmdbAPIKey}
                      onChange={(e) => setSharedTmdbAPIKey(e.target.value)}
                      placeholder="TMDB v3 API key"
                    />
                    <p className="text-muted-foreground text-xs">
                      Saved to server settings and shared by TMDB metadata providers and TMDB
                      collections.
                    </p>
                    <CurrentValueHint value={tmdbApiKeyQuery.data?.value ?? sharedTmdbAPIKey} />
                  </div>
                )}
                {providerType === "metadb" && (
                  <>
                    <div className="border-border border-t pt-4">
                      <h3 className="text-foreground mb-3 text-sm font-semibold uppercase tracking-[0.16em]">
                        MetaDB S3 credentials
                      </h3>
                      <p className="text-muted-foreground mb-3 text-sm">
                        Required for serving metadata images from S3.
                      </p>
                    </div>
                    {metadbS3Fields.map((field) => (
                      <div key={field.key} className="space-y-1.5">
                        <Label htmlFor={`setup-s3-${field.key}`}>{field.label}</Label>
                        <Input
                          id={`setup-s3-${field.key}`}
                          type={field.sensitive ? "password" : "text"}
                          value={providerSettings[field.key] ?? ""}
                          onChange={(e) => updateProviderSetting(field.key, e.target.value)}
                          placeholder={field.placeholder}
                        />
                        <CurrentValueHint value={providerSettings[field.key] ?? ""} />
                      </div>
                    ))}
                  </>
                )}
                <div className="flex items-center gap-2">
                  <Switch
                    id="setup-provider-attach"
                    checked={attachingProvider}
                    onCheckedChange={setAttachingProvider}
                    disabled={!primaryLibrary}
                  />
                  <Label htmlFor="setup-provider-attach">
                    Attach to {primaryLibrary?.name ?? "first library"}
                  </Label>
                </div>
                <Button type="submit" disabled={submittingProvider}>
                  {submittingProvider ? "Saving..." : "Create provider"}
                </Button>
              </form>

              {providers.length > 0 && primaryLibrary && (
                <div className="border-border border-t pt-4">
                  <h3 className="text-foreground mb-3 text-sm font-semibold uppercase tracking-[0.16em]">
                    Attach existing provider
                  </h3>
                  <div className="flex flex-col gap-3 sm:flex-row">
                    <Select value={selectedProviderId} onValueChange={setSelectedProviderId}>
                      <SelectTrigger className="sm:max-w-[240px]">
                        <SelectValue placeholder="Choose provider" />
                      </SelectTrigger>
                      <SelectContent>
                        {providers.map((providerItem) => (
                          <SelectItem key={providerItem.id} value={String(providerItem.id)}>
                            {providerItem.slug} ({providerItem.provider_type})
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                    <Button
                      type="button"
                      variant="outline"
                      onClick={handleAttachExistingProvider}
                      disabled={submittingProvider || !selectedProviderId}
                    >
                      Attach
                    </Button>
                  </div>
                </div>
              )}

              <div className="border-border flex gap-3 border-t pt-4">
                <Button onClick={handleFinish} disabled={finishing}>
                  {finishing ? "Finishing..." : "Finish setup"}
                </Button>
                <Button type="button" variant="ghost" onClick={() => navigate("/admin/libraries")}>
                  Go to admin
                </Button>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
