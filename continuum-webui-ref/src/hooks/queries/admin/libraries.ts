import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { api, getAccessToken } from "@/api/client";
import type {
  AdminJob,
  AdminJobsResponse,
  ApiError,
  CatalogSeedExportRequest,
  CatalogSeedImportRequest,
  CatalogSeedImportResponse,
  CatalogSeedImportSourcesResponse,
  CatalogSeedImportSource,
  CreateLibraryRequest,
  Library,
  LibraryMountCheckResponse,
  LibrarySkippedRoot,
  LibraryProviderChainEntry,
  ScanResponse,
  SetLibraryChainRequest,
} from "@/api/types";
import { adminKeys } from "../keys";
import { toast } from "sonner";

const ADMIN_STALE_TIME = 30_000;

class CatalogSeedRequestError extends Error {
  unmatchedRoots?: string[];
  activeJobId?: string;
  activeJob?: AdminJob;

  constructor(
    message: string,
    unmatchedRoots?: string[],
    activeJobId?: string,
    activeJob?: AdminJob,
  ) {
    super(message);
    this.name = "CatalogSeedRequestError";
    this.unmatchedRoots = unmatchedRoots;
    this.activeJobId = activeJobId;
    this.activeJob = activeJob;
  }
}

function buildAdminHeaders() {
  const headers: Record<string, string> = {};
  const token = getAccessToken();
  if (token) {
    headers.Authorization = `Bearer ${token}`;
  }
  return headers;
}

async function parseCatalogSeedError(res: Response): Promise<never> {
  let apiErr: ApiError = { error: "unknown", message: res.statusText };
  try {
    apiErr = (await res.json()) as ApiError;
  } catch {
    // Ignore JSON parse failures for non-JSON error bodies.
  }
  throw new CatalogSeedRequestError(
    apiErr.message || "Catalog seed request failed",
    apiErr.unmatched_roots,
    apiErr.active_job_id,
    apiErr.active_job,
  );
}

async function createCatalogExportJob(body?: CatalogSeedExportRequest): Promise<AdminJob> {
  const res = await fetch("/api/v1/admin/catalog/export-jobs", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      ...buildAdminHeaders(),
    },
    body: JSON.stringify(body ?? {}),
  });

  if (!res.ok) {
    await parseCatalogSeedError(res);
  }

  return (await res.json()) as AdminJob;
}

async function createCatalogImportJob(body: CatalogSeedImportRequest): Promise<AdminJob> {
  const form = new FormData();
  if (body.source === "local_path" && body.local_path) {
    form.append("local_path", body.local_path);
  }
  if (body.source === "export_job" && body.export_job_id) {
    form.append("export_job_id", body.export_job_id);
  }
  if (body.source === "bucket_artifact" && body.artifact_key) {
    form.append("artifact_key", body.artifact_key);
  }
  if (body.source === "remote_url" && body.remote_url) {
    form.append("remote_url", body.remote_url);
  }
  form.append("conflict_mode", body.conflict_mode);
  form.append("path_rewrites", JSON.stringify(body.path_rewrites));

  const res = await fetch("/api/v1/admin/catalog/import-jobs", {
    method: "POST",
    headers: buildAdminHeaders(),
    body: form,
  });

  if (!res.ok) {
    await parseCatalogSeedError(res);
  }

  return (await res.json()) as AdminJob;
}

async function importCatalogSeed(
  body: CatalogSeedImportRequest,
): Promise<CatalogSeedImportResponse> {
  const form = new FormData();
  if (body.source === "local_path" && body.local_path) {
    form.append("local_path", body.local_path);
  }
  if (body.source === "export_job" && body.export_job_id) {
    form.append("export_job_id", body.export_job_id);
  }
  if (body.source === "bucket_artifact" && body.artifact_key) {
    form.append("artifact_key", body.artifact_key);
  }
  if (body.source === "remote_url" && body.remote_url) {
    form.append("remote_url", body.remote_url);
  }
  form.append("conflict_mode", body.conflict_mode);
  form.append("path_rewrites", JSON.stringify(body.path_rewrites));

  const res = await fetch("/api/v1/admin/catalog/import", {
    method: "POST",
    headers: buildAdminHeaders(),
    body: form,
  });

  if (!res.ok) {
    await parseCatalogSeedError(res);
  }

  return (await res.json()) as CatalogSeedImportResponse;
}

