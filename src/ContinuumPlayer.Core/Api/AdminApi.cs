using ContinuumPlayer.Core.Models.Admin;
using ContinuumPlayer.Core.Models.Auth;
using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.Core.Models.HistoryImport;

namespace ContinuumPlayer.Core.Api;

// Simple list-wrapper response types used by endpoints that return JSON arrays wrapped in an object.
file class AdminSensitiveStatusResponse { public List<string> Configured { get; set; } = []; }
// AdminSectionsListResponse is in Models/Admin/AdminSection.cs

public class AdminApi(ContinuumApiClient client)
{
    // ===== Stats =====

    public Task<AdminStats> GetStatsAsync(CancellationToken ct = default)
        => client.GetAsync<AdminStats>("/api/v1/admin/stats", ct);

    public Task<List<AdminSession>> GetSessionsAsync(CancellationToken ct = default)
        => client.GetAsync<List<AdminSession>>("/api/v1/admin/sessions", ct);

    // ===== Users =====

    public Task<List<AdminUser>> GetUsersAsync(CancellationToken ct = default)
        => client.GetAsync<List<AdminUser>>("/api/v1/admin/users", ct);

    public Task<AdminUser> GetUserAsync(int id, CancellationToken ct = default)
        => client.GetAsync<AdminUser>($"/api/v1/admin/users/{id}", ct);

    public Task<AdminUser> CreateUserAsync(CreateUserRequest body, CancellationToken ct = default)
        => client.PostAsync<AdminUser>("/api/v1/admin/users", body, ct);

    public Task<AdminUser> UpdateUserAsync(int id, UpdateUserRequest body, CancellationToken ct = default)
        => client.PutAsync<AdminUser>($"/api/v1/admin/users/{id}", body, ct);

