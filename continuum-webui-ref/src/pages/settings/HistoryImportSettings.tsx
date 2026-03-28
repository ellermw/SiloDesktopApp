import { useMemo, useState } from "react";
import { useCurrentProfile } from "@/hooks/useCurrentProfile";
import { useProfiles } from "@/hooks/queries/profiles";
import {
  useCreateHistoryImportRun,
  useHistoryImportRun,
  useHistoryImportRuns,
  useHistoryImportSources,
  useLoginEmbyConnect,
} from "@/hooks/queries/history-import";
import type { EmbyConnectLoginResponse, HistoryImportRun } from "@/api/types";
import { SettingsGroup } from "@/components/settings/SettingsGroup";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Badge } from "@/components/ui/badge";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { canStartImport } from "./HistoryImportSettings.utils";

type ImportMode = "connect" | "saved";

export default function HistoryImportSettings() {
  const { profile } = useCurrentProfile();
  const { data: profiles = [] } = useProfiles();
  const { data: sources = [], isLoading: sourcesLoading } = useHistoryImportSources();
  const { data: recentRuns = [] } = useHistoryImportRuns();
  const [mode, setMode] = useState<ImportMode>("connect");
  const [profileId, setProfileId] = useState(profile?.id ?? "");
  const [activeRunId, setActiveRunId] = useState<string>();
  const [connectUsername, setConnectUsername] = useState("");
  const [connectPassword, setConnectPassword] = useState("");
  const [connectSession, setConnectSession] = useState<EmbyConnectLoginResponse | null>(null);
  const [connectServerId, setConnectServerId] = useState("");
  const [savedSourceId, setSavedSourceId] = useState("");
  const [savedUsername, setSavedUsername] = useState("");
  const [savedPassword, setSavedPassword] = useState("");

  const loginMutation = useLoginEmbyConnect();
  const createRunMutation = useCreateHistoryImportRun();
  const { data: activeRun } = useHistoryImportRun(activeRunId);

  const displayRun = activeRun ?? recentRuns[0] ?? null;
  const pending = loginMutation.isPending || createRunMutation.isPending;
  const effectiveProfileId = profileId || profile?.id || "";
  const effectiveSavedSourceId = savedSourceId || String(sources[0]?.id ?? "");

  const selectedSavedSource = useMemo(
    () => sources.find((source) => String(source.id) === effectiveSavedSourceId),
    [effectiveSavedSourceId, sources],
  );

  async function handleConnectLogin() {
    const result = await loginMutation.mutateAsync({
      username: connectUsername,
      password: connectPassword,
    });
    setConnectSession(result);
    setConnectServerId(result.servers[0]?.server_id ?? "");
  }

  async function handleStartImport() {
    if (!effectiveProfileId) return;

    if (mode === "connect") {
      const run = await createRunMutation.mutateAsync({
        profile_id: effectiveProfileId,
        source: "emby",
        connect_session_id: connectSession?.connect_session_id,
        server_id: connectServerId,
      });
      setActiveRunId(run.id);
      return;
    }

    if (mode === "saved" && selectedSavedSource) {
      const run = await createRunMutation.mutateAsync({
        profile_id: effectiveProfileId,
        source: "emby",
        source_id: selectedSavedSource.id,
        username: savedUsername,
        password: savedPassword,
      });
      setActiveRunId(run.id);
      return;
    }
  }

  return (
    <div className="space-y-6">
      <div className="space-y-3">
        <h2 className="text-2xl font-semibold tracking-tight sm:text-3xl">History import</h2>
        <p className="text-muted-foreground max-w-2xl text-sm leading-relaxed">
          Connect to Emby and import watch history into a Continuum profile.
        </p>
      </div>

      <SettingsGroup
        title="Source and credentials"
        description="Choose a supported source mode, then enter the credentials needed to find or use that server."
      >
        <Tabs value={mode} onValueChange={(value) => setMode(value as ImportMode)}>
          <TabsList className="surface-panel-subtle h-auto gap-1 rounded-[1.1rem] border-0 bg-transparent p-1">
            <TabsTrigger value="connect">Emby Connect</TabsTrigger>
            <TabsTrigger value="saved">Saved Server</TabsTrigger>
          </TabsList>

          <TabsContent value="connect" className="space-y-4 pt-4">
            <div className="grid gap-4 md:grid-cols-2">
              <div className="space-y-2">
                <Label>Emby Connect Email or Username</Label>
                <Input
                  value={connectUsername}
                  onChange={(e) => setConnectUsername(e.target.value)}
                />
              </div>
              <div className="space-y-2">
                <Label>Emby Connect Password</Label>
                <Input
                  type="password"
                  value={connectPassword}
                  onChange={(e) => setConnectPassword(e.target.value)}
                />
              </div>
            </div>
            <Button onClick={handleConnectLogin} disabled={loginMutation.isPending}>
              {loginMutation.isPending ? "Checking servers..." : "Find My Emby Servers"}
            </Button>
            {connectSession && (
              <div className="surface-panel-subtle space-y-4 rounded-[1.2rem] p-4">
                <div className="space-y-2">
                  <Label>Server</Label>
                  <Select value={connectServerId} onValueChange={setConnectServerId}>
                    <SelectTrigger>
                      <SelectValue placeholder="Choose a server" />
                    </SelectTrigger>
                    <SelectContent>
                      {connectSession.servers.map((server) => (
                        <SelectItem key={server.server_id} value={server.server_id}>
                          {server.name}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
                <p className="text-muted-foreground text-[13px]">
                  Continuum will exchange your selected server&apos;s temporary access key for a
                  local Emby token and use it only for this import.
                </p>
              </div>
            )}
          </TabsContent>

          <TabsContent value="saved" className="space-y-4 pt-4">
            {sourcesLoading ? (
              <div className="text-muted-foreground text-sm">Loading saved servers...</div>
            ) : sources.length === 0 ? (
              <div className="surface-panel-subtle text-muted-foreground rounded-[1.2rem] p-4 text-sm">
                No admin-defined import servers are available yet.
              </div>
            ) : (
              <>
                <div className="space-y-2">
                  <Label>Saved Server</Label>
                  <Select value={effectiveSavedSourceId} onValueChange={setSavedSourceId}>
                    <SelectTrigger>
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {sources.map((source) => (
                        <SelectItem key={source.id} value={String(source.id)}>
                          {source.name}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
                <div className="grid gap-4 md:grid-cols-2">
                  <div className="space-y-2">
                    <Label>Emby Username</Label>
                    <Input
                      value={savedUsername}
                      onChange={(e) => setSavedUsername(e.target.value)}
                    />
                  </div>
                  <div className="space-y-2">
                    <Label>Emby Password</Label>
                    <Input
                      type="password"
                      value={savedPassword}
                      onChange={(e) => setSavedPassword(e.target.value)}
                    />
                  </div>
                </div>
              </>
            )}
          </TabsContent>
        </Tabs>
      </SettingsGroup>

      <SettingsGroup
        title="Import target"
        description="Choose which Continuum profile should receive the imported watch state."
      >
        <div className="grid gap-4 md:grid-cols-[minmax(0,1fr)_auto] md:items-end">
          <div className="space-y-2">
            <Label>Import into profile</Label>
            <Select value={effectiveProfileId} onValueChange={setProfileId}>
              <SelectTrigger>
                <SelectValue placeholder="Choose a profile" />
              </SelectTrigger>
              <SelectContent>
                {profiles.map((item) => (
                  <SelectItem key={item.id} value={item.id}>
                    {item.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <Button
            onClick={handleStartImport}
            disabled={
              !canStartImport(
                mode,
                effectiveProfileId,
                connectSession,
                connectServerId,
                selectedSavedSource,
              ) || pending
            }
          >
            {createRunMutation.isPending ? "Starting..." : "Start Import"}
          </Button>
        </div>
      </SettingsGroup>

      <SettingsGroup
        title="Latest import summary"
        description="Review the most recent run or the run you selected from the history below."
      >
        <RunSummary run={displayRun} />
      </SettingsGroup>

      <SettingsGroup
        title="Recent imports"
        description="Open a previous run to inspect its status, totals, warnings, and unmatched titles."
      >
        <div className="flex items-center justify-between">
          <Label className="text-sm font-medium">Import history</Label>
          <Badge variant="outline">{recentRuns.length}</Badge>
        </div>
        <div className="space-y-2">
          {recentRuns.length === 0 ? (
            <div className="surface-panel-subtle text-muted-foreground rounded-[1.2rem] p-4 text-sm">
              No imports have been started yet.
            </div>
          ) : (
            recentRuns.map((run) => (
              <button
                key={run.id}
                type="button"
                onClick={() => setActiveRunId(run.id)}
                className="surface-panel-subtle flex w-full items-center justify-between rounded-[1.2rem] px-4 py-3 text-left transition-colors"
              >
                <div>
                  <div className="font-medium">{run.source_type.toUpperCase()} import</div>
                  <div className="text-muted-foreground text-[13px]">
                    {new Date(run.created_at).toLocaleString()}
                  </div>
                </div>
                <Badge variant={run.status === "completed" ? "outline" : "secondary"}>
                  {run.status}
                </Badge>
              </button>
            ))
          )}
        </div>
      </SettingsGroup>
    </div>
  );
}

function RunSummary({ run }: { run: HistoryImportRun | null }) {
  if (!run) {
    return (
      <div className="text-muted-foreground text-sm">
        Import summaries will appear here after you start a run.
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between gap-4">
        <div>
          <div className="font-medium">Latest Run</div>
          <div className="text-muted-foreground text-[13px]">
            Started {new Date(run.created_at).toLocaleString()}
          </div>
        </div>
        <Badge variant={run.status === "failed" ? "destructive" : "outline"}>{run.status}</Badge>
      </div>

      <div className="grid gap-2 md:grid-cols-3">
        <Metric label="Fetched" value={run.fetched} />
        <Metric label="Matched" value={run.matched} />
        <Metric label="Unmatched" value={run.unmatched} />
        <Metric label="Progress Updated" value={run.progress_updated} />
        <Metric label="History Created" value={run.history_created} />
        <Metric label="Skipped" value={run.skipped} />
      </div>

      {run.error_message && (
        <div className="rounded-[1rem] border border-red-500/40 bg-red-500/10 px-3 py-2 text-sm">
          {run.error_message}
        </div>
      )}

      {run.warnings.length > 0 && (
        <div className="space-y-2">
          <Label className="text-sm font-medium">Warnings</Label>
          <div className="space-y-1">
            {run.warnings.map((warning) => (
              <div key={warning} className="text-muted-foreground text-sm">
                {warning}
              </div>
            ))}
          </div>
        </div>
      )}

      {run.unmatched_samples.length > 0 && (
        <div className="space-y-2">
          <Label className="text-sm font-medium">Unmatched examples</Label>
          <div className="space-y-2">
            {run.unmatched_samples.map((sample, index) => (
              <div
                key={`${sample.title}-${index}`}
                className="surface-panel-subtle rounded-[1rem] px-3 py-2 text-sm"
              >
                <div className="font-medium">
                  {sample.title}
                  {sample.year ? ` (${sample.year})` : ""}
                </div>
                <div className="text-muted-foreground">
                  {sample.kind} · {sample.reason}
                </div>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}

function Metric({ label, value }: { label: string; value: number }) {
  return (
    <div className="surface-panel-subtle rounded-[1rem] px-3 py-2">
      <div className="text-muted-foreground text-xs font-medium uppercase tracking-wide">
        {label}
      </div>
      <div className="text-lg font-semibold tabular-nums">{value}</div>
    </div>
  );
}
