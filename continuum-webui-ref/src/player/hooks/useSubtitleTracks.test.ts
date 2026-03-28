import { describe, expect, it } from "vitest";
import { findActiveCueTexts } from "./useSubtitleTracks";

describe("findActiveCueTexts", () => {
  const cues = [
    { start: 10, end: 12, text: "<i>Hello</i>" },
    { start: 12, end: 14, text: "World" },
  ];

  it("matches cues on the current playback time", () => {
    expect(findActiveCueTexts(cues, 10.5)).toEqual(["Hello"]);
  });

  it("applies the media timeline offset for restarted HLS sessions", () => {
    expect(findActiveCueTexts(cues, 0.5, 10)).toEqual(["Hello"]);
  });

  it("returns no cues when the adjusted time is outside all cue windows", () => {
    expect(findActiveCueTexts(cues, 5)).toEqual([]);
  });
});
