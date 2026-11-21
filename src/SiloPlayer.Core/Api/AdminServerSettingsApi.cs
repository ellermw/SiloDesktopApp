using System.Text.Json;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

public sealed record AdminSettingsSnapshot(IReadOnlyDictionary<string, string> Values, string ETag)
{
    public override string ToString() => nameof(AdminSettingsSnapshot);
}
/// <summary>
/// The sensitive-status answer. <see cref="ManagedByEnv"/> is every key the server's environment sets (the answer's
/// <c>managed_by_env</c> list plus any key only its <c>managed_by_env_vars</c> map names); <see cref="Environment"/> adds the
/// variable that sets each. An older server that sends no map yields an environment with no names.
/// </summary>
public sealed record AdminSettingsSensitiveStatus(IReadOnlyCollection<string> Configured, IReadOnlyCollection<string> ManagedByEnv, AdminEnvManagedSettings Environment)
{
    public AdminSettingsSensitiveStatus(IReadOnlyCollection<string> configured, IReadOnlyCollection<string> managedByEnv)
        : this(configured, managedByEnv, new AdminEnvManagedSettings(managedByEnv, null)) { }
}
public sealed record AdminSettingsSaveResult(IReadOnlyDictionary<string, string> Values, string ETag, bool RestartRequired)
{
    public override string ToString() => nameof(AdminSettingsSaveResult);
}
public sealed record AdminSettingsConnectionResult(bool Success, string Message)
{
    public override string ToString() => nameof(AdminSettingsConnectionResult);
}
/// <summary>A test-email acknowledgement. <see cref="Message"/> is redacted, bounded server text.</summary>
public sealed record AdminEmailTestResult(bool Ok, long DurationMs, string? Message)
{
    public override string ToString() => nameof(AdminEmailTestResult);
}
/// <summary>The test command may have reached the server, but no valid acknowledgement arrived. It is never resent.</summary>
public sealed class AdminEmailTestOutcomeUnknownException() : Exception("The test email outcome is unknown.");
public sealed class AdminSettingsConflictException() : Exception("Settings changed on the server. Reload and review your changes.");
public sealed class AdminSettingsOutcomeUnknownException() : Exception("The save outcome is unknown. Reload and review before saving again.");

