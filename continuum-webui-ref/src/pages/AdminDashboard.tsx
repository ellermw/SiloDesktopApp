import type { ReactNode } from "react";
import { Link } from "react-router";
import { AdminSessionActions } from "@/components/AdminSessionActions";
import { useAdminSessionStream } from "@/components/AdminSessionStreamProvider";
import { useAdminStats } from "@/hooks/queries/admin/stats";
import { useAdminUsers } from "@/hooks/queries/admin/users";
import {
  useAdminLibraries,
  useScanAllLibraries,
  useScanLibrary,
} from "@/hooks/queries/admin/libraries";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
  Activity,
  Film,
  Tv,
  Users,
  HardDrive,
  RefreshCw,
  Play,
  Library,
  ScanLine,
} from "lucide-react";
import type { AdminSession, AdminStats, Library as LibraryType, AdminUser } from "@/api/types";

export default function AdminDashboard() {
  const { data: stats, isLoading: statsLoading, refetch: refetchStats } = useAdminStats();
  const {
    sessions,
    isLoading: sessionsLoading,
    refresh: refreshSessions,
  } = useAdminSessionStream();
  const { data: libraries = [] } = useAdminLibraries();
  const { data: users = [] } = useAdminUsers();
  const scanAll = useScanAllLibraries();

  const loading = statsLoading || sessionsLoading;

  if (loading) return <div className="text-muted-foreground p-8">Loading dashboard...</div>;

  return (
    <div className="space-y-6 lg:space-y-8">
      {/* Page header */}
      <div className="page-header">
        <div className="space-y-3">
          <h1 className="page-title text-[clamp(2rem,4vw,3.25rem)]">Dashboard</h1>
          <p className="page-subtitle text-sm sm:text-base">
            Live sessions, content health, and server activity in one view.
          </p>
        </div>
        <div className="flex gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={() => {
              refetchStats();
              void refreshSessions();
            }}
          >
            <RefreshCw className="h-3.5 w-3.5" />
            Refresh
          </Button>
          <Button
            variant="default"
            size="sm"
            onClick={() => {
              if (libraries.length > 0) {
                scanAll.mutate();
              }
            }}
            disabled={scanAll.isPending}
          >
            <ScanLine className="h-3.5 w-3.5" />
            Scan All Libraries
          </Button>
        </div>
      </div>

      {/* Stats row */}
      {stats && <StatsRow stats={stats} sessionCount={sessions.length} />}

      {/* Now Playing */}
      {sessions.length > 0 && (
        <div>
          <div className="mb-3 flex items-center justify-between">
            <div className="text-base font-bold">Now Playing</div>
            <Link
              to="/admin/activity"
              className="text-muted-foreground hover:text-primary text-[11px] transition-colors"
            >
              View all {sessions.length} streams ›
            </Link>
          </div>
          <div className="grid grid-cols-1 gap-3.5 lg:grid-cols-2">
            {sessions.slice(0, 4).map((session) => (
              <StreamCard key={session.session_id} session={session} />
            ))}
          </div>
          {sessions.length > 4 && (
            <Link
              to="/admin/activity"
              className="text-muted-foreground hover:text-primary mt-2 block text-center text-[12px] transition-colors"
            >
              +{sessions.length - 4} more active streams
            </Link>
          )}
        </div>
      )}

      {/* Two-column: Libraries + Users */}
      <div className="grid grid-cols-1 gap-3.5 xl:grid-cols-[1.4fr_1fr]">
        <LibrariesCard libraries={libraries} />
        <UsersCard users={users} />
      </div>

      {/* Recent Activity */}
      <ActivityCard sessions={sessions} />
    </div>
  );
}

// --- Sub-components ---

function StatsRow({ stats, sessionCount }: { stats: AdminStats; sessionCount: number }) {
  const storageGB = stats.total_storage_bytes / (1024 * 1024 * 1024);
  const storageTB = storageGB / 1024;
  const storageDisplay =
    storageTB >= 1 ? `${storageTB.toFixed(1)} TB` : `${storageGB.toFixed(0)} GB`;

  const statCards: { label: string; value: string; sub: string; icon: ReactNode }[] = [
    {
      label: "Active Streams",
      value: String(sessionCount),
      sub: sessionCount === 1 ? "1 session" : `${sessionCount} sessions`,
      icon: <Activity className="h-4 w-4" />,
    },
    {
      label: "Total Movies",
      value: stats.total_movies.toLocaleString(),
      sub: `of ${stats.total_items.toLocaleString()} items`,
      icon: <Film className="h-4 w-4" />,
    },
    {
      label: "Total Shows",
      value: stats.total_shows.toLocaleString(),
      sub: `${stats.total_files.toLocaleString()} files total`,
      icon: <Tv className="h-4 w-4" />,
    },
    {
      label: "Users",
      value: String(stats.total_users),
      sub: `${stats.total_users} registered`,
      icon: <Users className="h-4 w-4" />,
    },
    {
      label: "Storage",
      value: storageDisplay,
      sub: `${stats.total_files.toLocaleString()} files`,
      icon: <HardDrive className="h-4 w-4" />,
    },
  ];

  return (
    <div className="grid grid-cols-2 gap-3.5 sm:grid-cols-3 lg:grid-cols-5">
      {statCards.map((card) => (
        <div key={card.label} className="surface-panel rounded-[1.5rem] border-0 p-[18px] transition-colors duration-150">
          <div className="mb-2 flex items-center justify-between">
            <div className="text-muted-foreground text-[11px] font-medium">{card.label}</div>
            <div className="text-muted-foreground">{card.icon}</div>
          </div>
          <div className="mb-0.5 text-[28px] leading-none font-extrabold tracking-tight">
            {card.value}
          </div>
          <div className="text-muted-foreground text-[11px]">{card.sub}</div>
        </div>
      ))}
    </div>
  );
}

