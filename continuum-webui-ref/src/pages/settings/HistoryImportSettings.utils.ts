import type { EmbyConnectLoginResponse, HistoryImportSource } from "@/api/types";

export function resolveSavedSourceSelection(
  currentSavedSourceId: string,
  sources: HistoryImportSource[],
  lockedSavedSourceId: string,
) {
  if (currentSavedSourceId) {
    return {
      effectiveSavedSourceId: currentSavedSourceId,
      lockedSavedSourceId: currentSavedSourceId,
    };
  }

  if (lockedSavedSourceId) {
    return {
      effectiveSavedSourceId: lockedSavedSourceId,
      lockedSavedSourceId,
    };
  }

  const nextSavedSourceId = String(sources[0]?.id ?? "");
  return {
    effectiveSavedSourceId: nextSavedSourceId,
    lockedSavedSourceId: nextSavedSourceId,
  };
}

export function canStartImport(
  mode: "connect" | "saved",
  profileId: string,
  connectSession: EmbyConnectLoginResponse | null,
  connectServerId: string,
  selectedSavedSource: HistoryImportSource | undefined,
) {
  if (!profileId) return false;
  if (mode === "connect") return !!connectSession?.connect_session_id && !!connectServerId;
  return !!selectedSavedSource;
}
