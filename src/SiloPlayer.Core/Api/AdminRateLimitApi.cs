using System.Text.Json;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

/// <summary>The rate-limit config changed since it was read (412). Reload and rebase; the write is never replayed.</summary>
public sealed class AdminRateLimitConflictException() : Exception("Rate limit settings changed. Reload and review them before saving again.");
/// <summary>The PATCH may have reached the server but no valid acknowledgement arrived. It is never resent.</summary>
public sealed class AdminRateLimitOutcomeUnknownException() : Exception("The rate limit save outcome is unknown.");
/// <summary>The config read had no strong validator, so it cannot become a writable baseline.</summary>
public sealed class AdminRateLimitValidatorException() : Exception("Reload rate limit settings before editing.");

/// <summary>
/// <c>/api/v2/admin/rate-limits/{config,status}</c>, a resource separate from the batched settings
/// writer. Reads are bounded, lease-bound and fail together; the PATCH sends a captured intent once,
/// without authentication refresh or replay.
/// </summary>
public sealed class AdminRateLimitApi(SiloApiClient client)
{
    internal const string ConfigPath = "/api/v2/admin/rate-limits/config";
    internal const string StatusPath = "/api/v2/admin/rate-limits/status";
    private const int MaxDetail = 300;
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    /// <summary>Reads config and status in parallel; either failing fails the read and cancels the other.</summary>
    public async Task<AdminRateLimitSnapshot> GetAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        lease.ThrowIfRetired();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        timeout.CancelAfter(Deadline);
        async Task<ApiResponse<JsonElement>> Read(string path)
        {
            try { return await client.MonitorLogsSendAsync<JsonElement>(lease, HttpMethod.Get, path, ct: timeout.Token).ConfigureAwait(false); }
            catch
            {
                try { timeout.Cancel(); } catch (ObjectDisposedException) { }
                throw;
            }
        }
        var config = Read(ConfigPath);
        var status = Read(StatusPath);
        try { await Task.WhenAll(config, status).ConfigureAwait(false); }
        catch (Exception)
        {
            lease.ThrowIfRetired();
            ct.ThrowIfCancellationRequested();
            // Report the failure that caused the cancellation rather than the sibling it cancelled.
            var error = new[] { config, status }.Where(task => task.IsFaulted).Select(task => task.Exception!.InnerException!)
                .FirstOrDefault(inner => inner is not OperationCanceledException)
                ?? new[] { config, status }.Select(task => task.Exception?.InnerException).FirstOrDefault(inner => inner != null)
                ?? new OperationCanceledException();
            throw error is ApiException api ? AdminServerSettingsApi.Sanitize(api) : error;
        }
        lease.ThrowIfRetired();
        var configResponse = await config.ConfigureAwait(false);
        if (!AdminValidators.IsStrong(configResponse.ETag)) throw new AdminRateLimitValidatorException();
        var values = AdminRateLimitValues.FromConfig(configResponse.Body);
        var body = (await status.ConfigureAwait(false)).Body;
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("active", out var active) || active.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("Invalid rate limit status.");
        string? activeBackend = null;
        if (body.TryGetProperty("active_backend", out var backend) && backend.ValueKind != JsonValueKind.Null)
            activeBackend = backend.ValueKind == JsonValueKind.String && backend.GetString()!.Length <= 32 ? backend.GetString() : throw new InvalidDataException("Invalid rate limit status.");
        bool? redis = null;
        if (body.TryGetProperty("redis_available", out var available) && available.ValueKind != JsonValueKind.Null)
            redis = available.ValueKind is JsonValueKind.True or JsonValueKind.False ? available.GetBoolean() : throw new InvalidDataException("Invalid rate limit status.");
        return new(values, configResponse.ETag!, active.GetBoolean(), activeBackend, redis);
    }

    /// <summary>Sends one guarded merge. Returns <c>restart_required</c>; the 200 receipt carries no validator.</summary>
    public async Task<bool> PatchAsync(MonitorAuthorityLease lease, AdminRateLimitIntent intent, CancellationToken ct = default)
    {
        AdminValidators.RequireStrong(intent.ETag);
        var body = intent.Values.ToPatchBody();
        lease.ThrowIfRetired();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        timeout.CancelAfter(Deadline);
        ApiResponse<JsonElement> response;
        try
        {
            response = await client.MonitorLogsSendAsync<JsonElement>(lease, HttpMethod.Patch, ConfigPath, body, intent.ETag, timeout.Token).ConfigureAwait(false);
        }
        catch (LoggingSettingsConflictException) { throw new AdminRateLimitConflictException(); }
        catch (OperationCanceledException) when (!lease.IsCurrent || ct.IsCancellationRequested) { throw; }
        catch (ApiException error) when (error.StatusCode >= 500 || error.StatusCode == 408) { throw new AdminRateLimitOutcomeUnknownException(); }
        catch (ApiException error) { throw SanitizeWrite(error); }
        catch (Exception) { throw new AdminRateLimitOutcomeUnknownException(); }
        var root = response.Body;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("restart_required", out var restart) ||
            restart.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            (root.TryGetProperty("status", out var status) && status.ValueKind != JsonValueKind.String))
            throw new AdminRateLimitOutcomeUnknownException();
        return restart.GetBoolean();
    }

    /// <summary>Keeps the server's validation sentence (bounded and redacted) as the one narrow exception to the shared sanitizer.</summary>
    private static ApiException SanitizeWrite(ApiException error)
    {
        var safe = AdminServerSettingsApi.Sanitize(error);
        if (error.StatusCode is not (400 or 422) || string.IsNullOrWhiteSpace(error.ProblemDetail)) return safe;
        var detail = LocalLog.RedactDiagnosticText(error.ProblemDetail.ReplaceLineEndings(" ").Trim());
        if (detail.Length > MaxDetail) detail = detail[..MaxDetail].TrimEnd() + "…";
        return new(safe.ErrorCode, safe.Message, safe.StatusCode) { ProblemType = safe.ProblemType, ProblemDetail = detail };
    }
}
