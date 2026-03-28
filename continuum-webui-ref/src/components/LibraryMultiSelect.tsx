import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuCheckboxItem,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { ChevronDown } from "lucide-react";

interface LibraryOption {
  id: number;
  name: string;
}

function formatLibraryFilterSummary(libraryIds: number[], libraries: LibraryOption[]): string {
  if (libraryIds.length === 0) {
    return "All Libraries";
  }

  const names = libraryIds
    .map((libraryId) => libraries.find((library) => library.id === libraryId)?.name)
    .filter((name): name is string => Boolean(name));

  if (names.length === 0) {
    return `${libraryIds.length} libraries`;
  }
  if (names.length === 1) {
    return names[0] ?? "1 library";
  }
  if (names.length === 2) {
    return `${names[0] ?? "Library"}, ${names[1] ?? "Library"}`;
  }
  return `${names[0] ?? "Library"} +${names.length - 1} more`;
}

function toggleLibrarySelection(
  selectedIds: number[],
  libraryId: number,
  checked: boolean,
): number[] {
  if (checked) {
    return selectedIds.includes(libraryId) ? selectedIds : [...selectedIds, libraryId];
  }
  return selectedIds.filter((id) => id !== libraryId);
}

export default function LibraryMultiSelect({
  libraries,
  value,
  onChange,
}: {
  libraries: LibraryOption[];
  value: number[];
  onChange: (libraryIds: number[]) => void;
}) {
  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button type="button" variant="outline" className="w-full justify-between">
          <span className="truncate">{formatLibraryFilterSummary(value, libraries)}</span>
          <ChevronDown className="ml-2 h-4 w-4 shrink-0" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent className="w-[260px]" align="start">
        <DropdownMenuItem
          disabled={value.length === 0}
          onSelect={(event) => {
            event.preventDefault();
            onChange([]);
          }}
        >
          All Libraries
        </DropdownMenuItem>
        <DropdownMenuSeparator />
        {libraries.map((library) => (
          <DropdownMenuCheckboxItem
            key={library.id}
            checked={value.includes(library.id)}
            onCheckedChange={(checked) =>
              onChange(toggleLibrarySelection(value, library.id, Boolean(checked)))
            }
            onSelect={(event) => event.preventDefault()}
          >
            {library.name}
          </DropdownMenuCheckboxItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
