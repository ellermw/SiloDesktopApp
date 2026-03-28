import { SlidersHorizontal } from "lucide-react";

import type { GuidedFormState } from "@/components/collections/CollectionGuidedRulesEditor";
import { COLLECTION_SORT_OPTIONS } from "@/components/collections/collectionBuilderFields";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

interface CatalogFilterBarProps {
  state: GuidedFormState;
  onUpdate: (patch: Partial<GuidedFormState>) => void;
  activeFilterCount: number;
  onOpenFilters: () => void;
}

export default function CatalogFilterBar({
  state,
  onUpdate,
  activeFilterCount,
  onOpenFilters,
}: CatalogFilterBarProps) {
  return (
    <div className="flex flex-wrap items-center gap-3">
      {/* Media Type */}
      <Select
        value={state.mediaScope}
        onValueChange={(v) => onUpdate({ mediaScope: v as GuidedFormState["mediaScope"] })}
      >
        <SelectTrigger className="w-32">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="all">All Media</SelectItem>
          <SelectItem value="movie">Movies</SelectItem>
          <SelectItem value="series">Series</SelectItem>
        </SelectContent>
      </Select>

      {/* Sort By */}
      <Select
        value={state.sortField}
        onValueChange={(v) => onUpdate({ sortField: v })}
      >
        <SelectTrigger className="w-40">
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

      {/* Order */}
      <Select
        value={state.sortOrder}
        onValueChange={(v) => onUpdate({ sortOrder: v as "asc" | "desc" })}
      >
        <SelectTrigger className="w-28">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="desc">Descending</SelectItem>
          <SelectItem value="asc">Ascending</SelectItem>
        </SelectContent>
      </Select>

      {/* Filters button */}
      <Button variant="outline" size="sm" onClick={onOpenFilters} className="gap-2">
        <SlidersHorizontal className="h-4 w-4" />
        Filters
        {activeFilterCount > 0 && (
          <Badge variant="default" className="ml-1 px-1.5 py-0 text-[10px]">
            {activeFilterCount}
          </Badge>
        )}
      </Button>
    </div>
  );
}
