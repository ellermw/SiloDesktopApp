import { useMemo } from "react";

import { normalizeQueryDefinition, type QueryDefinition, type QueryRule } from "@/api/types";
import LibraryMultiSelect from "@/components/LibraryMultiSelect";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  SearchableMultiSelect,
  SearchableSelect,
} from "@/components/ui/searchable-select";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { useCatalogMetadataFilters } from "@/hooks/queries/catalog";

import { COLLECTION_SORT_OPTIONS } from "./collectionBuilderFields";

/** Flat form state that maps 1-to-1 with friendly form fields. */
export interface GuidedFormState {
  mediaScope: "all" | "movie" | "series";
  libraryIds: number[];
  genres: string[];
  yearFrom: string;
  yearTo: string;
  minRating: string;
  contentRating: string;
  originalLanguage: string;
  studio: string;
  network: string;
  country: string;
  status: string;
  addedInLast: string;
  releasedInLast: string;
  sortField: string;
  sortOrder: "asc" | "desc";
}

/** Extract a friendly form state from a QueryDefinition. */
export function queryDefinitionToGuidedState(qd: QueryDefinition): GuidedFormState {
  const normalized = normalizeQueryDefinition(qd);
  const state: GuidedFormState = {
    mediaScope: normalized.media_scope ?? "all",
    libraryIds: [...normalized.library_ids],
    genres: [],
    yearFrom: "",
    yearTo: "",
    minRating: "",
    contentRating: "",
    originalLanguage: "",
    studio: "",
    network: "",
    country: "",
    status: "",
    addedInLast: "",
    releasedInLast: "",
    sortField: normalized.sort.field,
    sortOrder: normalized.sort.order,
  };

  // Flatten all rules from all groups.
  const allRules: QueryRule[] = normalized.groups.flatMap((g) => g.rules);
  const genreValues: string[] = [];

  for (const rule of allRules) {
    switch (rule.field) {
      case "genre":
        if (rule.op === "is" || rule.op === "contains") {
          genreValues.push(String(rule.value));
        }
        break;
      case "year":
        if (rule.op === "gte" || rule.op === "gt") state.yearFrom = String(rule.value);
        if (rule.op === "lte" || rule.op === "lt") state.yearTo = String(rule.value);
        if (rule.op === "is") {
          state.yearFrom = String(rule.value);
          state.yearTo = String(rule.value);
        }
        break;
      case "rating":
      case "rating_imdb":
        if (rule.op === "gte" || rule.op === "gt") state.minRating = String(rule.value);
        break;
      case "content_rating":
        if (rule.op === "is") state.contentRating = String(rule.value);
        break;
      case "original_language":
        if (rule.op === "is") state.originalLanguage = String(rule.value);
        break;
      case "studio":
        if (rule.op === "is") state.studio = String(rule.value);
        break;
      case "network":
        if (rule.op === "is") state.network = String(rule.value);
        break;
      case "country":
        if (rule.op === "is") state.country = String(rule.value);
        break;
      case "status":
        if (rule.op === "is") state.status = String(rule.value);
        break;
      case "added_at":
        if (rule.op === "in_last") state.addedInLast = String(rule.value);
        break;
      case "release_date":
        if (rule.op === "in_last") state.releasedInLast = String(rule.value);
        break;
    }
  }

  state.genres = genreValues;
  return state;
}

