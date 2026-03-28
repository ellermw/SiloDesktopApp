import { useState, useEffect } from "react";
import {
  Sheet,
  SheetContent,
  SheetHeader,
  SheetTitle,
  SheetDescription,
  SheetFooter,
} from "@/components/ui/sheet";
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
import CollectionRulesEditor from "@/components/collections/CollectionRulesEditor";
import LibraryMultiSelect from "@/components/LibraryMultiSelect";
import { CollectionSearchableSelect } from "@/components/CollectionSearchableSelect";
import { SECTION_TYPES, FILTER_SECTION_TYPES, sectionTypeLabel } from "@/lib/sectionTypes";
import {
  queryDefinitionFromSectionConfig,
  queryDefinitionToSectionConfig,
  type QueryDefinition,
  type SettingsSectionEntry,
} from "@/api/types";
import { useAllUserCollections, type CollectionOption } from "@/hooks/queries/useAllUserCollections";

interface SectionDrawerProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  section: SettingsSectionEntry | null;
  libraries: { id: number; name: string }[];
  onSave: (section: SettingsSectionEntry) => void;
}

function getCollectionId(config?: Record<string, unknown>): string {
  const userValue = config?.user_collection_id;
  if (typeof userValue === "string" && userValue) return userValue;
  const libValue = config?.library_collection_id;
  return typeof libValue === "string" ? libValue : "";
}

interface BuildSectionSaveEntryInput {
  section: SettingsSectionEntry | null;
  sectionType: string;
  title: string;
  itemLimit: number;
  featured: boolean;
  queryDefinition: QueryDefinition;
  selectedCollectionId: string;
  collections?: CollectionOption[];
}

export function buildSectionSaveEntry({
  section,
  sectionType,
  title,
  itemLimit,
  featured,
  queryDefinition,
  selectedCollectionId,
  collections,
}: BuildSectionSaveEntryInput): SettingsSectionEntry {
  const showCollectionPicker = sectionType === "collection";
  let config: Record<string, unknown>;
  if (showCollectionPicker) {
    const selected = collections?.find((c) => c.id === selectedCollectionId);
    if (selected?.source === "user") {
      config = { user_collection_id: selectedCollectionId };
    } else {
      config = { library_collection_id: selectedCollectionId };
    }
  } else {
    config = queryDefinitionToSectionConfig(queryDefinition);
  }

  return {
    id: section?.id ?? crypto.randomUUID(),
    section_type: sectionType,
    title: title || sectionTypeLabel(sectionType),
    featured,
    item_limit: itemLimit,
    hidden: section?.hidden ?? false,
    is_custom: section?.is_custom ?? true,
    customized: section?.customized ?? false,
    position: section?.position ?? 0,
    config,
  };
}

