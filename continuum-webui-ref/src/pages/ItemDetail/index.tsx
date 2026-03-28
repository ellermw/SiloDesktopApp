import { useEffect } from "react";
import { useParams } from "react-router";
import { useCatalogItemDetail } from "@/hooks/queries/catalogRead";
import type { ItemDetail } from "@/api/types";
import { useDocumentTitle } from "@/hooks/useDocumentTitle";
import { Skeleton } from "@/components/ui/skeleton";
import { toast } from "sonner";
import MovieContent from "@/pages/ItemDetail/MovieContent";
import SeriesContent from "@/pages/ItemDetail/SeriesContent";
import SeasonContent from "@/pages/ItemDetail/SeasonContent";
import EpisodeContent from "@/pages/ItemDetail/EpisodeContent";

function ItemDetailSkeleton() {
  return (
    <div>
      <section className="relative isolate overflow-hidden border-b border-border/10">
        <div className="absolute inset-0 bg-gradient-to-r from-[var(--background)] via-[var(--background)]/70 to-transparent" />
        <div className="absolute inset-0 bg-gradient-to-t from-[var(--background)] via-[var(--background)]/40 to-transparent" />

        <div className="relative mx-auto flex min-h-[500px] max-w-[1400px] flex-col justify-end px-4 pt-28 pb-8 sm:px-6 lg:min-h-[55vh] lg:max-h-[720px] lg:px-10">
          <div className="flex flex-col gap-6 lg:flex-row lg:items-end">
            <Skeleton className="aspect-[2/3] w-[170px] flex-shrink-0 rounded-lg sm:w-[220px]" />

            <div className="max-w-3xl flex-1 space-y-4">
              <Skeleton className="h-4 w-16" />
              <Skeleton className="h-3 w-24" />
              <Skeleton className="h-10 w-80 max-w-full" />
              <div className="flex gap-2">
                <Skeleton className="h-6 w-14 rounded-md" />
                <Skeleton className="h-6 w-16 rounded-md" />
                <Skeleton className="h-6 w-20 rounded-md" />
              </div>
              <div className="flex gap-3">
                <Skeleton className="h-5 w-16" />
                <Skeleton className="h-5 w-16" />
              </div>
              <div className="space-y-2">
                <Skeleton className="h-4 w-full max-w-2xl" />
                <Skeleton className="h-4 w-5/6 max-w-xl" />
                <Skeleton className="h-4 w-3/4 max-w-lg" />
              </div>
              <Skeleton className="h-4 w-64" />
              <div className="flex gap-3 pt-2">
                <Skeleton className="h-10 w-28 rounded-lg" />
                <Skeleton className="h-10 w-10 rounded-lg" />
                <Skeleton className="h-10 w-10 rounded-lg" />
              </div>
            </div>
          </div>
        </div>
      </section>

      <div className="mx-auto max-w-[1400px] space-y-10 px-4 py-8 sm:px-6 lg:px-10">
        <div>
          <Skeleton className="mb-4 h-6 w-32" />
          <div className="flex gap-3 overflow-hidden">
            {Array.from({ length: 6 }, (_, i) => (
              <Skeleton key={i} className="aspect-[2/3] w-[150px] flex-shrink-0 rounded-lg" />
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}

export default function ItemDetail() {
  const { id } = useParams<{ id: string }>();
  const { data: item, isLoading: loading, error: itemError } = useCatalogItemDetail(id);

  useDocumentTitle(item?.title ?? "Item");

  useEffect(() => {
    if (itemError) {
      toast.error(itemError instanceof Error ? itemError.message : "Failed to load item");
    }
  }, [itemError]);

  if (loading) {
    return <ItemDetailSkeleton />;
  }

  if (!item) {
    return (
      <div className="page-shell text-muted-foreground py-8">
        Item not found.
      </div>
    );
  }

  switch (item.type) {
    case "movie":
      return <MovieContent item={item as ItemDetail & { type: "movie" }} />;
    case "series":
      return <SeriesContent item={item as ItemDetail & { type: "series" }} />;
    case "season":
      return <SeasonContent item={item as ItemDetail & { type: "season" }} />;
    case "episode":
      return <EpisodeContent item={item as ItemDetail & { type: "episode" }} />;
    default:
      return (
        <div className="page-shell text-muted-foreground py-8">
          Unsupported item type: {item.type}
        </div>
      );
  }
}
