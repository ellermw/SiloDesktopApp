import { Link } from "react-router";
import { Play, Square } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { useTasks, useRunTask, useCancelTask } from "@/hooks/queries/admin/tasks";
import type { TaskCategory, TaskInfo } from "@/api/types";

const CATEGORY_ORDER: TaskCategory[] = ["library", "metadata", "system"];

const CATEGORY_LABELS: Record<TaskCategory, string> = {
  library: "Library",
  metadata: "Metadata",
  system: "System",
};

function formatRelativeTime(dateStr: string): string {
  const diff = Date.now() - new Date(dateStr).getTime();
  const seconds = Math.floor(diff / 1000);
  if (seconds < 60) return "just now";
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.floor(hours / 24);
  return `${days}d ago`;
}

function formatNextRun(dateStr: string): string {
  const diff = new Date(dateStr).getTime() - Date.now();
  if (diff < 0) return "overdue";
  const minutes = Math.floor(diff / 60_000);
  if (minutes < 60) return `in ${minutes}m`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `in ${hours}h`;
  const days = Math.floor(hours / 24);
  return `in ${days}d`;
}

function TaskRow({ task }: { task: TaskInfo }) {
  const runTask = useRunTask();
  const cancelTask = useCancelTask();

  const isRunning = task.state === "running" || task.state === "cancelling";

  return (
    <div className="border-border flex flex-col gap-4 border-b px-4 py-4 last:border-b-0 sm:flex-row sm:items-center">
      <div className="min-w-0 flex-1">
        <Link
          to={`/admin/tasks/${task.key}`}
          className="text-foreground hover:text-primary text-sm font-medium transition-colors"
        >
          {task.name}
        </Link>

        <div className="text-muted-foreground mt-0.5 text-xs">
          {task.state === "idle" && (
            <>
              {task.last_execution && (
                <span>Last run: {formatRelativeTime(task.last_execution.completed_at)}</span>
              )}
              {!task.last_execution && <span>Never run</span>}
              {task.next_run_at && (
                <span className="ml-2">· Next: {formatNextRun(task.next_run_at)}</span>
              )}
            </>
          )}
        </div>

        {isRunning && (
          <div className="mt-1.5 space-y-1">
            <div className="bg-muted h-2 w-full overflow-hidden rounded-full">
              <div
                className={`h-full rounded-full transition-all duration-300 ${
                  task.state === "cancelling" ? "bg-yellow-500" : "bg-primary"
                }`}
                style={{ width: `${Math.max(task.progress, 2)}%` }}
              />
            </div>
            <p className="text-muted-foreground text-xs">
              {task.state === "cancelling"
                ? "Cancelling..."
                : task.progress_message || `${Math.round(task.progress)}%`}
            </p>
          </div>
        )}
      </div>

      {task.state === "idle" && task.last_execution && (
        <Badge
          variant={task.last_execution.status === "failed" ? "destructive" : "secondary"}
          className="shrink-0 self-start sm:self-center"
        >
          {task.last_execution.status}
        </Badge>
      )}

      {isRunning ? (
        <Button
          variant="outline"
          size="sm"
          className="w-full sm:w-auto"
          onClick={() => cancelTask.mutate(task.key)}
          disabled={cancelTask.isPending || task.state === "cancelling"}
        >
          <Square className="mr-1.5 h-3.5 w-3.5" />
          Cancel
        </Button>
      ) : (
        <Button
          variant="outline"
          size="sm"
          className="w-full sm:w-auto"
          onClick={() => runTask.mutate(task.key)}
          disabled={runTask.isPending}
        >
          <Play className="mr-1.5 h-3.5 w-3.5" />
          Run Now
        </Button>
      )}
    </div>
  );
}

export default function AdminTasks() {
  const { data: tasks, isLoading } = useTasks();

  const grouped = CATEGORY_ORDER.map((cat) => ({
    category: cat,
    label: CATEGORY_LABELS[cat],
    tasks: (tasks ?? []).filter((t) => t.category === cat),
  })).filter((g) => g.tasks.length > 0);

  return (
    <div className="page-shell space-y-6 py-4 sm:py-6">
      <div className="page-header gap-5">
        <div className="space-y-3">
          <h1 className="page-title text-[clamp(2rem,4vw,3rem)]">Scheduled tasks</h1>
          <p className="page-subtitle text-sm sm:text-base">
          View and manage background tasks. You can trigger tasks manually or adjust their
          schedules.
          </p>
        </div>
      </div>

      {isLoading && <p className="text-muted-foreground text-sm">Loading tasks...</p>}

      {grouped.map((group) => (
        <div key={group.category} className="space-y-3">
          <h2 className="text-muted-foreground text-xs font-medium tracking-[0.24em] uppercase">
            {group.label}
          </h2>
          <div className="surface-panel overflow-hidden rounded-[1.6rem] border-0">
            {group.tasks.map((task) => (
              <TaskRow key={task.key} task={task} />
            ))}
          </div>
        </div>
      ))}
    </div>
  );
}