export default function SectionDrawer({
  open,
  onOpenChange,
  section,
  libraries,
  onSave,
}: SectionDrawerProps) {
  const isEdit = section !== null;
  const isAdminSection = isEdit && !section.is_custom;

  const [sectionType, setSectionType] = useState("recently_added");
  const [title, setTitle] = useState("");
  const [itemLimit, setItemLimit] = useState(20);
  const [featured, setFeatured] = useState(false);
  const [queryDefinition, setQueryDefinition] = useState<QueryDefinition>(
    queryDefinitionFromSectionConfig(),
  );
  const [selectedCollectionId, setSelectedCollectionId] = useState("");

  const { collections, isLoading: collectionsLoading } = useAllUserCollections();

  // Reset form when opening or section changes
  useEffect(() => {
    if (open) {
      if (section) {
        setSectionType(section.section_type);
        setTitle(section.title);
        setItemLimit(section.item_limit);
        setFeatured(section.featured);
        setQueryDefinition(queryDefinitionFromSectionConfig(section.config));
        setSelectedCollectionId(getCollectionId(section.config));
      } else {
        setSectionType("recently_added");
        setTitle("");
        setItemLimit(20);
        setFeatured(false);
        setQueryDefinition(queryDefinitionFromSectionConfig());
        setSelectedCollectionId("");
      }
    }
  }, [open, section]);

  const showFilter = FILTER_SECTION_TYPES.has(sectionType);
  const showCollectionPicker = sectionType === "collection";

  function handleSave() {
    onSave(
      buildSectionSaveEntry({
        section,
        sectionType,
        title,
        itemLimit,
        featured,
        queryDefinition,
        selectedCollectionId,
        collections,
      }),
    );
    onOpenChange(false);
  }

  const saveDisabled = showCollectionPicker && !selectedCollectionId;

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="overflow-y-auto sm:max-w-lg">
        <SheetHeader>
          <SheetTitle>{isEdit ? "Edit Section" : "Add Section"}</SheetTitle>
          <SheetDescription>
            {isEdit
              ? "Modify this section's settings"
              : "Configure a new section for your home screen"}
          </SheetDescription>
        </SheetHeader>

        <div className="space-y-4 px-4">
          {/* Section Type */}
          <div className="space-y-2">
            <Label>Section Type</Label>
            {isAdminSection ? (
              <div className="bg-muted text-muted-foreground rounded-md px-3 py-2 text-sm">
                {sectionTypeLabel(sectionType)}
              </div>
            ) : (
              <Select value={sectionType} onValueChange={setSectionType}>
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {SECTION_TYPES.map((t) => (
                    <SelectItem key={t.value} value={t.value}>
                      {t.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            )}
          </div>

          {/* Title */}
          <div className="space-y-2">
            <Label>Title</Label>
            <Input
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              placeholder={sectionTypeLabel(sectionType)}
            />
          </div>

          {/* Item Limit */}
          <div className="space-y-2">
            <Label>Item Limit</Label>
            <Input
              type="number"
              value={itemLimit}
              onChange={(e) => setItemLimit(Number(e.target.value))}
              min={1}
              max={100}
            />
          </div>

          <div className="flex items-center justify-between gap-4 rounded-md border px-3 py-3">
            <div className="space-y-1">
              <Label htmlFor="section-featured">Featured</Label>
              <p className="text-muted-foreground text-sm">
                Use this section as the hero banner on the home screen.
              </p>
            </div>
            <Switch id="section-featured" checked={featured} onCheckedChange={setFeatured} />
          </div>

          {/* Divider */}
          <div className="border-border border-t" />

          {/* Collection picker (collection type only) */}
          {showCollectionPicker ? (
            <div className="space-y-2">
              <Label>Collection</Label>
              <CollectionSearchableSelect
                options={collections}
                value={selectedCollectionId}
                onChange={setSelectedCollectionId}
                disabled={collectionsLoading}
                isLoading={collectionsLoading}
              />
            </div>
          ) : (
            <>
              {!showFilter ? (
                <>
                  <div className="space-y-2">
                    <Label>Filter by Type</Label>
                    <Select
                      value={queryDefinition.media_scope ?? "_all"}
                      onValueChange={(value) =>
                        setQueryDefinition({
                          ...queryDefinition,
                          media_scope: value === "_all" ? undefined : (value as "movie" | "series"),
                        })
                      }
                    >
                      <SelectTrigger>
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="_all">All Types</SelectItem>
                        <SelectItem value="movie">Movies</SelectItem>
                        <SelectItem value="series">Series</SelectItem>
                      </SelectContent>
                    </Select>
                  </div>

                  <div className="space-y-2">
                    <Label>Filter by Library</Label>
                    <LibraryMultiSelect
                      libraries={libraries}
                      value={queryDefinition.library_ids}
                      onChange={(libraryIds) =>
                        setQueryDefinition({
                          ...queryDefinition,
                          library_ids: libraryIds,
                        })
                      }
                    />
                  </div>
                </>
              ) : null}
            </>
          )}

          {/* Filter Rules (genre / custom_filter only) */}
          {showFilter && (
            <div className="space-y-2">
              <Label>Filter Rules</Label>
              <CollectionRulesEditor
                value={queryDefinition}
                onChange={setQueryDefinition}
                libraries={libraries}
              />
            </div>
          )}
        </div>

        <SheetFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button onClick={handleSave} disabled={saveDisabled}>
            {isEdit ? "Save" : "Add Section"}
          </Button>
        </SheetFooter>
      </SheetContent>
    </Sheet>
  );
}
