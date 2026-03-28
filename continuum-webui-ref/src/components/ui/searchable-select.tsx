import * as React from "react";
import { Popover as PopoverPrimitive } from "radix-ui";
import { Check, ChevronsUpDown } from "lucide-react";

import { cn } from "@/lib/utils";

interface SearchableSelectProps {
  /** The full list of available options. */
  options: string[];
  /** Currently selected value (empty string = nothing selected). */
  value: string;
  /** Called when the user picks an option or clears the selection. */
  onChange: (value: string) => void;
  placeholder?: string;
  disabled?: boolean;
  /** Show a loading skeleton while options are being fetched. */
  isLoading?: boolean;
}

export function SearchableSelect({
  options,
  value,
  onChange,
  placeholder = "Select...",
  disabled = false,
  isLoading = false,
}: SearchableSelectProps) {
  const [open, setOpen] = React.useState(false);
  const [search, setSearch] = React.useState("");

  const filtered = React.useMemo(() => {
    if (!search) return options;
    const lower = search.toLowerCase();
    return options.filter((opt) => opt.toLowerCase().includes(lower));
  }, [options, search]);

  return (
    <PopoverPrimitive.Root open={open} onOpenChange={setOpen} modal={false}>
      <PopoverPrimitive.Trigger asChild disabled={disabled}>
        <button
          type="button"
          role="combobox"
          aria-expanded={open}
          className={cn(
            "border-input bg-background ring-offset-background placeholder:text-muted-foreground focus:ring-ring flex h-9 w-full items-center justify-between rounded-md border px-3 py-2 text-sm shadow-xs focus:ring-1 focus:outline-none disabled:cursor-not-allowed disabled:opacity-50",
            !value && "text-muted-foreground",
          )}
        >
          <span className="truncate">{value || placeholder}</span>
          <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
        </button>
      </PopoverPrimitive.Trigger>

      <PopoverPrimitive.Portal>
        <PopoverPrimitive.Content
          align="start"
          sideOffset={4}
          className="bg-popover text-popover-foreground z-50 w-[var(--radix-popover-trigger-width)] rounded-md border shadow-md"
          onOpenAutoFocus={(e) => e.preventDefault()}
        >
          <div className="p-2">
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Search..."
              className="border-input bg-background placeholder:text-muted-foreground flex h-8 w-full rounded-md border px-2 text-sm outline-none"
              autoFocus
            />
          </div>

          <div className="max-h-60 overflow-y-auto p-1">
            {isLoading ? (
              <p className="text-muted-foreground py-4 text-center text-sm">Loading...</p>
            ) : filtered.length === 0 ? (
              <p className="text-muted-foreground py-4 text-center text-sm">No results found</p>
            ) : (
              <>
                {/* Clear / "Any" option */}
                <button
                  type="button"
                  className={cn(
                    "hover:bg-accent hover:text-accent-foreground relative flex w-full cursor-pointer items-center rounded-sm px-2 py-1.5 text-sm select-none",
                    !value && "font-medium",
                  )}
                  onClick={() => {
                    onChange("");
                    setOpen(false);
                    setSearch("");
                  }}
                >
                  <Check
                    className={cn("mr-2 h-4 w-4 shrink-0", value ? "opacity-0" : "opacity-100")}
                  />
                  <span className="text-muted-foreground italic">Any</span>
                </button>

                {filtered.map((opt) => (
                  <button
                    type="button"
                    key={opt}
                    className={cn(
                      "hover:bg-accent hover:text-accent-foreground relative flex w-full cursor-pointer items-center rounded-sm px-2 py-1.5 text-sm select-none",
                      value === opt && "font-medium",
                    )}
                    onClick={() => {
                      onChange(opt);
                      setOpen(false);
                      setSearch("");
                    }}
                  >
                    <Check
                      className={cn(
                        "mr-2 h-4 w-4 shrink-0",
                        value === opt ? "opacity-100" : "opacity-0",
                      )}
                    />
                    {opt}
                  </button>
                ))}
              </>
            )}
          </div>
        </PopoverPrimitive.Content>
      </PopoverPrimitive.Portal>
    </PopoverPrimitive.Root>
  );
}