    public Task DeleteUserAsync(int id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/admin/users/{id}", ct);

    public Task<List<AdminUserProfile>> GetUserProfilesAsync(int userId, CancellationToken ct = default)
        => client.GetAsync<List<AdminUserProfile>>($"/api/v1/admin/users/{userId}/profiles", ct);

    public Task<List<UserIPEntry>> GetUserIPsAsync(int userId, int days = 30, CancellationToken ct = default)
        => client.GetAsync<List<UserIPEntry>>($"/api/v1/admin/users/{userId}/ips?days={days}", ct);

    public Task<List<IPUserEntry>> GetIPUsersAsync(string ip, int days = 30, CancellationToken ct = default)
        => client.GetAsync<List<IPUserEntry>>($"/api/v1/admin/ips?ip={Uri.EscapeDataString(ip)}&days={days}", ct);

    // ===== Libraries =====

    public Task<List<Library>> GetAdminLibrariesAsync(CancellationToken ct = default)
        => client.GetAsync<List<Library>>("/api/v1/libraries", ct);

    public Task<Library> CreateLibraryAsync(object body, CancellationToken ct = default)
        => client.PostAsync<Library>("/api/v1/libraries", body, ct);

    public Task<Library> UpdateLibraryAsync(int id, object body, CancellationToken ct = default)
        => client.PutAsync<Library>($"/api/v1/libraries/{id}", body, ct);

    public Task DeleteLibraryAsync(int id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/libraries/{id}", ct);

    public Task ScanLibraryAsync(int libraryId, CancellationToken ct = default)
        => client.PostNoContentAsync("/api/v1/scan", new { library_id = libraryId }, ct);

    public Task RunScanLibrariesTaskAsync(CancellationToken ct = default)
        => client.PostNoContentAsync("/api/v1/admin/tasks/scan_libraries/run", new { }, ct);

    public Task<LibraryMountCheckResponse> CheckLibraryMountAsync(int id, CancellationToken ct = default)
        => client.PostAsync<LibraryMountCheckResponse>($"/api/v1/libraries/{id}/check-mount", new { }, ct);

    public Task RefreshLibraryMetadataAsync(int id, CancellationToken ct = default)
        => client.PostNoContentAsync($"/api/v1/libraries/{id}/refresh-metadata", new { }, ct);

    public Task ConfirmEmptyRootCleanupAsync(int id, CancellationToken ct = default)
        => client.PostNoContentAsync($"/api/v1/libraries/{id}/confirm-empty-root-cleanup", new { }, ct);

    public Task<List<LibrarySkippedRoot>> GetSkippedRootsAsync(CancellationToken ct = default)
        => client.GetAsync<List<LibrarySkippedRoot>>("/api/v1/libraries/skipped-roots", ct);

    // ===== Tasks =====

    public Task<List<TaskInfo>> GetTasksAsync(CancellationToken ct = default)
        => client.GetAsync<List<TaskInfo>>("/api/v1/admin/tasks", ct);

    public Task<TaskInfo> GetTaskAsync(string key, CancellationToken ct = default)
        => client.GetAsync<TaskInfo>($"/api/v1/admin/tasks/{Uri.EscapeDataString(key)}", ct);

    public Task<List<ExecutionResult>> GetTaskHistoryAsync(string key, int limit = 20, CancellationToken ct = default)
        => client.GetAsync<List<ExecutionResult>>($"/api/v1/admin/tasks/{Uri.EscapeDataString(key)}/history?limit={limit}", ct);

    public Task RunTaskAsync(string key, CancellationToken ct = default)
        => client.PostNoContentAsync($"/api/v1/admin/tasks/{Uri.EscapeDataString(key)}/run", new { }, ct);

    public Task CancelTaskAsync(string key, CancellationToken ct = default)
        => client.PostNoContentAsync($"/api/v1/admin/tasks/{Uri.EscapeDataString(key)}/cancel", new { }, ct);

    public Task<TaskInfo> UpdateTaskTriggersAsync(string key, List<TriggerConfig> triggers, CancellationToken ct = default)
        => client.PutAsync<TaskInfo>($"/api/v1/admin/tasks/{Uri.EscapeDataString(key)}/triggers", triggers, ct);

    // ===== Nodes =====

    public Task<List<StreamNode>> GetNodesAsync(CancellationToken ct = default)
        => client.GetAsync<List<StreamNode>>("/api/v1/admin/nodes", ct);

    public Task<StreamNode> CreateNodeAsync(CreateNodeRequest body, CancellationToken ct = default)
        => client.PostAsync<StreamNode>("/api/v1/admin/nodes", body, ct);

    public Task<StreamNode> UpdateNodeAsync(int id, object body, CancellationToken ct = default)
        => client.PutAsync<StreamNode>($"/api/v1/admin/nodes/{id}", body, ct);

    public Task DeleteNodeAsync(int id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/admin/nodes/{id}", ct);

    public Task<CheckNodeResponse> CheckNodeAsync(int id, CancellationToken ct = default)
        => client.PostAsync<CheckNodeResponse>($"/api/v1/admin/nodes/{id}/check", new { }, ct);

    // ===== Rate Limits =====

    public Task<RateLimitConfig> GetRateLimitConfigAsync(CancellationToken ct = default)
        => client.GetAsync<RateLimitConfig>("/api/v1/admin/rate-limits/config", ct);

    public Task UpdateRateLimitConfigAsync(RateLimitConfig config, CancellationToken ct = default)
        => client.PutNoContentAsync("/api/v1/admin/rate-limits/config", config, ct);

    // ===== Settings =====

    public Task<Dictionary<string, string>> GetAdminSettingsAsync(CancellationToken ct = default)
        => client.GetAsync<Dictionary<string, string>>("/api/v1/admin/settings", ct);

    public Task UpdateAdminSettingAsync(string key, string value, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v1/admin/settings/{Uri.EscapeDataString(key)}", new { value }, ct);

    public async Task<HashSet<string>> GetSensitiveStatusAsync(CancellationToken ct = default)
    {
        var response = await client.GetAsync<AdminSensitiveStatusResponse>("/api/v1/admin/settings/sensitive-status", ct);
        return new HashSet<string>(response.Configured);
    }

    // ===== API Keys =====

    public Task<List<AdminAPIKey>> GetAPIKeysAsync(CancellationToken ct = default)
        => client.GetAsync<List<AdminAPIKey>>("/api/v1/admin/api-keys", ct);

    public Task<AdminAPIKey> CreateAPIKeyAsync(AdminCreateAPIKeyRequest body, CancellationToken ct = default)
        => client.PostAsync<AdminAPIKey>("/api/v1/admin/api-keys", body, ct);

    public Task DeleteAPIKeyAsync(int id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/admin/api-keys/{id}", ct);

    public Task UpdateAPIKeyTierAsync(int id, string rateTier, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v1/admin/api-keys/{id}/tier", new { rate_tier = rateTier }, ct);

    // ===== Playback History =====

    public Task<List<AdminPlaybackHistoryItem>> GetPlaybackHistoryAsync(
        int? userId = null,
        string? profileId = null,
        string? mediaItemId = null,
        bool? completed = null,
        int limit = 50,
        CancellationToken ct = default)
    {
        var query = $"/api/v1/admin/playback-history?limit={limit}";
        if (userId.HasValue) query += $"&user_id={userId}";
        if (profileId != null) query += $"&profile_id={Uri.EscapeDataString(profileId)}";
        if (mediaItemId != null) query += $"&media_item_id={Uri.EscapeDataString(mediaItemId)}";
        if (completed.HasValue) query += $"&completed={completed.Value.ToString().ToLowerInvariant()}";
        return client.GetAsync<List<AdminPlaybackHistoryItem>>(query, ct);
    }

    // ===== Logs =====

    public Task<OperationalLogListResponse> GetAppLogsAsync(
        string? level = null,
        string? component = null,
        string? requestId = null,
        string? q = null,
        string? playbackSessionId = null,
        string? cursor = null,
        int limit = 100,
        CancellationToken ct = default)
    {
        var query = $"/api/v1/admin/logs/app?limit={limit}";
        if (level != null) query += $"&level={Uri.EscapeDataString(level)}";
        if (component != null) query += $"&component={Uri.EscapeDataString(component)}";
        if (requestId != null) query += $"&request_id={Uri.EscapeDataString(requestId)}";
        if (q != null) query += $"&q={Uri.EscapeDataString(q)}";
        if (playbackSessionId != null) query += $"&playback_session_id={Uri.EscapeDataString(playbackSessionId)}";
        if (cursor != null) query += $"&cursor={Uri.EscapeDataString(cursor)}";
        return client.GetAsync<OperationalLogListResponse>(query, ct);
    }

    public Task<AuditLogListResponse> GetAuditLogsAsync(
        int? userId = null,
        string? requestId = null,
        string? method = null,
        string? clientIp = null,
        string? playbackSessionId = null,
        string? cursor = null,
        int limit = 100,
        CancellationToken ct = default)
    {
        var query = $"/api/v1/admin/logs/audit?limit={limit}";
        if (userId.HasValue) query += $"&user_id={userId}";
        if (requestId != null) query += $"&request_id={Uri.EscapeDataString(requestId)}";
        if (method != null) query += $"&method={Uri.EscapeDataString(method)}";
        if (clientIp != null) query += $"&client_ip={Uri.EscapeDataString(clientIp)}";
        if (playbackSessionId != null) query += $"&playback_session_id={Uri.EscapeDataString(playbackSessionId)}";
        if (cursor != null) query += $"&cursor={Uri.EscapeDataString(cursor)}";
        return client.GetAsync<AuditLogListResponse>(query, ct);
    }

    // ===== Collections =====

    public Task<AdminCollectionsResponse> GetCollectionsAsync(int? libraryId = null, CancellationToken ct = default)
    {
        var query = "/api/v1/admin/collections";
        if (libraryId.HasValue) query += $"?library_id={libraryId}";
        return client.GetAsync<AdminCollectionsResponse>(query, ct);
    }

    public Task<LibraryCollection> CreateCollectionAsync(CreateLibraryCollectionRequest body, CancellationToken ct = default)
        => client.PostAsync<LibraryCollection>("/api/v1/admin/collections", body, ct);

    public Task<LibraryCollection> UpdateCollectionAsync(string id, object body, CancellationToken ct = default)
        => client.PutAsync<LibraryCollection>($"/api/v1/admin/collections/{Uri.EscapeDataString(id)}", body, ct);

    public Task DeleteCollectionAsync(string id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/admin/collections/{Uri.EscapeDataString(id)}", ct);

    public Task<LibraryCollectionSyncRun> SyncCollectionAsync(string id, CancellationToken ct = default)
        => client.PostAsync<LibraryCollectionSyncRun>($"/api/v1/admin/collections/{Uri.EscapeDataString(id)}/sync", new { }, ct);

    // ===== Sections =====

    public async Task<List<AdminSection>> GetSectionsAsync(string? scope = null, CancellationToken ct = default)
    {
        var query = "/api/v1/admin/sections";
        if (scope != null) query += $"?scope={Uri.EscapeDataString(scope)}";
        var response = await client.GetAsync<AdminSectionsListResponse>(query, ct);
        return response.Sections;
    }

    public Task<object> CreateSectionAsync(object body, CancellationToken ct = default)
        => client.PostAsync<object>("/api/v1/admin/sections", body, ct);

    public Task<object> UpdateSectionAsync(string id, object body, CancellationToken ct = default)
        => client.PutAsync<object>($"/api/v1/admin/sections/{Uri.EscapeDataString(id)}", body, ct);

    public Task DeleteSectionAsync(string id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/admin/sections/{Uri.EscapeDataString(id)}", ct);

    public Task ReorderSectionsAsync(object body, CancellationToken ct = default)
        => client.PutNoContentAsync("/api/v1/admin/sections/reorder", body, ct);

    public Task RestoreSectionDefaultsAsync(string scope, int? libraryId, bool resetProfiles, CancellationToken ct = default)
        => client.PostNoContentAsync("/api/v1/admin/sections/restore-defaults",
            new { scope, library_id = libraryId, reset_profiles = resetProfiles }, ct);

    // ===== Recommendations =====

    public Task<RecommendationsStatus> GetRecommendationsStatusAsync(CancellationToken ct = default)
        => client.GetAsync<RecommendationsStatus>("/api/v1/admin/recommendations/status", ct);

    public Task RunEmbeddingsAsync(CancellationToken ct = default)
        => client.PostNoContentAsync("/api/v1/admin/recommendations/trigger/embeddings", new { }, ct);

    public Task RunTasteProfilesAsync(CancellationToken ct = default)
        => client.PostNoContentAsync("/api/v1/admin/recommendations/trigger/taste-profiles", new { }, ct);

    public Task RunCowatchAsync(CancellationToken ct = default)
        => client.PostNoContentAsync("/api/v1/admin/recommendations/trigger/cowatch", new { }, ct);

    public Task RunGenerateRecommendationsAsync(CancellationToken ct = default)
        => client.PostNoContentAsync("/api/v1/admin/recommendations/trigger/recommendations", new { }, ct);

    // ===== User Detail / Impersonation =====

    public Task<AdminUser> GetUserDetailAsync(int id, CancellationToken ct = default)
        => client.GetAsync<AdminUser>($"/api/v1/admin/users/{id}", ct);

    public Task<ImpersonationResponse> ImpersonateUserAsync(int id, CancellationToken ct = default)
        => client.PostAsync<ImpersonationResponse>($"/api/v1/admin/users/{id}/impersonate", new { }, ct);

    public Task<List<UserIPEntry>> GetAllIpsAsync(int days = 30, CancellationToken ct = default)
        => client.GetAsync<List<UserIPEntry>>($"/api/v1/admin/ips?days={days}", ct);

    public Task<List<AdminAPIKey>> GetUserApiKeysAsync(int userId, CancellationToken ct = default)
        => client.GetAsync<List<AdminAPIKey>>($"/api/v1/admin/users/{userId}/api-keys", ct);

    // ===== Item Matching =====

    public Task<ItemMatchSearchResponse> MatchSearchAsync(string itemId, ItemMatchSearchRequest request, CancellationToken ct = default)
        => client.PostAsync<ItemMatchSearchResponse>($"/api/v1/admin/items/{Uri.EscapeDataString(itemId)}/match/search", request, ct);

    public Task MatchApplyAsync(string itemId, ItemMatchApplyRequest request, CancellationToken ct = default)
        => client.PostNoContentAsync($"/api/v1/admin/items/{Uri.EscapeDataString(itemId)}/match/apply", request, ct);

    public Task RefreshItemMetadataAsync(string itemId, CancellationToken ct = default)
        => client.PostNoContentAsync($"/api/v1/admin/items/{Uri.EscapeDataString(itemId)}/refresh-metadata", new { }, ct);

    public Task UpdateItemMetadataAsync(string itemId, object request, CancellationToken ct = default)
        => client.PatchAsync<object>($"/api/v1/admin/items/{Uri.EscapeDataString(itemId)}/metadata", request, ct);

    // ===== Catalog Seed =====

    public Task<CatalogSeedExportResult> ExportCatalogAsync(CatalogSeedExportRequest request, CancellationToken ct = default)
        => client.PostAsync<CatalogSeedExportResult>("/api/v1/admin/catalog/export", request, ct);

    public Task<AdminJob> CreateExportJobAsync(CatalogSeedExportRequest request, CancellationToken ct = default)
        => client.PostAsync<AdminJob>("/api/v1/admin/catalog/export-jobs", request, ct);

    public Task<AdminJob> PublishExportJobAsync(string id, CancellationToken ct = default)
        => client.PostAsync<AdminJob>($"/api/v1/admin/catalog/export-jobs/{Uri.EscapeDataString(id)}/publish", new { }, ct);

    public Task<AdminJob> CreateImportJobAsync(CatalogSeedImportRequest request, CancellationToken ct = default)
        => client.PostAsync<AdminJob>("/api/v1/admin/catalog/import-jobs", request, ct);

    public Task<CatalogSeedImportSourcesResponse> GetCatalogImportSourcesAsync(CancellationToken ct = default)
        => client.GetAsync<CatalogSeedImportSourcesResponse>("/api/v1/admin/catalog/import-sources", ct);

    public Task<CatalogSeedImportSourcesResponse> GetLocalImportSourcesAsync(CancellationToken ct = default)
        => client.GetAsync<CatalogSeedImportSourcesResponse>("/api/v1/admin/catalog/local-import-sources", ct);

    public Task<CatalogSeedImportResponse> ImportCatalogAsync(CatalogSeedImportRequest request, CancellationToken ct = default)
        => client.PostAsync<CatalogSeedImportResponse>("/api/v1/admin/catalog/import", request, ct);

    // ===== Jobs =====

    public Task<AdminJobsResponse> GetJobsAsync(CancellationToken ct = default)
        => client.GetAsync<AdminJobsResponse>("/api/v1/admin/jobs", ct);

    public Task<AdminJob> GetJobAsync(string id, CancellationToken ct = default)
        => client.GetAsync<AdminJob>($"/api/v1/admin/jobs/{Uri.EscapeDataString(id)}", ct);

    // ===== Providers =====

    public Task<MetadataProvidersResponse> GetProvidersAsync(CancellationToken ct = default)
        => client.GetAsync<MetadataProvidersResponse>("/api/v1/admin/providers", ct);

    public Task<MetadataProvider> CreateProviderAsync(CreateProviderRequest request, CancellationToken ct = default)
        => client.PostAsync<MetadataProvider>("/api/v1/admin/providers", request, ct);

    public Task<MetadataProvider> UpdateProviderAsync(int id, object request, CancellationToken ct = default)
        => client.PutAsync<MetadataProvider>($"/api/v1/admin/providers/{id}", request, ct);

    public Task DeleteProviderAsync(int id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/admin/providers/{id}", ct);

    public Task<LibraryProviderChainResponse> GetLibraryProvidersAsync(int libraryId, CancellationToken ct = default)
        => client.GetAsync<LibraryProviderChainResponse>($"/api/v1/admin/libraries/{libraryId}/providers", ct);

    public Task UpdateLibraryProvidersAsync(int libraryId, SetLibraryChainRequest request, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v1/admin/libraries/{libraryId}/providers", request, ct);

    // ===== Library Extras =====

    public Task SetLibraryPosterAsync(int libraryId, object request, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v1/admin/libraries/{libraryId}/poster", request, ct);

    public Task DeleteLibraryPosterAsync(int libraryId, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/admin/libraries/{libraryId}/poster", ct);

    public Task<StaleMediaIdsResponse> GetStaleIdsAsync(CancellationToken ct = default)
        => client.GetAsync<StaleMediaIdsResponse>("/api/v1/admin/libraries/stale-ids", ct);

    public Task RematchStaleIdAsync(string contentId, CancellationToken ct = default)
        => client.PostNoContentAsync($"/api/v1/admin/libraries/stale-ids/{Uri.EscapeDataString(contentId)}/rematch", new { }, ct);

    public Task<List<UnmatchedLibraryItem>> GetUnmatchedItemsAsync(CancellationToken ct = default)
        => client.GetAsync<List<UnmatchedLibraryItem>>("/api/v1/admin/libraries/unmatched-items", ct);

    // ===== Invite Codes =====

    public Task<InviteCodesResponse> GetInviteCodesAsync(CancellationToken ct = default)
        => client.GetAsync<InviteCodesResponse>("/api/v1/admin/invite-codes", ct);

    public Task<InviteCode> CreateInviteCodeAsync(CreateInviteCodeRequest request, CancellationToken ct = default)
        => client.PostAsync<InviteCode>("/api/v1/admin/invite-codes", request, ct);

    public Task<InviteCode> UpdateInviteCodeAsync(int id, UpdateInviteCodeRequest request, CancellationToken ct = default)
        => client.PutAsync<InviteCode>($"/api/v1/admin/invite-codes/{id}", request, ct);

    public Task DeleteInviteCodeAsync(int id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/admin/invite-codes/{id}", ct);

    // ===== Subtitle Providers =====

    public Task<SubtitleProvidersResponse> GetSubtitleProvidersAsync(CancellationToken ct = default)
        => client.GetAsync<SubtitleProvidersResponse>("/api/v1/admin/subtitle-providers", ct);

    public Task UpdateSubtitleProviderAsync(string provider, SubtitleProviderUpdateRequest request, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v1/admin/subtitle-providers/{Uri.EscapeDataString(provider)}", request, ct);

    public Task<SubtitleProviderTestResponse> TestSubtitleProviderAsync(string provider, CancellationToken ct = default)
        => client.PostAsync<SubtitleProviderTestResponse>($"/api/v1/admin/subtitle-providers/{Uri.EscapeDataString(provider)}/test", new { }, ct);

    // ===== Collections Admin =====

    public Task<ImportMDBListCollectionResponse> ImportMDBListCollectionAsync(ImportMDBListCollectionRequest request, CancellationToken ct = default)
        => client.PostAsync<ImportMDBListCollectionResponse>("/api/v1/admin/collections/import/mdblist", request, ct);

    public Task<ImportTMDBCollectionResponse> ImportTMDBCollectionAsync(ImportTMDBCollectionRequest request, CancellationToken ct = default)
        => client.PostAsync<ImportTMDBCollectionResponse>("/api/v1/admin/collections/import/tmdb", request, ct);

    public Task DeleteCollectionImageAsync(string id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/admin/collections/{Uri.EscapeDataString(id)}/image", ct);

    // ===== History Import Sources (Admin) =====

    public Task<List<HistoryImportSource>> GetHistoryImportSourcesAsync(CancellationToken ct = default)
        => client.GetAsync<List<HistoryImportSource>>("/api/v1/admin/history-import-sources", ct);

    public Task<HistoryImportSource> CreateHistoryImportSourceAsync(CreateHistoryImportSourceRequest request, CancellationToken ct = default)
        => client.PostAsync<HistoryImportSource>("/api/v1/admin/history-import-sources", request, ct);

    public Task<HistoryImportSource> UpdateHistoryImportSourceAsync(int id, UpdateHistoryImportSourceRequest request, CancellationToken ct = default)
        => client.PutAsync<HistoryImportSource>($"/api/v1/admin/history-import-sources/{id}", request, ct);

    public Task DeleteHistoryImportSourceAsync(int id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/admin/history-import-sources/{id}", ct);

    // ===== Node Extras =====

    public Task<List<AdminSession>> GetNodeSessionsAsync(CancellationToken ct = default)
        => client.GetAsync<List<AdminSession>>("/api/v1/admin/node-sessions", ct);

    public Task ForceReloadNodesAsync(CancellationToken ct = default)
        => client.PostNoContentAsync("/api/v1/admin/nodes/force-reload", new { }, ct);

    public Task ForceReloadNodeAsync(int id, CancellationToken ct = default)
        => client.PostNoContentAsync($"/api/v1/admin/nodes/{id}/force-reload", new { }, ct);

    // ===== Playback Control =====

    public Task PauseSessionAsync(string sessionId, CancellationToken ct = default)
        => client.PostNoContentAsync($"/api/v1/admin/sessions/{Uri.EscapeDataString(sessionId)}/pause", new { }, ct);

    public Task ResumeSessionAsync(string sessionId, CancellationToken ct = default)
        => client.PostNoContentAsync($"/api/v1/admin/sessions/{Uri.EscapeDataString(sessionId)}/resume", new { }, ct);

    public Task StopSessionAsync(string sessionId, CancellationToken ct = default)
        => client.PostNoContentAsync($"/api/v1/admin/sessions/{Uri.EscapeDataString(sessionId)}/stop", new { }, ct);

    public Task TerminateSessionAsync(string sessionId, CancellationToken ct = default)
        => client.PostNoContentAsync($"/api/v1/admin/sessions/{Uri.EscapeDataString(sessionId)}/terminate", new { }, ct);

    public Task MessageSessionAsync(string sessionId, string message, CancellationToken ct = default)
        => client.PostNoContentAsync($"/api/v1/admin/sessions/{Uri.EscapeDataString(sessionId)}/message", new { message }, ct);
}
