import { Children } from "react";
import type { ReactNode } from "react";
import { Link } from "react-router";
import { ChevronLeft, ChevronRight } from "lucide-react";
import { Skeleton } from "@/components/ui/skeleton";
import { useCarouselEmbla } from "@/hooks/useCarouselEmbla";

interface MediaCarouselProps {
  title: string;
  titleHref?: string;
  loading?: boolean;
  children: ReactNode;
  skeletonCount?: number;
  skeletonAspect?: string;
  onViewAll?: () => void;
  /** Optional actions rendered next to the title (e.g. pin button) */
  headerActions?: ReactNode;
}

export default function MediaCarousel({
  title,
  titleHref,
  loading = false,
  children,
  skeletonCount = 7,
  skeletonAspect = "aspect-[2/3]",
  onViewAll,
  headerActions,
}: MediaCarouselProps) {
  const { emblaRef, canScrollPrev, canScrollNext, scrollPrev, scrollNext } = useCarouselEmbla();
  const slideChildren = loading
    ? Array.from({ length: skeletonCount }).map((_, i) => (
        <div key={i} className="w-[130px] sm:w-[150px] lg:w-[178px]">
          <Skeleton className={`w-full ${skeletonAspect} rounded-lg`} />
          <Skeleton className="mt-2 h-4 w-3/4 rounded" />
          <Skeleton className="mt-1 h-3 w-1/2 rounded" />
        </div>
      ))
    : Children.toArray(children);

  return (
    <section className="group/carousel relative isolate">
      <div className="mb-5 flex items-end justify-between gap-4 px-4 sm:px-6 lg:px-10 xl:px-12">
        <div className="flex items-center gap-2">
          {titleHref ? (
            <Link to={titleHref} className="group/title hover:text-primary transition-colors">
              <h2 className="text-foreground text-xl font-semibold tracking-tight">
                {title}
                <span className="text-muted-foreground group-hover/title:text-primary ml-2 text-sm transition-colors">
                  View
                </span>
              </h2>
            </Link>
          ) : (
            <h2 className="text-foreground text-xl font-semibold tracking-tight">{title}</h2>
          )}
          {headerActions}
        </div>
        {onViewAll && (
          <button
            onClick={onViewAll}
            className="text-muted-foreground hover:text-primary text-[12px] font-semibold tracking-[0.16em] uppercase transition-all active:scale-[0.98]"
          >
            Explore all
          </button>
        )}
      </div>

      <div className="relative">
        {/* Left arrow */}
        {canScrollPrev && (
          <button
            onClick={scrollPrev}
            className="from-background/80 absolute top-0 bottom-0 left-0 z-10 flex w-10 items-center justify-center bg-gradient-to-r to-transparent opacity-0 transition-opacity duration-[--duration-fast] group-hover/carousel:opacity-100"
            aria-label="Scroll left"
          >
            <ChevronLeft className="text-foreground h-6 w-6" />
          </button>
        )}

        <div ref={emblaRef} className="embla__viewport overflow-hidden">
          <div className="embla__container flex cursor-grab gap-4 px-4 sm:px-6 lg:gap-5 lg:px-10 xl:px-12">
            {slideChildren.map((child, index) => (
              <div key={index} className="embla__slide shrink-0">
                {child}
              </div>
            ))}
          </div>
        </div>

        {/* Right arrow */}
        {canScrollNext && (
          <button
            onClick={scrollNext}
            className="from-background/80 absolute top-0 right-0 bottom-0 z-10 flex w-10 items-center justify-center bg-gradient-to-l to-transparent opacity-0 transition-opacity duration-[--duration-fast] group-hover/carousel:opacity-100"
            aria-label="Scroll right"
          >
            <ChevronRight className="text-foreground h-6 w-6" />
          </button>
        )}
      </div>
    </section>
  );
}
