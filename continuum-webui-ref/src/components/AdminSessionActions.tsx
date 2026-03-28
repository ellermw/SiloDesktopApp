import { useEffect, useState } from "react";
import { MoreHorizontal, MessageSquare, Pause, Play, Square, OctagonAlert } from "lucide-react";
import { toast } from "sonner";
import { api } from "@/api/client";
import type { AdminSession } from "@/api/types";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { getPrimaryPlaybackAction, isSessionPaused } from "./adminSessionActionModel";

type SessionActionKind = "pause" | "resume" | "stop" | "terminate" | "message";

interface AdminSessionActionsProps {
  session: AdminSession;
  compact?: boolean;
}

const successMessages: Record<Exclude<SessionActionKind, "message">, string> = {
  pause: "Pause command sent",
  resume: "Resume command sent",
  stop: "Stop command sent",
  terminate: "Terminate command sent",
};

export function AdminSessionActions({ session, compact = false }: AdminSessionActionsProps) {
  const [pendingAction, setPendingAction] = useState<SessionActionKind | null>(null);
  const [messageOpen, setMessageOpen] = useState(false);
  const [message, setMessage] = useState("");
  const [optimisticPaused, setOptimisticPaused] = useState<boolean | null>(null);

  useEffect(() => {
    setOptimisticPaused(null);
  }, [session.session_id, session.is_paused]);

  const paused = optimisticPaused ?? isSessionPaused(session);
  const primaryAction = paused
    ? { action: "resume" as const, label: "Resume" }
    : getPrimaryPlaybackAction(session);

  async function runAction(action: Exclude<SessionActionKind, "message">) {
    setPendingAction(action);
    try {
      await api<{ command_id: string; status: string }>(
        `/admin/sessions/${session.session_id}/${action}`,
        {
          method: "POST",
        },
      );
      if (action === "pause") {
        setOptimisticPaused(true);
      } else if (action === "resume") {
        setOptimisticPaused(false);
      }
      toast.success(successMessages[action]);
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "Failed to send session command");
    } finally {
      setPendingAction(null);
    }
  }

  async function sendMessage() {
    const trimmed = message.trim();
    if (!trimmed) {
      toast.error("Message is required");
      return;
    }

    setPendingAction("message");
    try {
      await api<{ command_id: string; status: string }>(
        `/admin/sessions/${session.session_id}/message`,
        {
          method: "POST",
          body: JSON.stringify({ message: trimmed }),
        },
      );
      toast.success("Message sent");
      setMessage("");
      setMessageOpen(false);
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "Failed to send message");
    } finally {
      setPendingAction(null);
    }
  }

  return (
    <>
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button
            variant="ghost"
            size="icon"
            className={compact ? "h-7 w-7" : "h-8 w-8"}
            aria-label="Session actions"
            disabled={pendingAction !== null}
          >
            <MoreHorizontal className="h-4 w-4" />
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end" className="w-48">
          <DropdownMenuLabel>Playback Actions</DropdownMenuLabel>
          <DropdownMenuItem onSelect={() => void runAction(primaryAction.action)}>
            {primaryAction.action === "pause" ? (
              <Pause className="h-4 w-4" />
            ) : (
              <Play className="h-4 w-4" />
            )}
            {primaryAction.label}
          </DropdownMenuItem>
          <DropdownMenuItem onSelect={() => void runAction("stop")}>
            <Square className="h-4 w-4" />
            Stop
          </DropdownMenuItem>
          <DropdownMenuItem onSelect={() => setMessageOpen(true)}>
            <MessageSquare className="h-4 w-4" />
            Message…
          </DropdownMenuItem>
          <DropdownMenuSeparator />
          <DropdownMenuItem variant="destructive" onSelect={() => void runAction("terminate")}>
            <OctagonAlert className="h-4 w-4" />
            Terminate
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>

      <Dialog open={messageOpen} onOpenChange={setMessageOpen}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>Send Message</DialogTitle>
            <DialogDescription>
              Send a custom message to {session.username || `User #${session.user_id}`} during
              playback.
            </DialogDescription>
          </DialogHeader>
          <textarea
            value={message}
            onChange={(event) => setMessage(event.target.value)}
            rows={4}
            placeholder="Server restart in 5 minutes. Please finish this episode soon."
            className="border-border bg-background text-foreground min-h-24 w-full resize-y rounded-md border px-3 py-2 text-sm outline-none focus:border-[var(--primary)]"
          />
          <DialogFooter>
            <Button
              variant="outline"
              onClick={() => setMessageOpen(false)}
              disabled={pendingAction === "message"}
            >
              Cancel
            </Button>
            <Button onClick={() => void sendMessage()} disabled={pendingAction === "message"}>
              Send Message
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}
