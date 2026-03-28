import { useEffect, useRef } from "react";
import {
  BrowserRouter,
  Routes,
  Route,
  Navigate,
  useLocation,
  useNavigate,
  useParams,
  useSearchParams,
} from "react-router";
import { QueryClientProvider, useQueryClient } from "@tanstack/react-query";
import { queryClient } from "@/lib/query-client";
import { AuthProvider, useAuth } from "@/hooks/useAuth";
import { ThemeProvider } from "@/hooks/useTheme";
import { ErrorBoundary } from "@/components/ErrorBoundary";
import ImpersonationBanner from "@/components/ImpersonationBanner";
import { loadStoredImpersonationAdminSession } from "@/lib/impersonationSession";
import { Toaster } from "@/components/ui/sonner";
import Layout from "@/components/Layout";
import AdminLayout from "@/components/AdminLayout";
import Home from "@/pages/Home";
import Login from "@/pages/Login";
import SetupWizard from "@/pages/SetupWizard";
import Profiles from "@/pages/Profiles";
import Catalog from "@/pages/Catalog";
import LibraryPage from "@/pages/LibraryPage";
import ItemDetail from "@/pages/ItemDetail/index";
import Collections from "@/pages/Collections";
import CollectionEditor from "@/pages/CollectionEditor";
import AdminDashboard from "@/pages/AdminDashboard";
import AdminActivity from "@/pages/AdminActivity";
import AdminLogs from "@/pages/AdminLogs";
import AdminUsers from "@/pages/AdminUsers";
import AdminLibraries from "@/pages/AdminLibraries";
import AdminSettingsLayout from "@/pages/admin-settings/AdminSettingsLayout";
import AdminNodes from "@/pages/AdminNodes";
import AdminSections from "@/pages/AdminSections";
import AdminCollections from "@/pages/AdminCollections";
import AdminCollectionEditor from "@/pages/AdminCollectionEditor";
import AdminPlaybackHistory from "@/pages/AdminPlaybackHistory";
import AdminMaintenance from "@/pages/AdminMaintenance";
import AdminApiKeys from "@/pages/AdminApiKeys";
import AdminUserDetail from "@/pages/AdminUserDetail";
import AdminTasks from "@/pages/AdminTasks";
import AdminTaskDetail from "@/pages/AdminTaskDetail";
import AdminRecommendations from "@/pages/AdminRecommendations";
import Recommendations from "@/pages/Recommendations";
import Signup from "@/pages/Signup";
import SettingsLayout from "@/pages/SettingsLayout";
import AppearanceSettings from "@/pages/settings/AppearanceSettings";
import AccessibilitySettings from "@/pages/settings/AccessibilitySettings";
import PlaybackSettings from "@/pages/settings/PlaybackSettings";
import LibrarySettings from "@/pages/settings/LibrarySettings";
import HistoryImportSettings from "@/pages/settings/HistoryImportSettings";
import SubtitleAppearanceSettings from "@/pages/settings/SubtitleAppearanceSettings";
import HomeScreenSettings from "@/pages/settings/HomeScreenSettings";
import WatchRoute from "@/pages/WatchRoute";
import type { ReactNode } from "react";
import {
  buildLegacyBrowseCatalogHref,
  buildPersonalCatalogHref,
  buildPersonCatalogHref,
  buildQueryCatalogHref,
  buildUserCollectionCatalogHref,
} from "@/pages/catalogSearchParams";
import { toast } from "sonner";

function RequireAuth({ children }: { children: ReactNode }) {
  const { user, loading, setupLoading } = useAuth();
  if (loading || setupLoading) return <div className="p-8">Loading...</div>;
  if (!user) return <Navigate to="/login" replace />;
  return <>{children}</>;
}

function SetupGate({ children }: { children: ReactNode }) {
  const { user, setupLoading, setupRequired } = useAuth();
  if (setupLoading) return <div className="p-8">Loading...</div>;
  if (setupRequired && !user) return <Navigate to="/setup" replace />;
  return <>{children}</>;
}

