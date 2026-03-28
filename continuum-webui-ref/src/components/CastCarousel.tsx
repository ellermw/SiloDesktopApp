import ViewTransitionLink from "@/components/ViewTransitionLink";
import type { CastMember } from "@/api/types";
import { ScrollArea, ScrollBar } from "@/components/ui/scroll-area";
import { buildPersonCatalogHref } from "@/pages/catalogSearchParams";

interface CastCarouselProps {
  cast: CastMember[];
  limit?: number;
}

function getInitials(name: string): string {
  if (!name) return "?";
  const parts = name.split(" ");
  const first = parts[0];
  const last = parts[parts.length - 1];
  if (parts.length >= 2 && first && last) {
    return (first.charAt(0) + last.charAt(0)).toUpperCase();
  }
  return name.slice(0, 2).toUpperCase();
}

export default function CastCarousel({ cast, limit = 20 }: CastCarouselProps) {
  if (cast.length === 0) return null;

  const visible = cast
    .slice()
    .sort((a, b) => a.order - b.order)
    .slice(0, limit);

  return (
    <ScrollArea className="w-full">
      <div className="flex gap-3 pb-3">
        {visible.map((member) => (
          <ViewTransitionLink
            key={`${member.name}-${member.order}`}
            to={member.person_id ? buildPersonCatalogHref(member.person_id) : "#"}
            className="group/cast w-[110px] shrink-0 transition-opacity hover:opacity-90"
          >
            <div className="media-card-image mb-2.5 aspect-[2/3] overflow-hidden rounded-lg">
              {member.photo_url ? (
                <img
                  src={member.photo_url}
                  alt={member.name}
                  className="h-full w-full object-cover transition-transform duration-300 group-hover/cast:scale-105"
                  loading="lazy"
                />
              ) : (
                <div className="flex h-full w-full items-center justify-center bg-surface text-lg font-semibold text-muted-foreground">
                  {getInitials(member.name)}
                </div>
              )}
            </div>
            <div className="px-0.5">
              <div className="truncate text-[13px] font-medium text-foreground">{member.name}</div>
              <div className="truncate text-[11px] text-muted-foreground">{member.character}</div>
            </div>
          </ViewTransitionLink>
        ))}
      </div>
      <ScrollBar orientation="horizontal" />
    </ScrollArea>
  );
}
