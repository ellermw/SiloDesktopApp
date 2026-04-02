using ContinuumPlayer.Core.Models.Plugins;

namespace ContinuumPlayer.Core.Api;

public class PluginsApi(ContinuumApiClient client)
{
    // ===== Repositories =====

    public Task<PluginRepositoriesResponse> GetRepositoriesAsync(CancellationToken ct = default)
        => client.GetAsync<PluginRepositoriesResponse>("/api/v1/admin/plugins/repositories", ct);

    public Task<PluginRepository> CreateRepositoryAsync(CreatePluginRepositoryRequest request, CancellationToken ct = default)
        => client.PostAsync<PluginRepository>("/api/v1/admin/plugins/repositories", request, ct);

    public Task<PluginRepository> UpdateRepositoryAsync(int id, UpdatePluginRepositoryRequest request, CancellationToken ct = default)
        => client.PutAsync<PluginRepository>($"/api/v1/admin/plugins/repositories/{id}", request, ct);

    public Task DeleteRepositoryAsync(int id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/admin/plugins/repositories/{id}", ct);

    public Task RefreshRepositoryAsync(int id, CancellationToken ct = default)
        => client.PostNoContentAsync($"/api/v1/admin/plugins/repositories/{id}/refresh", new { }, ct);

    // ===== Catalog =====

    public Task<PluginCatalogResponse> GetCatalogAsync(CancellationToken ct = default)
        => client.GetAsync<PluginCatalogResponse>("/api/v1/admin/plugins/catalog", ct);

    // ===== Installations =====

    public Task<PluginInstallationsResponse> GetInstallationsAsync(CancellationToken ct = default)
        => client.GetAsync<PluginInstallationsResponse>("/api/v1/admin/plugins/installations", ct);

    public Task<PluginInstallation> InstallPluginAsync(InstallPluginRequest request, CancellationToken ct = default)
        => client.PostAsync<PluginInstallation>("/api/v1/admin/plugins/installations", request, ct);

    public Task<PluginInstallation> UpdateInstallationAsync(int id, UpdatePluginInstallationRequest request, CancellationToken ct = default)
        => client.PutAsync<PluginInstallation>($"/api/v1/admin/plugins/installations/{id}", request, ct);

    public Task DeleteInstallationAsync(int id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/admin/plugins/installations/{id}", ct);

    public Task UpdatePluginAsync(int id, CancellationToken ct = default)
        => client.PostNoContentAsync($"/api/v1/admin/plugins/installations/{id}/update", new { }, ct);

    // ===== Config =====

    public Task SaveGlobalConfigAsync(int installationId, SavePluginConfigRequest request, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v1/admin/plugins/installations/{installationId}/config/{Uri.EscapeDataString(request.Key)}", request, ct);

    // ===== Auth Bindings =====

    public Task SaveAuthBindingAsync(int installationId, SavePluginAuthBindingRequest request, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v1/admin/plugins/installations/{installationId}/auth-bindings/{Uri.EscapeDataString(request.CapabilityId)}", request, ct);

    // ===== Task Bindings =====

    public Task<PluginTaskBindingUpdateResponse> SaveTaskBindingAsync(int installationId, string capabilityId, SavePluginTaskBindingRequest request, CancellationToken ct = default)
        => client.PutAsync<PluginTaskBindingUpdateResponse>($"/api/v1/admin/plugins/installations/{installationId}/task-bindings/{Uri.EscapeDataString(capabilityId)}", request, ct);

    // ===== Analyzer Bindings =====

    public Task SaveAnalyzerBindingsAsync(int installationId, string capabilityId, SavePluginAnalyzerBindingsRequest request, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v1/admin/plugins/installations/{installationId}/analyzer-bindings/{Uri.EscapeDataString(capabilityId)}", request, ct);

    // ===== Legacy Metadata Import =====

    public Task<PluginLegacyMetadataImportResponse> ImportLegacyMetadataAsync(int installationId, string importType, CancellationToken ct = default)
        => client.PostAsync<PluginLegacyMetadataImportResponse>($"/api/v1/admin/plugins/installations/{installationId}/legacy-metadata-import", new { import_type = importType }, ct);
}
