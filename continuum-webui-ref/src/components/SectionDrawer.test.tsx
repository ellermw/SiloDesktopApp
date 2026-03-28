import { describe, expect, it } from "vitest";

import { queryDefinitionFromSectionConfig } from "@/api/types";

import { buildSectionSaveEntry } from "./SectionDrawer";

describe("SectionDrawer helpers", () => {
  it("builds a custom section entry with a user-selected featured flag", () => {
    const entry = buildSectionSaveEntry({
      section: null,
      sectionType: "recently_added",
      title: "Hero Picks",
      itemLimit: 12,
      featured: true,
      queryDefinition: queryDefinitionFromSectionConfig(),
      selectedCollectionId: "",
    });

    expect(entry).toMatchObject({
      title: "Hero Picks",
      featured: true,
      is_custom: true,
      item_limit: 12,
    });
  });
});
