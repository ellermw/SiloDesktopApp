import { describe, expect, it } from "vitest";
import { collectCachedHomeSections } from "./homeSectionCache";

describe("collectCachedHomeSections", () => {
  it("collects cached section payloads for the active layout only", () => {
    const cached = collectCachedHomeSections(
      [
        {
          id: "hero",
          section_type: "recently_added",
          title: "Featured",
          featured: true,
          item_limit: 5,
          is_custom: false,
          customized: false,
        },
        {
          id: "continue",
          section_type: "continue_watching",
          title: "Continue Watching",
          featured: false,
          item_limit: 10,
          is_custom: false,
          customized: false,
        },
      ],
      (sectionId) =>
        sectionId === "continue"
          ? {
              section: {
                id: "continue",
                section_type: "continue_watching",
                title: "Continue Watching",
                featured: false,
                item_limit: 10,
                total_count: 1,
                is_custom: false,
                customized: false,
                items: [
                  {
                    content_id: "item-1",
                    type: "movie",
                    title: "Item One",
                    year: 2024,
                    genres: [],
                    status: "matched",
                    rating_imdb: null,
                    overview: "",
                    poster_url: "",
                    poster_thumbhash: "",
                    backdrop_url: "",
                    backdrop_thumbhash: "",
                    logo_url: "",
                  },
                ],
              },
            }
          : undefined,
    );

    expect(Array.from(cached.keys())).toEqual(["continue"]);
    expect(cached.get("continue")?.items).toHaveLength(1);
  });
});