/** Build a QueryDefinition from the guided form state. */
export function guidedStateToQueryDefinition(
  state: GuidedFormState,
  existing: QueryDefinition,
): QueryDefinition {
  const rules: QueryRule[] = [];

  // Genres — one rule per entry.
  for (const genre of state.genres) {
    if (genre) {
      rules.push({ field: "genre", op: "is", value: genre });
    }
  }

  // Year range.
  if (state.yearFrom) {
    rules.push({ field: "year", op: "gte", value: Number(state.yearFrom) });
  }
  if (state.yearTo) {
    rules.push({ field: "year", op: "lte", value: Number(state.yearTo) });
  }

  // Minimum rating.
  if (state.minRating) {
    rules.push({ field: "rating_imdb", op: "gte", value: Number(state.minRating) });
  }

  // Simple text/select fields.
  if (state.contentRating) {
    rules.push({ field: "content_rating", op: "is", value: state.contentRating });
  }
  if (state.originalLanguage) {
    rules.push({ field: "original_language", op: "is", value: state.originalLanguage });
  }
  if (state.studio) {
    rules.push({ field: "studio", op: "is", value: state.studio });
  }
  if (state.network) {
    rules.push({ field: "network", op: "is", value: state.network });
  }
  if (state.country) {
    rules.push({ field: "country", op: "is", value: state.country });
  }
  if (state.status) {
    rules.push({ field: "status", op: "is", value: state.status });
  }

  // Duration fields.
  if (state.addedInLast) {
    rules.push({ field: "added_at", op: "in_last", value: state.addedInLast });
  }
  if (state.releasedInLast) {
    rules.push({ field: "release_date", op: "in_last", value: state.releasedInLast });
  }

  return normalizeQueryDefinition({
    library_ids: state.libraryIds,
    media_scope: state.mediaScope === "all" ? undefined : state.mediaScope,
    match: "all",
    groups: rules.length > 0 ? [{ match: "all", rules }] : [],
    sort: {
      field: state.sortField as QueryDefinition["sort"]["field"],
      order: state.sortOrder,
    },
    limit: existing.limit,
  });
}

interface CollectionGuidedRulesEditorProps {
  value: QueryDefinition;
  onChange: (value: QueryDefinition) => void;
  libraries?: Array<{ id: number; name: string }>;
  allowLibrarySelection?: boolean;
  readOnly?: boolean;
}