function RequireProfile({ children }: { children: ReactNode }) {
  const { profile } = useAuth();
  if (!profile) return <Navigate to="/profiles" replace />;
  return <>{children}</>;
}

function RequireAdmin({ children }: { children: ReactNode }) {
  const { user } = useAuth();
  if (user?.role !== "admin") return <Navigate to="/" replace />;
  return <>{children}</>;
}

/** Clears user-scoped query caches on profile switch or logout. */
function QueryCacheManager() {
  const { user, profile } = useAuth();
  const qc = useQueryClient();
  const prevProfileId = useRef(profile?.id);

  useEffect(() => {
    if (!user) {
      qc.clear();
      prevProfileId.current = undefined;
      return;
    }
    if (prevProfileId.current && prevProfileId.current !== profile?.id) {
      qc.removeQueries({ queryKey: ["favorites"] });
      qc.removeQueries({ queryKey: ["watchlist"] });
      qc.removeQueries({ queryKey: ["history"] });
      qc.removeQueries({ queryKey: ["collections"] });
      qc.removeQueries({ queryKey: ["libraryPlaybackPreferences"] });
      qc.removeQueries({ queryKey: ["progress"] });
      qc.removeQueries({ queryKey: ["sections"] });
    }
    prevProfileId.current = profile?.id;
  }, [user, profile?.id, qc]);

  return null;
}

function AppChrome() {
  const { user, isImpersonating, endImpersonation } = useAuth();
  const navigate = useNavigate();

  if (!user?.impersonation || !isImpersonating) {
    return null;
  }

  async function handleEndImpersonation() {
    const returnPath = loadStoredImpersonationAdminSession()?.returnPath ?? "/admin/users";

    try {
      await endImpersonation();
      navigate(returnPath, { replace: true });
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "Failed to end impersonation");
    }
  }

  return (
    <ImpersonationBanner
      userName={user.username}
      impersonatorName={user.impersonation.impersonator_username}
      onEnd={handleEndImpersonation}
    />
  );
}

function LegacySearchRedirect() {
  const [searchParams] = useSearchParams();
  return <Navigate to={buildQueryCatalogHref(searchParams.get("q") ?? undefined)} replace />;
}

function LegacyBrowseRedirect() {
  const [searchParams] = useSearchParams();
  const href = buildLegacyBrowseCatalogHref(searchParams);

  if (!href) {
    return <Navigate to={buildQueryCatalogHref()} replace />;
  }

  return <Navigate to={href} replace />;
}

function LegacyPersonalCatalogRedirect({
  source,
}: {
  source: "favorites" | "watchlist" | "history";
}) {
  return <Navigate to={buildPersonalCatalogHref(source)} replace />;
}

function LegacyUserCollectionRedirect() {
  const { id } = useParams<{ id: string }>();
  const location = useLocation();

  if (!id) {
    return <Navigate to="/collections" replace />;
  }

  return (
    <Navigate
      to={buildUserCollectionCatalogHref(id, new URLSearchParams(location.search).get("title") ?? undefined)}
      replace
    />
  );
}

function LegacyPersonCatalogRedirect() {
  const { id } = useParams<{ id: string }>();

  if (!id) {
    return <Navigate to={buildQueryCatalogHref()} replace />;
  }

  return <Navigate to={buildPersonCatalogHref(id)} replace />;
}

