using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

internal sealed record AdminResourceResponse(JsonElement Body, string? ETag, string? Location, TimeSpan RetryAfter, int Status);
public sealed class AdminResourceOutcomeUnknownException() : Exception("The change may have reached the server. Reload and review before trying again.");
/// <summary>A configuration larger than the desktop safety bound; its message is the official text, shown as is.</summary>
public sealed class AdminResourceTooManyException(string message) : Exception(message);
/// <summary>
/// The second request of a two-request action failed after the first was applied. <see cref="Key"/> is the official message naming what
/// was not saved; the reason comes from <see cref="Exception.InnerException"/>, which also decides whether a reload is required.
/// </summary>
public sealed class AdminResourceFollowUpException(string key, Exception inner) : Exception("A follow-up request failed.", inner)
{
    public string Key { get; } = key;
}

public partial class SiloApiClient
{
    private static readonly HttpRequestOptionsKey<bool> AdminResourceSensitiveOption = new("SiloPlayer.AdminResourceSensitive");
    /// <summary>Bounded, once-only acting-admin transport; no authentication replay or mutating retry.</summary>
    internal async Task<AdminResourceResponse> AdminResourceSendAsync(MonitorAuthorityLease lease, HttpMethod method, string path,
        object? body = null, string? etag = null, CancellationToken ct = default)
    {
        lease.ThrowIfRetired();
        if (etag != null) AdminValidators.RequireStrong(etag);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = CreateContextBoundRequest(lease.Context, method, path, body,
            etag == null ? null : new Dictionary<string, string> { ["If-Match"] = etag });
        request.Options.Set(AdminResourceSensitiveOption, true);
        var write = method != HttpMethod.Get;
        try
        {
            using var response = await SendWithRetryAsync(request, deadline.Token, allowTokenRefresh: false).ConfigureAwait(false);
            lease.ThrowIfRetired();
            var bytes = await MonitorLogsReadBoundedAsync(response.Content, lease, 2 * 1024 * 1024, deadline.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                // Only a timeout or a server fault after sending leaves a write's outcome unknown; any other 4xx is a definite refusal.
                if (write && ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout))
                    throw new AdminResourceOutcomeUnknownException();
                response.Content.Dispose(); response.Content = new ByteArrayContent(bytes);
                try { await ThrowApiException(response, request, deadline.Token).ConfigureAwait(false); }
                catch (ApiException error) { throw SafeAdminResourceRefusal(error); }
            }
            deadline.Token.ThrowIfCancellationRequested(); lease.ThrowIfRetired();
            using var document = JsonDocument.Parse(bytes.Length == 0 ? "{}"u8.ToArray() : bytes);
            var value = document.RootElement.Clone();
            var retry = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5);
            retry = TimeSpan.FromSeconds(Math.Clamp(retry.TotalSeconds, 5, 60));
            return new(value, response.Headers.ETag?.ToString(), response.Headers.Location?.OriginalString, retry, (int)response.StatusCode);
        }
        catch (ApiException) { throw; }
        catch (AdminResourceOutcomeUnknownException) { throw; }
        catch (Exception) when (write && lease.IsCurrent) { throw new AdminResourceOutcomeUnknownException(); }
        finally { lease.ThrowIfRetired(); }
    }

    /// <summary>
    /// A refusal as the admin may read it: the server's own problem title, detail and field errors (they say what to correct), without
    /// echoed bodies, credentials or signed URLs, which are dropped, never summarized.
    /// </summary>
    internal static ApiException SafeAdminResourceRefusal(ApiException error)
    {
        var detail = SafeAdminResourceText(error.ProblemDetail);
        var fields = error.FieldErrors.Take(16).Select(field => new ApiFieldError(SafeAdminResourceLocation(field.Location), SafeAdminResourceFieldCode(field.Code), SafeAdminResourceText(field.Detail))).ToArray();
        return new ApiException(SafeAdminResourceCode(error.ErrorCode), detail ?? "The administrator request was rejected.", error.StatusCode, retryAfterSeconds: error.RetryAfterSeconds)
        {
            ProblemType = SafeAdminResourceCode(error.ProblemType), ProblemTitle = SafeAdminResourceText(error.ProblemTitle), ProblemDetail = detail, FieldErrors = fields,
        };
    }

    private static readonly Regex UnsafeAdminResourceText = new(@"://|token|password|secret|bearer|authorization|api[ _-]?key|signature|\bsig=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static string? SafeAdminResourceText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var value = text.Trim();
        return value.Length > 300 || value.Any(char.IsControl) || UnsafeAdminResourceText.IsMatch(value) ? null : value;
    }
    private static string? SafeAdminResourceLocation(string? text) => text != null && Regex.IsMatch(text, @"^[A-Za-z0-9_.\[\]-]{1,128}$") ? text : null;
    private static string SafeAdminResourceFieldCode(string? code) => code != null && Regex.IsMatch(code, @"^[a-z0-9_.-]{1,64}$") ? code : "invalid";

    private static string SafeAdminResourceCode(string? code) => code is "permission_denied" or "profile_verification_required" or "profile_unverified"
        or "demo_restricted" or "dependency_unavailable" or "validation_failed" or "not_found" or "conflict" or "precondition_failed"
        or "precondition_required" or "capability_disabled" or "capability_not_configured" or "capability_unsupported" or "rate_limited"
        or "authentication_required" or "job_not_cancelable" ? code : "unknown";
}
