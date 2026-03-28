import type { ReactNode } from "react";
import { useAdminSessionStream } from "@/components/AdminSessionStreamProvider";
import { useAdminStats } from "@/hooks/queries/admin/stats";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { Film, FileVideo, Users, Play } from "lucide-react";

export default function AdminStats() {
  const { data: stats, isLoading: statsLoading } = useAdminStats();
  const { sessions, isLoading: sessionsLoading } = useAdminSessionStream();
  const loading = statsLoading || sessionsLoading;

  if (loading) return <div className="page-shell py-8">Loading stats...</div>;

  return (
    <div className="page-shell space-y-6 py-4 sm:py-6">
      <div className="page-header gap-5">
        <div className="space-y-3">
          <h1 className="page-title text-[clamp(2rem,4vw,3rem)]">System stats</h1>
          <p className="page-subtitle text-sm sm:text-base">
            Track the size of the library and inspect the sessions currently playing through the
            system.
          </p>
        </div>
      </div>

      {stats && (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 md:grid-cols-4">
          <StatCard
            title="Media Items"
            value={stats.total_items}
            icon={<Film className="h-4 w-4" />}
          />
          <StatCard
            title="Files"
            value={stats.total_files}
            icon={<FileVideo className="h-4 w-4" />}
          />
          <StatCard title="Users" value={stats.total_users} icon={<Users className="h-4 w-4" />} />
          <StatCard
            title="Active Sessions"
            value={sessions.length}
            icon={<Play className="h-4 w-4" />}
          />
        </div>
      )}

      <div className="space-y-3">
        <h2 className="text-lg font-medium tracking-tight">Active playback sessions</h2>
        {sessions.length === 0 ? (
          <div className="surface-panel text-muted-foreground rounded-[1.6rem] py-12 text-center text-sm">
            No active sessions.
          </div>
        ) : (
          <div className="surface-panel overflow-x-auto rounded-[1.6rem] border-0">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Session ID</TableHead>
                  <TableHead>User ID</TableHead>
                  <TableHead>File ID</TableHead>
                  <TableHead>Method</TableHead>
                  <TableHead>Started</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {sessions.map((s) => (
                  <TableRow key={s.session_id}>
                    <TableCell className="font-mono text-xs">
                      {s.session_id.slice(0, 8)}...
                    </TableCell>
                    <TableCell>{s.user_id}</TableCell>
                    <TableCell>{s.media_file_id}</TableCell>
                    <TableCell>{s.play_method}</TableCell>
                    <TableCell className="text-xs">
                      {new Date(s.started_at).toLocaleString()}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </div>
    </div>
  );
}

function StatCard({ title, value, icon }: { title: string; value: number; icon: ReactNode }) {
  return (
    <Card className="surface-panel rounded-[1.6rem] border-0 shadow-none">
      <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
        <CardTitle className="text-muted-foreground text-sm font-medium">{title}</CardTitle>
        <div className="text-muted-foreground">{icon}</div>
      </CardHeader>
      <CardContent>
        <div className="text-3xl font-semibold tracking-tight">{value.toLocaleString()}</div>
      </CardContent>
    </Card>
  );
}
