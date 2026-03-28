import type { QueryDefinition } from "@/api/types";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

import { COLLECTION_SORT_OPTIONS } from "./collectionBuilderFields";

interface CollectionOrderingEditorProps {
  query: QueryDefinition;
  sortConfig: Record<string, unknown>;
  onQueryChange: (query: QueryDefinition) => void;
  onSortConfigChange: (sortConfig: Record<string, unknown>) => void;
  readOnly?: boolean;
}

export default function CollectionOrderingEditor({
  query,
  sortConfig,
  onQueryChange,
  onSortConfigChange,
  readOnly = false,
}: CollectionOrderingEditorProps) {
  const orderingMode = sortConfig.mode === "manual_pins" ? "manual_pins" : "query_sort";

  return (
    <div className="space-y-4">
      <div className="grid gap-4 md:grid-cols-3">
        <div className="space-y-2">
          <Label>Sort By</Label>
          <Select
            value={query.sort.field}
            onValueChange={(field) =>
              onQueryChange({
                ...query,
                sort: {
                  ...query.sort,
                  field: field as QueryDefinition["sort"]["field"],
                },
              })
            }
            disabled={readOnly}
          >
            <SelectTrigger>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {COLLECTION_SORT_OPTIONS.map((option) => (
                <SelectItem key={option.value} value={option.value}>
                  {option.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        <div className="space-y-2">
          <Label>Order</Label>
          <Select
            value={query.sort.order}
            onValueChange={(order) =>
              onQueryChange({
                ...query,
                sort: {
                  ...query.sort,
                  order: order as "asc" | "desc",
                },
              })
            }
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

        <div className="space-y-2">
          <Label>Manual Pins</Label>
          <Select
            value={orderingMode}
            onValueChange={(mode) =>
              onSortConfigChange({
                ...sortConfig,
                mode,
              })
            }
            disabled={readOnly}
          >
            <SelectTrigger>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="query_sort">Follow Query Sort</SelectItem>
              <SelectItem value="manual_pins">Preserve Manual Pins</SelectItem>
            </SelectContent>
          </Select>
        </div>
      </div>

      <p className="text-muted-foreground text-xs">
        Smart collections always preview using the query sort. Manual pins are stored for the saved
        collection so curated items can stay near the top later.
      </p>
    </div>
  );
}
