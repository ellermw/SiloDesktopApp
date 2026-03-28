import { describe, expect, it } from "vitest";
import {
  ALL_PLAYBACK_COMMANDS,
  buildPlaybackRealtimeAck,
  buildPlaybackRealtimeHello,
  buildPlaybackRealtimeResult,
  parsePlaybackRealtimeCommand,
  SUPPORTED_PLAYBACK_COMMANDS,
} from "./realtime-protocol";

describe("realtime protocol", () => {
  it("parses known command envelopes", () => {
    const command = parsePlaybackRealtimeCommand(
      JSON.stringify({
        type: "command",
        command_id: "cmd-1",
        session_id: "session-1",
        name: "server_restarting",
        payload: { message: "Restarting soon" },
      }),
    );

    expect(command).toEqual({
      type: "command",
      command_id: "cmd-1",
      session_id: "session-1",
      name: "server_restarting",
      reason: undefined,
      issued_by: undefined,
      deadline_ms: undefined,
      payload: { message: "Restarting soon" },
    });
  });

  it("rejects unknown commands", () => {
    const command = parsePlaybackRealtimeCommand(
      JSON.stringify({
        type: "command",
        command_id: "cmd-1",
        session_id: "session-1",
        name: "launch_missiles",
      }),
    );

    expect(command).toBeNull();
  });

  it("builds hello, ack, and result envelopes", () => {
    expect(buildPlaybackRealtimeHello("session-1")).toEqual({
      type: "hello",
      session_id: "session-1",
      client: {
        name: "continuum-web",
        version: "1",
      },
      capabilities: {
        commands: SUPPORTED_PLAYBACK_COMMANDS,
      },
    });

    expect(buildPlaybackRealtimeAck("session-1", "cmd-1")).toEqual({
      type: "ack",
      command_id: "cmd-1",
      session_id: "session-1",
      status: "accepted",
    });

    expect(buildPlaybackRealtimeResult("session-1", "cmd-1", "rejected", "unsupported")).toEqual({
      type: "result",
      command_id: "cmd-1",
      session_id: "session-1",
      status: "rejected",
      error: "unsupported",
    });
  });

  it("keeps the supported command subset within the full command set", () => {
    expect(SUPPORTED_PLAYBACK_COMMANDS.every((name) => ALL_PLAYBACK_COMMANDS.includes(name))).toBe(
      true,
    );
  });
});