interface SearchableMultiSelectProps {
  /** The full list of available options. */
  options: string[];
  /** Currently selected values. */
  value: string[];
  /** Called when the selection changes. */
  onChange: (value: string[]) => void;
  placeholder?: string;
  disabled?: boolean;
  /** Show a loading skeleton while options are being fetched. */
  isLoading?: boolean;
}

export function SearchableMultiSelect({
  options,
  value,
  onChange,
  placeholder = "Select...",
  disabled = false,
  isLoading = false,
}: SearchableMultiSelectProps) {
  const [open, setOpen] = React.useState(false);
  const [search, setSearch] = React.useState("");

  const selected = React.useMemo(() => new Set(value), [value]);

  const filtered = React.useMemo(() => {
    if (!search) return options;
    const lower = search.toLowerCase();
    return options.filter((opt) => opt.toLowerCase().includes(lower));
  }, [options, search]);

  function toggle(opt: string) {
    if (selected.has(opt)) {
      onChange(value.filter((v) => v !== opt));
    } else {
      onChange([...value, opt]);
    }
  }

  const displayText =
    value.length === 0 ? placeholder : value.length <= 3 ? value.join(", ") : `${value.length} selected`;

  return (
    <PopoverPrimitive.Root open={open} onOpenChange={setOpen} modal={false}>
      <PopoverPrimitive.Trigger asChild disabled={disabled}>
        <button
          type="button"
          role="combobox"
          aria-expanded={open}
          className={cn(
            "border-input bg-background ring-offset-background placeholder:text-muted-foreground focus:ring-ring flex h-9 w-full items-center justify-between rounded-md border px-3 py-2 text-sm shadow-xs focus:ring-1 focus:outline-none disabled:cursor-not-allowed disabled:opacity-50",
            value.length === 0 && "text-muted-foreground",
          )}
        >
          <span className="truncate">{displayText}</span>
          <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
        </button>
      </PopoverPrimitive.Trigger>

      <PopoverPrimitive.Portal>
        <PopoverPrimitive.Content
          align="start"
          sideOffset={4}
          className="bg-popover text-popover-foreground z-50 w-[var(--radix-popover-trigger-width)] rounded-md border shadow-md"
          onOpenAutoFocus={(e) => e.preventDefault()}
        >
          <div className="p-2">
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Search..."
              className="border-input bg-background placeholder:text-muted-foreground flex h-8 w-full rounded-md border px-2 text-sm outline-none"
              autoFocus
            />
          </div>

          <div className="max-h-60 overflow-y-auto p-1">
            {isLoading ? (
              <p className="text-muted-foreground py-4 text-center text-sm">Loading...</p>
            ) : filtered.length === 0 ? (
              <p className="text-muted-foreground py-4 text-center text-sm">No results found</p>
            ) : (
              <>
                {value.length > 0 ? (
                  <button
                    type="button"
                    className="hover:bg-accent hover:text-accent-foreground relative flex w-full cursor-pointer items-center rounded-sm px-2 py-1.5 text-sm select-none"
                    onClick={() => onChange([])}
                  >
                    <Check className="mr-2 h-4 w-4 shrink-0 opacity-0" />
                    <span className="text-muted-foreground italic">Clear all</span>
                  </button>
                ) : null}

                {filtered.map((opt) => (
                  <button
                    type="button"
                    key={opt}
                    className={cn(
                      "hover:bg-accent hover:text-accent-foreground relative flex w-full cursor-pointer items-center rounded-sm px-2 py-1.5 text-sm select-none",
                      selected.has(opt) && "font-medium",
                    )}
                    onClick={() => toggle(opt)}
                  >
                    <Check
                      className={cn(
                        "mr-2 h-4 w-4 shrink-0",
                        selected.has(opt) ? "opacity-100" : "opacity-0",
                      )}
                    />
                    {opt}
                  </button>
                ))}
              </>
            )}
          </div>
        </PopoverPrimitive.Content>
      </PopoverPrimitive.Portal>
    </PopoverPrimitive.Root>
  );
}