/// <summary>Bounded, lease-bound server settings wire operations. Writes are never replayed.</summary>
public sealed class AdminServerSettingsApi(SiloApiClient client)
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    public async Task<AdminSettingsSnapshot> GetEffectiveAsync(MonitorAuthorityLease lease, IEnumerable<string> keys,
        IEnumerable<string> secrets, CancellationToken ct = default)
    {
        using var timeout = Timeout(lease, ct);
        ApiResponse<Dictionary<string, JsonElement>> response;
        try { response = await client.MonitorLogsSendAsync<Dictionary<string, JsonElement>>(lease, HttpMethod.Get,
            "/api/v2/admin/settings/effective", ct: timeout.Token).ConfigureAwait(false); }
        catch (ApiException error) { throw Sanitize(error); }
        AdminSettingsForm.RequireStrongETag(response.ETag);
        return new(new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(Project(response.Body, keys, secrets)), response.ETag!);
    }

    public async Task<AdminSettingsSensitiveStatus> GetSensitiveStatusAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        using var timeout = Timeout(lease, ct);
        ApiResponse<SensitiveResponse> response;
        try { response = await client.MonitorLogsSendAsync<SensitiveResponse>(lease, HttpMethod.Get,
            "/api/v2/admin/settings/sensitive-status", ct: timeout.Token).ConfigureAwait(false); }
        catch (ApiException error) { throw Sanitize(error); }
        if (response.Body.Configured == null || response.Body.ManagedByEnv == null) throw new InvalidDataException("Invalid sensitive settings status.");
        var environment = new AdminEnvManagedSettings(response.Body.ManagedByEnv, ReadEnvVars(response.Body.ManagedByEnvVars));
        return new(response.Body.Configured, environment.Keys, environment);
    }

    public async Task<LoggingRestartKeys> GetRestartKeysAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        using var timeout = Timeout(lease, ct);
        ApiResponse<RestartResponse> response;
        try { response = await client.MonitorLogsSendAsync<RestartResponse>(lease, HttpMethod.Get,
            "/api/v2/admin/settings/restart-keys", ct: timeout.Token).ConfigureAwait(false); }
        catch (ApiException error) { throw Sanitize(error); }
        if (response.Body.Keys == null || response.Body.Prefixes == null) throw new InvalidDataException("Invalid restart settings status.");
        return new() { Keys = response.Body.Keys, Prefixes = response.Body.Prefixes };
    }

    public async Task<AdminSettingsSaveResult> PutAsync(MonitorAuthorityLease lease, IReadOnlyDictionary<string, string> values,
        string etag, IEnumerable<string> keys, IEnumerable<string> secrets, CancellationToken ct = default)
    {
        AdminSettingsForm.RequireStrongETag(etag);
        var allowed = new HashSet<string>(keys, StringComparer.Ordinal);
        if (values.Count == 0 || values.Keys.Any(key => !allowed.Contains(key))) throw new ArgumentException("Invalid settings write.", nameof(values));
        lease.ThrowIfRetired();
        using var timeout = Timeout(lease, ct);
        try
        {
            var response = await client.MonitorLogsSendAsync<SaveResponse>(lease, HttpMethod.Put,
                "/api/v2/admin/settings", new Dictionary<string, object?> { ["values"] = values }, etag, timeout.Token).ConfigureAwait(false);
            AdminSettingsForm.RequireStrongETag(response.ETag);
            if (response.Body.Values == null || response.Body.RestartRequired == null) throw new InvalidDataException("Invalid settings acknowledgement.");
            return new(new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(Project(response.Body.Values, keys, secrets)), response.ETag!, response.Body.RestartRequired.Value);
        }
        catch (LoggingSettingsConflictException) { throw new AdminSettingsConflictException(); }
        catch (OperationCanceledException) when (!lease.IsCurrent) { throw; }
        catch (ApiException error) when (error.StatusCode >= 500 || error.StatusCode == 408) { throw new AdminSettingsOutcomeUnknownException(); }
        catch (ApiException error) { throw Sanitize(error); }
        catch (Exception) { throw new AdminSettingsOutcomeUnknownException(); }
    }

    public async Task<AdminSettingsConnectionResult> CheckConnectionAsync(MonitorAuthorityLease lease, string kind,
        IReadOnlyDictionary<string, string> values, IReadOnlyCollection<string> dirtyKeys, CancellationToken ct = default)
    {
        if (!ConnectionCheckKeys.TryGetValue(kind, out var allowed)) throw new ArgumentException("Unsupported connection check.", nameof(kind));
        if (values.Keys.Any(key => !allowed.Contains(key)) || dirtyKeys.Any(key => !allowed.Contains(key)))
            throw new ArgumentException("Invalid connection check values.", nameof(values));
        using var timeout = Timeout(lease, ct, TimeSpan.FromSeconds(20));
        ApiResponse<ConnectionResponse> response;
        try
        {
            response = await client.MonitorLogsSendAsync<ConnectionResponse>(lease, HttpMethod.Post,
                "/api/v2/admin/settings/check/" + kind, new Dictionary<string, object?> { ["values"] = values, ["dirty_keys"] = dirtyKeys }, ct: timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (ApiException error) { throw Sanitize(error); }
        catch (Exception) { throw new InvalidDataException("Connection check failed."); }
        if (response.Body.Success == null || response.Body.Message == null) throw new InvalidDataException("Invalid connection result.");
        // Replace submitted literals before diagnostic rewriting can change only part of a credential.
        var message = response.Body.Message;
        if (values.TryGetValue("redis.url", out var secret) && !string.IsNullOrEmpty(secret))
        {
            var secrets = new List<string> { secret };
            if (Uri.TryCreate(secret, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.UserInfo))
            {
                // Servers may echo credentials separately, either escaped or decoded.
                secrets.Add(uri.UserInfo);
                secrets.AddRange(uri.UserInfo.Split(':', 2));
                secrets.AddRange(secrets.ToArray().Select(Uri.UnescapeDataString));
            }
            foreach (var component in secrets.Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).OrderByDescending(value => value.Length))
                message = message.Replace(component, "<redacted>", StringComparison.Ordinal);
        }
        // S3 credentials are write-only: a submitted access or secret key is never shown back.
        foreach (var credential in ConnectionCheckSecrets.Select(key => values.GetValueOrDefault(key, ""))
            .Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).OrderByDescending(value => value.Length))
            message = message.Replace(credential, "<redacted>", StringComparison.Ordinal);
        message = LocalLog.RedactDiagnosticText(message);
        if (message.Length > 500) message = message[..500] + "…";
        return new(response.Body.Success.Value, message);
    }

    /// <summary>Keys each supported connection check may submit (official RedisGroup and StorageSettings S3Group).</summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> ConnectionCheckKeys = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
    {
        ["redis"] = new HashSet<string>(StringComparer.Ordinal) { "redis.url", "redis.db" },
        ["mdblist"] = new HashSet<string>(StringComparer.Ordinal) { "mdblist.api_key" },
        ["s3_public"] = new HashSet<string>(AdminStorageRules.PublicS3Keys, StringComparer.Ordinal),
        ["s3_private"] = new HashSet<string>(AdminStorageRules.PrivateS3Keys, StringComparer.Ordinal),
    };
    private static readonly string[] ConnectionCheckSecrets = AdminStorageRules.Secrets.Append("mdblist.api_key").ToArray();

    /// <summary>
    /// Sends one synchronous SMTP test through the server's SAVED settings. The body
    /// carries only the trimmed recipient. There is no authentication replay or retry;
    /// a timeout or malformed acknowledgement is an unknown outcome.
    /// </summary>
    public async Task<AdminEmailTestResult> SendTestEmailAsync(MonitorAuthorityLease lease, string recipient, CancellationToken ct = default)
    {
        var to = AdminEmailAddress.Trim(recipient);
        if (!AdminEmailAddress.IsValid(to)) throw new ArgumentException("Invalid test email recipient.", nameof(recipient));
        lease.ThrowIfRetired();
        using var timeout = Timeout(lease, ct, TimeSpan.FromSeconds(45)); // server SMTP send deadline is 30s
        ApiResponse<EmailTestResponse> response;
        try
        {
            response = await client.MonitorLogsSendAsync<EmailTestResponse>(lease, HttpMethod.Post,
                "/api/v2/admin/email/test", new { to }, ct: timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!lease.IsCurrent || ct.IsCancellationRequested) { throw; }
        catch (ApiException error) when (error.StatusCode is 408 or 502 or 504) { throw new AdminEmailTestOutcomeUnknownException(); }
        catch (ApiException error) { throw Sanitize(error); }
        catch (Exception) { throw new AdminEmailTestOutcomeUnknownException(); }
        var body = response.Body;
        if (body.Ok == null || body.DurationMs is not { } duration || duration < 0) throw new AdminEmailTestOutcomeUnknownException();
        string? message = null;
        if (!string.IsNullOrWhiteSpace(body.Message))
        {
            message = LocalLog.RedactDiagnosticText(body.Message.Trim());
            if (message.Length > 500) message = message[..500] + "…";
        }
        return new(body.Ok.Value, duration, message);
    }

    private static CancellationTokenSource Timeout(MonitorAuthorityLease lease, CancellationToken ct, TimeSpan? deadline = null)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        linked.CancelAfter(deadline ?? Deadline);
        return linked;
    }

    internal static string SafeCode(string? code) => code is "permission_denied" or "profile_unverified" or "profile_verification_required"
        or "demo_restricted" or "dependency_unavailable" or "validation_failed" or "precondition_required" or "precondition_failed"
        or "capability_disabled" or "capability_not_configured" or "capability_unsupported" or "rate_limited" or "authentication_required"
        ? code : "unknown";
    internal static ApiException Sanitize(ApiException error) => new(SafeCode(error.ErrorCode), "Settings request failed.", error.StatusCode)
    {
        ProblemType = SafeCode(error.ProblemType) is var code && code != "unknown" ? code : null
    };

    private static Dictionary<string, string> Project(Dictionary<string, JsonElement> values, IEnumerable<string> keys, IEnumerable<string> secrets)
    {
        var allowed = new HashSet<string>(keys, StringComparer.Ordinal);
        allowed.ExceptWith(secrets);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            if (!allowed.Contains(key)) continue;
            if (value.ValueKind == JsonValueKind.String) result[key] = value.GetString()!;
            else if (value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False) result[key] = value.GetRawText();
        }
        return result;
    }

    /// <summary>
    /// Reads the <c>managed_by_env_vars</c> object (setting key to variable name). The official client does not validate the
    /// answer: an absent map, and a map that is not an object, name no variable and the <c>managed_by_env</c> list alone decides what
    /// is managed (<c>useSettingsForm.ts:65</c>). This client does the same instead of failing the whole status read, which would lock
    /// or block every page that reads it. A value that is not a usable name is kept as "managed, name unknown".
    /// </summary>
    internal static IReadOnlyList<KeyValuePair<string, string?>>? ReadEnvVars(JsonElement? map)
    {
        if (map is not { } element || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        if (element.ValueKind != JsonValueKind.Object) return null;
        var entries = new List<KeyValuePair<string, string?>>();
        foreach (var property in element.EnumerateObject())
            entries.Add(new(property.Name, property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null));
        return entries;
    }

    private sealed record SensitiveResponse { public string[]? Configured { get; init; } public string[]? ManagedByEnv { get; init; } public JsonElement? ManagedByEnvVars { get; init; } }
    private sealed record RestartResponse { public string[]? Keys { get; init; } public string[]? Prefixes { get; init; } }
    private sealed record SaveResponse { public Dictionary<string, JsonElement>? Values { get; init; } public bool? RestartRequired { get; init; } }
    private sealed record EmailTestResponse { public bool? Ok { get; init; } public long? DurationMs { get; init; } public string? Message { get; init; } }
    private sealed record ConnectionResponse { public bool? Success { get; init; } public string? Message { get; init; } }
}