async function listCatalogImportSources(): Promise<CatalogSeedImportSource[]> {
  return api<CatalogSeedImportSourcesResponse>("/admin/catalog/import-sources").then(
    (data) => data.sources ?? [],
  );
}

async function listLocalImportSources(): Promise<CatalogSeedImportSource[]> {
  return api<CatalogSeedImportSourcesResponse>("/admin/catalog/local-import-sources").then(
    (data) => data.sources ?? [],
  );
}

async function publishCatalogExportJob(id: string): Promise<AdminJob> {
  const res = await fetch(`/api/v1/admin/catalog/export-jobs/${encodeURIComponent(id)}/publish`, {
    method: "POST",
    headers: buildAdminHeaders(),
  });

  if (!res.ok) {
    await parseCatalogSeedError(res);
  }

  return (await res.json()) as AdminJob;
}

export function useAdminLibraries() {
  return useQuery({
    queryKey: adminKeys.libraries(),
    queryFn: () => api<Library[]>("/libraries").then((d) => d ?? []),
    staleTime: ADMIN_STALE_TIME,
  });
}

export function useSkippedLibraryRoots() {
  return useQuery({
    queryKey: adminKeys.librarySkippedRoots(),
    queryFn: () => api<LibrarySkippedRoot[]>("/libraries/skipped-roots").then((d) => d ?? []),
    staleTime: ADMIN_STALE_TIME,
  });
}

export function useCreateLibrary() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateLibraryRequest) =>
      api("/libraries", {
        method: "POST",
        body: JSON.stringify(body),
      }),
    onSuccess: () => {
      toast.success("Library created");
      queryClient.invalidateQueries({ queryKey: adminKeys.libraries() });
    },
    onError: (err) => {
      toast.error(err instanceof Error ? err.message : "Failed to save");
    },
  });
}

export function useUpdateLibrary() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: number; body: Partial<CreateLibraryRequest> }) =>
      api(`/libraries/${id}`, {
        method: "PUT",
        body: JSON.stringify(body),
      }),
    onSuccess: () => {
      toast.success("Library updated");
      queryClient.invalidateQueries({ queryKey: adminKeys.libraries() });
    },
    onError: (err) => {
      toast.error(err instanceof Error ? err.message : "Failed to save");
    },
  });
}

export function useDeleteLibrary() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => api<AdminJob>(`/libraries/${id}`, { method: "DELETE" }),
    onSuccess: () => {
      toast.success("Library deletion started");
      queryClient.invalidateQueries({ queryKey: adminKeys.libraries() });
      queryClient.invalidateQueries({ queryKey: adminKeys.jobs("delete_library") });
    },
    onError: (err) => {
      toast.error(err instanceof Error ? err.message : "Failed to delete");
    },
  });
}

export function useScanLibrary() {
  return useMutation({
    mutationFn: (id: number) =>
      api<ScanResponse>("/scan", {
        method: "POST",
        body: JSON.stringify({ library_id: id }),
      }),
    onSuccess: () => {
      toast.success("Full ingest scan started");
    },
    onError: (err) => {
      toast.error(err instanceof Error ? err.message : "Scan failed");
    },
  });
}

export function useCheckLibraryMount() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) =>
      api<LibraryMountCheckResponse>(`/libraries/${id}/check-mount`, { method: "POST" }),
    onSuccess: (data) => {
      toast.success(data.healthy ? "Mount check passed" : "Mount check found unreachable roots");
      queryClient.invalidateQueries({ queryKey: adminKeys.libraries() });
    },
    onError: (err) => {
      toast.error(err instanceof Error ? err.message : "Mount check failed");
    },
  });
}

