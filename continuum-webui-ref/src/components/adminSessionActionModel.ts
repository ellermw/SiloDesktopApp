import type { AdminSession } from "@/api/types";

export type AdminSessionActionName = "pause" | "resume";

export function isSessionPaused(session: AdminSession): boolean {
  return Boolean(session.is_paused);
}

export function getPrimaryPlaybackAction(session: AdminSession): {
  action: AdminSessionActionName;
  label: string;
} {
  if (isSessionPaused(session)) {
    return { action: "resume", label: "Resume" };
  }
  return { action: "pause", label: "Pause" };
}
