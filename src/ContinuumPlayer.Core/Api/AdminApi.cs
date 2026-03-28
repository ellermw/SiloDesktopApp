using ContinuumPlayer.Core.Models.Admin;
using ContinuumPlayer.Core.Models.Catalog;

namespace ContinuumPlayer.Core.Api;

// Simple list-wrapper response types used by endpoints that return JSON arrays wrapped in an object.
file class AdminSessionsResponse { public List<AdminSession> Sessions { get; set; } = []; }
file class AdminUsersResponse { public List<AdminUser> Users { get; set; } = []; }
file class AdminUserProfilesResponse { public List<AdminUserProfile> Profiles { get; set; } = []; }
file class AdminUserIPsResponse { public List<UserIPEntry> Ips { get; set; } = []; }
file class AdminIPUsersResponse { public List<IPUserEntry> Users { get; set; } = []; }
file class AdminTasksResponse { public List<TaskInfo> Tasks { get; set; } = []; }
file class AdminTaskHistoryResponse { public List<ExecutionResult> History { get; set; } = []; }
file class AdminNodesResponse { public List<StreamNode> Nodes { get; set; } = []; }
file class AdminAPIKeysResponse { public List<AdminAPIKey> ApiKeys { get; set; } = []; }
file class AdminPlaybackHistoryResponse { public List<AdminPlaybackHistoryItem> Items { get; set; } = []; }
file class AdminSensitiveStatusResponse { public Dictionary<string, bool> Keys { get; set; } = []; }
file class AdminSkippedRootsResponse { public List<string> Roots { get; set; } = []; }
file class AdminSectionsResponse { public List<object> Sections { get; set; } = []; }

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

    public Task CheckLibraryMountAsync(int id, CancellationToken ct = default)
        => client.PostNoContentAsync($"/api/v1/libraries/{id}/check-mount", new { }, ct);

    public Task RefreshLibraryMetadataAsync(int id, CancellationToken ct = default)
        => client.PostNoContentAsync($"/api/v1/libraries/{id}/refresh-metadata", new { }, ct);

    public Task<List<string>> GetSkippedRootsAsync(CancellationToken ct = default)
        => client.GetAsync<List<string>>("/api/v1/libraries/skipped-roots", ct);

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

    // ===== Settings =====

    public Task<Dictionary<string, string>> GetAdminSettingsAsync(CancellationToken ct = default)
        => client.GetAsync<Dictionary<string, string>>("/api/v1/admin/settings", ct);

    public Task UpdateAdminSettingAsync(string key, string value, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v1/admin/settings/{Uri.EscapeDataString(key)}", new { value }, ct);

    public Task<Dictionary<string, bool>> GetSensitiveStatusAsync(CancellationToken ct = default)
        => client.GetAsync<Dictionary<string, bool>>("/api/v1/admin/settings/sensitive-status", ct);

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
        string? cursor = null,
        int limit = 100,
        CancellationToken ct = default)
    {
        var query = $"/api/v1/admin/logs/app?limit={limit}";
        if (level != null) query += $"&level={Uri.EscapeDataString(level)}";
        if (component != null) query += $"&component={Uri.EscapeDataString(component)}";
        if (cursor != null) query += $"&cursor={Uri.EscapeDataString(cursor)}";
        return client.GetAsync<OperationalLogListResponse>(query, ct);
    }

    public Task<AuditLogListResponse> GetAuditLogsAsync(
        int? userId = null,
        string? cursor = null,
        int limit = 100,
        CancellationToken ct = default)
    {
        var query = $"/api/v1/admin/logs/audit?limit={limit}";
        if (userId.HasValue) query += $"&user_id={userId}";
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

    public Task<List<object>> GetSectionsAsync(string? scope = null, CancellationToken ct = default)
    {
        var query = "/api/v1/admin/sections";
        if (scope != null) query += $"?scope={Uri.EscapeDataString(scope)}";
        return client.GetAsync<List<object>>(query, ct);
    }

    public Task<object> CreateSectionAsync(object body, CancellationToken ct = default)
        => client.PostAsync<object>("/api/v1/admin/sections", body, ct);

    public Task<object> UpdateSectionAsync(string id, object body, CancellationToken ct = default)
        => client.PutAsync<object>($"/api/v1/admin/sections/{Uri.EscapeDataString(id)}", body, ct);

    public Task DeleteSectionAsync(string id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/admin/sections/{Uri.EscapeDataString(id)}", ct);

    public Task ReorderSectionsAsync(object body, CancellationToken ct = default)
        => client.PutNoContentAsync("/api/v1/admin/sections/reorder", body, ct);

    public Task RestoreSectionDefaultsAsync(CancellationToken ct = default)
        => client.PostNoContentAsync("/api/v1/admin/sections/restore-defaults", new { }, ct);

    // ===== Recommendations =====

    public Task RunEmbeddingsAsync(CancellationToken ct = default)
        => client.PostNoContentAsync("/api/v1/admin/recommendations/embeddings/run", new { }, ct);

    public Task RunTasteProfilesAsync(CancellationToken ct = default)
        => client.PostNoContentAsync("/api/v1/admin/recommendations/taste-profiles/run", new { }, ct);

    public Task RunCowatchAsync(CancellationToken ct = default)
        => client.PostNoContentAsync("/api/v1/admin/recommendations/cowatch/run", new { }, ct);

    public Task RunGenerateRecommendationsAsync(CancellationToken ct = default)
        => client.PostNoContentAsync("/api/v1/admin/recommendations/generate/run", new { }, ct);
}
