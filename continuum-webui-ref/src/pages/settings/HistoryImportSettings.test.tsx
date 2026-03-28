import { describe, expect, it } from "vitest";

import type { EmbyConnectLoginResponse, HistoryImportSource } from "@/api/types";
import { canStartImport, resolveSavedSourceSelection } from "./HistoryImportSettings.utils";

describe("HistoryImportSettings helpers", () => {
  it("locks the initial saved server selection instead of following reordered sources", () => {
    const sources: HistoryImportSource[] = [
      { id: 7, name: "Quickflix" } as HistoryImportSource,
      { id: 9, name: "Backlog" } as HistoryImportSource,
    ];

    const initial = resolveSavedSourceSelection("", sources, "");
    expect(initial).toEqual({
      effectiveSavedSourceId: "7",
      lockedSavedSourceId: "7",
    });

    const reordered = resolveSavedSourceSelection(
      initial.effectiveSavedSourceId,
      [
        { id: 9, name: "Backlog" } as HistoryImportSource,
        { id: 7, name: "Quickflix" } as HistoryImportSource,
      ],
      initial.lockedSavedSourceId,
    );

    expect(reordered).toEqual({
      effectiveSavedSourceId: "7",
      lockedSavedSourceId: "7",
    });
  });

  it("only allows imports when the selected mode has the required inputs", () => {
    const connectSession = {
      connect_session_id: "connect-session-1",
      servers: [{ server_id: "server-1", name: "Main" }],
    } as EmbyConnectLoginResponse;
    const savedSource = { id: 7, name: "Quickflix" } as HistoryImportSource;

    expect(canStartImport("connect", "profile-1", connectSession, "server-1", undefined)).toBe(
      true,
    );
    expect(canStartImport("connect", "profile-1", null, "server-1", undefined)).toBe(false);
    expect(canStartImport("connect", "", connectSession, "server-1", undefined)).toBe(false);
    expect(canStartImport("saved", "profile-1", null, "", savedSource)).toBe(true);
    expect(canStartImport("saved", "profile-1", null, "", undefined)).toBe(false);
  });
});
