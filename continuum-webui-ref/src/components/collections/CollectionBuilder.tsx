import { useState } from "react";
import type { ReactNode } from "react";

import type { CollectionPreviewRequest, QueryDefinition, SmartCollectionAccess } from "@/api/types";
import { createEmptyQueryDefinition, normalizeQueryDefinition } from "@/api/types";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  buildCollectionPreviewRequest,
  useAdminCollectionPreview,
  useUserCollectionPreview,
} from "@/hooks/queries/collectionPreviews";

import CollectionAccessEditor from "./CollectionAccessEditor";
import CollectionGuidedRulesEditor from "./CollectionGuidedRulesEditor";
import CollectionOrderingEditor from "./CollectionOrderingEditor";
import CollectionPreviewPane from "./CollectionPreviewPane";
import CollectionRulesEditor from "./CollectionRulesEditor";

export interface CollectionBuilderValue {
  title: string;
  description: string;
  collection_type: "manual" | "smart";
  visibility: "visible" | "hidden";
  featured: boolean;
  query_definition: QueryDefinition;
  sort_config: Record<string, unknown>;
  access: SmartCollectionAccess;
}

export interface CollectionBuilderProps {
  mode: "admin" | "user";
  value: CollectionBuilderValue;
  onChange: (value: CollectionBuilderValue) => void;
  onSubmit: () => void;
  submitLabel?: string;
  libraries?: Array<{ id: number; name: string }>;
  profiles?: Array<{ id: string; name: string }>;
  isPending?: boolean;
  readOnly?: boolean;
  allowLibrarySelection?: boolean;
  allowAccessControls?: boolean;
  creatorProfileId?: string | null;
  defaultAdvanced?: boolean;
  previewLayout?: "stacked" | "sidebar";
  sidebarContent?: ReactNode;
  children?: ReactNode;
}

export function createCollectionBuilderValue(
  overrides?: Partial<CollectionBuilderValue>,
): CollectionBuilderValue {
  return {
    title: overrides?.title ?? "",
    description: overrides?.description ?? "",
    collection_type: overrides?.collection_type ?? "smart",
    visibility: overrides?.visibility ?? "visible",
    featured: overrides?.featured ?? false,
    query_definition: normalizeQueryDefinition(
      overrides?.query_definition ?? createEmptyQueryDefinition(),
    ),
    sort_config: overrides?.sort_config ?? {},
    access: overrides?.access ?? { is_shared: false, allowed_profile_ids: [] },
  };
}

export function buildCollectionBuilderPreviewRequest(
  value: CollectionBuilderValue,
  limit = 12,
): CollectionPreviewRequest | null {
  if (value.collection_type !== "smart") {
    return null;
  }
  return buildCollectionPreviewRequest(value.query_definition, limit);
}

