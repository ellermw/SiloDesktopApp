using System.Text.Json;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Api;

public enum RecommendationJobKind { Embeddings, TasteProfiles, Cowatch, Recommendations }
public sealed record AdminAiProvider(string Id, string Type, string Name, string BaseUrl, bool HasApiKey, IReadOnlyList<string> Capabilities, IReadOnlyList<string> UsedBy);
public sealed record AdminAiProviderType(string Type, string Label, string DefaultBaseUrl, IReadOnlyList<string> Capabilities, bool KeyRequired);
public sealed record AdminAiProviders(IReadOnlyList<AdminAiProvider> Providers, IReadOnlyList<AdminAiProviderType> Types);
public sealed record AdminAiModel(string Id, string Kind);
public sealed record AdminAiProviderCheck(bool Success, string Message, IReadOnlyList<AdminAiModel> Models);
public sealed record AdminAiProviderDraft(string Type, string Name, string BaseUrl, string? ApiKey, IReadOnlyList<string> Capabilities)
{
    public override string ToString() => nameof(AdminAiProviderDraft);
}
public sealed record AdminRecommendationJob(bool Running, long Count, long? Total, string? LastStatus, string? LastError)
{
    public AdminRecommendationRun? LastRun { get; init; }
    public AdminRecommendationRun? LastPartialRun { get; init; }
}
public sealed record AdminRecommendationStatus(AdminRecommendationJob Embeddings, AdminRecommendationJob TasteProfiles,
    AdminRecommendationJob Cowatch, AdminRecommendationJob Recommendations, string LockConflict, long StaleSweepPending, string? CacheRefreshedAt)
{
    public AdminRecommendationRun? StaleSweepLastRun { get; init; }
}
public sealed record AdminJellyfinWebStatus(bool Installed, bool Enabled, string? Version, string? InstallRoot, string? ActiveDirectory,
    bool OperationRunning, string? Operation, long? Progress, string? Error)
{
    public bool ApiEnabled { get; init; }
    public string ApiState { get; init; } = "";
    public string WebState { get; init; } = "";
    public string Listen { get; init; } = "";
    public string PublicUrl { get; init; } = "";
    public string PinnedVersion { get; init; } = "";
    public bool InstallerReady { get; init; }
    public bool RestartRequired { get; init; }
    public IReadOnlyList<AdminJellyfinPrerequisite> Prerequisites { get; init; } = [];
    public IReadOnlyDictionary<string, string> Details { get; init; } = new Dictionary<string, string>();
    public string Phase { get; init; } = "";
    public string OperationMessage { get; init; } = "";
    public string OperationId { get; init; } = "";
    public string OperationState { get; init; } = "";
    public string? OperationStartedAt { get; init; }
    public string? OperationCompletedAt { get; init; }
}
public sealed class AdminIntegrationsOutcomeUnknownException() : Exception("The command outcome is unknown. Reload and review before trying again.");

/// <summary>Bounded admin-only transports for Configure integrations. All mutating calls are intentionally one-shot.</summary>
public sealed partial class AdminIntegrationsApi(SiloApiClient client)
{
    private const int MaxBytes = 1024 * 1024;
    private static readonly TimeSpan ReadDeadline = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CommandDeadline = TimeSpan.FromSeconds(30);

