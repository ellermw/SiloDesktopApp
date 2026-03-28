import { Link, Outlet, useLocation } from "react-router";
import {
  ArrowLeft,
  Play,
  Library,
  Clock,
  Subtitles,
  LayoutDashboard,
  Palette,
  Eye,
} from "lucide-react";
import type { LucideIcon } from "lucide-react";
import { useDocumentTitle } from "@/hooks/useDocumentTitle";
import { resolveSettingsDocumentTitle } from "@/lib/documentTitle";
import { cn } from "@/lib/utils";

interface NavItem {
  path: string;
  label: string;
  icon: LucideIcon;
  description: string;
}

const NAV_ITEMS: NavItem[] = [
  {
    path: "appearance",
    label: "Appearance",
    icon: Palette,
    description: "Theme and interface tone",
  },
  {
    path: "accessibility",
    label: "Accessibility",
    icon: Eye,
    description: "Readability and contrast",
  },
  {
    path: "playback",
    label: "Playback",
    icon: Play,
    description: "Quality, language, and skipping",
  },
  {
    path: "libraries",
    label: "Libraries",
    icon: Library,
    description: "Visibility and access",
  },
  {
    path: "history-import",
    label: "History Import",
    icon: Clock,
    description: "Emby watch history",
  },
  {
    path: "subtitle-appearance",
    label: "Subtitles",
    icon: Subtitles,
    description: "Style and positioning",
  },
  {
    path: "home-screen",
    label: "Home Screen",
    icon: LayoutDashboard,
    description: "Sections and layout",
  },
];

export default function SettingsLayout() {
  const location = useLocation();
  const segments = location.pathname.split("/");
  const activeSegment = segments[2] || "playback";

  useDocumentTitle(resolveSettingsDocumentTitle(location.pathname));

  return (
    <div className="min-h-[100dvh]">
      <main className="page-shell-wide flex min-h-[100dvh] flex-col py-4 sm:py-6">
        <div className="page-header gap-5">
          <div className="min-w-0 space-y-3">
            <h1 className="page-title text-[clamp(2rem,4vw,3rem)]">Settings</h1>
            <p className="page-subtitle text-sm sm:text-base">
              Manage your playback preferences, libraries, and display options.
            </p>
          </div>
          <Link
            to="/"
            className="surface-panel-subtle text-muted-foreground hover:text-foreground inline-flex shrink-0 items-center gap-2 rounded-[1.1rem] px-3 py-2 text-sm font-medium transition-colors"
          >
            <ArrowLeft className="h-4 w-4" />
            <span className="hidden sm:inline">Back</span>
          </Link>
        </div>

        <nav
          aria-label="Settings sections"
          className="surface-panel-subtle mt-6 overflow-x-auto rounded-[1.4rem] p-1"
          style={{ WebkitOverflowScrolling: "touch" }}
        >
          <div className="flex min-w-max items-stretch gap-1">
            {NAV_ITEMS.map((item) => {
              const isActive = item.path === activeSegment;
              const Icon = item.icon;
              return (
                <Link
                  key={item.path}
                  to={`/settings/${item.path}`}
                  aria-current={isActive ? "page" : undefined}
                  className={cn(
                    "inline-flex items-center gap-2 rounded-[1rem] px-3 py-2.5 text-sm font-medium whitespace-nowrap transition-colors",
                    isActive
                      ? "bg-background text-foreground shadow-sm"
                      : "text-muted-foreground hover:bg-background/70 hover:text-foreground",
                  )}
                >
                  <Icon className="h-4 w-4" />
                  {item.label}
                </Link>
              );
            })}
          </div>
        </nav>

        <div className="min-w-0 flex-1 pt-8">
          <div className="mx-auto w-full max-w-3xl">
            <Outlet />
          </div>
        </div>
      </main>
    </div>
  );
}
