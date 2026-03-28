import { describe, expect, it } from "vitest";
import { shouldForceVideoTranscodeForOriginalRemux } from "./useTranscodeQuality";

describe("shouldForceVideoTranscodeForOriginalRemux", () => {
  it("forces encoded video for macOS Chromium browsers", () => {
    expect(
      shouldForceVideoTranscodeForOriginalRemux(
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Safari/537.36",
      ),
    ).toBe(true);
  });

  it("does not force encoded video for Windows Chromium browsers", () => {
    expect(
      shouldForceVideoTranscodeForOriginalRemux(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/145.0.0.0 Safari/537.36",
      ),
    ).toBe(false);
  });

  it("does not force encoded video for Safari on macOS", () => {
    expect(
      shouldForceVideoTranscodeForOriginalRemux(
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.4 Safari/605.1.15",
      ),
    ).toBe(false);
  });
});
