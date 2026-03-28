import { describe, expect, it } from "vitest";
import { detectMaxResolutionFromScreen } from "./useCodecDetection";

describe("detectMaxResolutionFromScreen", () => {
  it("treats a 2560x1440 display as above the 720p bucket", () => {
    expect(detectMaxResolutionFromScreen(2560, 1440)).toBe("2160p");
  });

  it("keeps a 1280x720 display in the 720p bucket", () => {
    expect(detectMaxResolutionFromScreen(1280, 720)).toBe("720p");
  });
});
