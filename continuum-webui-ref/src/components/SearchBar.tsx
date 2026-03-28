import { useEffect, useRef, useState } from "react";
import { useViewTransitionNavigate } from "@/hooks/useViewTransition";
import { Input } from "@/components/ui/input";
import { buildQueryCatalogHref } from "@/pages/catalogSearchParams";
import { Search } from "lucide-react";
import type { FormEvent } from "react";

interface SearchBarProps {
  initialQuery?: string;
  autoFocus?: boolean;
  prominent?: boolean;
}

export default function SearchBar({
  initialQuery = "",
  autoFocus = false,
  prominent = false,
}: SearchBarProps) {
  const [query, setQuery] = useState(initialQuery);
  const navigate = useViewTransitionNavigate();
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (autoFocus && inputRef.current) {
      inputRef.current.focus();
    }
  }, [autoFocus]);

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (query.trim()) {
      navigate(buildQueryCatalogHref(query.trim()));
    }
  }

  if (prominent) {
    return (
      <form onSubmit={handleSubmit} className="relative w-full max-w-xl">
        <Search className="text-muted-foreground absolute top-4 left-4 h-5 w-5" />
        <Input
          ref={inputRef}
          placeholder="Search movies, series..."
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          className="surface-panel h-14 rounded-[1.4rem] border-0 pl-12 pr-4 text-base shadow-none"
        />
      </form>
    );
  }

  return (
    <form onSubmit={handleSubmit} className="relative">
      <Search className="text-muted-foreground absolute top-2.5 left-2.5 h-4 w-4" />
      <Input
        ref={inputRef}
        placeholder="Search..."
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        className="pl-9"
      />
    </form>
  );
}
