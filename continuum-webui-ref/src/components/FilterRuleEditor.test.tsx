import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";

import {
  COLLECTION_FIELD_OPTIONS,
  COLLECTION_SORT_OPTIONS,
  getCollectionFieldOption,
} from "@/components/collections/collectionBuilderFields";

import FilterRuleEditor from "./FilterRuleEditor";

describe("FilterRuleEditor", () => {
  it("renders rule-management controls as non-submit buttons", () => {
    const markup = renderToStaticMarkup(
      <form>
        <FilterRuleEditor
          value={{
            match: "all",
            groups: [{ match: "all", rules: [{ field: "genre", op: "is", value: "" }] }],
          }}
          onChange={() => {}}
        />
      </form>,
    );

    const buttons = [...markup.matchAll(/<button\b[^>]*>/g)].map((match) => match[0]);

    expect(buttons.length).toBeGreaterThan(0);
    expect(buttons.every((button) => button.includes('type="button"'))).toBe(true);
    expect(markup).not.toContain('type="submit"');
  });

  it("exposes canonical shared field and sort vocabulary", () => {
    expect(COLLECTION_FIELD_OPTIONS.map((field) => field.value)).toEqual(
      expect.arrayContaining([
        "rating_imdb",
        "release_date",
        "watched",
        "favorited",
        "in_watchlist",
      ]),
    );
    expect(COLLECTION_SORT_OPTIONS.map((sort) => sort.value)).toContain("rating_imdb");
    expect(COLLECTION_SORT_OPTIONS.map((sort) => sort.value)).not.toContain("rating");
  });

  it("describes range and boolean editing for shared rule fields", () => {
    expect(getCollectionFieldOption("rating_imdb")).toMatchObject({
      inputType: "number",
      supportsRange: true,
    });
    expect(getCollectionFieldOption("release_date")).toMatchObject({
      inputType: "text",
      supportsRange: true,
    });
    expect(getCollectionFieldOption("watched")).toMatchObject({
      inputType: "boolean",
      valueType: "boolean",
    });
  });
});
