import type { ReactNode } from "react";
import { useState, useMemo } from "react";
import { Link } from "react-router";
import { AdminSessionActions } from "@/components/AdminSessionActions";
import { useOperationalLogs } from "@/hooks/queries/admin/logs";
import { useAdminSessionStream } from "@/components/AdminSessionStreamProvider";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import type { AdminSession, OperationalLogEntry, IPUserEntry } from "@/api/types";
import { useIPUsers } from "@/hooks/queries/admin/ips";
import { formatCodecLabel } from "@/player/playback-info";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
  RefreshCw,
  Search,
  Radio,
  Filter,
  X,
  Play,
  Terminal,
  ChevronDown,
  ChevronUp,
} from "lucide-react";

type SortField = "username" | "media" | "method" | "node" | "started";
type SortDir = "asc" | "desc";

export default function AdminActivity() {
  const { sessions, isLoading, refresh, connectionState, error } = useAdminSessionStream();
  const [search, setSearch] = useState("");
  const [methodFilter, setMethodFilter] = useState<string | null>(null);
  const [nodeFilter, setNodeFilter] = useState<string | null>(null);
  const [typeFilter, setTypeFilter] = useState<string | null>(null);
  const [sortField, setSortField] = useState<SortField>("started");
  const [sortDir, setSortDir] = useState<SortDir>("desc");
  const [ipSearch, setIPSearch] = useState("");
  const [activeIP, setActiveIP] = useState("");
  const { data: ipUsers = [], isLoading: ipLoading } = useIPUsers(activeIP);

  // Aggregate counts
  const methods = useMemo(() => {
    const counts: Record<string, number> = {};
    for (const s of sessions)
      counts[s.play_method || "unknown"] = (counts[s.play_method || "unknown"] || 0) + 1;
    return counts;
  }, [sessions]);

  const nodes = useMemo(() => {
    const counts: Record<string, number> = {};
    for (const s of sessions)
      counts[s.reporting_node || "unknown"] = (counts[s.reporting_node || "unknown"] || 0) + 1;
    return counts;
  }, [sessions]);

  // Filter + sort
  const filtered = useMemo(() => {
    let result = sessions;
    if (search) {
      const q = search.toLowerCase();
      result = result.filter(
        (s) =>
          s.username?.toLowerCase().includes(q) ||
          s.media_title?.toLowerCase().includes(q) ||
          s.series_name?.toLowerCase().includes(q),
      );
    }
    if (methodFilter) result = result.filter((s) => s.play_method === methodFilter);
    if (nodeFilter) result = result.filter((s) => s.reporting_node === nodeFilter);
    if (typeFilter) result = result.filter((s) => s.media_type === typeFilter);

    return [...result].sort((a, b) => {
      let cmp = 0;
      switch (sortField) {
        case "username":
          cmp = (a.username || "").localeCompare(b.username || "");
          break;
        case "media":
          cmp = getDisplayTitle(a).localeCompare(getDisplayTitle(b));
          break;
        case "method":
          cmp = (a.play_method || "").localeCompare(b.play_method || "");
          break;
        case "node":
          cmp = (a.reporting_node || "").localeCompare(b.reporting_node || "");
          break;
        case "started":
          cmp = new Date(a.started_at).getTime() - new Date(b.started_at).getTime();
          break;
      }
      return sortDir === "asc" ? cmp : -cmp;
    });
  }, [sessions, search, methodFilter, nodeFilter, typeFilter, sortField, sortDir]);

  const toggleSort = (field: SortField) => {
    if (sortField === field) {
      setSortDir((d) => (d === "asc" ? "desc" : "asc"));
    } else {
      setSortField(field);
      setSortDir(field === "started" ? "desc" : "asc");
    }
  };

  const activeFilters = [methodFilter, nodeFilter, typeFilter].filter(Boolean).length;

  if (isLoading) return <div className="text-muted-foreground p-8">Loading activity...</div>;

  return (
    <div className="space-y-5 lg:space-y-6">
      {/* Header */}
      <div className="page-header">
        <div className="space-y-3">
          <div className="flex items-center gap-3">
            <h1 className="page-title text-[clamp(2rem,4vw,3rem)]">Activity</h1>
            {sessions.length > 0 && (
              <span className="live-badge flex items-center gap-1.5">
                <Radio className="h-3 w-3" />
                {sessions.length} live
              </span>
            )}
          </div>
          <p className="text-muted-foreground mt-1 text-[13px]">
            {sessions.length === 0
              ? "No active streams"
              : `${sessions.length} active stream${sessions.length !== 1 ? "s" : ""} across ${Object.keys(nodes).length} node${Object.keys(nodes).length !== 1 ? "s" : ""}`}
          </p>
        </div>
        <div className="flex items-center gap-3">
          <div className="text-right">
            <div className="text-muted-foreground text-[10px] font-semibold tracking-wider uppercase">
              Stream
            </div>
            <div className="text-[12px]">{formatConnectionState(connectionState)}</div>
            {error && <div className="text-muted-foreground text-[11px]">{error}</div>}
          </div>
          <Button variant="outline" size="sm" onClick={() => void refresh()}>
            <RefreshCw className="h-3.5 w-3.5" />
            Refresh
          </Button>
        </div>
      </div>

      {/* IP Lookup */}
      <div className="surface-panel rounded-[1.6rem] border-0 p-4">
        <form
          onSubmit={(e) => {
            e.preventDefault();
            setActiveIP(ipSearch.trim());
          }}
          className="flex items-center gap-2"
        >
          <Input
            type="text"
            placeholder="IP lookup (e.g. 203.0.113.50)"
            value={ipSearch}
            onChange={(e) => setIPSearch(e.target.value)}
            className="max-w-xs font-mono text-sm"
          />
          <Button type="submit" variant="outline" size="sm" disabled={!ipSearch.trim()}>
            <Search className="mr-1 h-3.5 w-3.5" />
            Lookup
          </Button>
        </form>
        {activeIP && (
          <div className="mt-3">
            {ipLoading ? (
              <p className="text-muted-foreground text-sm">Searching...</p>
            ) : ipUsers.length === 0 ? (
              <p className="text-muted-foreground text-sm">
                No users found for {activeIP} in the last 30 days.
              </p>
            ) : (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>User</TableHead>
                    <TableHead>First Seen</TableHead>
                    <TableHead>Last Seen</TableHead>
                    <TableHead className="text-right">Requests</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {ipUsers.map((entry: IPUserEntry) => (
                    <TableRow key={entry.user_id}>
                      <TableCell>
                        <Link
                          to={`/admin/users/${entry.user_id}`}
                          className="text-primary font-medium hover:underline"
                        >
                          {entry.username || `User #${entry.user_id}`}
                        </Link>
                      </TableCell>
                      <TableCell className="text-sm">
                        {new Date(entry.first_seen).toLocaleString()}
                      </TableCell>
                      <TableCell className="text-sm">
                        {new Date(entry.last_seen).toLocaleString()}
                      </TableCell>
                      <TableCell className="text-right">
                        {entry.request_count.toLocaleString()}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
          </div>
        )}
      </div>

      {/* Summary strip */}
      {sessions.length > 0 && (
        <div className="surface-panel rounded-[1.6rem] border-0 p-4">
          {/* Method distribution bar */}
          <div className="mb-3">
            <div className="text-muted-foreground mb-2 text-[10px] font-semibold tracking-wider uppercase">
              Play Method
            </div>
            <div className="flex h-1.5 overflow-hidden rounded-full">
              {Object.entries(methods)
                .sort(([a], [b]) => a.localeCompare(b))
                .map(([method, count]) => (
                  <div
                    key={method}
                    className={`transition-all duration-500 ${methodBarColor(method)}`}
                    style={{ width: `${(count / sessions.length) * 100}%` }}
                  />
                ))}
            </div>
            <div className="mt-2 flex flex-wrap gap-x-4 gap-y-1">
              {Object.entries(methods)
                .sort(([a], [b]) => a.localeCompare(b))
                .map(([method, count]) => (
                  <button
                    key={method}
                    onClick={() => setMethodFilter(methodFilter === method ? null : method)}
                    className={`flex items-center gap-1.5 text-[11px] transition-opacity ${
                      methodFilter && methodFilter !== method ? "opacity-30" : ""
                    }`}
                  >
                    <span
                      className={`inline-block h-2 w-2 rounded-full ${methodDotColor(method)}`}
                    />
                    <span className="font-medium capitalize">{method}</span>
                    <span className="text-muted-foreground tabular-nums">{count}</span>
                  </button>
                ))}
            </div>
          </div>

          {/* Node breakdown */}
          {Object.keys(nodes).length > 1 && (
            <div className="border-border border-t pt-3">
              <div className="text-muted-foreground mb-2 text-[10px] font-semibold tracking-wider uppercase">
                By Node
              </div>
              <div className="flex flex-wrap gap-1.5">
                {Object.entries(nodes)
                  .sort(([, a], [, b]) => b - a)
                  .map(([node, count]) => (
                    <button
                      key={node}
                      onClick={() => setNodeFilter(nodeFilter === node ? null : node)}
                      className={`bg-surface border-border hover:border-primary/20 rounded-md border px-2.5 py-1 text-[11px] font-medium transition-all ${
                        nodeFilter === node
                          ? "border-primary/40 bg-primary/10 text-primary"
                          : nodeFilter
                            ? "opacity-30"
                            : ""
                      }`}
                    >
                      {node}
                      <span className="text-muted-foreground ml-1.5 tabular-nums">{count}</span>
                    </button>
                  ))}
              </div>
            </div>
          )}
        </div>
      )}

      {/* Search + filters */}
      <div className="flex flex-wrap items-center gap-2">
        <div className="relative min-w-[200px] flex-1">
          <Search className="text-muted-foreground pointer-events-none absolute top-1/2 left-3 h-3.5 w-3.5 -translate-y-1/2" />
          <Input
            placeholder="Filter by user or media..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="h-8 pl-9 text-[13px]"
          />
          {search && (
            <button
              onClick={() => setSearch("")}
              className="text-muted-foreground hover:text-foreground absolute top-1/2 right-2.5 -translate-y-1/2"
            >
              <X className="h-3.5 w-3.5" />
            </button>
          )}
        </div>
        <div className="flex gap-1">
          {(["movie", "series"] as const).map((t) => (
            <button
              key={t}
              onClick={() => setTypeFilter(typeFilter === t ? null : t)}
              className={`rounded-md border px-2.5 py-1.5 text-[11px] font-medium capitalize transition-all ${
                typeFilter === t
                  ? "border-primary/40 bg-primary/10 text-primary"
                  : "border-border bg-surface text-muted-foreground hover:text-foreground"
              }`}
            >
              {t === "movie" ? "Movies" : "Series"}
            </button>
          ))}
        </div>
        {activeFilters > 0 && (
          <button
            onClick={() => {
              setMethodFilter(null);
              setNodeFilter(null);
              setTypeFilter(null);
            }}
            className="text-muted-foreground hover:text-foreground flex items-center gap-1 text-[11px]"
          >
            <X className="h-3 w-3" />
            Clear filters
          </button>
        )}
      </div>

      {/* Filter status */}
      {(search || activeFilters > 0) && (
        <div className="text-muted-foreground text-[11px]">
          Showing {filtered.length} of {sessions.length} streams
        </div>
      )}

      {/* Stream table */}
      {filtered.length === 0 ? (
        <EmptyState hasData={sessions.length > 0} />
      ) : (
        <div className="bg-card border-border overflow-hidden rounded-lg border">
          {/* Table header */}
          <div className="border-border bg-surface/50 hidden grid-cols-[minmax(140px,1.5fr)_minmax(220px,2.2fr)_minmax(150px,1.4fr)_minmax(150px,1.4fr)_minmax(90px,1fr)_80px] items-center gap-3 border-b px-4 py-2.5 sm:grid">
            <SortHeader field="username" current={sortField} dir={sortDir} onClick={toggleSort}>
              User
            </SortHeader>
            <SortHeader field="media" current={sortField} dir={sortDir} onClick={toggleSort}>
              Stream
            </SortHeader>
            <SortHeader field="method" current={sortField} dir={sortDir} onClick={toggleSort}>
              Video
            </SortHeader>
            <div className="text-muted-foreground text-[10px] font-semibold tracking-wider uppercase">
              Audio
            </div>
            <SortHeader field="node" current={sortField} dir={sortDir} onClick={toggleSort}>
              Node
            </SortHeader>
            <SortHeader
              field="started"
              current={sortField}
              dir={sortDir}
              onClick={toggleSort}
              className="justify-end"
            >
              Time
            </SortHeader>
          </div>

          {/* Rows */}
          <div className="max-h-[calc(100vh-420px)] min-h-[200px] overflow-y-auto">
            {filtered.map((session, i) => (
              <StreamRow key={session.session_id} session={session} even={i % 2 === 0} />
            ))}
          </div>
        </div>
      )}
    </div>
  );
}

// --- Sub-components ---

function StreamRow({ session, even }: { session: AdminSession; even: boolean }) {
  const [ffmpegOpen, setFFmpegOpen] = useState(false);
  const title = getDisplayTitle(session);
  const subtitle = getDisplaySubtitle(session);
  const username = session.username || `User #${session.user_id}`;
  const elapsed = getElapsed(session.started_at);
  const historyHref = `/admin/history?user_id=${session.user_id}${session.profile_id ? `&profile_id=${encodeURIComponent(session.profile_id)}` : ""}`;
  const sourceContainer = session.source_container?.trim().toUpperCase();
  const streamBitrate = formatSessionBitrate(session.stream_bitrate_kbps);
  const streamMeta = [sourceContainer, streamBitrate].filter(Boolean).join(" · ");
  const userMeta = session.client_ip?.trim() || "—";
  const videoDecision = session.video_decision || session.play_method;
  const audioDecision =
    session.audio_decision || (session.transcode_audio ? "transcode" : session.play_method);
  const logsHref = `/admin/logs?playback_session_id=${encodeURIComponent(session.session_id)}&focus=playback`;
  const ffmpegLogsHref = `${logsHref}&component=ffmpeg`;
  const ffmpegLogs = useOperationalLogs(
    {
      playback_session_id: session.session_id,
      component: "ffmpeg",
      limit: 12,
    },
    ffmpegOpen,
  );
  const ffmpegRows = ffmpegLogs.data?.entries ?? [];

  return (
    <div
      className={`border-border/30 hover:bg-surface/60 border-b transition-colors duration-100 ${
        even ? "" : "bg-surface/20"
      }`}
    >
      {/* Desktop row */}
      <div className="hidden grid-cols-[minmax(140px,1.5fr)_minmax(220px,2.2fr)_minmax(150px,1.4fr)_minmax(150px,1.4fr)_minmax(90px,1fr)_80px] items-center gap-3 px-4 py-2.5 sm:grid">
        {/* User */}
        <div className="flex min-w-0 items-center gap-2">
          <div
            className="text-primary-foreground flex h-6 w-6 flex-shrink-0 items-center justify-center rounded-full text-[9px] font-bold"
            style={{ background: "var(--primary)" }}
          >
            {username.charAt(0).toUpperCase()}
          </div>
          <div className="min-w-0">
            <Link
              to={historyHref}
              className="hover:text-primary block truncate text-[13px] font-medium transition-colors"
            >
              {username}
            </Link>
            <div className="text-muted-foreground truncate text-[10px]">{userMeta}</div>
          </div>
        </div>

        {/* Stream */}
        <div className="min-w-0">
          <div className="truncate text-[13px] font-medium">{title}</div>
          {subtitle && <div className="text-muted-foreground truncate text-[10px]">{subtitle}</div>}
          {streamMeta && (
            <div className="text-muted-foreground truncate text-[10px]">{streamMeta}</div>
          )}
        </div>

        {/* Video */}
        <div className="min-w-0">
          <span
            className={`mb-1 inline-flex rounded border px-1.5 py-0.5 text-[9px] font-semibold ${methodBadgeColor(videoDecision)}`}
          >
            {formatDecisionLabel(videoDecision)}
          </span>
          <div className="truncate text-[12px] font-medium">{formatVideoSummary(session)}</div>
          <div className="text-muted-foreground truncate text-[10px]">
            {formatVideoDetail(session)}
          </div>
        </div>

        {/* Audio */}
        <div className="min-w-0">
          <span
            className={`mb-1 inline-flex rounded border px-1.5 py-0.5 text-[9px] font-semibold ${methodBadgeColor(audioDecision)}`}
          >
            {formatDecisionLabel(audioDecision)}
          </span>
          <div className="truncate text-[12px] font-medium">{formatAudioSummary(session)}</div>
          <div className="text-muted-foreground truncate text-[10px]">
            {formatAudioDetail(session)}
          </div>
        </div>

        {/* Node */}
        <div className="min-w-0">
          <div className="text-muted-foreground truncate text-[12px]">
            {session.node_display_name || session.reporting_node || "—"}
          </div>
          {(session.profile_name || session.profile_id) && (
            <div className="text-muted-foreground truncate text-[10px]">
              {session.profile_name || session.profile_id}
            </div>
          )}
        </div>

        {/* Duration */}
        <div className="text-muted-foreground text-right font-mono text-[12px] tabular-nums">
          <div>{elapsed}</div>
          <div className="mt-1 flex items-center justify-end gap-2">
            <AdminSessionActions session={session} compact />
            <button
              type="button"
              onClick={() => setFFmpegOpen((open) => !open)}
              className="text-primary inline-flex items-center gap-1 text-[11px]"
            >
              <Terminal className="h-3 w-3" />
              FFmpeg
              {ffmpegOpen ? <ChevronUp className="h-3 w-3" /> : <ChevronDown className="h-3 w-3" />}
            </button>
            <Link to={logsHref} className="text-primary inline-block text-[11px]">
              View Logs
            </Link>
            <Link to={ffmpegLogsHref} className="text-primary/80 inline-block text-[11px]">
              FFmpeg Logs
            </Link>
          </div>
        </div>
      </div>

      {/* Mobile row */}
      <div className="flex gap-3 px-4 py-3 sm:hidden">
        <div
          className="text-primary-foreground flex h-7 w-7 flex-shrink-0 items-center justify-center rounded-full text-[10px] font-bold"
          style={{ background: "var(--primary)" }}
        >
          {username.charAt(0).toUpperCase()}
        </div>
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <Link
              to={historyHref}
              className="hover:text-primary truncate text-[13px] font-semibold transition-colors"
            >
              {username}
            </Link>
            <span
              className={`inline-flex flex-shrink-0 rounded border px-1.5 py-0.5 text-[9px] font-semibold ${methodBadgeColor(session.play_method)}`}
            >
              {session.play_method || "?"}
            </span>
          </div>
          <div className="text-muted-foreground mt-0.5 flex items-center gap-1.5 text-[11px]">
            <span className="truncate">{title}</span>
            <span className="flex-shrink-0">·</span>
            <span className="flex-shrink-0 font-mono tabular-nums">{elapsed}</span>
          </div>
          <div className="text-muted-foreground mt-1 truncate text-[10px]">
            {[userMeta, streamMeta || null].filter(Boolean).join(" · ")}
          </div>
          <div className="mt-2 grid grid-cols-2 gap-2">
            <div className="rounded-md border border-white/6 bg-white/[0.03] px-2 py-1.5">
              <div className="mb-1 flex items-center gap-1.5">
                <span
                  className={`inline-flex rounded border px-1.5 py-0.5 text-[9px] font-semibold ${methodBadgeColor(videoDecision)}`}
                >
                  {formatDecisionLabel(videoDecision)}
                </span>
                <span className="text-muted-foreground text-[9px] tracking-wide uppercase">
                  Video
                </span>
              </div>
              <div className="truncate text-[11px] font-medium">{formatVideoSummary(session)}</div>
              <div className="text-muted-foreground truncate text-[10px]">
                {formatVideoDetail(session)}
              </div>
            </div>
            <div className="rounded-md border border-white/6 bg-white/[0.03] px-2 py-1.5">
              <div className="mb-1 flex items-center gap-1.5">
                <span
                  className={`inline-flex rounded border px-1.5 py-0.5 text-[9px] font-semibold ${methodBadgeColor(audioDecision)}`}
                >
                  {formatDecisionLabel(audioDecision)}
                </span>
                <span className="text-muted-foreground text-[9px] tracking-wide uppercase">
                  Audio
                </span>
              </div>
              <div className="truncate text-[11px] font-medium">{formatAudioSummary(session)}</div>
              <div className="text-muted-foreground truncate text-[10px]">
                {formatAudioDetail(session)}
              </div>
            </div>
          </div>
          <div className="mt-2 flex items-center gap-3">
            <AdminSessionActions session={session} compact />
            <button
              type="button"
              onClick={() => setFFmpegOpen((open) => !open)}
              className="text-primary inline-flex items-center gap-1 text-[11px] font-medium"
            >
              <Terminal className="h-3 w-3" />
              FFmpeg
              {ffmpegOpen ? <ChevronUp className="h-3 w-3" /> : <ChevronDown className="h-3 w-3" />}
            </button>
            <Link to={logsHref} className="text-primary inline-block text-[11px] font-medium">
              View Logs
            </Link>
            <Link
              to={ffmpegLogsHref}
              className="text-primary/80 inline-block text-[11px] font-medium"
            >
              FFmpeg Logs
            </Link>
          </div>
        </div>
      </div>

      {ffmpegOpen && (
        <FFmpegLogPanel
          sessionID={session.session_id}
          rows={ffmpegRows}
          isLoading={ffmpegLogs.isLoading}
          isFetching={ffmpegLogs.isFetching}
          logsHref={`${logsHref}&component=ffmpeg`}
        />
      )}
    </div>
  );
}

function FFmpegLogPanel({
  sessionID,
  rows,
  isLoading,
  isFetching,
  logsHref,
}: {
  sessionID: string;
  rows: OperationalLogEntry[];
  isLoading: boolean;
  isFetching: boolean;
  logsHref: string;
}) {
  return (
    <div className="border-border/50 border-t bg-[linear-gradient(180deg,rgba(18,20,24,0.95),rgba(10,11,14,0.98))] px-4 py-3">
      <div className="mb-2 flex items-center justify-between gap-3">
        <div>
          <div className="flex items-center gap-2">
            <div className="rounded-full border border-emerald-400/30 bg-emerald-400/10 px-2 py-0.5 text-[10px] font-semibold tracking-[0.2em] text-emerald-300 uppercase">
              FFmpeg
            </div>
            <div className="text-[11px] font-medium text-white/85">Live transcode console</div>
          </div>
          <div className="mt-1 font-mono text-[10px] text-white/45">{sessionID}</div>
        </div>
        <div className="flex items-center gap-3">
          {isFetching && <div className="text-[10px] text-white/45">Refreshing…</div>}
          <Link
            to={logsHref}
            className="text-[11px] font-medium text-emerald-300 hover:text-emerald-200"
          >
            Open full ffmpeg logs
          </Link>
        </div>
      </div>

      <div className="overflow-hidden rounded-xl border border-white/8 bg-black/60 shadow-[0_18px_60px_rgba(0,0,0,0.35)]">
        {isLoading ? (
          <div className="px-4 py-6 font-mono text-[11px] text-white/50">
            Loading ffmpeg output…
          </div>
        ) : rows.length === 0 ? (
          <div className="px-4 py-6 font-mono text-[11px] text-white/45">
            No ffmpeg rows yet for this session. If the session is direct play or remux without a
            transcode worker, nothing will appear here.
          </div>
        ) : (
          <div className="max-h-64 overflow-y-auto">
            {rows.map((row) => (
              <div
                key={row.id}
                className="grid grid-cols-[120px_1fr] gap-3 border-b border-white/6 px-4 py-2.5 last:border-b-0"
              >
                <div className="space-y-1">
                  <div className="font-mono text-[10px] text-white/55">
                    {formatTimeOnly(row.timestamp)}
                  </div>
                  <div className="text-[10px] tracking-[0.18em] text-white/35 uppercase">
                    {row.message.includes("stderr") ? "stderr" : "event"}
                  </div>
                </div>
                <div className="min-w-0">
                  <div className="font-mono text-[11px] leading-5 break-words text-emerald-100">
                    {ffmpegRowText(row)}
                  </div>
                  <div className="mt-1 flex flex-wrap gap-x-3 gap-y-1 text-[10px] text-white/35">
                    {row.node_id && <span>{row.node_id}</span>}
                    {stringAttr(row, "target_resolution") !== "-" && (
                      <span>{stringAttr(row, "target_resolution")}</span>
                    )}
                    {stringAttr(row, "hw_accel") !== "-" && (
                      <span>{stringAttr(row, "hw_accel")}</span>
                    )}
                    {stringAttr(row, "restart_count") !== "-" && (
                      <span>restart {stringAttr(row, "restart_count")}</span>
                    )}
                  </div>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

function SortHeader({
  field,
  current,
  dir,
  onClick,
  children,
  className = "",
}: {
  field: SortField;
  current: SortField;
  dir: SortDir;
  onClick: (field: SortField) => void;
  children: ReactNode;
  className?: string;
}) {
  const active = current === field;
  return (
    <button
      onClick={() => onClick(field)}
      className={`text-muted-foreground flex items-center gap-1 text-[10px] font-semibold tracking-wider uppercase transition-colors ${
        active ? "text-foreground" : "hover:text-foreground"
      } ${className}`}
    >
      {children}
      {active && <span className="text-[8px]">{dir === "asc" ? "▲" : "▼"}</span>}
    </button>
  );
}

function formatConnectionState(state: "connecting" | "live" | "disconnected") {
  switch (state) {
    case "connecting":
      return "Connecting";
    case "live":
      return "Live";
    default:
      return "Disconnected";
  }
}

function ffmpegRowText(entry: OperationalLogEntry) {
  const line = stringAttr(entry, "ffmpeg_line");
  if (line !== "-") return line;
  const event = stringAttr(entry, "ffmpeg_event");
  if (event !== "-") return event;
  return entry.message;
}

function formatTimeOnly(value: string) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleTimeString();
}

function EmptyState({ hasData }: { hasData: boolean }) {
  return (
    <div className="text-muted-foreground flex flex-col items-center justify-center py-20 text-sm">
      {hasData ? (
        <>
          <Filter className="mb-3 h-8 w-8 opacity-20" />
          <span>No streams match your filters</span>
        </>
      ) : (
        <>
          <Play className="mb-3 h-8 w-8 opacity-20" />
          <span>No active streams</span>
        </>
      )}
    </div>
  );
}

// --- Helpers ---

function getDisplayTitle(session: AdminSession): string {
  if (session.series_name && session.season_number != null && session.episode_number != null) {
    return session.series_name;
  }
  return session.media_title || `File #${session.media_file_id}`;
}

function getDisplaySubtitle(session: AdminSession): string | null {
  if (session.series_name && session.season_number != null && session.episode_number != null) {
    const ep = `S${session.season_number}E${session.episode_number}`;
    return session.media_title ? `${ep} · ${session.media_title}` : ep;
  }
  if (session.media_type === "movie") return "Movie";
  if (session.media_type === "series") return "Series";
  return null;
}

function getElapsed(dateStr: string): string {
  const diff = Math.max(0, Date.now() - new Date(dateStr).getTime());
  const totalSec = Math.floor(diff / 1000);
  const h = Math.floor(totalSec / 3600);
  const m = Math.floor((totalSec % 3600) / 60);
  const s = totalSec % 60;
  if (h > 0) return `${h}:${String(m).padStart(2, "0")}:${String(s).padStart(2, "0")}`;
  return `${m}:${String(s).padStart(2, "0")}`;
}

function formatDecisionLabel(decision?: string): string {
  switch (decision) {
    case "direct":
      return "Direct";
    case "remux":
      return "Remux";
    case "transcode":
      return "Transcode";
    default:
      return "Unknown";
  }
}

function stringAttr(entry: OperationalLogEntry, key: string) {
  const value = entry.attrs?.[key];
  if (typeof value === "string" && value.length > 0) return value;
  if (typeof value === "number") return String(value);
  return "-";
}

function formatSessionBitrate(kbps?: number | null): string | null {
  if (!kbps || kbps <= 0) {
    return null;
  }
  if (kbps >= 1000) {
    return `${(kbps / 1000).toFixed(1)} Mbps`;
  }
  return `${Math.round(kbps)} kbps`;
}

function formatVideoSummary(session: AdminSession): string {
  return (
    [formatCodec(session.source_video_codec), session.source_video_resolution?.trim()]
      .filter(Boolean)
      .join(" · ") || "Unknown source"
  );
}

function formatVideoDetail(session: AdminSession): string {
  const decision = session.video_decision || session.play_method;
  if (decision === "transcode") {
    const target = [formatCodec(session.target_video_codec), session.target_resolution?.trim()]
      .filter(Boolean)
      .join(" · ");
    return target ? `→ ${target}` : "Transcoding";
  }
  if (decision === "remux") {
    return "Container remux";
  }
  if (decision === "direct") {
    return "No video conversion";
  }
  return "—";
}

function formatAudioSummary(session: AdminSession): string {
  const lead = session.source_audio_title?.trim() || session.source_audio_language?.trim();
  const format = [
    formatCodec(session.source_audio_codec),
    formatChannelLayout(session.source_audio_channels),
  ]
    .filter(Boolean)
    .join(" ");
  return [lead, format].filter(Boolean).join(" · ") || "Unknown source";
}

function formatAudioDetail(session: AdminSession): string {
  const decision =
    session.audio_decision || (session.transcode_audio ? "transcode" : session.play_method);
  if (decision === "transcode") {
    const target = [
      formatCodec(session.target_audio_codec || "aac"),
      decision === "transcode" ? formatChannelLayout(session.source_audio_channels) : null,
    ]
      .filter(Boolean)
      .join(" ");
    return target ? `→ ${target}` : "Audio transcode";
  }
  if (decision === "remux") {
    return "Container remux";
  }
  if (decision === "direct") {
    return "No audio conversion";
  }
  return "—";
}

function formatCodec(codec?: string): string | null {
  const trimmed = codec?.trim();
  return trimmed ? formatCodecLabel(trimmed) : null;
}

function formatChannelLayout(channels?: number | null): string | null {
  if (!channels || channels <= 0) {
    return null;
  }
  if (channels === 1) {
    return "1.0";
  }
  if (channels === 2) {
    return "2.0";
  }
  if (channels === 6) {
    return "5.1";
  }
  if (channels === 8) {
    return "7.1";
  }
  return `${channels}ch`;
}

function methodBadgeColor(method: string): string {
  switch (method) {
    case "direct":
      return "bg-green-500/10 text-green-400 border-green-500/15";
    case "remux":
      return "bg-blue-500/10 text-blue-400 border-blue-500/15";
    case "transcode":
      return "bg-amber-500/10 text-amber-400 border-amber-500/15";
    default:
      return "bg-surface text-muted-foreground border-border";
  }
}

function methodBarColor(method: string): string {
  switch (method) {
    case "direct":
      return "bg-green-500";
    case "remux":
      return "bg-blue-500";
    case "transcode":
      return "bg-amber-500";
    default:
      return "bg-muted-foreground";
  }
}

function methodDotColor(method: string): string {
  switch (method) {
    case "direct":
      return "bg-green-400";
    case "remux":
      return "bg-blue-400";
    case "transcode":
      return "bg-amber-400";
    default:
      return "bg-muted-foreground";
  }
}
