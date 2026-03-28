import { Link } from "react-router";
import { ChevronLeft, ChevronRight } from "lucide-react";

interface BreadcrumbSegment {
  label: string;
  href?: string;
}

interface DetailBreadcrumbProps {
  segments: BreadcrumbSegment[];
}

export default function DetailBreadcrumb({ segments }: DetailBreadcrumbProps) {
  if (segments.length === 0) return null;

  const backSegment = [...segments].reverse().find((segment) => segment.href);

  return (
    <div className="flex items-center gap-2.5">
      {backSegment?.href && (
        <Link
          to={backSegment.href}
          className="glass-subtle flex items-center justify-center rounded-full p-1.5 text-muted-foreground transition-colors hover:text-foreground"
        >
          <ChevronLeft className="size-5" />
        </Link>
      )}
      <div className="flex items-center gap-1.5">
        {segments.map((segment, i) => (
          <span key={i} className="flex items-center gap-1.5">
            {i > 0 && <ChevronRight className="size-4 text-muted-foreground/40" />}
            {segment.href && i < segments.length - 1 ? (
              <Link
                to={segment.href}
                className="text-[13px] text-muted-foreground transition-colors hover:text-foreground"
              >
                {segment.label}
              </Link>
            ) : (
              <span className="text-[13px] text-muted-foreground/60">{segment.label}</span>
            )}
          </span>
        ))}
      </div>
    </div>
  );
}