function StreamCard({ session }: { session: AdminSession }) {
  const isEpisode =
    session.series_name && session.season_number != null && session.episode_number != null;
  const title = isEpisode
    ? session.media_title || `S${session.season_number}E${session.episode_number}`
    : session.media_title || `File #${session.media_file_id}`;
  const username = session.username || `User #${session.user_id}`;
  const elapsed = getTimeAgo(session.started_at);
  const historyHref = `/admin/history?user_id=${session.user_id}${session.profile_id ? `&profile_id=${encodeURIComponent(session.profile_id)}` : ""}`;

  const methodColor =
    session.play_method === "direct"
      ? "bg-green-500/10 text-green-400 border-green-500/15"
      : session.play_method === "remux"
        ? "bg-blue-500/10 text-blue-400 border-blue-500/15"
        : "bg-amber-500/10 text-amber-400 border-amber-500/15";

  return (
    <div className="surface-panel flex gap-3.5 rounded-[1.5rem] border-0 p-3.5 transition-colors duration-150">
      {/* Poster */}
      <div
        className="bg-surface border-border flex w-[70px] flex-shrink-0 items-center justify-center overflow-hidden rounded-lg border"
        style={{ aspectRatio: "2/3" }}
      >
        {session.poster_url ? (
          <img
            src={session.poster_url}
            alt={session.media_title}
            className="h-full w-full object-cover"
          />
        ) : (
          <Play className="text-primary/40 h-5 w-5" />
        )}
      </div>

      {/* Info */}
      <div className="flex min-w-0 flex-1 flex-col">
        {isEpisode ? (
          <>
            <Link
              to={historyHref}
              className="hover:text-primary truncate text-sm font-bold transition-colors"
            >
              {session.series_name}
            </Link>
            <div className="text-muted-foreground mb-1.5 text-xs">
              S{session.season_number} · E{session.episode_number}
              {session.media_title ? ` — ${session.media_title}` : ""}
            </div>
          </>
        ) : (
          <>
            <Link
              to={historyHref}
              className="hover:text-primary truncate text-sm font-bold transition-colors"
            >
              {title}
            </Link>
            {session.media_type && (
              <div className="text-muted-foreground mb-1.5 text-xs">
                {session.media_type === "movie" ? "Movie" : "Series"}
              </div>
            )}
          </>
        )}

        {/* Tags */}
        <div className="mb-1.5 flex flex-wrap gap-1">
          <span
            className={`inline-flex rounded border px-1.5 py-0.5 text-[9px] font-semibold ${methodColor}`}
          >
            {session.play_method || "unknown"}
          </span>
          {session.reporting_node && (
            <span className="border-primary/10 bg-primary/5 text-primary inline-flex rounded border px-1.5 py-0.5 text-[9px] font-semibold">
              {session.node_display_name || session.reporting_node}
            </span>
          )}
          {(session.profile_name || session.profile_id) && (
            <span className="border-border bg-surface text-muted-foreground inline-flex rounded border px-1.5 py-0.5 text-[9px] font-semibold">
              {session.profile_name || session.profile_id}
            </span>
          )}
        </div>

        {/* User */}
        <div className="mt-auto flex items-center gap-1.5">
          <div
            className="text-primary-foreground flex h-[22px] w-[22px] items-center justify-center rounded-full text-[9px] font-bold"
            style={{ background: `var(--primary)` }}
          >
            {username.charAt(0).toUpperCase()}
          </div>
          <span className="text-xs font-medium">{username}</span>
          <span className="text-muted-foreground ml-auto text-[10px]">{elapsed}</span>
        </div>
      </div>
    </div>
  );
}

