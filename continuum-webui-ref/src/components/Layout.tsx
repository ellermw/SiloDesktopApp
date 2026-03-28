import { useState } from "react";
import { Link, useLocation } from "react-router";
import { Menu, Search } from "lucide-react";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { useAuth } from "@/hooks/useAuth";
import { useCurrentProfile } from "@/hooks/useCurrentProfile";
import AppSidebar from "@/components/AppSidebar";
import ViewTransitionLink from "@/components/ViewTransitionLink";
import {
  buildQueryCatalogHref,
  parseCatalogSearchParams,
} from "@/pages/catalogSearchParams";
import type { ReactNode } from "react";

interface LayoutProps {
  children: ReactNode;
}

export default function Layout({ children }: LayoutProps) {
  const location = useLocation();
  const [mobileOpen, setMobileOpen] = useState(false);
  const { user } = useAuth();
  const { profile } = useCurrentProfile();

  const isHomePath = location.pathname === "/";
  const isLibraryRoute = location.pathname.startsWith("/library/");
  const isItemRoute = location.pathname.startsWith("/item/");
  const isSearchLandingRoute =
    location.pathname === "/catalog" &&
    (() => {
      const state = parseCatalogSearchParams(new URLSearchParams(location.search));
      return state.source === "query" && !state.q;
    })();
  const needsNoPadding = isHomePath || isLibraryRoute || isItemRoute || isSearchLandingRoute;

  // Immersion: auto-collapse sidebar on detail pages so content gets full stage
  const isDetailImmersion = isItemRoute;

  return (
    <div className="bg-background relative min-h-[100dvh] overflow-x-hidden">
      <div className="pointer-events-none fixed inset-x-0 top-0 z-0 h-40 bg-gradient-to-b from-primary/8 to-transparent blur-3xl" />
      {/* Desktop sidebar — hidden below lg */}
      <div className="hidden lg:block">
        <AppSidebar collapsed={isDetailImmersion} />
      </div>

      {/* Mobile header — visible below lg */}
      <div className="glass-dark border-border/70 sticky top-0 z-30 mx-3 mt-3 flex items-center justify-between rounded-2xl border px-4 py-3 lg:hidden">
        <div className="flex items-center gap-3">
          <button
            onClick={() => setMobileOpen(true)}
            className="text-muted-foreground hover:text-foreground hover:bg-accent/60 active:scale-[0.98] flex h-10 w-10 items-center justify-center rounded-xl transition-all"
            aria-label="Open menu"
          >
            <Menu className="h-5 w-5" />
          </button>
          <ViewTransitionLink to="/" className="flex items-center gap-2.5">
            <div className="text-primary border-border/70 bg-surface/90 flex h-9 w-9 items-center justify-center rounded-xl border text-sm font-bold shadow-[0_14px_30px_-20px_rgba(0,0,0,0.6)]">
              ▶
            </div>
            <span className="text-foreground text-[15px] font-extrabold tracking-[0.02em]">
              Continuum
            </span>
          </ViewTransitionLink>
        </div>
        <div className="flex items-center gap-2">
          <ViewTransitionLink
            to={buildQueryCatalogHref()}
            className="text-muted-foreground hover:text-foreground hover:bg-accent/60 active:scale-[0.98] flex h-10 w-10 items-center justify-center rounded-xl transition-all"
          >
            <Search className="h-5 w-5" />
          </ViewTransitionLink>
          <Link
            to="/settings/playback"
            className="bg-primary text-primary-foreground flex h-9 w-9 items-center justify-center rounded-xl text-xs font-bold shadow-[0_16px_32px_-22px_rgba(0,0,0,0.7)]"
          >
            {profile?.name?.charAt(0).toUpperCase() ??
              user?.username?.charAt(0).toUpperCase() ??
              "?"}
          </Link>
        </div>
      </div>

      {/* Mobile sidebar drawer */}
      <Sheet open={mobileOpen} onOpenChange={setMobileOpen}>
        <SheetContent side="left" className="w-[280px] p-0 sm:max-w-[280px]">
          <SheetHeader className="sr-only">
            <SheetTitle>Navigation</SheetTitle>
          </SheetHeader>
          <AppSidebar onNavigate={() => setMobileOpen(false)} />
        </SheetContent>
      </Sheet>

      {/* Main content — offset by sidebar width on desktop */}
      <main
        className={`relative min-h-screen main-transition ${isDetailImmersion ? "lg:ml-16" : "lg:ml-[260px]"}`}
        style={{ viewTransitionName: "main-content" }}
      >
        {needsNoPadding ? children : (
          <div className="relative z-10 px-4 py-4 sm:px-6 lg:px-10 lg:py-8 xl:px-12">{children}</div>
        )}
      </main>
    </div>
  );
}
