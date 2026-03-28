import { Link, useLocation } from "react-router";
import { useAdminSessionStream } from "@/components/AdminSessionStreamProvider";
import {
  LayoutDashboard,
  Radio,
  Library,
  LayoutPanelTop,
  PanelsTopLeft,
  Users,
  History,
  SlidersHorizontal,
  Server,
  Bot,
  ArrowLeft,
  Wrench,
  KeyRound,
  CalendarClock,
  ScrollText,
} from "lucide-react";
import type { ReactNode } from "react";

interface SidebarItem {
  label: string;
  icon: ReactNode;
  href: string;
  exact?: boolean;
  badge?: ReactNode;
}

interface SidebarSection {
  label: string;
  items: SidebarItem[];
}

interface AdminSidebarProps {
  onNavigate?: () => void;
}

function useSessionCount() {
  const { sessions } = useAdminSessionStream();
  return sessions.length;
}

export default function AdminSidebar({ onNavigate }: AdminSidebarProps) {
  const location = useLocation();
  const sessionCount = useSessionCount();

  const sections: SidebarSection[] = [
    {
      label: "Overview",
      items: [
        {
          label: "Dashboard",
          icon: <LayoutDashboard className="h-[18px] w-[18px]" />,
          href: "/admin",
          exact: true,
        },
        {
          label: "Activity",
          icon: <Radio className="h-[18px] w-[18px]" />,
          href: "/admin/activity",
          badge:
            sessionCount > 0 ? <span className="live-badge">{sessionCount} live</span> : undefined,
        },
        {
          label: "Logs",
          icon: <ScrollText className="h-[18px] w-[18px]" />,
          href: "/admin/logs",
        },
      ],
    },
    {
      label: "Content",
      items: [
        {
          label: "Libraries",
          icon: <Library className="h-[18px] w-[18px]" />,
          href: "/admin/libraries",
        },
        {
          label: "Collections",
          icon: <LayoutPanelTop className="h-[18px] w-[18px]" />,
          href: "/admin/collections",
        },
        {
          label: "Sections",
          icon: <PanelsTopLeft className="h-[18px] w-[18px]" />,
          href: "/admin/sections",
        },
      ],
    },
    {
      label: "Users",
      items: [
        {
          label: "Users",
          icon: <Users className="h-[18px] w-[18px]" />,
          href: "/admin/users",
        },
        {
          label: "Playback History",
          icon: <History className="h-[18px] w-[18px]" />,
          href: "/admin/history",
        },
      ],
    },
    {
      label: "Server",
      items: [
        {
          label: "Scheduled Tasks",
          icon: <CalendarClock className="h-[18px] w-[18px]" />,
          href: "/admin/tasks",
        },
        {
          label: "Nodes",
          icon: <Server className="h-[18px] w-[18px]" />,
          href: "/admin/nodes",
        },
        {
          label: "Maintenance",
          icon: <Wrench className="h-[18px] w-[18px]" />,
          href: "/admin/maintenance",
        },
        {
          label: "Settings",
          icon: <SlidersHorizontal className="h-[18px] w-[18px]" />,
          href: "/admin/settings",
        },
        {
          label: "Recommendations",
          icon: <Bot className="h-[18px] w-[18px]" />,
          href: "/admin/recommendations",
        },
        {
          label: "API Keys",
          icon: <KeyRound className="h-[18px] w-[18px]" />,
          href: "/admin/api-keys",
        },
      ],
    },
  ];

  function isActive(item: SidebarItem) {
    if (item.exact) return location.pathname === item.href;
    return location.pathname === item.href || location.pathname.startsWith(`${item.href}/`);
  }

  return (
    <aside className="border-sidebar-border/70 bg-sidebar/92 fixed top-0 bottom-0 left-0 z-40 flex w-[240px] flex-col border-r backdrop-blur-2xl">
      {/* Logo */}
      <div className="flex items-center gap-2.5 px-5 pt-6 pb-1">
        <div className="text-primary border-sidebar-border/80 bg-sidebar-accent flex h-[38px] w-[38px] items-center justify-center rounded-xl border text-sm font-bold shadow-[0_18px_40px_-28px_rgba(0,0,0,0.9)]">
          ▶
        </div>
        <span className="text-[17px] font-extrabold tracking-[0.01em]">Continuum</span>
      </div>
      {/* Nav sections */}
      <nav className="sidebar-scroll flex-1 space-y-5 overflow-y-auto px-3">
        {sections.map((section) => (
          <div key={section.label}>
            <div className="text-muted-foreground mb-2 px-2 text-[10px] font-semibold tracking-[0.18em] uppercase">
              {section.label}
            </div>
            <div className="space-y-0.5">
              {section.items.map((item) => {
                const active = isActive(item);
                return (
                  <Link
                    key={item.href}
                    to={item.href}
                    onClick={onNavigate}
                    className={`relative flex items-center gap-2.5 rounded-xl px-3 py-2.5 text-[13px] font-medium transition-colors duration-150 ${
                      active
                        ? "text-primary bg-accent"
                        : "text-muted-foreground hover:text-foreground hover:bg-accent/70"
                    } `}
                  >
                    {active && (
                      <span
                        className="absolute top-1/2 left-[-12px] h-[18px] w-[3px] -translate-y-1/2 rounded-r-sm"
                        style={{ background: "var(--primary)" }}
                      />
                    )}
                    <span className="flex w-[18px] flex-shrink-0 items-center justify-center">
                      {item.icon}
                    </span>
                    <span>{item.label}</span>
                    {item.badge && <span className="ml-auto">{item.badge}</span>}
                  </Link>
                );
              })}
            </div>
          </div>
        ))}
      </nav>

      {/* Footer */}
      <div className="space-y-3 px-3 pb-4">
        {/* Back to app */}
        <Link
          to="/"
          onClick={onNavigate}
          className="text-muted-foreground hover:text-foreground hover:bg-accent/70 flex items-center gap-2.5 rounded-xl px-3 py-2.5 text-[13px] font-medium transition-colors duration-150"
        >
          <ArrowLeft className="h-[18px] w-[18px]" />
          <span>Back to App</span>
        </Link>

        {/* Server info */}
        <div className="surface-panel-subtle rounded-2xl p-3">
          <div className="flex items-center gap-1.5 text-xs font-semibold">
            <span className="inline-block h-1.5 w-1.5 rounded-full bg-green-500" />
            Continuum
          </div>
          <div className="text-muted-foreground mt-0.5 text-[11px]">Server Online</div>
        </div>
      </div>
    </aside>
  );
}
