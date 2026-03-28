import { describe, expect, it } from "vitest";
import { buildAdminPlaybackHistorySearchParams } from "./history";

describe("buildAdminPlaybackHistorySearchParams", () => {
  it("serializes media item, user, profile, completion, and limit filters", () => {
    const search = buildAdminPlaybackHistorySearchParams({
      userId: 7,
      profileId: "prof-1",
      mediaItemId: "movie-100",
      completed: "false",
      limit: 50,
    });

    expect(search.toString()).toBe(
      "user_id=7&profile_id=prof-1&media_item_id=movie-100&completed=false&limit=50",
    );
  });

  it("defaults completion to all and limit to 100", () => {
    const search = buildAdminPlaybackHistorySearchParams({});
    expect(search.toString()).toBe("completed=all&limit=100");
  });
});
