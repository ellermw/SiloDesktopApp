import { describe, expect, it } from "vitest";
import { buildAdminSessionStreamUrl } from "./AdminSessionStreamProvider";

describe("buildAdminSessionStreamUrl", () => {
  it("includes auth token and websocket scheme", () => {
    expect(
      buildAdminSessionStreamUrl("token-123", {
        protocol: "https:",
        host: "example.com",
      }),
    ).toBe("wss://example.com/api/v1/admin/sessions/ws?token=token-123");
  });

  it("omits the query string when no token is available", () => {
    expect(
      buildAdminSessionStreamUrl(null, {
        protocol: "http:",
        host: "localhost:5173",
      }),
    ).toBe("ws://localhost:5173/api/v1/admin/sessions/ws");
  });
});