function AppRoutes() {
  return (
    <Routes>
      <Route path="/login" element={<Login />} />
      <Route path="/setup" element={<SetupWizard />} />
      <Route path="/signup" element={<Signup />} />
      <Route
        path="/*"
        element={
          <SetupGate>
            <RequireAuth>
              <Routes>
                <Route path="/profiles" element={<Profiles />} />
                <Route
                  path="/watch/:id"
                  element={
                    <RequireProfile>
                      <WatchRoute />
                    </RequireProfile>
                  }
                />
                {/* Admin area — own layout, no profile required */}
                <Route
                  path="/admin/*"
                  element={
                    <RequireAdmin>
                      <AdminLayout />
                    </RequireAdmin>
                  }
                >
                  <Route index element={<AdminDashboard />} />
                  <Route path="activity" element={<AdminActivity />} />
                  <Route path="logs" element={<AdminLogs />} />
                  <Route path="libraries" element={<AdminLibraries />} />
                  <Route path="maintenance" element={<AdminMaintenance />} />
                  <Route path="collections" element={<AdminCollections />} />
                  <Route path="collections/new" element={<AdminCollectionEditor />} />
                  <Route path="collections/:id/edit" element={<AdminCollectionEditor />} />
                  <Route path="history" element={<AdminPlaybackHistory />} />
                  <Route path="users" element={<AdminUsers />} />
                  <Route path="users/:id" element={<AdminUserDetail />} />
                  <Route path="nodes" element={<AdminNodes />} />
                  <Route path="sections" element={<AdminSections />} />
                  <Route path="settings" element={<AdminSettingsLayout />} />
                  <Route path="recommendations" element={<AdminRecommendations />} />
                  <Route path="api-keys" element={<AdminApiKeys />} />
                  <Route path="tasks" element={<AdminTasks />} />
                  <Route path="tasks/:key" element={<AdminTaskDetail />} />
                  <Route path="stats" element={<Navigate to="/admin" replace />} />
                  <Route path="*" element={<Navigate to="/admin" replace />} />
                </Route>
                {/* Settings area — own layout, requires profile */}
                <Route
                  path="/settings/*"
                  element={
                    <RequireProfile>
                      <Layout>
                        <SettingsLayout />
                      </Layout>
                    </RequireProfile>
                  }
                >
                  <Route index element={<Navigate to="appearance" replace />} />
                  <Route path="appearance" element={<AppearanceSettings />} />
                  <Route path="accessibility" element={<AccessibilitySettings />} />
                  <Route path="playback" element={<PlaybackSettings />} />
                  <Route path="libraries" element={<LibrarySettings />} />
                  <Route path="history-import" element={<HistoryImportSettings />} />
                  <Route path="subtitle-appearance" element={<SubtitleAppearanceSettings />} />
                  <Route path="home-screen" element={<HomeScreenSettings />} />
                  <Route path="*" element={<Navigate to="appearance" replace />} />
                </Route>
                <Route
                  path="/*"
                  element={
                    <RequireProfile>
                      <Layout>
                        <Routes>
                          <Route path="/" element={<Home />} />
                          <Route path="/catalog" element={<Catalog />} />
                          <Route path="/library/:libraryId" element={<LibraryPage />} />
                          <Route path="/search" element={<LegacySearchRedirect />} />
                          <Route path="/browse" element={<LegacyBrowseRedirect />} />
                          <Route path="/item/:id" element={<ItemDetail />} />
                          <Route path="/person/:id" element={<LegacyPersonCatalogRedirect />} />
                          <Route
                            path="/favorites"
                            element={<LegacyPersonalCatalogRedirect source="favorites" />}
                          />
                          <Route
                            path="/watchlist"
                            element={<LegacyPersonalCatalogRedirect source="watchlist" />}
                          />
                          <Route
                            path="/history"
                            element={<LegacyPersonalCatalogRedirect source="history" />}
                          />
                          <Route path="/collections" element={<Collections />} />
                          <Route path="/collections/new" element={<CollectionEditor />} />
                          <Route path="/collections/:id/edit" element={<CollectionEditor />} />
                          <Route path="/collections/:id" element={<LegacyUserCollectionRedirect />} />
                          <Route path="/recommendations" element={<Recommendations />} />
                          <Route path="*" element={<Navigate to="/" replace />} />
                        </Routes>
                      </Layout>
                    </RequireProfile>
                  }
                />
              </Routes>
            </RequireAuth>
          </SetupGate>
        }
      />
    </Routes>
  );
}

export default function App() {
  return (
    <BrowserRouter>
      <AuthProvider>
        <QueryClientProvider client={queryClient}>
          <ErrorBoundary>
            <ThemeProvider>
              <QueryCacheManager />
              <AppChrome />
              <AppRoutes />
              <Toaster theme="dark" />
            </ThemeProvider>
          </ErrorBoundary>
        </QueryClientProvider>
      </AuthProvider>
    </BrowserRouter>
  );
}