export function useScanAllLibraries() {
  return useMutation({
    mutationFn: () =>
      api<{ status: string }>("/admin/tasks/scan_libraries/run", {
        method: "POST",
      }),
    onSuccess: () => {
      toast.success("Full ingest scan started for all libraries");
    },
    onError: (err) => {
      toast.error(err instanceof Error ? err.message : "Scan failed");
    },
  });
}

export function useLibraryProviders(libraryId: number | null) {
  return useQuery({
    queryKey: adminKeys.libraryProviders(libraryId ?? 0),
    queryFn: () =>
      api<LibraryProviderChainEntry[]>(`/libraries/${libraryId}/providers`).then((d) => d ?? []),
    enabled: libraryId !== null,
    staleTime: ADMIN_STALE_TIME,
  });
}

export function useSetLibraryProviders() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: number; body: SetLibraryChainRequest }) =>
      api(`/libraries/${id}/providers`, {
        method: "PUT",
        body: JSON.stringify(body),
      }),
    onSuccess: (_data, variables) => {
      toast.success("Provider chain updated");
      queryClient.invalidateQueries({
        queryKey: adminKeys.libraryProviders(variables.id),
      });
    },
    onError: (err) => {
      toast.error(err instanceof Error ? err.message : "Failed to update provider chain");
    },
  });
}

export function useUploadLibraryPoster() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, file }: { id: number; file: File }) => {
      const form = new FormData();
      form.append("poster", file);
      const res = await fetch(`/api/v1/libraries/${id}/poster`, {
        method: "PUT",
        headers: buildAdminHeaders(),
        body: form,
      });
      if (!res.ok) {
        const err = await res.json().catch(() => ({ message: res.statusText }));
        throw new Error(err.message || "Failed to upload poster");
      }
      return (await res.json()) as Library;
    },
    onSuccess: () => {
      toast.success("Library poster updated");
      queryClient.invalidateQueries({ queryKey: adminKeys.libraries() });
    },
    onError: (err) => {
      toast.error(err instanceof Error ? err.message : "Failed to upload poster");
    },
  });
}

export function useDeleteLibraryPoster() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => api(`/libraries/${id}/poster`, { method: "DELETE" }),
    onSuccess: () => {
      toast.success("Library poster removed");
      queryClient.invalidateQueries({ queryKey: adminKeys.libraries() });
    },
    onError: (err) => {
      toast.error(err instanceof Error ? err.message : "Failed to remove poster");
    },
  });
}

export function useRefreshLibraryMetadata() {
  return useMutation({
    mutationFn: (id: number) => api(`/libraries/${id}/refresh-metadata`, { method: "POST" }),
    onSuccess: () => {
      toast.success("Metadata refresh started");
    },
    onError: (err) => {
      toast.error(err instanceof Error ? err.message : "Refresh failed");
    },
  });
}

export function useConfirmEmptyRootCleanup() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) =>
      api(`/libraries/${id}/confirm-empty-root-cleanup`, { method: "POST" }),
    onSuccess: () => {
      toast.success("Deletion confirmed for the next empty-root scan");
      queryClient.invalidateQueries({ queryKey: adminKeys.libraries() });
    },
    onError: (err) => {
      toast.error(err instanceof Error ? err.message : "Failed to confirm cleanup");
    },
  });
}

export function useCatalogExportJobs(jobType = "catalog_export") {
  return useQuery({
    queryKey: adminKeys.jobs(jobType),
    queryFn: () =>
      api<AdminJobsResponse>(`/admin/jobs?job_type=${encodeURIComponent(jobType)}&limit=10`).then(
        (data) => data.jobs ?? [],
      ),
    staleTime: 0,
    refetchInterval: (query) => {
      const jobs = query.state.data as AdminJob[] | undefined;
      return jobs?.some((job) => job.status === "queued" || job.status === "running")
        ? 5_000
        : 30_000;
    },
  });
}

