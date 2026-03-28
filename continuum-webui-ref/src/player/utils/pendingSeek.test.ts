import { describe, expect, it } from "vitest";
import { resolvePendingSeekTime } from "./pendingSeek";

describe("resolvePendingSeekTime", () => {
  it("holds the scrubbed position while stale playback time is still arriving", () => {
    expect(resolvePendingSeekTime(120, 420)).toEqual({
      currentTime: 420,
      pendingSeekTime: 420,
    });
  });

  it("clears the pending seek once playback lands near the requested time", () => {
    expect(resolvePendingSeekTime(419.5, 420)).toEqual({
      currentTime: 419.5,
      pendingSeekTime: null,
    });
  });

  it("passes through the live playback time when no seek is pending", () => {
    expect(resolvePendingSeekTime(120, null)).toEqual({
      currentTime: 120,
      pendingSeekTime: null,
    });
  });
});
