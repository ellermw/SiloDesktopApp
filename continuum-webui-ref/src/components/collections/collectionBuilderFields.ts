import type { QuerySort } from "@/api/types";

export interface CollectionOperatorOption {
  value: string;
  label: string;
}

export interface CollectionFieldOption {
  value: string;
  label: string;
  operators: CollectionOperatorOption[];
  inputType: "text" | "number" | "select" | "boolean";
  valueType?: "string" | "number" | "boolean";
  supportsRange?: boolean;
  selectOptions?: string[];
}

export const COLLECTION_FIELD_OPTIONS: CollectionFieldOption[] = [
  {
    value: "genre",
    label: "Genre",
    operators: [
      { value: "is", label: "is" },
      { value: "is_not", label: "is not" },
      { value: "contains", label: "contains" },
    ],
    inputType: "text",
    valueType: "string",
  },
  {
    value: "year",
    label: "Year",
    operators: [
      { value: "is", label: "equals" },
      { value: "gte", label: ">=" },
      { value: "lte", label: "<=" },
      { value: "gt", label: ">" },
      { value: "lt", label: "<" },
      { value: "between", label: "between" },
    ],
    inputType: "number",
    valueType: "number",
    supportsRange: true,
  },
  {
    value: "rating_imdb",
    label: "IMDb Rating",
    operators: [
      { value: "gte", label: ">=" },
      { value: "lte", label: "<=" },
      { value: "gt", label: ">" },
      { value: "lt", label: "<" },
      { value: "between", label: "between" },
    ],
    inputType: "number",
    valueType: "number",
    supportsRange: true,
  },
  {
    value: "type",
    label: "Type",
    operators: [
      { value: "is", label: "is" },
      { value: "is_not", label: "is not" },
    ],
    inputType: "select",
    valueType: "string",
    selectOptions: ["movie", "series"],
  },
  {
    value: "content_rating",
    label: "Content Rating",
    operators: [
      { value: "is", label: "is" },
      { value: "is_not", label: "is not" },
    ],
    inputType: "text",
    valueType: "string",
  },
  {
    value: "studio",
    label: "Studio",
    operators: [
      { value: "is", label: "is" },
      { value: "is_not", label: "is not" },
    ],
    inputType: "text",
    valueType: "string",
  },
  {
    value: "network",
    label: "Network",
    operators: [
      { value: "is", label: "is" },
      { value: "is_not", label: "is not" },
    ],
    inputType: "text",
    valueType: "string",
  },
  {
    value: "country",
    label: "Country",
    operators: [
      { value: "is", label: "is" },
      { value: "is_not", label: "is not" },
    ],
    inputType: "text",
    valueType: "string",
  },
  {
    value: "status",
    label: "Status",
    operators: [
      { value: "is", label: "is" },
      { value: "is_not", label: "is not" },
    ],
    inputType: "select",
    valueType: "string",
    selectOptions: ["pending", "matched", "unmatched"],
  },
  {
    value: "added_at",
    label: "Added",
    operators: [
      { value: "gt", label: "after" },
      { value: "lt", label: "before" },
      { value: "between", label: "between" },
      { value: "in_last", label: "in the last" },
    ],
    inputType: "text",
    valueType: "string",
    supportsRange: true,
  },
  {
    value: "release_date",
    label: "Release Date",
    operators: [
      { value: "gt", label: "after" },
      { value: "lt", label: "before" },
      { value: "between", label: "between" },
      { value: "in_last", label: "in the last" },
    ],
    inputType: "text",
    valueType: "string",
    supportsRange: true,
  },
  {
    value: "watched",
    label: "Watched",
    operators: [{ value: "is", label: "is" }],
    inputType: "boolean",
    valueType: "boolean",
  },
  {
    value: "favorited",
    label: "Favorited",
    operators: [{ value: "is", label: "is" }],
    inputType: "boolean",
    valueType: "boolean",
  },
  {
    value: "in_watchlist",
    label: "In Watchlist",
    operators: [{ value: "is", label: "is" }],
    inputType: "boolean",
    valueType: "boolean",
  },
  {
    value: "bitrate",
    label: "Bitrate",
    operators: [
      { value: "gte", label: ">=" },
      { value: "lte", label: "<=" },
      { value: "gt", label: ">" },
      { value: "lt", label: "<" },
      { value: "between", label: "between" },
    ],
    inputType: "number",
    valueType: "number",
    supportsRange: true,
  },
];

export const COLLECTION_SORT_OPTIONS: Array<{ value: QuerySort["field"]; label: string }> = [
  { value: "added_at", label: "Recently Added" },
  { value: "release_date", label: "Release Date" },
  { value: "rating_imdb", label: "IMDb Rating" },
  { value: "title", label: "Title" },
  { value: "year", label: "Year" },
];

export function getCollectionFieldOption(field: string): CollectionFieldOption | undefined {
  const normalizedField = field === "rating" ? "rating_imdb" : field;
  return COLLECTION_FIELD_OPTIONS.find((option) => option.value === normalizedField);
}