export function useCatalogImportJobs(jobType = "catalog_import") {
  return useQuery({
    queryKey: adminKeys.jobs(jobType),
    queryFn: () =>
      api<AdminJobsResponse>(`/admin/jobs?job_type=${encodeURIComponent(jobType)}&limit=10`).then(
        (data) => data.jobs ?? [],
      ),
    staleTime: 0,
    refetchInterval: (query) => {
      const jobs = query.state.data as AdminJob[] | undefined;
      return jobs?.some((job) => job.status === "queued" || job.status === "running")
        ? 5_000
        : 30_000;
    },
  });
}

export function useLibraryDeleteJobs(jobType = "delete_library") {
  return useQuery({
    queryKey: adminKeys.jobs(jobType),
    queryFn: () =>
      api<AdminJobsResponse>(`/admin/jobs?job_type=${encodeURIComponent(jobType)}&limit=20`).then(
        (data) => data.jobs ?? [],
      ),
    staleTime: 0,
    refetchInterval: (query) => {
      const jobs = query.state.data as AdminJob[] | undefined;
      return jobs?.some((job) => job.status === "queued" || job.status === "running")
        ? 2_000
        : 30_000;
    },
  });
}

export function useCatalogImportSources() {
  return useQuery({
    queryKey: adminKeys.catalogImportSources(),
    queryFn: listCatalogImportSources,
    staleTime: 0,
    refetchInterval: 30_000,
  });
}

export function useLocalImportSources() {
  return useQuery({
    queryKey: adminKeys.localImportSources(),
    queryFn: listLocalImportSources,
    staleTime: 0,
    refetchInterval: 30_000,
  });
}

export function useCreateCatalogExportJob() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body?: CatalogSeedExportRequest) => createCatalogExportJob(body),
    onSuccess: () => {
      toast.success("Catalog export queued");
      queryClient.invalidateQueries({ queryKey: adminKeys.jobs("catalog_export") });
    },
    onError: (err) => {
      if (err instanceof CatalogSeedRequestError && err.activeJobId) {
        toast.error(err.message);
        queryClient.invalidateQueries({ queryKey: adminKeys.jobs("catalog_export") });
        return;
      }
      toast.error(err instanceof Error ? err.message : "Failed to queue catalog export");
    },
  });
}

export function usePublishCatalogExportJob() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => publishCatalogExportJob(id),
    onSuccess: () => {
      toast.success("Catalog export published");
      queryClient.invalidateQueries({ queryKey: adminKeys.jobs("catalog_export") });
    },
    onError: (err) => {
      toast.error(err instanceof Error ? err.message : "Failed to publish catalog export");
    },
  });
}

export function useImportCatalogSeed() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: CatalogSeedImportRequest) => {
      try {
        const job = await createCatalogImportJob(body);
        return { mode: "job" as const, job };
      } catch (err) {
        if (
          err instanceof CatalogSeedRequestError &&
          body.source === "local_path" &&
          err.message === "Job repository is not configured"
        ) {
          const result = await importCatalogSeed(body);
          return { mode: "sync" as const, result };
        }
        throw err;
      }
    },
    onSuccess: (payload) => {
      if (payload.mode === "job") {
        toast.success("Catalog import queued");
        queryClient.invalidateQueries({ queryKey: adminKeys.jobs("catalog_import") });
        return;
      }
      toast.success(
        `Catalog imported: ${payload.result.items_created} items, ${payload.result.files_created} files`,
      );
      queryClient.invalidateQueries({ queryKey: adminKeys.libraries() });
    },
    onError: (err) => {
      if (err instanceof CatalogSeedRequestError && err.unmatchedRoots?.length) {
        toast.error(
          `${err.message}: ${err.unmatchedRoots.slice(0, 2).join(", ")}${err.unmatchedRoots.length > 2 ? "..." : ""}`,
        );
        return;
      }
      toast.error(err instanceof Error ? err.message : "Failed to import catalog seed");
    },
  });
}
