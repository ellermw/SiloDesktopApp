import { useState, useCallback, useEffect, useMemo, useRef } from "react";
import type { ReactNode } from "react";
import { Link, useLocation, useParams } from "react-router";
import ViewTransitionLink from "@/components/ViewTransitionLink";
import { getProfileMenuSide, isSidebarExpanded } from "@/components/AppSidebar.logic";
import { useAuth } from "@/hooks/useAuth";
import { useCurrentProfile } from "@/hooks/useCurrentProfile";
import { useUserLibraries } from "@/hooks/queries/libraries";
import { useSidebarPins, useToggleSidebarPin } from "@/hooks/queries/sidebarPins";
import { useViewTransitionNavigate } from "@/hooks/useViewTransition";
import {
  buildLibraryCollectionCatalogHref,
  buildPersonalCatalogHref,
  buildQueryCatalogHref,
  buildSectionCatalogHref,
  parseCatalogSearchParams,
} from "@/pages/catalogSearchParams";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import ThemeSwitcher from "@/components/ThemeSwitcher";
import {
  Film,
  Tv,
  Library,
  Search,
  Heart,
  List,
  Clock,
  FolderOpen,
  LogOut,
  UserCircle,
  Shield,
  Settings,
  Sparkles,
  Home,
  ChevronDown,
  ChevronRight,
  PinOff,
  LayoutGrid,
} from "lucide-react";

function getLibraryIcon(type: string) {
  switch (type) {
    case "movies":
      return <Film className="h-[18px] w-[18px]" />;
    case "series":
      return <Tv className="h-[18px] w-[18px]" />;
    default:
      return <Library className="h-[18px] w-[18px]" />;
  }
}

function SidebarLabel({ children, show }: { children: ReactNode; show: boolean }) {
  return (
    <span
      className={`truncate transition-[opacity,max-width] duration-200 ease-out ${
        show ? "max-w-[180px] opacity-100 delay-[50ms]" : "max-w-0 overflow-hidden opacity-0"
      }`}
    >
      {children}
    </span>
  );
}

function SidebarSectionHeader({
  children,
  show,
  collapsible = false,
  expanded = false,
  onToggle,
}: {
  children: ReactNode;
  show: boolean;
  collapsible?: boolean;
  expanded?: boolean;
  onToggle?: () => void;
}) {
  const textClass =
    "text-muted-foreground text-[10px] font-semibold tracking-[0.22em] uppercase";

  if (collapsible) {
    return (
      <div className="mb-2">
        <button
          onClick={onToggle}
          disabled={!show}
          aria-hidden={!show}
          tabIndex={show ? 0 : -1}
          className={`${textClass} hover:text-sidebar-foreground flex h-5 w-full items-center gap-1 px-3 transition-opacity duration-150 ${
            show ? "opacity-100" : "pointer-events-none opacity-0"
          }`}
        >
          {expanded ? <ChevronDown className="h-3 w-3" /> : <ChevronRight className="h-3 w-3" />}
          <span>{children}</span>
        </button>
      </div>
    );
  }

  return (
    <div className="mb-2 px-3">
      <div
        aria-hidden={!show}
        className={`${textClass} flex h-5 items-center transition-opacity duration-150 ${
          show ? "opacity-100" : "opacity-0"
        }`}
      >
        {children}
      </div>
    </div>
  );
}

interface AppSidebarProps {
  onNavigate?: () => void;
  collapsed?: boolean;
}