    public async Task<AdminAiProviders> GetProvidersAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        var body = await ReadAsync<ProvidersWire>(lease, HttpMethod.Get, "/api/v2/admin/ai/providers", null, ReadDeadline, ct).ConfigureAwait(false);
        if (body.Providers == null || body.Types == null || body.Providers.Length > 128 || body.Types.Length > 32) throw new InvalidDataException("Invalid AI provider list.");
        return new(body.Providers.Select(wire => Provider(wire)).ToArray(), body.Types.Select(Type).ToArray());
    }

    public async Task<AdminAiProviderCheck> GetProviderModelsAsync(MonitorAuthorityLease lease, string id, CancellationToken ct = default) =>
        Check(await ReadAsync<ProviderCheckWire>(lease, HttpMethod.Get, ProviderPath(id) + "/models", null, ReadDeadline, ct).ConfigureAwait(false));

    public async Task<AdminAiProviderCheck> CheckProviderAsync(MonitorAuthorityLease lease, AdminAiProviderDraft draft, string? id = null, CancellationToken ct = default)
    {
        ValidateDraft(draft);
        return Check(await CommandAsync<ProviderCheckWire>(lease, HttpMethod.Post, "/api/v2/admin/ai/providers/check",
            new { id, type = draft.Type, base_url = draft.BaseUrl, api_key = draft.ApiKey }, ct).ConfigureAwait(false), draft.ApiKey);
    }

    public async Task<AdminAiProvider> CreateProviderAsync(MonitorAuthorityLease lease, AdminAiProviderDraft draft, CancellationToken ct = default)
    {
        ValidateDraft(draft);
        var result = await CommandAsync<ProviderWire>(lease, HttpMethod.Post, "/api/v2/admin/ai/providers",
            new { type = draft.Type, name = draft.Name, base_url = draft.BaseUrl, api_key = draft.ApiKey, capabilities = draft.Capabilities }, ct).ConfigureAwait(false);
        try { return Provider(result, draft.ApiKey); } catch (InvalidDataException) { throw new AdminIntegrationsOutcomeUnknownException(); }
    }

    public async Task<AdminAiProvider> UpdateProviderAsync(MonitorAuthorityLease lease, string id, AdminAiProviderDraft draft, CancellationToken ct = default)
    {
        ValidateDraft(draft);
        var body = new Dictionary<string, object?> { ["name"] = draft.Name, ["base_url"] = draft.BaseUrl };
        if (draft.ApiKey != null) body["api_key"] = draft.ApiKey;
        if (draft.Type == "custom") body["capabilities"] = draft.Capabilities;
        var result = await CommandAsync<ProviderWire>(lease, HttpMethod.Patch, ProviderPath(id), body, ct).ConfigureAwait(false);
        try { return Provider(result, draft.ApiKey); } catch (InvalidDataException) { throw new AdminIntegrationsOutcomeUnknownException(); }
    }

    public async Task DeleteProviderAsync(MonitorAuthorityLease lease, string id, CancellationToken ct = default)
    {
        lease.ThrowIfRetired(); using var timeout = Timeout(lease, ct, CommandDeadline);
        try { await client.AdminIntegrationsDeleteAsync(lease, ProviderPath(id), MaxBytes, timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!lease.IsCurrent || ct.IsCancellationRequested) { throw; }
        catch (ApiException error) when (error.StatusCode is 408 or >= 500) { throw new AdminIntegrationsOutcomeUnknownException(); }
        catch (ApiException error) { throw Sanitize(error); }
        catch (Exception) { throw new AdminIntegrationsOutcomeUnknownException(); }
    }

    public async Task<AdminRecommendationStatus> GetRecommendationsStatusAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        var body = await ReadAsync<RecommendationStatusWire>(lease, HttpMethod.Get, "/api/v2/admin/recommendations/status", null, ReadDeadline, ct).ConfigureAwait(false);
        if (body.Embeddings == null || body.TasteProfiles == null || body.Cowatch == null || body.Recommendations == null) throw new InvalidDataException("Invalid recommendations status.");
        return new(Job(body.Embeddings), Job(body.TasteProfiles), Job(body.Cowatch), Job(body.Recommendations),
            Safe(body.LockConflict), Math.Max(0, body.StaleSweep?.Pending ?? 0), Bound(body.CacheRefreshedAt, 128)) { StaleSweepLastRun = Run(body.StaleSweep?.LastRun) };
    }

    public async Task TriggerRecommendationJobAsync(MonitorAuthorityLease lease, RecommendationJobKind kind, CancellationToken ct = default)
    {
        var path = kind switch
        {
            RecommendationJobKind.Embeddings => "/api/v2/admin/recommendations/trigger/embeddings",
            RecommendationJobKind.TasteProfiles => "/api/v2/admin/recommendations/trigger/taste-profiles",
            RecommendationJobKind.Cowatch => "/api/v2/admin/recommendations/trigger/cowatch",
            RecommendationJobKind.Recommendations => "/api/v2/admin/recommendations/trigger/recommendations",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var response = await CommandAsync<JobStartWire>(lease, HttpMethod.Post, path, null, ct).ConfigureAwait(false);
        if (!string.Equals(response.Status, "started", StringComparison.Ordinal)) throw new AdminIntegrationsOutcomeUnknownException();
    }

    public async Task<AdminRecommendationReset> ResetEmbeddingsAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        var result = await CommandAsync<ResetWire>(lease, HttpMethod.Post, "/api/v2/admin/recommendations/embeddings/reset", null, ct).ConfigureAwait(false);
        if (result.Embeddings is not >= 0 || result.TasteProfiles is not >= 0 || result.TasteClusters is not >= 0 || result.CachedRows is not >= 0) throw new AdminIntegrationsOutcomeUnknownException();
        return new(result.Embeddings.Value, result.TasteProfiles.Value, result.TasteClusters.Value, result.CachedRows.Value);
    }

    public async Task<AdminJellyfinWebStatus> GetJellyfinWebStatusAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        var body = await ReadAsync<JellyfinWebWire>(lease, HttpMethod.Get, "/api/v2/admin/jellyfin-compat/status", null, ReadDeadline, ct).ConfigureAwait(false);
        return WebStatus(body);
    }

    public async Task<AdminJellyfinWebStatus> InstallJellyfinWebAsync(MonitorAuthorityLease lease, string version, CancellationToken ct = default)
    {
        if (version.Trim().Length > 64) throw new ArgumentException("Invalid web version.", nameof(version));
        var body = new Dictionary<string, object>(); if (version.Trim() != "") body["version"] = version.Trim();
        return WebReceipt(await CommandAsync<JellyfinWebWire>(lease, HttpMethod.Post, "/api/v2/admin/jellyfin-compat/web/install", body, ct).ConfigureAwait(false), "install");
    }
    public async Task<AdminJellyfinWebStatus> RemoveJellyfinWebAsync(MonitorAuthorityLease lease, CancellationToken ct = default) =>
        WebReceipt(await CommandAsync<JellyfinWebWire>(lease, HttpMethod.Post, "/api/v2/admin/jellyfin-compat/web/remove", null, ct).ConfigureAwait(false), "remove");

    private async Task<T> ReadAsync<T>(MonitorAuthorityLease lease, HttpMethod method, string path, object? body, TimeSpan deadline, CancellationToken ct)
    {
        lease.ThrowIfRetired(); using var timeout = Timeout(lease, ct, deadline);
        try { return (await client.AdminIntegrationsSendAsync<T>(lease, method, path, body, MaxBytes, timeout.Token).ConfigureAwait(false)).Body; }
        catch (ApiException error) { throw Sanitize(error); }
    }

    private async Task<T> CommandAsync<T>(MonitorAuthorityLease lease, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        lease.ThrowIfRetired(); using var timeout = Timeout(lease, ct, CommandDeadline);
        try { return (await client.AdminIntegrationsSendAsync<T>(lease, method, path, body, MaxBytes, timeout.Token).ConfigureAwait(false)).Body; }
        catch (OperationCanceledException) when (!lease.IsCurrent || ct.IsCancellationRequested) { throw; }
        catch (ApiException error) when (error.StatusCode is 408 or >= 500) { throw new AdminIntegrationsOutcomeUnknownException(); }
        catch (ApiException error) { throw Sanitize(error); }
        catch (Exception) { throw new AdminIntegrationsOutcomeUnknownException(); }
    }

    private static AdminAiProvider Provider(ProviderWire? wire, string? submittedKey = null)
    {
        if (wire?.Id is not { Length: > 0 and <= 64 } id || wire.Type is not { Length: > 0 and <= 32 } type || string.IsNullOrWhiteSpace(wire.Name) || wire.BaseUrl is not { Length: > 0 and <= 2048 } baseUrl || wire.HasApiKey == null)
            throw new InvalidDataException("Invalid AI provider.");
        var capabilities = Strings(wire.Capabilities, 3, 32);
        var usedBy = Strings(wire.UsedBy, 3, 32);
        if (!SiloPlayer.Core.Services.Admin.AdminIntegrationPolicy.IsProviderEndpoint(baseUrl) ||
            !string.IsNullOrEmpty(submittedKey) && new[] { id, type, wire.Name, baseUrl }.Concat(capabilities).Concat(usedBy).Any(value => value.Contains(submittedKey, StringComparison.Ordinal)))
            throw new InvalidDataException("Invalid AI provider.");
        return new(id, type, Safe(wire.Name, 80), baseUrl, wire.HasApiKey.Value, capabilities, usedBy);
    }
    private static AdminAiProviderType Type(ProviderTypeWire? wire)
    {
        if (wire?.Type is not { Length: > 0 and <= 32 } type || string.IsNullOrWhiteSpace(wire.Label) || wire.KeyRequired == null) throw new InvalidDataException("Invalid AI provider type.");
        if (!string.IsNullOrWhiteSpace(wire.DefaultBaseUrl) && !SiloPlayer.Core.Services.Admin.AdminIntegrationPolicy.IsProviderEndpoint(wire.DefaultBaseUrl)) throw new InvalidDataException("Invalid provider endpoint.");
        return new(type, Safe(wire.Label, 80), Bound(wire.DefaultBaseUrl, 2048) ?? "", Strings(wire.Capabilities, 3, 32), wire.KeyRequired.Value);
    }
    private static AdminAiProviderCheck Check(ProviderCheckWire? wire, string? submittedKey = null)
    {
        if (wire?.Success == null || wire.Models == null) throw new InvalidDataException("Invalid AI provider check.");
        var message = wire.Message ?? "";
        if (!string.IsNullOrEmpty(submittedKey)) message = message.Replace(submittedKey, "<redacted>", StringComparison.Ordinal);
        return new(wire.Success.Value, LocalLog.RedactDiagnosticText(Bound(message, 500) ?? ""), wire.Models.Where(model =>
            string.IsNullOrEmpty(submittedKey) || (model?.Id?.Contains(submittedKey, StringComparison.Ordinal) != true &&
                model?.Kind?.Contains(submittedKey, StringComparison.Ordinal) != true)).Take(256).Select(model =>
        {
            if (model?.Id is not { Length: > 0 and <= 256 } id) throw new InvalidDataException("Invalid AI model.");
            return new AdminAiModel(id, Bound(model.Kind, 32) ?? "");
        }).ToArray());
    }
    private static AdminRecommendationJob Job(JobWire job)
    {
        if (job.Running == null || job.Count is < 0 || job.Total is < 0) throw new InvalidDataException("Invalid recommendation job.");
        return new(job.Running.Value, job.Count ?? 0, job.Total, Bound(job.LastRun?.Status, 64), Safe(job.LastRun?.Error)) { LastRun = Run(job.LastRun), LastPartialRun = Run(job.LastPartialRun) };
    }
    private static void ValidateDraft(AdminAiProviderDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.Type) || draft.Type.Length > 32 || !SiloPlayer.Core.Services.Admin.AdminIntegrationPolicy.IsProviderEndpoint(draft.BaseUrl) || draft.Name.Length > 80 || draft.ApiKey?.Length > 4096 || draft.Capabilities.Count > 3)
            throw new ArgumentException("Invalid AI provider draft.", nameof(draft));
    }
    private static string ProviderPath(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64) throw new ArgumentException("Invalid AI provider id.", nameof(id));
        return "/api/v2/admin/ai/providers/" + Uri.EscapeDataString(id);
    }
    private static IReadOnlyList<string> Strings(string[]? values, int maxItems, int maxLength)
    {
        if (values == null || values.Length > maxItems || values.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > maxLength)) throw new InvalidDataException("Invalid string collection.");
        return Array.AsReadOnly(values.Distinct(StringComparer.Ordinal).ToArray());
    }
    private static string? Bound(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Length <= max ? value : value[..max] + "…";
    private static CancellationTokenSource Timeout(MonitorAuthorityLease lease, CancellationToken ct, TimeSpan deadline) { var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct); linked.CancelAfter(deadline); return linked; }
    private static ApiException Sanitize(ApiException error) => new(AdminServerSettingsApi.SafeCode(error.ErrorCode), "Integration request failed.", error.StatusCode) { ProblemType = AdminServerSettingsApi.SafeCode(error.ProblemType) };

    private sealed record ProvidersWire { public ProviderWire?[]? Providers { get; init; } public ProviderTypeWire?[]? Types { get; init; } }
    private sealed record ProviderWire { public string? Id { get; init; } public string? Type { get; init; } public string? Name { get; init; } public string? BaseUrl { get; init; } public bool? HasApiKey { get; init; } public string[]? Capabilities { get; init; } public string[]? UsedBy { get; init; } }
    private sealed record ProviderTypeWire { public string? Type { get; init; } public string? Label { get; init; } public string? DefaultBaseUrl { get; init; } public string[]? Capabilities { get; init; } public bool? KeyRequired { get; init; } }
    private sealed record ProviderCheckWire { public bool? Success { get; init; } public string? Message { get; init; } public ModelWire?[]? Models { get; init; } }
    private sealed record ModelWire { public string? Id { get; init; } public string? Kind { get; init; } }
    private sealed record RecommendationStatusWire { public JobWire? Embeddings { get; init; } public JobWire? TasteProfiles { get; init; } public JobWire? Cowatch { get; init; } public JobWire? Recommendations { get; init; } public string? LockConflict { get; init; } public SweepWire? StaleSweep { get; init; } public string? CacheRefreshedAt { get; init; } }
    private sealed record JobWire { public bool? Running { get; init; } public long? Count { get; init; } public long? Total { get; init; } public RunWire? LastRun { get; init; } public RunWire? LastPartialRun { get; init; } }
    private sealed record RunWire { public string? Status { get; init; } public string? Error { get; init; } public string? StartedAt { get; init; } public string? CompletedAt { get; init; } public Dictionary<string, JsonElement>? Result { get; init; } }
    private sealed record SweepWire { public long? Pending { get; init; } public RunWire? LastRun { get; init; } }
    private sealed record JobStartWire { public string? Status { get; init; } }
    private sealed record ResetWire { public long? Embeddings { get; init; } public long? TasteProfiles { get; init; } public long? TasteClusters { get; init; } public long? CachedRows { get; init; } }
    private sealed record OperationWire { public string? Id { get; init; } public string? State { get; init; } public string? Kind { get; init; } public long? ProgressPercent { get; init; } public string? Error { get; init; } public string? Phase { get; init; } public string? Message { get; init; } public string? StartedAt { get; init; } public string? CompletedAt { get; init; } }
}