export default function CollectionBuilder({
  mode,
  value,
  onChange,
  onSubmit,
  submitLabel = "Save Collection",
  libraries = [],
  profiles = [],
  isPending = false,
  readOnly = false,
  allowLibrarySelection = true,
  allowAccessControls = false,
  creatorProfileId,
  defaultAdvanced = false,
  previewLayout = "stacked",
  sidebarContent,
  children,
}: CollectionBuilderProps) {
  const [advanced, setAdvanced] = useState(defaultAdvanced);

  const previewRequest = buildCollectionBuilderPreviewRequest(value);
  const adminPreview = useAdminCollectionPreview(mode === "admin" ? previewRequest : null);
  const userPreview = useUserCollectionPreview(mode === "user" ? previewRequest : null);
  const previewQuery = mode === "admin" ? adminPreview : userPreview;
  const previewPanel =
    value.collection_type === "smart" ? (
      <div className="space-y-4">
        {sidebarContent}
        <section className="space-y-4">
          <h2 className="text-lg font-semibold">Preview</h2>
          <CollectionPreviewPane
            preview={previewQuery.data}
            enabled
            isLoading={previewQuery.isLoading || previewQuery.isFetching}
          />
        </section>
      </div>
    ) : sidebarContent ? (
      <div className="space-y-4">{sidebarContent}</div>
    ) : null;

  const formSections = (
    <>
      <section className="space-y-4">
        <div className="flex items-center justify-between">
          <h2 className="text-lg font-semibold">Basics</h2>
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => setAdvanced((current) => !current)}
          >
            {advanced ? "Switch to Guided" : "Switch to Advanced"}
          </Button>
        </div>

        <div className="grid gap-4 md:grid-cols-2">
          <div className="space-y-2">
            <Label htmlFor={`collection-title-${mode}`}>
              {mode === "admin" ? "Title" : "Name"}
            </Label>
            <Input
              id={`collection-title-${mode}`}
              value={value.title}
              onChange={(event) => onChange({ ...value, title: event.target.value })}
              disabled={readOnly}
              required
            />
          </div>

          <div className="space-y-2">
            <Label>Collection Mode</Label>
            <Select
              value={value.collection_type}
              onValueChange={(next) =>
                onChange({
                  ...value,
                  collection_type: next as "manual" | "smart",
                })
              }
              disabled={readOnly}
            >
              <SelectTrigger>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="smart">Smart</SelectItem>
                <SelectItem value="manual">Manual</SelectItem>
              </SelectContent>
            </Select>
          </div>
        </div>

        {mode === "admin" ? (
          <>
            <div className="space-y-2">
              <Label htmlFor="collection-description">Description</Label>
              <textarea
                id="collection-description"
                rows={4}
                value={value.description}
                onChange={(event) => onChange({ ...value, description: event.target.value })}
                disabled={readOnly}
                className="border-input placeholder:text-muted-foreground focus-visible:border-ring focus-visible:ring-ring/50 flex min-h-[110px] w-full rounded-md border bg-transparent px-3 py-2 text-sm shadow-xs transition-[color,box-shadow] outline-none focus-visible:ring-[3px]"
              />
            </div>

            <div className="grid gap-4 md:grid-cols-2">
              <div className="space-y-2">
                <Label>Visibility</Label>
                <Select
                  value={value.visibility}
                  onValueChange={(next) =>
                    onChange({ ...value, visibility: next as "visible" | "hidden" })
                  }
                  disabled={readOnly}
                >
                  <SelectTrigger>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="visible">Visible</SelectItem>
                    <SelectItem value="hidden">Hidden</SelectItem>
                  </SelectContent>
                </Select>
              </div>

              <div className="border-border flex items-center justify-between rounded-lg border px-4 py-3">
                <div>
                  <p className="text-sm font-medium">Featured</p>
                  <p className="text-muted-foreground text-xs">
                    Surface this collection near the top of the library.
                  </p>
                </div>
                <Switch
                  checked={value.featured}
                  onCheckedChange={(checked) => onChange({ ...value, featured: checked })}
                  disabled={readOnly}
                />
              </div>
            </div>
          </>
        ) : null}
      </section>

      {children}

      {value.collection_type === "smart" ? (
        <>
          <section className="space-y-4">
            <h2 className="text-lg font-semibold">
              {advanced ? "Rules" : "Filters"}
            </h2>
            {advanced ? (
              <CollectionRulesEditor
                value={value.query_definition}
                onChange={(query_definition) => onChange({ ...value, query_definition })}
                libraries={libraries}
                allowLibrarySelection={allowLibrarySelection}
                readOnly={readOnly}
              />
            ) : (
              <CollectionGuidedRulesEditor
                value={value.query_definition}
                onChange={(query_definition) => onChange({ ...value, query_definition })}
                libraries={libraries}
                allowLibrarySelection={allowLibrarySelection}
                readOnly={readOnly}
              />
            )}
          </section>

          {advanced ? (
            <section className="space-y-4">
              <h2 className="text-lg font-semibold">Ordering</h2>
              <CollectionOrderingEditor
                query={value.query_definition}
                sortConfig={value.sort_config}
                onQueryChange={(query_definition) => onChange({ ...value, query_definition })}
                onSortConfigChange={(sort_config) => onChange({ ...value, sort_config })}
                readOnly={readOnly}
              />
            </section>
          ) : null}
        </>
      ) : (
        <section className="space-y-2">
          <h2 className="text-lg font-semibold">Ordering</h2>
          <div className="text-muted-foreground rounded-lg border border-dashed px-4 py-5 text-sm">
            Manual collections are curated after save by adding titles directly.
          </div>
        </section>
      )}

      {allowAccessControls ? (
        <section className="space-y-4">
          <h2 className="text-lg font-semibold">Access</h2>
          <CollectionAccessEditor
            value={value.access}
            onChange={(access) => onChange({ ...value, access })}
            profiles={profiles}
            readOnly={readOnly}
            creatorProfileId={creatorProfileId}
          />
        </section>
      ) : null}

      <Button
        type="submit"
        className="w-full"
        disabled={isPending || readOnly}
      >
        {isPending ? "Saving..." : submitLabel}
      </Button>
    </>
  );

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        onSubmit();
      }}
      className="space-y-6"
    >
      {previewLayout === "sidebar" && previewPanel ? (
        <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_22rem]">
          <div className="space-y-6">{formSections}</div>
          <div className="xl:sticky xl:top-6 xl:self-start">{previewPanel}</div>
        </div>
      ) : (
        <>
          {formSections}
          {previewLayout === "stacked" ? previewPanel : null}
        </>
      )}
    </form>
  );
}