export default function AppSidebar({ onNavigate, collapsed = false }: AppSidebarProps) {
  const location = useLocation();
  const navigate = useViewTransitionNavigate();
  const params = useParams<{ libraryId: string }>();
  const { user, logout, clearProfile } = useAuth();
  const { profile } = useCurrentProfile();
  const isAdmin = user?.role === "admin";
  const { data: libraries } = useUserLibraries();
  const { pins } = useSidebarPins();
  const { togglePin } = useToggleSidebarPin();
  const catalogState = useMemo(
    () =>
      location.pathname === "/catalog"
        ? parseCatalogSearchParams(new URLSearchParams(location.search))
        : null,
    [location.pathname, location.search],
  );
  const activeLibraryId =
    params.libraryId
      ? Number(params.libraryId)
      : catalogState?.source === "section" && catalogState.scope === "library"
        ? (catalogState.library_id ?? null)
      : catalogState?.source === "library_collection"
          ? (catalogState.library_id ?? null)
          : null;
  // Hover-to-expand when collapsed (150ms enter delay prevents accidental expansion)
  const [hovered, setHovered] = useState(false);
  const [profileMenuOpen, setProfileMenuOpen] = useState(false);
  const hoverTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const handleMouseEnter = useCallback(() => {
    if (!collapsed) return;
    hoverTimerRef.current = setTimeout(() => setHovered(true), 150);
  }, [collapsed]);
  const handleMouseLeave = useCallback(() => {
    if (hoverTimerRef.current) clearTimeout(hoverTimerRef.current);
    hoverTimerRef.current = null;
    if (profileMenuOpen) return;
    setHovered(false);
  }, [profileMenuOpen]);
  useEffect(() => {
    return () => {
      if (hoverTimerRef.current) clearTimeout(hoverTimerRef.current);
    };
  }, []);

  const sidebarExpanded = isSidebarExpanded(collapsed, hovered, profileMenuOpen);
  const showLabels = sidebarExpanded;
  const [librariesExpanded, setLibrariesExpanded] = useState(true);
  const [expandedLibraries, setExpandedLibraries] = useState<Set<number>>(
    () =>
      new Set(
        Object.keys(pins)
          .map(Number)
          .filter((id) => Number.isInteger(id) && id > 0),
      ),
  );

  const toggleLibraryExpand = useCallback((libId: number) => {
    setExpandedLibraries((prev) => {
      const next = new Set(prev);
      if (next.has(libId)) {
        next.delete(libId);
      } else {
        next.add(libId);
      }
      return next;
    });
  }, []);

  function isActive(href: string, exact?: boolean) {
    if (exact) return location.pathname === href;
    return location.pathname === href || location.pathname.startsWith(`${href}/`);
  }

  function isCatalogSourceActive(source: "query" | "favorites" | "watchlist" | "history") {
    return location.pathname === "/catalog" && catalogState?.source === source;
  }

  function isPinnedCatalogActive(libId: number, pin: { type: "collection" | "section"; id: string }) {
    if (location.pathname !== "/catalog" || !catalogState) {
      return false;
    }

    if (pin.type === "collection") {
      return catalogState.source === "library_collection" && catalogState.collection_id === pin.id;
    }

    return (
      catalogState.source === "section" &&
      catalogState.scope === "library" &&
      catalogState.library_id === libId &&
      catalogState.section_id === pin.id
    );
  }

  const navLinkClass = (href: string, exact?: boolean) =>
    navLinkClassForState(isActive(href, exact));

  const navLinkClassForState = (active: boolean) =>
    `relative flex items-center gap-2.5 rounded-xl px-3 py-3 text-[13px] font-medium transition-all duration-200 ${
      active
        ? "text-sidebar-accent-foreground bg-sidebar-accent/90 shadow-[0_16px_30px_-24px_rgba(0,0,0,0.7)]"
        : "text-muted-foreground hover:text-sidebar-foreground hover:bg-sidebar-accent/70"
    }`;

  return (
    <aside
      className={`fixed top-0 bottom-0 left-0 z-40 flex flex-col border-r border-sidebar-border/70 bg-sidebar/88 backdrop-blur-2xl sidebar-transition ${collapsed && !sidebarExpanded ? "w-16" : "w-[260px]"}`}
      onMouseEnter={handleMouseEnter}
      onMouseLeave={handleMouseLeave}
      style={collapsed && sidebarExpanded ? { zIndex: 45, boxShadow: "0 25px 50px -12px rgb(0 0 0 / 0.5)" } : undefined}
    >
      {/* Logo */}
      <div className={`flex items-center py-6 ${showLabels ? "gap-2.5 px-5" : "pl-3 pr-2"}`}>
        <div className="text-primary border-sidebar-border/80 bg-sidebar-accent flex h-10 w-10 shrink-0 items-center justify-center rounded-2xl border text-sm font-bold shadow-[0_20px_40px_-28px_rgba(0,0,0,0.8)]">
          ▶
        </div>
        <div className={`min-w-0 overflow-hidden whitespace-nowrap transition-[opacity,max-width] duration-200 ease-out ${
          showLabels ? "max-w-[200px] opacity-100 delay-[50ms]" : "max-w-0 opacity-0"
        }`}>
          <span className="text-sidebar-foreground block text-[17px] font-extrabold tracking-[0.01em]">
            Continuum
          </span>
          <span className="text-[10px] font-medium text-muted-foreground">Media server</span>
        </div>
      </div>

      {/* Main nav */}
      <nav className="sidebar-scroll flex-1 space-y-7 overflow-y-auto px-3 pb-5">
        {/* Home */}
        <div className="space-y-0.5">
          <ViewTransitionLink to="/" onClick={onNavigate} className={navLinkClass("/", true)}>
            {isActive("/", true) && (
              <span
                className="absolute top-1/2 left-0 h-[18px] w-[3px] -translate-y-1/2 rounded-r-sm"
                style={{ background: "var(--primary)" }}
              />
            )}
            <Home className="h-[18px] w-[18px] shrink-0" />
            <SidebarLabel show={showLabels}>Home</SidebarLabel>
          </ViewTransitionLink>
        </div>

        {/* Libraries */}
        {libraries && libraries.length > 0 && (
          <div>
            <SidebarSectionHeader
              show={showLabels}
              collapsible
              expanded={librariesExpanded}
              onToggle={() => setLibrariesExpanded((v) => !v)}
            >
              Libraries
            </SidebarSectionHeader>
            {(showLabels ? librariesExpanded : true) && (
              <div className="space-y-0.5">
                {libraries.map((lib) => {
                  const href = `/library/${lib.id}`;
                  const active = activeLibraryId === lib.id;
                  const libraryPins = pins[String(lib.id)] ?? [];
                  const hasPins = libraryPins.length > 0;
                  const isExpanded = hasPins && !expandedLibraries.has(lib.id);

                  return (
                    <div key={lib.id}>
                      {/* Library row: chevron (if pins) + icon + name link */}
                      <div
                        className={`relative flex items-center rounded-lg text-[13px] font-medium transition-colors duration-150 ${
                          active
                            ? "text-sidebar-accent-foreground bg-sidebar-accent"
                            : "text-muted-foreground hover:text-sidebar-foreground hover:bg-sidebar-accent"
                        }`}
                      >
                        {active && (
                          <span
                            className="absolute top-1/2 left-0 h-[18px] w-[3px] -translate-y-1/2 rounded-r-sm"
                            style={{ background: "var(--primary)" }}
                          />
                        )}
                        {showLabels && hasPins ? (
                          <button
                            onClick={() => toggleLibraryExpand(lib.id)}
                            className="flex h-full items-center py-3 pr-1 pl-3"
                            aria-label={isExpanded ? "Collapse" : "Expand"}
                          >
                            {isExpanded ? (
                              <ChevronDown className="h-3.5 w-3.5 opacity-60" />
                            ) : (
                              <ChevronRight className="h-3.5 w-3.5 opacity-60" />
                            )}
                          </button>
                        ) : showLabels ? (
                          <span className="w-3 shrink-0 pl-3" />
                        ) : null}
                        <ViewTransitionLink
                          to={href}
                          onClick={onNavigate}
                          className={`flex items-center gap-2.5 py-2.5 ${
                            showLabels ? "flex-1 pr-3" : "w-full px-3"
                          }`}
                        >
                          <span className="flex w-[18px] flex-shrink-0 items-center justify-center">
                            {getLibraryIcon(lib.type)}
                          </span>
                          <SidebarLabel show={showLabels}>{lib.name}</SidebarLabel>
                        </ViewTransitionLink>
                      </div>

                      {/* Pinned items under this library — hidden when collapsed */}
                      {showLabels && hasPins && isExpanded && (
                        <div className="border-sidebar-border ml-3 space-y-0.5 border-l pl-2">
                          {libraryPins.map((pin) => {
                            const pinHref =
                              pin.type === "collection"
                                ? buildLibraryCollectionCatalogHref(pin.id, pin.label)
                                : buildSectionCatalogHref({
                                    scope: "library",
                                    libraryId: lib.id,
                                    sectionId: pin.id,
                                    title: pin.label,
                                  });
                            const pinActive = isPinnedCatalogActive(lib.id, pin);

                            return (
                              <div
                                key={`${pin.type}-${pin.id}`}
                                className="group/pin relative flex items-center"
                              >
                                <ViewTransitionLink
                                  to={pinHref}
                                  onClick={onNavigate}
                                  className={`flex flex-1 items-center gap-2 rounded-xl px-2.5 py-2.5 text-[12.5px] font-medium transition-colors duration-150 ${
                                    pinActive
                                      ? "text-sidebar-accent-foreground bg-sidebar-accent/90"
                                      : "text-muted-foreground hover:text-sidebar-foreground hover:bg-sidebar-accent/70"
                                  }`}
                                >
                                  {pin.type === "collection" ? (
                                    <FolderOpen className="h-3.5 w-3.5 opacity-60" />
                                  ) : (
                                    <LayoutGrid className="h-3.5 w-3.5 opacity-60" />
                                  )}
                                  <span className="truncate">{pin.label}</span>
                                </ViewTransitionLink>
                                <button
                                  onClick={() =>
                                    togglePin(lib.id, {
                                      type: pin.type,
                                      id: pin.id,
                                      label: pin.label,
                                    })
                                  }
                                  className="text-muted-foreground hover:text-destructive absolute right-1 rounded p-1 opacity-0 transition-opacity group-hover/pin:opacity-100"
                                  title="Unpin"
                                >
                                  <PinOff className="h-3 w-3" />
                                </button>
                              </div>
                            );
                          })}
                        </div>
                      )}
                    </div>
                  );
                })}
              </div>
            )}
          </div>
        )}

        {/* Discover */}
        <div>
          <SidebarSectionHeader show={showLabels}>Discover</SidebarSectionHeader>
          <div className="space-y-0.5">
            <ViewTransitionLink
              to={buildQueryCatalogHref()}
              onClick={onNavigate}
              className={navLinkClassForState(isCatalogSourceActive("query"))}
            >
              {isCatalogSourceActive("query") && (
                <span
                  className="absolute top-1/2 left-0 h-[18px] w-[3px] -translate-y-1/2 rounded-r-sm"
                  style={{ background: "var(--primary)" }}
                />
              )}
              <Search className="h-[18px] w-[18px] shrink-0" />
              <SidebarLabel show={showLabels}>Search</SidebarLabel>
            </ViewTransitionLink>
            <ViewTransitionLink
              to="/recommendations"
              onClick={onNavigate}
              className={navLinkClass("/recommendations")}
            >
              {isActive("/recommendations") && (
                <span
                  className="absolute top-1/2 left-0 h-[18px] w-[3px] -translate-y-1/2 rounded-r-sm"
                  style={{ background: "var(--primary)" }}
                />
              )}
              <Sparkles className="h-[18px] w-[18px] shrink-0" />
              <SidebarLabel show={showLabels}>Recommendations</SidebarLabel>
            </ViewTransitionLink>
          </div>
        </div>

        {/* Your Stuff */}
        <div>
          <SidebarSectionHeader show={showLabels}>Your Stuff</SidebarSectionHeader>
          <div className="space-y-0.5">
            <ViewTransitionLink
              to={buildPersonalCatalogHref("favorites")}
              onClick={onNavigate}
              className={navLinkClassForState(isCatalogSourceActive("favorites"))}
            >
              {isCatalogSourceActive("favorites") && (
                <span
                  className="absolute top-1/2 left-0 h-[18px] w-[3px] -translate-y-1/2 rounded-r-sm"
                  style={{ background: "var(--primary)" }}
                />
              )}
              <Heart className="h-[18px] w-[18px] shrink-0" />
              <SidebarLabel show={showLabels}>Favorites</SidebarLabel>
            </ViewTransitionLink>
            <ViewTransitionLink
              to={buildPersonalCatalogHref("watchlist")}
              onClick={onNavigate}
              className={navLinkClassForState(isCatalogSourceActive("watchlist"))}
            >
              {isCatalogSourceActive("watchlist") && (
                <span
                  className="absolute top-1/2 left-0 h-[18px] w-[3px] -translate-y-1/2 rounded-r-sm"
                  style={{ background: "var(--primary)" }}
                />
              )}
              <List className="h-[18px] w-[18px] shrink-0" />
              <SidebarLabel show={showLabels}>Watchlist</SidebarLabel>
            </ViewTransitionLink>
            <ViewTransitionLink
              to="/collections"
              onClick={onNavigate}
              className={navLinkClass("/collections")}
            >
              {isActive("/collections") && (
                <span
                  className="absolute top-1/2 left-0 h-[18px] w-[3px] -translate-y-1/2 rounded-r-sm"
                  style={{ background: "var(--primary)" }}
                />
              )}
              <FolderOpen className="h-[18px] w-[18px] shrink-0" />
              <SidebarLabel show={showLabels}>Collections</SidebarLabel>
            </ViewTransitionLink>
            <ViewTransitionLink
              to={buildPersonalCatalogHref("history")}
              onClick={onNavigate}
              className={navLinkClassForState(isCatalogSourceActive("history"))}
            >
              {isCatalogSourceActive("history") && (
                <span
                  className="absolute top-1/2 left-0 h-[18px] w-[3px] -translate-y-1/2 rounded-r-sm"
                  style={{ background: "var(--primary)" }}
                />
              )}
              <Clock className="h-[18px] w-[18px] shrink-0" />
              <SidebarLabel show={showLabels}>History</SidebarLabel>
            </ViewTransitionLink>
          </div>
        </div>
      </nav>

      {/* Footer */}
      <div className="border-sidebar-border/70 space-y-2 border-t px-3 py-3">
        <ViewTransitionLink
          to="/settings/playback"
          onClick={onNavigate}
          className={navLinkClass("/settings")}
        >
          <Settings className="h-[18px] w-[18px] shrink-0" />
          <SidebarLabel show={showLabels}>Settings</SidebarLabel>
        </ViewTransitionLink>
        {isAdmin && (
          <Link to="/admin" onClick={onNavigate} className={navLinkClass("/admin")}>
            <Shield className="h-[18px] w-[18px] shrink-0" />
            <SidebarLabel show={showLabels}>Admin</SidebarLabel>
          </Link>
        )}

        {/* User profile dropdown */}
        <DropdownMenu
          onOpenChange={(open) => {
            if (hoverTimerRef.current) clearTimeout(hoverTimerRef.current);
            hoverTimerRef.current = null;
            setProfileMenuOpen(open);
            if (!open && !collapsed) return;
            setHovered(open);
          }}
        >
          <DropdownMenuTrigger asChild>
            <button
              className={`hover:bg-sidebar-accent/70 flex items-center rounded-xl py-3 transition-colors duration-150 ${
                showLabels ? "w-full gap-2.5 px-3" : "mx-auto h-10 w-10 justify-center px-0"
              }`}
            >
              <Avatar className="h-7 w-7 shrink-0">
                <AvatarFallback className="bg-primary text-primary-foreground text-xs font-bold shadow-[0_14px_32px_-20px_rgba(0,0,0,0.8)]">
                  {profile?.name?.charAt(0).toUpperCase() ??
                    user?.username?.charAt(0).toUpperCase() ??
                    "?"}
                </AvatarFallback>
              </Avatar>
              <span className={`text-sidebar-foreground truncate text-[13px] font-medium transition-[opacity,max-width] duration-200 ease-out ${
                showLabels ? "max-w-[180px] opacity-100 delay-[50ms]" : "max-w-0 overflow-hidden opacity-0"
              }`}>
                {profile?.name ?? user?.username ?? "User"}
              </span>
            </button>
          </DropdownMenuTrigger>
          <DropdownMenuContent
            side={getProfileMenuSide(collapsed)}
            align="start"
            className="w-[min(24rem,calc(100vw-1.5rem))] rounded-2xl p-2"
          >
            {profile && (
              <>
                <DropdownMenuLabel className="flex items-center gap-2">
                  <Avatar className="h-5 w-5">
                    <AvatarFallback className="bg-primary text-primary-foreground text-[10px]">
                      {profile.name.charAt(0).toUpperCase()}
                    </AvatarFallback>
                  </Avatar>
                  {profile.name}
                </DropdownMenuLabel>
                <DropdownMenuSeparator />
              </>
            )}

            <DropdownMenuLabel className="text-muted-foreground px-2.5 pt-1 text-[11px] font-normal uppercase tracking-[0.14em]">
              Theme
            </DropdownMenuLabel>
            <ThemeSwitcher />

            <DropdownMenuSeparator />

            {profile && (
              <DropdownMenuItem
                onClick={() => {
                  clearProfile();
                  navigate("/profiles");
                }}
              >
                <UserCircle /> Switch Profile
              </DropdownMenuItem>
            )}
            <DropdownMenuItem onClick={logout}>
              <LogOut /> Logout ({user?.username})
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </div>
    </aside>
  );
}
