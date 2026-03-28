import type { QueryDefinition } from "@/api/types";
import type { FilterConfig } from "@/api/types";
import FilterRuleEditor from "@/components/FilterRuleEditor";
import LibraryMultiSelect from "@/components/LibraryMultiSelect";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

interface CollectionRulesEditorProps {
  value: QueryDefinition;
  onChange: (value: QueryDefinition) => void;
  libraries?: Array<{ id: number; name: string }>;
  allowLibrarySelection?: boolean;
  readOnly?: boolean;
}

export default function CollectionRulesEditor({
  value,
  onChange,
  libraries = [],
  allowLibrarySelection = true,
  readOnly = false,
}: CollectionRulesEditorProps) {
  const filterConfig: FilterConfig = {
    match: value.match,
    groups: value.groups,
    sort: value.sort.field,
    order: value.sort.order,
  };

  return (
    <div className="space-y-4">
      <div className="grid gap-4 md:grid-cols-2">
        <div className="space-y-2">
          <Label>Media Scope</Label>
          <Select
            value={value.media_scope ?? "all"}
            onValueChange={(next) =>
              onChange({
                ...value,
                media_scope: next === "all" ? undefined : (next as "movie" | "series"),
              })
            }
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
              value={value.library_ids}
              onChange={(libraryIds) => onChange({ ...value, library_ids: libraryIds })}
            />
          </div>
        ) : null}
      </div>

      <div className="space-y-2">
        <Label>Rule Groups</Label>
        <FilterRuleEditor
          value={filterConfig}
          onChange={(next) =>
            onChange({
              ...value,
              match: next.match,
              groups: next.groups,
              sort: {
                field: (next.sort ?? value.sort.field) as QueryDefinition["sort"]["field"],
                order: (next.order ?? value.sort.order) as QueryDefinition["sort"]["order"],
              },
            })
          }
        />
      </div>
    </div>
  );
}
