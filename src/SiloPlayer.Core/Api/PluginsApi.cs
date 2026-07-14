using SiloPlayer.Core.Models.Plugins;

namespace SiloPlayer.Core.Api;

public class PluginsApi(SiloApiClient client)
{
    private const int DefaultUploadChunkSize = 512 * 1024;
    private const int MinimumUploadChunkSize = 128 * 1024;
    // ===== Repositories =====

    public Task<List<PluginRepository>> GetRepositoriesAsync(CancellationToken ct = default)
        => client.GetAsync<List<PluginRepository>>("/api/v1/admin/plugins/repositories", ct);

    public Task<PluginRepository> CreateRepositoryAsync(CreatePluginRepositoryRequest request, CancellationToken ct = default)
        => client.PostAsync<PluginRepository>("/api/v1/admin/plugins/repositories", request, ct);

    public Task<PluginRepository> UpdateRepositoryAsync(int id, UpdatePluginRepositoryRequest request, CancellationToken ct = default)
        => client.PutAsync<PluginRepository>($"/api/v1/admin/plugins/repositories/{id}", request, ct);

    public Task DeleteRepositoryAsync(int id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/admin/plugins/repositories/{id}", ct);

    public Task RefreshRepositoryAsync(int id, CancellationToken ct = default)
        => client.PostNoContentAsync($"/api/v1/admin/plugins/repositories/{id}/refresh", new { }, ct);

    // ===== Catalog =====

    public Task<List<PluginCatalogEntry>> GetCatalogAsync(CancellationToken ct = default)
        => client.GetAsync<List<PluginCatalogEntry>>("/api/v1/admin/plugins/catalog", ct);

    public Task<PluginCatalogSettings> GetCatalogSettingsAsync(CancellationToken ct = default)
        => client.GetAsync<PluginCatalogSettings>("/api/v1/admin/plugins/catalog-settings", ct);

    public Task<PluginCatalogSettings> UpdateCatalogSettingsAsync(bool includeApprovedCommunityPlugins,
        CancellationToken ct = default)
        => client.PutAsync<PluginCatalogSettings>("/api/v1/admin/plugins/catalog-settings",
            new Dictionary<string, object?>
            {
                ["include_approved_community_plugins"] = includeApprovedCommunityPlugins,
            }, ct);

    // ===== Installations =====

    public Task<List<PluginInstallation>> GetInstallationsAsync(CancellationToken ct = default)
        => client.GetAsync<List<PluginInstallation>>("/api/v1/admin/plugins/installations", ct);

    public Task<PluginInstallation> InstallPluginAsync(InstallPluginRequest request, CancellationToken ct = default)
        => client.PostAsync<PluginInstallation>("/api/v1/admin/plugins/installations", request, ct);

    public Task<PluginInstallation> UpdateInstallationAsync(int id, UpdatePluginInstallationRequest request, CancellationToken ct = default)
        => client.PutAsync<PluginInstallation>($"/api/v1/admin/plugins/installations/{id}", request, ct);

    public Task DeleteInstallationAsync(int id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/admin/plugins/installations/{id}", ct);

    public Task UpdatePluginAsync(int id, CancellationToken ct = default)
        => client.PostNoContentAsync($"/api/v1/admin/plugins/installations/{id}/update", new { }, ct);

    public Task<PluginInstallation> UploadPluginAsync(
        string fileName,
        byte[] fileBytes,
        string contentType = "application/octet-stream",
        IProgress<int>? progress = null,
        CancellationToken ct = default)
        => fileBytes.Length > DefaultUploadChunkSize
            ? UploadPluginInChunksAsync(fileName, fileBytes, progress, ct)
            : client.PostMultipartAsync<PluginInstallation>(
                "/api/v1/admin/plugins/uploads",
                new Dictionary<string, string?>(),
                "archive",
                fileName,
                fileBytes,
                contentType,
                ct);

    private async Task<PluginInstallation> UploadPluginInChunksAsync(
        string fileName,
        byte[] fileBytes,
        IProgress<int>? progress,
        CancellationToken ct)
    {
        var chunkSize = DefaultUploadChunkSize;
        while (true)
        {
            string? uploadId = null;
            try
            {
                var session = await client.PostAsync<PluginChunkedUploadSession>(
                    "/api/v1/admin/plugins/uploads/chunked",
                    new { filename = fileName, size_bytes = fileBytes.LongLength, chunk_size = chunkSize },
                    ct);
                uploadId = session.UploadId;
                ReportPluginUploadProgress(session, fileBytes.LongLength, progress);

                for (var index = session.ReceivedChunks; index < session.TotalChunks; index++)
                {
                    var start = checked(index * session.ChunkSize);
                    var length = Math.Min(session.ChunkSize, fileBytes.Length - start);
                    session = await client.PutBytesAsync<PluginChunkedUploadSession>(
                        $"/api/v1/admin/plugins/uploads/chunked/{Uri.EscapeDataString(session.UploadId)}/chunks/{index}",
                        fileBytes.AsMemory(start, length),
                        ct: ct);
                    ReportPluginUploadProgress(session, fileBytes.LongLength, progress);
                }

                return await client.PostAsync<PluginInstallation>(
                    $"/api/v1/admin/plugins/uploads/chunked/{Uri.EscapeDataString(session.UploadId)}/complete",
                    new { },
                    ct);
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrWhiteSpace(uploadId))
                {
                    try
                    {
                        await client.DeleteAsync(
                            $"/api/v1/admin/plugins/uploads/chunked/{Uri.EscapeDataString(uploadId)}",
                            CancellationToken.None);
                    }
                    catch { }
                }

                if (ex is not ApiException { StatusCode: 413 } || chunkSize <= MinimumUploadChunkSize)
                    throw;
                chunkSize = Math.Max(MinimumUploadChunkSize, chunkSize / 2);
            }
        }
    }

    private static void ReportPluginUploadProgress(PluginChunkedUploadSession session, long fallbackTotal,
        IProgress<int>? progress)
    {
        if (progress is null) return;
        var total = session.SizeBytes > 0 ? session.SizeBytes : fallbackTotal;
        var uploaded = Math.Min(session.ReceivedBytes, total);
        progress.Report(total > 0 ? Math.Clamp((int)Math.Round(uploaded * 100d / total), 0, 100) : 0);
    }

    // ===== Config =====

    public Task SaveGlobalConfigAsync(int installationId, SavePluginConfigRequest request, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v1/admin/plugins/installations/{installationId}/config", request, ct);

    // ===== Auth Bindings =====

    public Task SaveAuthBindingAsync(int installationId, SavePluginAuthBindingRequest request, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v1/admin/plugins/installations/{installationId}/auth-bindings/{Uri.EscapeDataString(request.CapabilityId)}", request, ct);

    // ===== Task Bindings =====

    public Task<PluginTaskBindingUpdateResponse> SaveTaskBindingAsync(int installationId, string capabilityId, SavePluginTaskBindingRequest request, CancellationToken ct = default)
        => client.PutAsync<PluginTaskBindingUpdateResponse>($"/api/v1/admin/plugins/installations/{installationId}/task-bindings/{Uri.EscapeDataString(capabilityId)}", request, ct);

    // NOTE: Analyzer bindings endpoint removed — does not exist in server router.
    // The SaveAnalyzerBindingsAsync method was calling a non-existent endpoint.

    // ===== Legacy Metadata Import =====

    public Task<PluginLegacyMetadataImportResponse> ImportLegacyMetadataAsync(int installationId, string importType, CancellationToken ct = default)
        => client.PostAsync<PluginLegacyMetadataImportResponse>($"/api/v1/admin/plugins/installations/{installationId}/legacy-metadata-import", new { import_type = importType }, ct);
}

public class PluginChunkedUploadSession
{
    public string UploadId { get; set; } = "";
    public string Filename { get; set; } = "";
    public long SizeBytes { get; set; }
    public int ChunkSize { get; set; }
    public int TotalChunks { get; set; }
    public int ReceivedChunks { get; set; }
    public long ReceivedBytes { get; set; }
    public bool Complete { get; set; }
    public string ExpiresAt { get; set; } = "";
}