export default function CollectionGuidedRulesEditor({
  value,
  onChange,
  libraries = [],
  allowLibrarySelection = true,
  readOnly = false,
}: CollectionGuidedRulesEditorProps) {
  const state = useMemo(() => queryDefinitionToGuidedState(value), [value]);
  const filtersQuery = useCatalogMetadataFilters();
  const filters = filtersQuery.data;
  const filtersLoading = filtersQuery.isLoading;

  function update(patch: Partial<GuidedFormState>) {
    const next = { ...state, ...patch };
    onChange(guidedStateToQueryDefinition(next, value));
  }

  return (
    <div className="space-y-4">
      {/* Media Type & Libraries */}
      <div className="grid gap-4 md:grid-cols-2">
        <div className="space-y-2">
          <Label>Media Type</Label>
          <Select
            value={state.mediaScope}
            onValueChange={(v) => update({ mediaScope: v as GuidedFormState["mediaScope"] })}
            disabled={readOnly}
          >
            <SelectTrigger>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All Media</SelectItem>
              <SelectItem value="movie">Movies</SelectItem>
              <SelectItem value="series">Series</SelectItem>
            </SelectContent>
          </Select>
        </div>

        {allowLibrarySelection ? (
          <div className="space-y-2">
            <Label>Libraries</Label>
            <LibraryMultiSelect
              libraries={libraries}
              value={state.libraryIds}
              onChange={(libraryIds) => update({ libraryIds })}
            />
          </div>
        ) : null}
      </div>

      {/* Genres */}
      <div className="space-y-2">
        <Label>Genres</Label>
        <SearchableMultiSelect
          options={filters?.genres ?? []}
          value={state.genres}
          onChange={(genres) => update({ genres })}
          placeholder="Select genres..."
          disabled={readOnly}
          isLoading={filtersLoading}
        />
        <p className="text-muted-foreground text-xs">
          Items must match all selected genres.
        </p>
      </div>

      {/* Year Range */}
      <div className="grid gap-4 md:grid-cols-2">
        <div className="space-y-2">
          <Label>Year From</Label>
          <Input
            type="number"
            value={state.yearFrom}
            onChange={(e) => update({ yearFrom: e.target.value })}
            placeholder="e.g. 2000"
            disabled={readOnly}
          />
        </div>
        <div className="space-y-2">
          <Label>Year To</Label>
          <Input
            type="number"
            value={state.yearTo}
            onChange={(e) => update({ yearTo: e.target.value })}
            placeholder="e.g. 2025"
            disabled={readOnly}
          />
        </div>
      </div>

      {/* Rating & Content Rating */}
      <div className="grid gap-4 md:grid-cols-2">
        <div className="space-y-2">
          <Label>Minimum IMDb Rating</Label>
          <Input
            type="number"
            min={0}
            max={10}
            step={0.1}
            value={state.minRating}
            onChange={(e) => update({ minRating: e.target.value })}
            placeholder="e.g. 7.0"
            disabled={readOnly}
          />
        </div>
        <div className="space-y-2">
          <Label>Content Rating</Label>
          <SearchableSelect
            options={filters?.content_ratings ?? []}
            value={state.contentRating}
            onChange={(contentRating) => update({ contentRating })}
            placeholder="Select rating..."
            disabled={readOnly}
            isLoading={filtersLoading}
          />
        </div>
      </div>

      {/* Original Language */}
      <div className="space-y-2">
        <Label>Original Language</Label>
        <SearchableSelect
          options={filters?.original_languages ?? []}
          value={state.originalLanguage}
          onChange={(originalLanguage) => update({ originalLanguage })}
          placeholder="Select language..."
          disabled={readOnly}
          isLoading={filtersLoading}
        />
      </div>

      {/* Studio & Network */}
      <div className="grid gap-4 md:grid-cols-2">
        <div className="space-y-2">
          <Label>Studio</Label>
          <SearchableSelect
            options={filters?.studios ?? []}
            value={state.studio}
            onChange={(studio) => update({ studio })}
            placeholder="Select studio..."
            disabled={readOnly}
            isLoading={filtersLoading}
          />
        </div>
        <div className="space-y-2">
          <Label>Network</Label>
          <SearchableSelect
            options={filters?.networks ?? []}
            value={state.network}
            onChange={(network) => update({ network })}
            placeholder="Select network..."
            disabled={readOnly}
            isLoading={filtersLoading}
          />
        </div>
      </div>

      {/* Country & Status */}
      <div className="grid gap-4 md:grid-cols-2">
        <div className="space-y-2">
          <Label>Country</Label>
          <SearchableSelect
            options={filters?.countries ?? []}
            value={state.country}
            onChange={(country) => update({ country })}
            placeholder="Select country..."
            disabled={readOnly}
            isLoading={filtersLoading}
          />
        </div>
        <div className="space-y-2">
          <Label>Status</Label>
          <Select
            value={state.status || "__any__"}
            onValueChange={(v) => update({ status: v === "__any__" ? "" : v })}
            disabled={readOnly}
          >
            <SelectTrigger>
              <SelectValue placeholder="Any" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="__any__">Any</SelectItem>
              <SelectItem value="pending">Pending</SelectItem>
              <SelectItem value="matched">Matched</SelectItem>
              <SelectItem value="unmatched">Unmatched</SelectItem>
            </SelectContent>
          </Select>
        </div>
      </div>

      {/* Recency filters */}
      <div className="grid gap-4 md:grid-cols-2">
        <div className="space-y-2">
          <Label>Added in the Last</Label>
          <Input
            value={state.addedInLast}
            onChange={(e) => update({ addedInLast: e.target.value })}
            placeholder="e.g. 30d, 2w, 6m"
            disabled={readOnly}
          />
        </div>
        <div className="space-y-2">
          <Label>Released in the Last</Label>
          <Input
            value={state.releasedInLast}
            onChange={(e) => update({ releasedInLast: e.target.value })}
            placeholder="e.g. 90d, 1y"
            disabled={readOnly}
          />
        </div>
      </div>

      {/* Sort */}
      <div className="border-border grid gap-4 border-t pt-4 md:grid-cols-2">
        <div className="space-y-2">
          <Label>Sort By</Label>
          <Select
            value={state.sortField}
            onValueChange={(v) => update({ sortField: v })}
            disabled={readOnly}
          >
            <SelectTrigger>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {COLLECTION_SORT_OPTIONS.map((opt) => (
                <SelectItem key={opt.value} value={opt.value}>
                  {opt.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <div className="space-y-2">
          <Label>Order</Label>
          <Select
            value={state.sortOrder}
            onValueChange={(v) => update({ sortOrder: v as "asc" | "desc" })}
            disabled={readOnly}
          >
            <SelectTrigger>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="desc">Descending</SelectItem>
              <SelectItem value="asc">Ascending</SelectItem>
            </SelectContent>
          </Select>
        </div>
      </div>
    </div>
  );
}
