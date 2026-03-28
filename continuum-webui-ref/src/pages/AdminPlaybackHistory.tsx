import { useEffect } from "react";
import { Link, useSearchParams } from "react-router";
import { useAdminUsers } from "@/hooks/queries/admin/users";
import { useAdminPlaybackHistory, useAdminUserProfiles } from "@/hooks/queries/admin/history";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";

const ALL_USERS = "all";
const ALL_PROFILES = "all";
const ALL_COMPLETION = "all";

export default function AdminPlaybackHistory() {
  const [searchParams, setSearchParams] = useSearchParams();
  const { data: users = [] } = useAdminUsers();

  const selectedUser = searchParams.get("user_id") ?? ALL_USERS;
  const selectedProfile = searchParams.get("profile_id") ?? ALL_PROFILES;
  const selectedCompleted = searchParams.get("completed") ?? ALL_COMPLETION;
  const selectedMediaItemId = searchParams.get("media_item_id")?.trim() ?? "";

  const selectedUserId = selectedUser !== ALL_USERS ? Number(selectedUser) : undefined;
  const history = useAdminPlaybackHistory({
    userId: selectedUserId,
    profileId: selectedProfile !== ALL_PROFILES ? selectedProfile : undefined,
    mediaItemId: selectedMediaItemId || undefined,
    completed:
      selectedCompleted === "true" || selectedCompleted === "false" ? selectedCompleted : "all",
    limit: 100,
  });
  const profiles = useAdminUserProfiles(selectedUserId);

  useEffect(() => {
    if (selectedUser === ALL_USERS && selectedProfile !== ALL_PROFILES) {
      const next = new URLSearchParams(searchParams);
      next.delete("profile_id");
      setSearchParams(next, { replace: true });
      return;
    }
    if (
      selectedUser !== ALL_USERS &&
      selectedProfile !== ALL_PROFILES &&
      profiles.data &&
      profiles.data.length > 0 &&
      !profiles.data.some((profile) => profile.id === selectedProfile)
    ) {
      const next = new URLSearchParams(searchParams);
      next.delete("profile_id");
      setSearchParams(next, { replace: true });
    }
  }, [profiles.data, searchParams, selectedProfile, selectedUser, setSearchParams]);

  function updateFilter(key: string, value: string) {
    const next = new URLSearchParams(searchParams);
    if (value === ALL_USERS || value === ALL_PROFILES || value === ALL_COMPLETION) {
      next.delete(key);
    } else {
      next.set(key, value);
    }
    if (key === "user_id") {
      next.delete("profile_id");
    }
    setSearchParams(next, { replace: true });
  }

  function resetFilters() {
    setSearchParams(new URLSearchParams(), { replace: true });
  }

  const rows = history.data ?? [];
  const activeMediaItemLabel = rows[0]?.media_title || selectedMediaItemId;

  return (
    <div className="page-shell space-y-6 py-4 sm:py-6">
      <div className="page-header gap-5">
        <div className="space-y-3">
          <h1 className="page-title text-[clamp(2rem,4vw,3rem)]">Playback History</h1>
          <p className="page-subtitle text-sm sm:text-base">
            Finalized playback attempts across all users and profiles.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {selectedMediaItemId && (
            <div className="border-border bg-muted/40 flex items-center gap-2 rounded-md border px-3 py-2 text-xs">
              <span className="text-muted-foreground font-medium">Item filter active</span>
              <span className="font-semibold">{activeMediaItemLabel}</span>
            </div>
          )}

          <Select value={selectedUser} onValueChange={(value) => updateFilter("user_id", value)}>
            <SelectTrigger className="w-[220px]">
              <SelectValue placeholder="All users" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={ALL_USERS}>All users</SelectItem>
              {users.map((user) => (
                <SelectItem key={user.id} value={String(user.id)}>
                  {user.username}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>

          <Select
            value={selectedProfile}
            onValueChange={(value) => updateFilter("profile_id", value)}
            disabled={selectedUser === ALL_USERS}
          >
            <SelectTrigger className="w-[220px]">
              <SelectValue
                placeholder={selectedUser === ALL_USERS ? "Choose a user first" : "All profiles"}
              />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={ALL_PROFILES}>All profiles</SelectItem>
              {(profiles.data ?? []).map((profile) => (
                <SelectItem key={profile.id} value={profile.id}>
                  {profile.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>

          <Select
            value={selectedCompleted}
            onValueChange={(value) => updateFilter("completed", value)}
          >
            <SelectTrigger className="w-[180px]">
              <SelectValue placeholder="All attempts" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={ALL_COMPLETION}>All attempts</SelectItem>
              <SelectItem value="true">Completed</SelectItem>
              <SelectItem value="false">Partial</SelectItem>
            </SelectContent>
          </Select>

          <Button variant="outline" size="sm" onClick={resetFilters}>
            Reset
          </Button>
        </div>
      </div>

      <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
        <InfoCard label="Visible Rows" value={String(rows.length)} />
        <InfoCard label="Completed" value={String(rows.filter((row) => row.completed).length)} />
        <InfoCard label="Partial" value={String(rows.filter((row) => !row.completed).length)} />
      </div>

      <Card className="surface-panel rounded-[1.6rem] border-0">
        <CardHeader className="flex flex-row items-center justify-between space-y-0">
          <CardTitle className="text-sm font-bold">Recent Playback</CardTitle>
          <div className="text-muted-foreground text-xs">
            {history.isFetching ? "Refreshing..." : "Auto-refreshing"}
          </div>
        </CardHeader>
        <CardContent>
          {history.isLoading ? (
            <div className="text-muted-foreground py-8 text-center text-sm">
              Loading playback history...
            </div>
          ) : history.error ? (
            <div className="text-destructive py-8 text-center text-sm">
              {history.error instanceof Error
                ? history.error.message
                : "Failed to load playback history"}
            </div>
          ) : rows.length === 0 ? (
            <div className="text-muted-foreground py-8 text-center text-sm">
              No playback history matches the current filters.
            </div>
          ) : (
            <div className="overflow-x-auto">
              <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Media</TableHead>
                  <TableHead>User</TableHead>
                  <TableHead>Profile</TableHead>
                  <TableHead>Method</TableHead>
                  <TableHead>Watch Time</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Ended</TableHead>
                  <TableHead className="text-right">Logs</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {rows.map((row) => {
                  const title =
                    row.media_title || row.media_item_id || `File #${row.media_file_id}`;
                  return (
                    <TableRow key={row.session_id}>
                      <TableCell>
                        <div className="space-y-1">
                          <div className="font-medium">{title}</div>
                          <div className="text-muted-foreground text-xs">
                            {row.media_type || "unknown"} · session {row.session_id.slice(0, 8)}
                          </div>
                        </div>
                      </TableCell>
                      <TableCell>{row.username || `User #${row.user_id}`}</TableCell>
                      <TableCell>
                        <div className="space-y-1">
                          <div>{row.profile_name || row.profile_id}</div>
                          <div className="text-muted-foreground text-xs">{row.profile_id}</div>
                        </div>
                      </TableCell>
                      <TableCell>
                        <Badge variant="secondary">{row.play_method}</Badge>
                      </TableCell>
                      <TableCell>
                        <div className="space-y-1">
                          <div>{formatDuration(row.watched_seconds)}</div>
                          <div className="text-muted-foreground text-xs">
                            of {formatDuration(row.duration_seconds)}
                          </div>
                        </div>
                      </TableCell>
                      <TableCell>
                        <Badge variant={row.completed ? "default" : "outline"}>
                          {row.completed ? "Completed" : "Partial"}
                        </Badge>
                      </TableCell>
                      <TableCell>
                        <div className="space-y-1">
                          <div>{formatDateTime(row.ended_at)}</div>
                          <div className="text-muted-foreground text-xs">
                            started {formatRelative(row.started_at)}
                          </div>
                        </div>
                      </TableCell>
                      <TableCell className="text-right">
                        <div className="flex justify-end gap-3">
                          <Link
                            to={`/admin/logs?playback_session_id=${encodeURIComponent(row.session_id)}&focus=playback`}
                            className="text-primary text-sm font-medium"
                          >
                            View Logs
                          </Link>
                          <Link
                            to={`/admin/logs?playback_session_id=${encodeURIComponent(row.session_id)}&focus=playback&component=ffmpeg`}
                            className="text-primary/80 text-sm font-medium"
                          >
                            FFmpeg Logs
                          </Link>
                        </div>
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
              </Table>
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

function InfoCard({ label, value }: { label: string; value: string }) {
  return (
    <div className="surface-panel rounded-[1.5rem] border-0 p-4">
      <div className="text-muted-foreground text-[11px] font-medium">{label}</div>
      <div className="mt-1 text-2xl font-extrabold tracking-tight">{value}</div>
    </div>
  );
}

function formatDuration(seconds: number | null) {
  if (!seconds || Number.isNaN(seconds)) return "0m";
  const rounded = Math.max(0, Math.floor(seconds));
  const hours = Math.floor(rounded / 3600);
  const minutes = Math.floor((rounded % 3600) / 60);
  const secs = rounded % 60;

  if (hours > 0) return `${hours}h ${minutes}m`;
  if (minutes > 0) return `${minutes}m ${secs}s`;
  return `${secs}s`;
}

function formatDateTime(value: string) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleString();
}

function formatRelative(value: string) {
  const date = new Date(value);
  const diff = Date.now() - date.getTime();
  if (Number.isNaN(date.getTime())) return value;
  const minutes = Math.max(0, Math.floor(diff / 60_000));
  if (minutes < 1) return "just now";
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.floor(hours / 24);
  return `${days}d ago`;
}
