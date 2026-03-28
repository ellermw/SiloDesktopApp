import { useEffect, useCallback, useRef } from "react";
import { useWindowVirtualizer } from "@tanstack/react-virtual";
import type { BrowseItem } from "@/api/types";
import ItemCard from "./ItemCard";
import { Skeleton } from "@/components/ui/skeleton";
import { useGridLayout } from "@/hooks/useGridLayout";

interface SharedItemGridProps {
  loading?: boolean;
  sortField?: string;
}

interface WindowedItemGridProps extends SharedItemGridProps {
  totalItems: number;
  pages: Map<number, BrowseItem[]>;
  pageSize: number;
  onVisibleRangeChange: (startIndex: number, endIndex: number) => void;
  items?: never;
}

interface StaticItemGridProps extends SharedItemGridProps {
  items: BrowseItem[];
  totalItems?: never;
  pages?: never;
  pageSize?: never;
  onVisibleRangeChange?: never;
}

type ItemGridProps = WindowedItemGridProps | StaticItemGridProps;

function hasStaticItems(props: ItemGridProps): props is StaticItemGridProps {
  return Array.isArray((props as StaticItemGridProps).items);
}

const GRID_GAP = 12;
const TEXT_AREA_HEIGHT = 44;
const GRID_CLASSES =
  "grid grid-cols-3 sm:grid-cols-4 md:grid-cols-5 lg:grid-cols-7 xl:grid-cols-8 gap-3";

export default function ItemGrid({ ...props }: ItemGridProps) {
  const { loading, sortField } = props;
  const totalItems = hasStaticItems(props) ? props.items.length : props.totalItems;
  const pages = hasStaticItems(props)
    ? new Map<number, BrowseItem[]>([[0, props.items]])
    : props.pages;
  const pageSize = hasStaticItems(props) ? Math.max(props.items.length, 1) : props.pageSize;
  const onVisibleRangeChange = hasStaticItems(props) ? () => undefined : props.onVisibleRangeChange;
  const { containerRef, layout } = useGridLayout({
    gap: GRID_GAP,
    textAreaHeight: TEXT_AREA_HEIGHT,
  });
  const { columnCount, rowHeight } = layout;

  // Grow incrementally: loaded items + 5 pages of skeleton buffer, capped at total.
  // This keeps the scrollbar thumb a reasonable size instead of being
  // microscopic when totalItems is large.
  // Use a high-water mark so displayCount never shrinks — prevents the
  // virtualizer height from collapsing when intermediate pages leave the
  // active window, which would yank scroll position back up.
  let maxLoadedEnd = 0;
  pages.forEach((items, pageIndex) => {
    maxLoadedEnd = Math.max(maxLoadedEnd, pageIndex * pageSize + items.length);
  });
  const rawDisplayCount = Math.min(totalItems, maxLoadedEnd + pageSize * 5);
  const displayCountRef = useRef(0);
  const prevTotalRef = useRef(totalItems);
  if (prevTotalRef.current !== totalItems) {
    displayCountRef.current = 0;
    prevTotalRef.current = totalItems;
  }
  displayCountRef.current = Math.max(displayCountRef.current, rawDisplayCount);
  const displayCount = displayCountRef.current;
  const rowCount = Math.ceil(displayCount / columnCount);

  const virtualizer = useWindowVirtualizer({
    count: rowCount,
    estimateSize: () => rowHeight,
    overscan: 5,
    scrollMargin: containerRef.current?.offsetTop ?? 0,
  });

  const virtualRows = virtualizer.getVirtualItems();

  // Report visible item range to parent for page fetching
  const firstRow = virtualRows[0]?.index ?? 0;
  const lastRow = virtualRows[virtualRows.length - 1]?.index ?? 0;

  useEffect(() => {
    const start = firstRow * columnCount;
    const end = Math.min((lastRow + 1) * columnCount - 1, displayCount - 1);
    onVisibleRangeChange(start, Math.max(end, 0));
  }, [firstRow, lastRow, columnCount, displayCount, onVisibleRangeChange]);

  const getItem = useCallback(
    (globalIndex: number): BrowseItem | undefined => {
      const pageIndex = Math.floor(globalIndex / pageSize);
      const itemIndex = globalIndex % pageSize;
      return pages.get(pageIndex)?.[itemIndex];
    },
    [pages, pageSize],
  );

  if (loading) {
    return (
      <div ref={containerRef} className={GRID_CLASSES}>
        {Array.from({ length: 24 }).map((_, i) => (
          <div key={i}>
            <Skeleton className="aspect-[2/3] rounded-lg" />
            <Skeleton className="mt-2 h-4 w-3/4" />
          </div>
        ))}
      </div>
    );
  }

  if (totalItems === 0) {
    return <div className="text-muted-foreground py-12 text-center">No items found.</div>;
  }

  return (
    <div>
      <div
        style={{
          height: virtualizer.getTotalSize(),
          position: "relative",
          overflow: "visible",
        }}
      >
        <div
          ref={containerRef}
          className={GRID_CLASSES}
          style={{
            position: "absolute",
            top: 0,
            left: 0,
            right: 0,
            overflow: "visible",
            transform: `translateY(${(virtualRows[0]?.start ?? 0) - virtualizer.options.scrollMargin}px)`,
          }}
        >
          {virtualRows.flatMap((virtualRow) => {
            const startIndex = virtualRow.index * columnCount;
            const cellCount = Math.min(columnCount, displayCount - startIndex);
            const cells = [];

            for (let colIndex = 0; colIndex < cellCount; colIndex++) {
              const globalIndex = startIndex + colIndex;
              const item = getItem(globalIndex);

              if (!item) {
                cells.push(
                  <div key={`skeleton-${globalIndex}`}>
                    <Skeleton className="aspect-[2/3] rounded-lg" />
                    <Skeleton className="mt-2 h-4 w-3/4" />
                  </div>,
                );
              } else {
                cells.push(
                  <div key={item.content_id}>
                    <ItemCard item={item} sortField={sortField} />
                  </div>,
                );
              }
            }

            return cells;
          })}
        </div>
      </div>
    </div>
  );
}
