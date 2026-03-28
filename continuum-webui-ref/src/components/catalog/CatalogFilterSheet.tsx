import { createEmptyQueryDefinition, type QueryDefinition } from "@/api/types";
import type { GuidedFormState } from "@/components/collections/CollectionGuidedRulesEditor";
import CollectionRulesEditor from "@/components/collections/CollectionRulesEditor";
import LibraryMultiSelect from "@/components/LibraryMultiSelect";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ScrollArea } from "@/components/ui/scroll-area";
import { SearchableMultiSelect, SearchableSelect } from "@/components/ui/searchable-select";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Sheet,
  SheetClose,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
} from "@/components/ui/sheet";
import type { CatalogFiltersResponse } from "@/api/types";

/** Default secondary filter values for "Clear All". */
const EMPTY_SECONDARY_FILTERS: Partial<GuidedFormState> = {
  libraryIds: [],
  genres: [],
  yearFrom: "",
  yearTo: "",
  minRating: "",
  contentRating: "",
  studio: "",
  network: "",
  country: "",
  status: "",
  addedInLast: "",
  releasedInLast: "",
};

interface CatalogFilterSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  state: GuidedFormState;
  onUpdate: (patch: Partial<GuidedFormState>) => void;
  libraries: Array<{ id: number; name: string }>;
  allowLibrarySelection: boolean;
  editorMode: "guided" | "advanced";
  onEditorModeChange: (mode: "guided" | "advanced") => void;
  queryDefinition: QueryDefinition;
  onQueryDefinitionChange: (qd: QueryDefinition) => void;
  filters?: CatalogFiltersResponse;
  filtersLoading?: boolean;
}

export default function CatalogFilterSheet({
  open,
  onOpenChange,
  state,
  onUpdate,
  libraries,
  allowLibrarySelection,
  editorMode,
  onEditorModeChange,
  queryDefinition,
  onQueryDefinitionChange,
  filters,
  filtersLoading = false,
}: CatalogFilterSheetProps) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="flex flex-col sm:max-w-md">
        <SheetHeader>
          <div className="flex items-center justify-between">
            <div>
              <SheetTitle>Filters</SheetTitle>
              <SheetDescription>Refine your catalog results</SheetDescription>
            </div>
            <div className="flex items-center gap-1">
              <Button
                type="button"
                variant={editorMode === "guided" ? "default" : "outline"}
                size="xs"
                onClick={() => onEditorModeChange("guided")}
              >
                Guided
              </Button>
              <Button
                type="button"
                variant={editorMode === "advanced" ? "default" : "outline"}
                size="xs"
                onClick={() => onEditorModeChange("advanced")}
              >
                Advanced
              </Button>
            </div>
          </div>
        </SheetHeader>

        <ScrollArea className="flex-1">
          <div className="space-y-4 px-4 pb-4">
            {editorMode === "advanced" ? (
              <CollectionRulesEditor
                value={queryDefinition ?? createEmptyQueryDefinition()}
                onChange={onQueryDefinitionChange}
                libraries={libraries}
                allowLibrarySelection={allowLibrarySelection}
              />
            ) : (
              <>
                {/* Libraries */}
                {allowLibrarySelection && (
                  <div className="space-y-2">
                    <Label>Libraries</Label>
                    <LibraryMultiSelect
                      libraries={libraries}
                      value={state.libraryIds}
                      onChange={(libraryIds) => onUpdate({ libraryIds })}
                    />
                  </div>
                )}

                {/* Genres */}
                <div className="space-y-2">
                  <Label>Genres</Label>
                  <SearchableMultiSelect
                    options={filters?.genres ?? []}
                    value={state.genres}
                    onChange={(genres) => onUpdate({ genres })}
                    placeholder="Select genres..."
                    isLoading={filtersLoading}
                  />
                  <p className="text-muted-foreground text-xs">
                    Items must match all selected genres.
                  </p>
                </div>

                {/* Year Range */}
                <div className="grid grid-cols-2 gap-4">
                  <div className="space-y-2">
                    <Label>Year From</Label>
                    <Input
                      type="number"
                      value={state.yearFrom}
                      onChange={(e) => onUpdate({ yearFrom: e.target.value })}
                      placeholder="e.g. 2000"
                    />
                  </div>
                  <div className="space-y-2">
                    <Label>Year To</Label>
                    <Input
                      type="number"
                      value={state.yearTo}
                      onChange={(e) => onUpdate({ yearTo: e.target.value })}
                      placeholder="e.g. 2025"
                    />
                  </div>
                </div>

                {/* Rating & Content Rating */}
                <div className="grid grid-cols-2 gap-4">
                  <div className="space-y-2">
                    <Label>Min IMDb Rating</Label>
                    <Input
                      type="number"
                      min={0}
                      max={10}
                      step={0.1}
                      value={state.minRating}
                      onChange={(e) => onUpdate({ minRating: e.target.value })}
                      placeholder="e.g. 7.0"
                    />
                  </div>
                  <div className="space-y-2">
                    <Label>Content Rating</Label>
                    <SearchableSelect
                      options={filters?.content_ratings ?? []}
                      value={state.contentRating}
                      onChange={(contentRating) => onUpdate({ contentRating })}
                      placeholder="Select rating..."
                      isLoading={filtersLoading}
                    />
                  </div>
                </div>

                {/* Studio & Network */}
                <div className="grid grid-cols-2 gap-4">
                  <div className="space-y-2">
                    <Label>Studio</Label>
                    <SearchableSelect
                      options={filters?.studios ?? []}
                      value={state.studio}
                      onChange={(studio) => onUpdate({ studio })}
                      placeholder="Select studio..."
                      isLoading={filtersLoading}
                    />
                  </div>
                  <div className="space-y-2">
                    <Label>Network</Label>
                    <SearchableSelect
                      options={filters?.networks ?? []}
                      value={state.network}
                      onChange={(network) => onUpdate({ network })}
                      placeholder="Select network..."
                      isLoading={filtersLoading}
                    />
                  </div>
                </div>

                {/* Country & Status */}
                <div className="grid grid-cols-2 gap-4">
                  <div className="space-y-2">
                    <Label>Country</Label>
                    <SearchableSelect
                      options={filters?.countries ?? []}
                      value={state.country}
                      onChange={(country) => onUpdate({ country })}
                      placeholder="Select country..."
                      isLoading={filtersLoading}
                    />
                  </div>
                  <div className="space-y-2">
                    <Label>Status</Label>
                    <Select
                      value={state.status || "__any__"}
                      onValueChange={(v) => onUpdate({ status: v === "__any__" ? "" : v })}
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
                <div className="grid grid-cols-2 gap-4">
                  <div className="space-y-2">
                    <Label>Added in the Last</Label>
                    <Input
                      value={state.addedInLast}
                      onChange={(e) => onUpdate({ addedInLast: e.target.value })}
                      placeholder="e.g. 30d, 2w, 6m"
                    />
                  </div>
                  <div className="space-y-2">
                    <Label>Released in the Last</Label>
                    <Input
                      value={state.releasedInLast}
                      onChange={(e) => onUpdate({ releasedInLast: e.target.value })}
                      placeholder="e.g. 90d, 1y"
                    />
                  </div>
                </div>
              </>
            )}
          </div>
        </ScrollArea>

        <SheetFooter className="flex-row justify-between border-t pt-4">
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => onUpdate(EMPTY_SECONDARY_FILTERS)}
          >
            Clear All
          </Button>
          <SheetClose asChild>
            <Button type="button" size="sm">
              Done
            </Button>
          </SheetClose>
        </SheetFooter>
      </SheetContent>
    </Sheet>
  );
}
