using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Models.HistoryImport;

namespace SiloPlayer.Core.Api;

// Simple list-wrapper response types used by endpoints that return JSON arrays wrapped in an object.
file class AdminSensitiveStatusResponse { public List<string> Configured { get; set; } = []; public List<string> ManagedByEnv { get; set; } = []; }
// AdminSectionsListResponse is in Models/Admin/AdminSection.cs

public class AdminApi(SiloApiClient client)
{
    // ===== Client diagnostics =====

    public Task<DiagnosticStatus> GetDiagnosticsStatusAsync(CancellationToken ct = default)
        => client.GetAsync<DiagnosticStatus>("/api/v1/diagnostics/status", ct);

    public Task<DiagnosticReportListResponse> GetDiagnosticReportsAsync(string query = "", CancellationToken ct = default)
        => client.GetAsync<DiagnosticReportListResponse>("/api/v1/admin/diagnostics/reports" + query, ct);

    public Task<DiagnosticReport> GetDiagnosticReportAsync(string id, CancellationToken ct = default)
        => client.GetAsync<DiagnosticReport>($"/api/v1/admin/diagnostics/reports/{Uri.EscapeDataString(id)}", ct);

    public Task DeleteDiagnosticReportAsync(string id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/admin/diagnostics/reports/{Uri.EscapeDataString(id)}", ct);

    public Task<byte[]> DownloadDiagnosticReportAsync(string id, CancellationToken ct = default)
        => client.GetBytesAsync($"/api/v1/admin/diagnostics/reports/{Uri.EscapeDataString(id)}/download?proxy=1", ct);

    // ===== Stats =====

    public Task<AdminStats> GetStatsAsync(CancellationToken ct = default)
        => client.GetAsync<AdminStats>("/api/v1/admin/stats", ct);

    public Task<List<AdminSession>> GetSessionsAsync(CancellationToken ct = default)
        => client.GetAsync<List<AdminSession>>("/api/v1/admin/sessions", ct);

    public Task<MarkerHistoryResponse> GetMarkerHistoryAsync(int limit = 50, CancellationToken ct = default)
        => client.GetAsync<MarkerHistoryResponse>(
            $"/api/v1/admin/markers/history?limit={Math.Clamp(limit, 1, 100)}",
            ct);

    public Task<List<AccessGroup>> GetAccessGroupsAsync(CancellationToken ct = default)
        => client.GetAsync<List<AccessGroup>>("/api/v1/admin/access-groups", ct);

    public Task<AccessGroup> CreateAccessGroupAsync(string name, CancellationToken ct = default)
        => client.PostAsync<AccessGroup>("/api/v1/admin/access-groups",
            new Dictionary<string, object?> { ["name"] = name }, ct);

    public Task<AccessGroup> UpdateAccessGroupAsync(long id, UpdateAccessGroupRequest body,
        CancellationToken ct = default)
        => client.PutAsync<AccessGroup>($"/api/v1/admin/access-groups/{id}",
            new Dictionary<string, object?>
            {
                ["name"] = body.Name,
                ["description"] = body.Description,
                ["library_ids"] = body.LibraryIds,
                ["max_playback_quality"] = body.MaxPlaybackQuality,
                ["download_allowed"] = body.DownloadAllowed,
                ["download_transcode_allowed"] = body.DownloadTranscodeAllowed,
                ["max_streams"] = body.MaxStreams,
                ["max_transcodes"] = body.MaxTranscodes,
                ["allowed_permissions"] = body.AllowedPermissions,
                ["requests_allowed"] = body.RequestsAllowed,
                ["is_default"] = body.IsDefault,
            }, ct);

    public Task DeleteAccessGroupAsync(long id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/admin/access-groups/{id}", ct);

    // ===== Emailed Invitations =====

    public Task<List<Invitation>> GetInvitationsAsync(CancellationToken ct = default)
        => client.GetAsync<List<Invitation>>("/api/v1/admin/invitations", ct);

    public Task<SendInvitationResponse> CreateInvitationAsync(
        CreateInvitationRequest request,
        CancellationToken ct = default)
        => client.PostAsync<SendInvitationResponse>("/api/v1/admin/invitations", request, ct);

    public Task<SendInvitationResponse> ResendInvitationAsync(long id, CancellationToken ct = default)
        => client.PostAsync<SendInvitationResponse>($"/api/v1/admin/invitations/{id}/resend", new { }, ct);

    public Task RevokeInvitationAsync(long id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/admin/invitations/{id}", ct);

    // ===== Devices =====

    public async Task<List<AdminDeviceSummary>> GetDevicesAsync(CancellationToken ct = default)
        => (await client.GetAsync<AdminDevicesResponse>("/api/v1/admin/devices", ct)).Devices;

    public Task<AdminDeviceDetail> GetDeviceAsync(int userId, string deviceId,
        CancellationToken ct = default)
        => client.GetAsync<AdminDeviceDetail>(
            $"/api/v1/admin/devices/{userId}/{Uri.EscapeDataString(deviceId)}", ct);

    public Task UpdateDeviceSettingAsync(int userId, string profileId, string deviceId,
        string key, string value, CancellationToken ct = default)
        => client.PutAsync<object>(
            $"/api/v1/admin/users/{userId}/profiles/{Uri.EscapeDataString(profileId)}/device-settings/{Uri.EscapeDataString(key)}/{Uri.EscapeDataString(deviceId)}",
            new Dictionary<string, object?> { ["value"] = value }, ct);

    public Task DeleteDeviceSettingAsync(int userId, string profileId, string deviceId,
        string key, CancellationToken ct = default)
        => client.DeleteAsync(
            $"/api/v1/admin/users/{userId}/profiles/{Uri.EscapeDataString(profileId)}/device-settings/{Uri.EscapeDataString(key)}/{Uri.EscapeDataString(deviceId)}",
            ct);

    public Task DeleteAllDeviceSettingsAsync(int userId, string profileId, string deviceId,
        CancellationToken ct = default)
        => client.DeleteAsync(
            $"/api/v1/admin/users/{userId}/profiles/{Uri.EscapeDataString(profileId)}/devices/{Uri.EscapeDataString(deviceId)}/settings",
            ct);

    public async Task<List<AdminUserSetting>> GetUserSettingsAsync(int userId, CancellationToken ct = default)
        => (await client.GetAsync<AdminUserSettingsResponse>($"/api/v1/admin/users/{userId}/settings", ct)).Settings;

    public Task UpdateUserSettingAsync(int userId, string key, string value, CancellationToken ct = default)
        => client.PutAsync<object>(
            $"/api/v1/admin/users/{userId}/settings/{Uri.EscapeDataString(key)}",
            new Dictionary<string, object?> { ["value"] = value }, ct);

    public Task DeleteUserSettingAsync(int userId, string key, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/admin/users/{userId}/settings/{Uri.EscapeDataString(key)}", ct);

    public async Task<List<AdminDeviceSetting>> GetUserDeviceSettingsAsync(int userId, CancellationToken ct = default)
        => (await client.GetAsync<AdminUserDeviceSettingsResponse>(
            $"/api/v1/admin/users/{userId}/device-settings", ct)).Settings;

    // ===== Autoscan =====

    public Task<AutoscanSettings> GetAutoscanSettingsAsync(CancellationToken ct = default)
        => client.GetAsync<AutoscanSettings>("/api/v1/admin/autoscan/settings", ct);
    public Task<AutoscanSettings> UpdateAutoscanSettingsAsync(AutoscanSettings body, CancellationToken ct = default)
        => client.PutAsync<AutoscanSettings>("/api/v1/admin/autoscan/settings", body, ct);
    public async Task<List<AutoscanConnection>> GetAutoscanConnectionsAsync(CancellationToken ct = default)
        => (await client.GetAsync<AutoscanConnectionsResponse>("/api/v1/admin/autoscan/connections", ct)).Connections;
    public Task<AutoscanConnection> CreateAutoscanConnectionAsync(AutoscanConnectionInput body, CancellationToken ct = default)
        => client.PostAsync<AutoscanConnection>("/api/v1/admin/autoscan/connections", body, ct);
    public Task<AutoscanConnection> UpdateAutoscanConnectionAsync(string id, AutoscanConnectionInput body, CancellationToken ct = default)
        => client.PutAsync<AutoscanConnection>($"/api/v1/admin/autoscan/connections/{Uri.EscapeDataString(id)}", body, ct);
    public Task DeleteAutoscanConnectionAsync(string id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/admin/autoscan/connections/{Uri.EscapeDataString(id)}", ct);
    public Task<AutoscanConnectionTestResult> TestAutoscanConnectionAsync(object body, CancellationToken ct = default)
        => client.PostAsync<AutoscanConnectionTestResult>("/api/v1/admin/autoscan/connections/test", body, ct);
    public async Task<List<AutoscanSource>> GetAutoscanSourcesAsync(CancellationToken ct = default)
        => (await client.GetAsync<AutoscanSourcesResponse>("/api/v1/admin/autoscan/sources", ct)).Sources;
    public async Task<List<AutoscanAvailableSource>> GetAvailableAutoscanSourcesAsync(CancellationToken ct = default)
        => (await client.GetAsync<AutoscanAvailableSourcesResponse>("/api/v1/admin/autoscan/scan-source-plugins", ct)).Plugins;
    public Task<AutoscanSource> CreateAutoscanSourceAsync(AutoscanSourceCreateInput body, CancellationToken ct = default)
        => client.PostAsync<AutoscanSource>("/api/v1/admin/autoscan/sources", body, ct);
    public Task<AutoscanSource> UpdateAutoscanSourceAsync(string id, AutoscanSourceInput body, CancellationToken ct = default)
        => client.PutAsync<AutoscanSource>($"/api/v1/admin/autoscan/sources/{Uri.EscapeDataString(id)}", body, ct);
    public Task DeleteAutoscanSourceAsync(string id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/admin/autoscan/sources/{Uri.EscapeDataString(id)}", ct);
    public Task<AutoscanRewriteSuggestions> GetAutoscanRewriteSuggestionsAsync(string id, CancellationToken ct = default)
        => client.GetAsync<AutoscanRewriteSuggestions>($"/api/v1/admin/autoscan/sources/{Uri.EscapeDataString(id)}/rewrite-suggestions", ct);
    public Task<AutoscanSource> CreateAutoscanWebhookAsync(string id, CancellationToken ct = default)
        => client.PostAsync<AutoscanSource>($"/api/v1/admin/autoscan/sources/{Uri.EscapeDataString(id)}/webhook", new { }, ct);
    public Task<AutoscanSource> RotateAutoscanWebhookAsync(string id, CancellationToken ct = default)
        => client.PostAsync<AutoscanSource>($"/api/v1/admin/autoscan/sources/{Uri.EscapeDataString(id)}/webhook/rotate", new { }, ct);
    public Task DeleteAutoscanWebhookAsync(string id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/admin/autoscan/sources/{Uri.EscapeDataString(id)}/webhook", ct);
    public Task<AutoscanStatus> GetAutoscanStatusAsync(CancellationToken ct = default)
        => client.GetAsync<AutoscanStatus>("/api/v1/admin/autoscan/status", ct);
    public Task<AutoscanEventsResponse> GetAutoscanEventsAsync(string query = "", CancellationToken ct = default)
        => client.GetAsync<AutoscanEventsResponse>("/api/v1/admin/autoscan/events" + query, ct);
    public Task<AutoscanScansResponse> GetAutoscanScansAsync(string query = "", CancellationToken ct = default)
        => client.GetAsync<AutoscanScansResponse>("/api/v1/admin/autoscan/scans" + query, ct);
    public Task<object> TriggerAutoscanAsync(CancellationToken ct = default)
        => client.PostAsync<object>("/api/v1/admin/autoscan/trigger", new { }, ct);

    // ===== Policy =====
    public Task<PolicyCapability> GetPolicyCapabilityAsync(CancellationToken ct = default) => client.GetAsync<PolicyCapability>("/api/v1/policy/capability", ct);
    public Task<List<PolicyVendorModule>> GetPolicyVendorAsync(CancellationToken ct = default) => client.GetAsync<List<PolicyVendorModule>>("/api/v1/admin/policy/vendor", ct);
    public Task<List<PolicyDocument>> GetPolicyDocumentsAsync(CancellationToken ct = default) => client.GetAsync<List<PolicyDocument>>("/api/v1/admin/policy/documents", ct);
    public Task<PolicyDocument> GetPolicyDocumentAsync(long id, CancellationToken ct = default) => client.GetAsync<PolicyDocument>($"/api/v1/admin/policy/documents/{id}", ct);
    public Task<PolicyDocument> CreatePolicyDocumentAsync(string domain, string name, CancellationToken ct = default) => client.PostAsync<PolicyDocument>("/api/v1/admin/policy/documents", new { domain, name }, ct);
    public Task DeletePolicyDocumentAsync(long id, CancellationToken ct = default) => client.DeleteAsync($"/api/v1/admin/policy/documents/{id}", ct);
    public Task<List<PolicyVersionSummary>> GetPolicyVersionsAsync(long id, CancellationToken ct = default) => client.GetAsync<List<PolicyVersionSummary>>($"/api/v1/admin/policy/documents/{id}/versions", ct);
    public Task<PolicyVersion> GetPolicyVersionAsync(long id, long versionId, CancellationToken ct = default) => client.GetAsync<PolicyVersion>($"/api/v1/admin/policy/documents/{id}/versions/{versionId}", ct);
    public Task<PolicyCreateVersionResult> CreatePolicyVersionAsync(long id, string source, string? comment, CancellationToken ct = default) => client.PostAsync<PolicyCreateVersionResult>($"/api/v1/admin/policy/documents/{id}/versions", new { source, comment }, ct);
    public Task<PolicyActivateVersionResult> ActivatePolicyVersionAsync(long id, long versionId, CancellationToken ct = default) => client.PostAsync<PolicyActivateVersionResult>($"/api/v1/admin/policy/documents/{id}/versions/{versionId}/activate", new { }, ct);
    public Task<PolicySetDocumentEnabledResult> SetPolicyDocumentEnabledAsync(long id, bool enabled, CancellationToken ct = default) => client.PostAsync<PolicySetDocumentEnabledResult>($"/api/v1/admin/policy/documents/{id}/enabled", new { enabled }, ct);
    public Task<PolicyValidateResult> ValidatePolicyAsync(string domain, string source, CancellationToken ct = default) => client.PostAsync<PolicyValidateResult>("/api/v1/admin/policy/validate", new { domain, source }, ct);
    public Task<PolicySimulateResult> SimulatePolicyAsync(string domain, string? source, object input, CancellationToken ct = default) => client.PostAsync<PolicySimulateResult>("/api/v1/admin/policy/simulate", new { domain, source, input }, ct);
    public Task<PolicyDecisionListResult> GetPolicyDecisionsAsync(string query = "", CancellationToken ct = default) => client.GetAsync<PolicyDecisionListResult>("/api/v1/admin/policy/decisions" + query, ct);
    public Task<PolicyDecisionEntry> GetPolicyDecisionAsync(long id, CancellationToken ct = default) => client.GetAsync<PolicyDecisionEntry>($"/api/v1/admin/policy/decisions/{id}", ct);

    // ===== Users =====

    public Task<List<AdminUser>> GetUsersAsync(CancellationToken ct = default)
        => client.GetAsync<List<AdminUser>>("/api/v1/admin/users", ct);

    public Task<AdminUser> GetUserAsync(int id, CancellationToken ct = default)
        => client.GetAsync<AdminUser>($"/api/v1/admin/users/{id}", ct);

    public Task<AdminUser> CreateUserAsync(CreateUserRequest body, CancellationToken ct = default)
        => client.PostAsync<AdminUser>("/api/v1/admin/users", body, ct);

    public Task<AdminUser> UpdateUserAsync(int id, UpdateUserRequest body, CancellationToken ct = default)
    {
        var fields = new Dictionary<string, object?>();
        AddIfPresent(fields, "username", body.Username);
        AddIfPresent(fields, "email", body.Email);
        AddIfPresent(fields, "password", body.Password);
        AddIfPresent(fields, "role", body.Role);
        AddIfPresent(fields, "permissions", body.Permissions);
        AddIfPresent(fields, "enabled", body.Enabled);
        AddIfPresent(fields, "max_playback_quality", body.MaxPlaybackQuality);
        AddIfPresent(fields, "max_streams", body.MaxStreams);
        AddIfPresent(fields, "max_transcodes", body.MaxTranscodes);
        AddIfPresent(fields, "transcode_allowed", body.TranscodeAllowed);
        AddIfPresent(fields, "audio_transcode_allowed", body.AudioTranscodeAllowed);
        AddIfPresent(fields, "max_profiles", body.MaxProfiles);
        AddIfPresent(fields, "download_allowed", body.DownloadAllowed);
        AddIfPresent(fields, "download_transcode_allowed", body.DownloadTranscodeAllowed);
        if (body.LibraryIdsSpecified)
            fields["library_ids"] = body.LibraryIds;
        if (body.AccessGroupIdSpecified)
            fields["access_group_id"] = body.AccessGroupId;
        return client.PutAsync<AdminUser>($"/api/v1/admin/users/{id}", fields, ct);
    }

    private static void AddIfPresent(Dictionary<string, object?> fields, string name, object? value)
    {
        if (value is not null)
            fields[name] = value;
    }

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
        => client.GetAsync<List<TaskInfo>>($"/api/v1/admin/tasks?_={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}", ct);

    public Task<TaskInfo> GetTaskAsync(string key, CancellationToken ct = default)
        => client.GetAsync<TaskInfo>($"/api/v1/admin/tasks/{Uri.EscapeDataString(key)}", ct);

    public Task<List<ExecutionResult>> GetTaskHistoryAsync(string key, int limit = 20, CancellationToken ct = default)
        => client.GetAsync<List<ExecutionResult>>($"/api/v1/admin/tasks/{Uri.EscapeDataString(key)}/history?limit={limit}", ct);

    public Task<MetadataRefreshMetrics> GetTaskMetricsAsync(string key, CancellationToken ct = default)
        => client.GetAsync<MetadataRefreshMetrics>($"/api/v1/admin/tasks/{Uri.EscapeDataString(key)}/metrics", ct);

    public Task RunTaskAsync(string key, CancellationToken ct = default)
        => client.PostNoContentAsync($"/api/v1/admin/tasks/{Uri.EscapeDataString(key)}/run", new { }, ct);

    public Task CancelTaskAsync(string key, CancellationToken ct = default)
        => client.PostNoContentAsync($"/api/v1/admin/tasks/{Uri.EscapeDataString(key)}/cancel", new { }, ct);

    // ===== System Info =====

    public Task<AdminBuildInfo> GetBuildInfoAsync(CancellationToken ct = default)
        => client.GetAsync<AdminBuildInfo>("/api/v1/admin/system/build", ct);

    public Task<Dictionary<string, object>> GetHWAccelInfoAsync(CancellationToken ct = default)
        => client.GetAsync<Dictionary<string, object>>("/api/v1/admin/system/hw-accel", ct);

    public Task<AdminHWAccelInfo> GetHWAccelDetectionAsync(CancellationToken ct = default)
        => client.GetAsync<AdminHWAccelInfo>("/api/v1/admin/system/hw-accel", ct);

    // ===== Admin Jobs (long-running: library_refresh, catalog_import, etc.) =====

    public async Task<List<AdminJob>> GetAdminJobsAsync(string? jobType = null, int limit = 50, CancellationToken ct = default)
    {
        var url = $"/api/v1/admin/jobs?limit={limit}";
        if (!string.IsNullOrEmpty(jobType)) url += $"&job_type={Uri.EscapeDataString(jobType)}";
        var resp = await client.GetAsync<AdminJobsResponse>(url, ct);
        return resp?.Jobs ?? [];
    }

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

    public Task<AdminRestartResponse> UpdateRateLimitConfigAsync(RateLimitConfig config, CancellationToken ct = default)
        => client.PutAsync<AdminRestartResponse>("/api/v1/admin/rate-limits/config", config, ct);

    // ===== Settings =====

    public Task<Dictionary<string, string>> GetAdminSettingsAsync(CancellationToken ct = default)
        => client.GetAsync<Dictionary<string, string>>("/api/v1/admin/settings", ct);

    // Response includes RestartRequired so the UI can show the restart prompt from the server's source of truth.
    public Task<AdminSettingUpdateResponse> UpdateAdminSettingAsync(string key, string value, CancellationToken ct = default)
        => client.PutAsync<AdminSettingUpdateResponse>(
            $"/api/v1/admin/settings/{Uri.EscapeDataString(key)}",
            new Dictionary<string, object?> { ["value"] = value },
            ct);

    public async Task<(HashSet<string> Configured, HashSet<string> ManagedByEnv)> GetSensitiveStatusAsync(CancellationToken ct = default)
    {
        var response = await client.GetAsync<AdminSensitiveStatusResponse>("/api/v1/admin/settings/sensitive-status", ct);
        return (new HashSet<string>(response.Configured), new HashSet<string>(response.ManagedByEnv));
    }

    /// <summary>
    /// Runs a connection check for an external service (redis, s3_metadata,
    /// s3_operational, embeddings, plugin). POSTs the current settings values
    /// (including unsaved dirty ones) so the server can test against whatever
    /// the admin is about to save, not just what is persisted.
    /// </summary>
    public Task<ConnectionCheckResponse> CheckSettingsConnectionAsync(
        string kind, AdminSettingsConnectionCheckRequest body, CancellationToken ct = default)
        => client.PostAsync<ConnectionCheckResponse>(
            $"/api/v1/admin/settings/check/{Uri.EscapeDataString(kind)}", body, ct);

    // ===== API Keys =====

    public Task<List<AdminAPIKey>> GetAPIKeysAsync(CancellationToken ct = default)
        => client.GetAsync<List<AdminAPIKey>>("/api/v1/admin/api-keys", ct);

    public Task<AdminAPIKey> CreateAPIKeyAsync(AdminCreateAPIKeyRequest body, CancellationToken ct = default)
        => client.PostAsync<AdminAPIKey>("/api/v1/admin/api-keys", body, ct);

    public Task DeleteAPIKeyAsync(int id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/admin/api-keys/{id}", ct);

    public Task UpdateAPIKeyTierAsync(int id, string tier, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v1/admin/api-keys/{id}/tier", new { tier }, ct);

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

    public Task AddCollectionItemAsync(string collectionId, string itemId, CancellationToken ct = default)
        => client.PutNoContentAsync(
            $"/api/v1/admin/collections/{Uri.EscapeDataString(collectionId)}/items/{Uri.EscapeDataString(itemId)}",
            null,
            ct);

    public Task<LibraryCollectionSyncRun> SyncCollectionAsync(string id, CancellationToken ct = default)
        => client.PostAsync<LibraryCollectionSyncRun>($"/api/v1/admin/collections/{Uri.EscapeDataString(id)}/sync", new { }, ct);

    public Task<LibraryCollectionGroupListResponse> GetCollectionGroupsAsync(int libraryId, CancellationToken ct = default)
        => client.GetAsync<LibraryCollectionGroupListResponse>($"/api/v1/admin/libraries/{libraryId}/collection-groups", ct);

    public Task<LibraryCollectionGroup> CreateCollectionGroupAsync(
        int libraryId,
        CreateLibraryCollectionGroupRequest request,
        CancellationToken ct = default)
        => client.PostAsync<LibraryCollectionGroup>($"/api/v1/admin/libraries/{libraryId}/collection-groups", request, ct);

    public Task<LibraryCollectionGroup> UpdateCollectionGroupAsync(
        string id,
        UpdateLibraryCollectionGroupRequest request,
        CancellationToken ct = default)
        => client.PutAsync<LibraryCollectionGroup>($"/api/v1/admin/collection-groups/{Uri.EscapeDataString(id)}", request, ct);

    public Task DeleteCollectionGroupAsync(string id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/admin/collection-groups/{Uri.EscapeDataString(id)}", ct);

    public Task ReorderCollectionGroupsAsync(int libraryId, IReadOnlyList<string> groupIds, CancellationToken ct = default)
        => client.PutNoContentAsync(
            $"/api/v1/admin/libraries/{libraryId}/collection-groups/reorder",
            new ReorderLibraryCollectionGroupsRequest { Ids = [.. groupIds] },
            ct);

    public Task ReorderCollectionsInGroupAsync(
        string groupId,
        IReadOnlyList<string> collectionIds,
        int? libraryId = null,
        bool moveOmittedToUngrouped = false,
        CancellationToken ct = default)
    {
        var path = $"/api/v1/admin/collection-groups/{Uri.EscapeDataString(groupId)}/collections/reorder";
        var query = new List<string>();
        if (groupId == "ungrouped" && libraryId.HasValue)
            query.Add($"library_id={libraryId.Value}");
        if (moveOmittedToUngrouped)
            query.Add("move_omitted=ungrouped");
        if (query.Count > 0)
            path += "?" + string.Join("&", query);

        return client.PutNoContentAsync(
            path,
            new ReorderLibraryCollectionsInGroupRequest { Ids = [.. collectionIds] },
            ct);
    }

    public Task<PushRelayRegisterResponse> RegisterPushRelayAsync(string relayUrl, CancellationToken ct = default)
        => client.PostAsync<PushRelayRegisterResponse>(
            "/api/v1/admin/notifications/push/relay/register",
            new PushRelayRegisterRequest { RelayUrl = relayUrl },
            ct);

    public Task ReorderAdminCollectionsAsync(ReorderAdminCollectionsRequest request, CancellationToken ct = default)
        => client.PutNoContentAsync("/api/v1/admin/collections/order", request, ct);

    // ===== Sections =====

    // B10: WebUI uses ?scope=home or ?scope=library&library_id={N}. The desktop
    // previously sent the bare library id as the scope value, which the server
    // doesn't recognize.
    public async Task<List<AdminSection>> GetSectionsAsync(string scope = "home", int? libraryId = null, CancellationToken ct = default)
    {
        var query = $"/api/v1/admin/sections?scope={Uri.EscapeDataString(scope)}";
        if (scope == "library" && libraryId.HasValue)
            query += $"&library_id={libraryId.Value}";
        var response = await client.GetAsync<AdminSectionsListResponse>(query, ct);
        return response.Sections;
    }

    public Task<object> CreateSectionAsync(object body, CancellationToken ct = default)
        => client.PostAsync<object>("/api/v1/admin/sections", body, ct);

    public Task<BulkCreateSectionsResponse> BulkCreateSectionsAsync(object body, CancellationToken ct = default)
        => client.PostAsync<BulkCreateSectionsResponse>("/api/v1/admin/sections/bulk-create", body, ct);

    public Task<AdminSectionPreviewResponse> PreviewSectionAsync(AdminSectionPreviewRequest body, CancellationToken ct = default)
        => client.PostAsync<AdminSectionPreviewResponse>("/api/v1/admin/sections/preview", body, ct);

    public Task<object> UpdateSectionAsync(string id, object body, CancellationToken ct = default)
        => client.PutAsync<object>($"/api/v1/admin/sections/{Uri.EscapeDataString(id)}", body, ct);

    public Task DeleteSectionAsync(string id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/admin/sections/{Uri.EscapeDataString(id)}", ct);

    public Task ReorderSectionsAsync(object body, CancellationToken ct = default)
        => client.PutNoContentAsync("/api/v1/admin/sections/reorder", body, ct);

    public Task RestoreSectionDefaultsAsync(string scope, int? libraryId, bool resetProfiles, CancellationToken ct = default)
        => client.PostNoContentAsync("/api/v1/admin/sections/restore-defaults",
            new Dictionary<string, object?>
            {
                ["scope"] = scope,
                ["library_id"] = libraryId,
                ["reset_profiles"] = resetProfiles,
            }, ct);

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

    public Task<AdminJob> RefreshItemMetadataAsync(
        string itemId,
        string mode = "quick",
        CancellationToken ct = default)
        => client.PostAsync<AdminJob>(
            $"/api/v1/admin/items/{Uri.EscapeDataString(itemId)}/refresh-metadata",
            new { mode },
            ct);

    public Task<RedetectIntroResponse> RedetectEpisodeIntroAsync(
        string itemId,
        CancellationToken ct = default)
        => client.PostAsync<RedetectIntroResponse>(
            $"/api/v1/admin/items/{Uri.EscapeDataString(itemId)}/redetect-intro",
            new Dictionary<string, object?>(),
            ct);

    public Task<ItemFilesResponse> GetItemFilesAsync(string itemId, CancellationToken ct = default)
        => client.GetAsync<ItemFilesResponse>(
            $"/api/v1/admin/items/{Uri.EscapeDataString(itemId)}/files",
            ct);

    public Task<ItemSplitResponse> SplitItemAsync(
        string itemId,
        ItemSplitRequest request,
        CancellationToken ct = default)
        => client.PostAsync<ItemSplitResponse>(
            $"/api/v1/admin/items/{Uri.EscapeDataString(itemId)}/split",
            request,
            ct);

    public Task UpdateItemMetadataAsync(string itemId, object request, CancellationToken ct = default)
        => client.PatchAsync<object>($"/api/v1/admin/items/{Uri.EscapeDataString(itemId)}/metadata", request, ct);

    public Task<Person> RefreshPersonAsync(string personId, CancellationToken ct = default)
        => client.PostAsync<Person>(
            $"/api/v1/admin/people/{Uri.EscapeDataString(personId)}/refresh",
            new { }, ct);

    public Task<Person> UpdatePersonAsync(string personId, object request, CancellationToken ct = default)
        => client.PatchAsync<Person>(
            $"/api/v1/admin/people/{Uri.EscapeDataString(personId)}",
            request, ct);

    public Task<ItemImagesResponse> GetItemImagesAsync(string itemId, CancellationToken ct = default)
        => client.GetAsync<ItemImagesResponse>(
            $"/api/v1/admin/items/{Uri.EscapeDataString(itemId)}/images",
            ct);

    public Task<ApplyItemImageResponse> ApplyItemImageAsync(
        string itemId,
        ApplyItemImageRequest request,
        CancellationToken ct = default)
        => client.PostAsync<ApplyItemImageResponse>(
            $"/api/v1/admin/items/{Uri.EscapeDataString(itemId)}/images/apply",
            request,
            ct);

    public Task<MetadataTranslationJobResponse> StartItemMetadataTranslationAsync(
        string itemId,
        TranslateItemMetadataRequest request,
        CancellationToken ct = default)
        => client.PostAsync<MetadataTranslationJobResponse>(
            $"/api/v1/admin/items/{Uri.EscapeDataString(itemId)}/metadata-translation",
            request,
            ct);

    public Task<MetadataTranslationJobsResponse> GetItemMetadataTranslationJobsAsync(
        string itemId,
        CancellationToken ct = default)
        => client.GetAsync<MetadataTranslationJobsResponse>(
            $"/api/v1/admin/items/{Uri.EscapeDataString(itemId)}/metadata-translation/jobs",
            ct);

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

    // B11: WebUI passes ?job_type={type}&limit={N}. Catalog seed flows filter by type.
    public Task<AdminJobsResponse> GetJobsAsync(string? jobType = null, int limit = 50, CancellationToken ct = default)
    {
        var query = $"/api/v1/admin/jobs?limit={limit}";
        if (jobType != null) query += $"&job_type={Uri.EscapeDataString(jobType)}";
        return client.GetAsync<AdminJobsResponse>(query, ct);
    }

    public Task<AdminJob> GetJobAsync(string id, CancellationToken ct = default)
        => client.GetAsync<AdminJob>($"/api/v1/admin/jobs/{Uri.EscapeDataString(id)}", ct);

    public Task<AdminJob> CancelAdminJobAsync(string id, CancellationToken ct = default)
        => client.PostAsync<AdminJob>($"/api/v1/admin/jobs/{Uri.EscapeDataString(id)}/cancel", new { }, ct);

    // ===== Providers =====

    public Task<List<MetadataProvider>> GetProvidersAsync(CancellationToken ct = default)
        => client.GetAsync<List<MetadataProvider>>("/api/v1/admin/providers", ct);

    public Task<MetadataProvider> CreateProviderAsync(CreateProviderRequest request, CancellationToken ct = default)
        => client.PostAsync<MetadataProvider>("/api/v1/admin/providers", request, ct);

    public Task<MetadataProvider> UpdateProviderAsync(int id, object request, CancellationToken ct = default)
        => client.PutAsync<MetadataProvider>($"/api/v1/admin/providers/{id}", request, ct);

    public Task DeleteProviderAsync(int id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/admin/providers/{id}", ct);

    // B9: Path is /libraries/{id}/providers (no /admin/) — matches WebUI useLibraryProviders.
    public Task<LibraryProviderChainResponse> GetLibraryProvidersAsync(int libraryId, CancellationToken ct = default)
        => client.GetAsync<LibraryProviderChainResponse>($"/api/v1/libraries/{libraryId}/providers", ct);

    public Task UpdateLibraryProvidersAsync(int libraryId, SetLibraryChainRequest request, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v1/libraries/{libraryId}/providers", request, ct);

    public Task<LibraryProviderChainResponse> GetLibraryProviderDefaultsAsync(string libraryType, CancellationToken ct = default)
        => client.GetAsync<LibraryProviderChainResponse>($"/api/v1/libraries/provider-defaults?library_type={Uri.EscapeDataString(libraryType)}", ct);

    public Task<FilesystemBrowseResponse> BrowseFilesystemAsync(string path, CancellationToken ct = default)
        => client.GetAsync<FilesystemBrowseResponse>($"/api/v1/admin/filesystem/browse?path={Uri.EscapeDataString(path)}", ct);

    // ===== Library Reorder =====

    public Task ReorderLibrariesAsync(object body, CancellationToken ct = default)
        => client.PutNoContentAsync("/api/v1/libraries/reorder", body, ct);

    // ===== Cancel Scans =====

    public Task CancelLibraryScansAsync(int libraryId, CancellationToken ct = default)
        => client.PostNoContentAsync("/api/v1/scan/cancel", new Dictionary<string, object> { ["library_id"] = libraryId }, ct);

    // ===== Library Roots (Ambiguous) =====

    public Task<LibraryRootsResponse> GetLibraryRootsAsync(int libraryId, string? state = null, CancellationToken ct = default)
    {
        var query = $"/api/v1/libraries/roots?library_id={libraryId}";
        if (state != null) query += $"&state={Uri.EscapeDataString(state)}";
        return client.GetAsync<LibraryRootsResponse>(query, ct);
    }

    public Task UpsertLibraryRootOverrideAsync(UpsertLibraryRootOverrideRequest body, CancellationToken ct = default)
        => client.PutNoContentAsync("/api/v1/libraries/roots/override", body, ct);

    public Task DeleteLibraryRootOverrideAsync(DeleteLibraryRootOverrideRequest body, CancellationToken ct = default)
        => client.DeleteWithBodyAsync("/api/v1/libraries/roots/override", body, ct);

    // ===== Library Extras =====

    public Task SetLibraryPosterAsync(int libraryId, byte[] fileBytes, string fileName, string contentType, CancellationToken ct = default)
        => client.PutMultipartNoContentAsync($"/api/v1/libraries/{libraryId}/poster", "poster", fileName, fileBytes, contentType, ct);

    public Task DeleteLibraryPosterAsync(int libraryId, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/libraries/{libraryId}/poster", ct);

    public Task<List<StaleMediaId>> GetStaleIdsAsync(CancellationToken ct = default)
        => client.GetAsync<List<StaleMediaId>>("/api/v1/libraries/stale-ids", ct);

    public Task RematchStaleIdAsync(string contentId, CancellationToken ct = default)
        => client.PostNoContentAsync($"/api/v1/libraries/stale-ids/{Uri.EscapeDataString(contentId)}/rematch", new { }, ct);

    public Task<UnmatchedLibraryItemsResponse> GetUnmatchedItemsAsync(int limit = 10, int offset = 0, string? search = null, CancellationToken ct = default)
    {
        var path = $"/api/v1/libraries/unmatched-items?limit={limit}&offset={offset}";
        if (!string.IsNullOrWhiteSpace(search)) path += $"&search={Uri.EscapeDataString(search.Trim())}";
        return client.GetAsync<UnmatchedLibraryItemsResponse>(path, ct);
    }

    // ===== Invite Codes =====

    public Task<List<InviteCode>> GetInviteCodesAsync(CancellationToken ct = default)
        => client.GetAsync<List<InviteCode>>("/api/v1/admin/invite-codes", ct);

    public Task<InviteCode> CreateInviteCodeAsync(CreateInviteCodeRequest request, CancellationToken ct = default)
        => client.PostAsync<InviteCode>("/api/v1/admin/invite-codes", request, ct);

    public Task<InviteCode> UpdateInviteCodeAsync(int id, UpdateInviteCodeRequest request, CancellationToken ct = default)
        => client.PutAsync<InviteCode>($"/api/v1/admin/invite-codes/{id}", request, ct);

    public Task<InviteCode> TopUpInviteCodeAsync(int id, TopUpInviteCodeRequest request, CancellationToken ct = default)
        => client.PostAsync<InviteCode>($"/api/v1/admin/invite-codes/{id}/top-up", request, ct);

    public Task DeleteInviteCodeAsync(int id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/admin/invite-codes/{id}", ct);

    // ===== Subtitle Providers =====

    public Task<SubtitleProvidersResponse> GetSubtitleProvidersAsync(CancellationToken ct = default)
        => client.GetAsync<SubtitleProvidersResponse>("/api/v1/admin/subtitle-providers", ct);

    public Task UpdateSubtitleProviderAsync(string provider, SubtitleProviderUpdateRequest request, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v1/admin/subtitle-providers/{Uri.EscapeDataString(provider)}", request, ct);

    public Task<SubtitleProviderTestResponse> TestSubtitleProviderAsync(string provider, CancellationToken ct = default)
        => client.PostAsync<SubtitleProviderTestResponse>($"/api/v1/admin/subtitle-providers/{Uri.EscapeDataString(provider)}/test", new { }, ct);

    public Task<AdminDownloadedSubtitlesResponse> GetDownloadedSubtitlesAsync(AdminDownloadedSubtitlesFilters filters, CancellationToken ct = default)
    {
        var query = new List<string>
        {
            $"limit={filters.Limit}",
            $"offset={filters.Offset}",
        };
        if (!string.IsNullOrWhiteSpace(filters.Provider))
            query.Add($"provider={Uri.EscapeDataString(filters.Provider)}");
        if (!string.IsNullOrWhiteSpace(filters.Language))
            query.Add($"language={Uri.EscapeDataString(filters.Language)}");
        if (filters.UserId.HasValue)
            query.Add($"user_id={filters.UserId.Value}");
        if (filters.MediaFileId.HasValue)
            query.Add($"media_file_id={filters.MediaFileId.Value}");
        if (!string.IsNullOrWhiteSpace(filters.Query))
            query.Add($"q={Uri.EscapeDataString(filters.Query)}");

        return client.GetAsync<AdminDownloadedSubtitlesResponse>($"/api/v1/admin/subtitles?{string.Join("&", query)}", ct);
    }

    public Task<AdminDownloadedSubtitleUpdateResponse> UpdateDownloadedSubtitleAsync(int id, AdminUpdateDownloadedSubtitleRequest request, CancellationToken ct = default)
        => client.PatchAsync<AdminDownloadedSubtitleUpdateResponse>(
            $"/api/v1/admin/subtitles/{id}",
            new Dictionary<string, object?>
            {
                ["language"] = request.Language,
                ["release_name"] = request.ReleaseName,
                ["hearing_impaired"] = request.HearingImpaired,
            },
            ct);

    public Task<byte[]> DownloadDownloadedSubtitleAsync(int id, CancellationToken ct = default)
        => client.GetBytesAsync($"/api/v1/admin/subtitles/{id}/download", ct);

    public Task DeleteDownloadedSubtitleAsync(int id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/admin/subtitles/{id}", ct);

    // ===== Collections Admin =====

    public Task<ImportMDBListCollectionResponse> ImportMDBListCollectionAsync(ImportMDBListCollectionRequest request, CancellationToken ct = default)
        => client.PostAsync<ImportMDBListCollectionResponse>("/api/v1/admin/collections/import/mdblist", request, ct);

    public Task<ImportTMDBCollectionResponse> ImportTMDBCollectionAsync(ImportTMDBCollectionRequest request, CancellationToken ct = default)
        => client.PostAsync<ImportTMDBCollectionResponse>("/api/v1/admin/collections/import/tmdb", request, ct);

    public Task<ImportTraktCollectionResponse> ImportTraktCollectionAsync(ImportTraktCollectionRequest request, CancellationToken ct = default)
        => client.PostAsync<ImportTraktCollectionResponse>("/api/v1/admin/collections/import/trakt", request, ct);

    public Task<CollectionTemplateCatalog> GetAdminCollectionTemplatesAsync(CancellationToken ct = default)
        => client.GetAsync<CollectionTemplateCatalog>("/api/v1/admin/collections/templates", ct);

    public Task<CollectionTemplateBundleCatalog> GetAdminCollectionTemplateBundlesAsync(CancellationToken ct = default)
        => client.GetAsync<CollectionTemplateBundleCatalog>("/api/v1/admin/collections/template-bundles", ct);

    public Task<ApplyCollectionTemplateBundleResponse> ApplyCollectionTemplateBundleAsync(
        string bundleId, ApplyCollectionTemplateBundleRequest request, CancellationToken ct = default)
        => client.PostAsync<ApplyCollectionTemplateBundleResponse>(
            $"/api/v1/admin/collections/template-bundles/{Uri.EscapeDataString(bundleId)}/apply", request, ct);

    public Task<AdminJob> QueueCollectionTemplateBundleApplyAsync(
        string bundleId, ApplyCollectionTemplateBundleRequest request, CancellationToken ct = default)
        => client.PostAsync<AdminJob>(
            $"/api/v1/admin/collections/template-bundles/{Uri.EscapeDataString(bundleId)}/apply-job", request, ct);

    public Task UploadCollectionImageAsync(string id, string type, byte[] fileBytes, string fileName, string contentType, CancellationToken ct = default)
        => client.PutMultipartNoContentAsync($"/api/v1/admin/collections/{Uri.EscapeDataString(id)}/image?type={Uri.EscapeDataString(type)}", "file", fileName, fileBytes, contentType, ct);

    public Task DeleteCollectionImageAsync(string id, string type, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/admin/collections/{Uri.EscapeDataString(id)}/image?type={Uri.EscapeDataString(type)}", ct);

    // ===== History Import Sources (Admin) =====

    public Task<List<HistoryImportSource>> GetHistoryImportSourcesAsync(CancellationToken ct = default)
        => client.GetAsync<List<HistoryImportSource>>("/api/v1/admin/history-import-sources", ct);

    public Task<HistoryImportSource> CreateHistoryImportSourceAsync(CreateHistoryImportSourceRequest request, CancellationToken ct = default)
        => client.PostAsync<HistoryImportSource>("/api/v1/admin/history-import-sources", request, ct);

    public Task<HistoryImportSource> UpdateHistoryImportSourceAsync(int id, UpdateHistoryImportSourceRequest request, CancellationToken ct = default)
        => client.PutAsync<HistoryImportSource>($"/api/v1/admin/history-import-sources/{id}", request, ct);

    public Task DeleteHistoryImportSourceAsync(int id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/admin/history-import-sources/{id}", ct);

    // Token management
    public Task SetSourceTokenAsync(int sourceId, string token, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v1/admin/history-imports/sources/{sourceId}/token",
            new Dictionary<string, object?> { ["token"] = token }, ct);

    public Task ClearSourceTokenAsync(int sourceId, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/admin/history-imports/sources/{sourceId}/token", ct);

    public Task<PlexAdminLoginResponse> PlexAdminLoginAsync(
        PlexAdminLoginRequest request,
        CancellationToken ct = default)
        => client.PostAsync<PlexAdminLoginResponse>("/api/v1/admin/history-imports/plex/login", request, ct);

    // External user discovery
    public Task<List<HistoryImportExternalUser>> DiscoverExternalUsersAsync(int sourceId, CancellationToken ct = default)
        => client.GetAsync<List<HistoryImportExternalUser>>($"/api/v1/admin/history-imports/sources/{sourceId}/users", ct);

    // Mappings CRUD
    public Task<List<HistoryImportUserMapping>> GetMappingsAsync(int sourceId, CancellationToken ct = default)
        => client.GetAsync<List<HistoryImportUserMapping>>($"/api/v1/admin/history-imports/mappings?source_id={sourceId}", ct);

    public Task<HistoryImportUserMapping> CreateMappingAsync(CreateHistoryImportMappingRequest request, CancellationToken ct = default)
        => client.PostAsync<HistoryImportUserMapping>("/api/v1/admin/history-imports/mappings", request, ct);

    public Task DeleteMappingAsync(int id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/admin/history-imports/mappings/{id}", ct);

    // Run per-mapping
    public Task<HistoryImportRun> RunMappingAsync(int mappingId, CancellationToken ct = default)
        => client.PostAsync<HistoryImportRun>($"/api/v1/admin/history-imports/mappings/{mappingId}/run", new { }, ct);

    // Bulk run all mappings for a source
    public Task<AdminHistoryImportBulkRunResult> BulkRunSourceAsync(int sourceId, CancellationToken ct = default)
        => client.PostAsync<AdminHistoryImportBulkRunResult>($"/api/v1/admin/history-imports/sources/{sourceId}/bulk-run", new { }, ct);

    // Cancel a run
    public Task CancelRunAsync(string runId, CancellationToken ct = default)
        => client.PostNoContentAsync($"/api/v1/admin/history-imports/runs/{Uri.EscapeDataString(runId)}/cancel", new { }, ct);

    // Admin runs (filtered by source)
    public Task<List<HistoryImportRun>> GetAdminRunsAsync(int? sourceId = null, int limit = 20, CancellationToken ct = default)
    {
        var url = "/api/v1/admin/history-imports/runs?limit=" + limit;
        if (sourceId.HasValue) url += $"&source_id={sourceId.Value}";
        return client.GetAsync<List<HistoryImportRun>>(url, ct);
    }

    public Task<AdminServerRestartResponse> RestartServerAsync(CancellationToken ct = default)
        => client.PostAsync<AdminServerRestartResponse>("/api/v1/admin/server/restart", new { }, ct);

    // ===== Node Extras =====

    public Task<List<AdminSession>> GetNodeSessionsAsync(CancellationToken ct = default)
        => client.GetAsync<List<AdminSession>>("/api/v1/admin/node-sessions", ct);

    public Task ForceReloadNodesAsync(CancellationToken ct = default)
        => client.PostNoContentAsync("/api/v1/admin/nodes/force-reload", new { }, ct);

    public Task ForceReloadNodeAsync(int id, CancellationToken ct = default)
        => client.PostNoContentAsync($"/api/v1/admin/nodes/{id}/force-reload", new { }, ct);

    // ===== Playback Control =====

    public Task<AdminSessionCommandResponse> PauseSessionAsync(string sessionId, CancellationToken ct = default)
        => client.PostAsync<AdminSessionCommandResponse>($"/api/v1/admin/sessions/{Uri.EscapeDataString(sessionId)}/pause", new { }, ct);

    public Task<AdminSessionCommandResponse> ResumeSessionAsync(string sessionId, CancellationToken ct = default)
        => client.PostAsync<AdminSessionCommandResponse>($"/api/v1/admin/sessions/{Uri.EscapeDataString(sessionId)}/resume", new { }, ct);

    public Task<AdminSessionCommandResponse> StopSessionAsync(string sessionId, CancellationToken ct = default)
        => client.PostAsync<AdminSessionCommandResponse>($"/api/v1/admin/sessions/{Uri.EscapeDataString(sessionId)}/stop", new { }, ct);

    public Task<AdminSessionCommandResponse> TerminateSessionAsync(string sessionId, CancellationToken ct = default)
        => client.PostAsync<AdminSessionCommandResponse>($"/api/v1/admin/sessions/{Uri.EscapeDataString(sessionId)}/terminate", new { }, ct);

    public Task<AdminSessionCommandResponse> MessageSessionAsync(string sessionId, string message, CancellationToken ct = default)
        => client.PostAsync<AdminSessionCommandResponse>($"/api/v1/admin/sessions/{Uri.EscapeDataString(sessionId)}/message", new { message }, ct);
}

public sealed class AdminSessionCommandResponse
{
    public string CommandId { get; set; } = "";
    public string Status { get; set; } = "";
}

public sealed class RedetectIntroResponse
{
    public string Status { get; set; } = "";
}