function LibrariesCard({ libraries }: { libraries: LibraryType[] }) {
  const scanLibrary = useScanLibrary();

  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-3">
        <CardTitle className="text-sm font-bold">Libraries</CardTitle>
        <Link
          to="/admin/libraries"
          className="text-muted-foreground hover:text-primary text-[11px] transition-colors"
        >
          Manage ›
        </Link>
      </CardHeader>
      <CardContent className="space-y-2">
        {libraries.length === 0 ? (
          <div className="text-muted-foreground py-4 text-center text-sm">
            No libraries configured.
          </div>
        ) : (
          libraries.map((lib) => (
            <div
              key={lib.id}
              className="bg-surface border-border hover:bg-surface-hover flex cursor-pointer items-center gap-3 rounded-md border p-3 transition-colors duration-150"
            >
              {lib.poster_url ? (
                <img
                  src={lib.poster_url}
                  alt={lib.name}
                  className="border-border h-8 w-14 flex-shrink-0 rounded border object-cover"
                />
              ) : (
                <div className="bg-primary/5 border-primary/10 flex h-10 w-10 flex-shrink-0 items-center justify-center rounded-lg border">
                  <Library className="text-primary h-4 w-4" />
                </div>
              )}
              <div className="min-w-0 flex-1">
                <div className="text-sm font-bold">{lib.name}</div>
                <div className="text-muted-foreground text-[11px]">
                  {lib.type} · {lib.paths.length} {lib.paths.length === 1 ? "path" : "paths"}
                </div>
              </div>
              <div className="flex flex-shrink-0 items-center gap-2">
                <Button
                  variant="ghost"
                  size="icon"
                  className="h-7 w-7"
                  onClick={(e) => {
                    e.stopPropagation();
                    scanLibrary.mutate(lib.id);
                  }}
                  title="Scan"
                >
                  <RefreshCw className="h-3 w-3" />
                </Button>
                <div
                  className={`h-2 w-2 rounded-full ${lib.enabled ? "bg-green-500" : "bg-muted-foreground/30"}`}
                />
              </div>
            </div>
          ))
        )}
      </CardContent>
    </Card>
  );
}

function UsersCard({ users }: { users: AdminUser[] }) {
  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-3">
        <CardTitle className="text-sm font-bold">Users</CardTitle>
        <Link
          to="/admin/users"
          className="text-muted-foreground hover:text-primary text-[11px] transition-colors"
        >
          Manage ›
        </Link>
      </CardHeader>
      <CardContent>
        {users.length === 0 ? (
          <div className="text-muted-foreground py-4 text-center text-sm">No users.</div>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>User</TableHead>
                <TableHead>Role</TableHead>
                <TableHead>Status</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {users.slice(0, 8).map((u) => (
                <TableRow key={u.id}>
                  <TableCell>
                    <div className="flex items-center gap-2.5">
                      <div
                        className="text-primary-foreground flex h-7 w-7 flex-shrink-0 items-center justify-center rounded-full text-[10px] font-bold"
                        style={{ background: `var(--primary)` }}
                      >
                        {u.username.charAt(0).toUpperCase()}
                      </div>
                      <div>
                        <div className="text-[13px] font-semibold">{u.username}</div>
                        <div className="text-muted-foreground text-[10px]">{u.email}</div>
                      </div>
                    </div>
                  </TableCell>
                  <TableCell>
                    <Badge variant={u.role === "admin" ? "default" : "secondary"}>{u.role}</Badge>
                  </TableCell>
                  <TableCell>
                    <Badge variant={u.enabled ? "outline" : "destructive"}>
                      {u.enabled ? "Active" : "Disabled"}
                    </Badge>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </CardContent>
    </Card>
  );
}

function ActivityCard({ sessions }: { sessions: AdminSession[] }) {
  if (sessions.length === 0) return null;

  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-3">
        <CardTitle className="text-sm font-bold">Recent Activity</CardTitle>
        <Link
          to="/admin/activity"
          className="text-muted-foreground hover:text-primary text-[11px] transition-colors"
        >
          View all ›
        </Link>
      </CardHeader>
      <CardContent>
        <div className="space-y-0">
          {sessions.slice(0, 10).map((s) => {
            const title = s.media_title || `File #${s.media_file_id}`;
            const username = s.username || `User #${s.user_id}`;
            return (
              <div
                key={s.session_id}
                className="border-border/30 flex items-start gap-3 border-b py-2.5"
              >
                <div className="text-primary bg-primary/5 border-primary/10 flex h-[30px] w-[30px] flex-shrink-0 items-center justify-center rounded-lg border">
                  <Play className="h-3.5 w-3.5" />
                </div>
                <div className="min-w-0 flex-1">
                  <div className="text-muted-foreground text-xs leading-relaxed">
                    <span className="text-foreground font-semibold">{username}</span>
                    {" started watching "}
                    <Link
                      to={`/admin/history?user_id=${s.user_id}${s.profile_id ? `&profile_id=${encodeURIComponent(s.profile_id)}` : ""}`}
                      className="text-foreground hover:text-primary font-semibold transition-colors"
                    >
                      {title}
                    </Link>
                  </div>
                  <div className="text-muted-foreground mt-0.5 text-[10px]">
                    {getTimeAgo(s.started_at)}
                  </div>
                </div>
                <div className="flex-shrink-0">
                  <AdminSessionActions session={s} compact />
                </div>
              </div>
            );
          })}
        </div>
      </CardContent>
    </Card>
  );
}

// --- Helpers ---

function getTimeAgo(dateStr: string): string {
  const now = Date.now();
  const then = new Date(dateStr).getTime();
  const diff = Math.max(0, now - then);
  const minutes = Math.floor(diff / 60000);
  if (minutes < 1) return "Just now";
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.floor(hours / 24);
  return `${days}d ago`;
}
