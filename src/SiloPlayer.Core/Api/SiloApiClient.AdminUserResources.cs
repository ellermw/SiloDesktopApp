using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

internal sealed record AdminResourceResponse(JsonElement Body, string? ETag, string? Location, TimeSpan RetryAfter, int Status);
public sealed class AdminResourceOutcomeUnknownException() : Exception("The change may have reached the server. Reload and review before trying again.");

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
                if (write && ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout))
                    throw new AdminResourceOutcomeUnknownException();
                response.Content.Dispose(); response.Content = new ByteArrayContent(bytes);
                try { await ThrowApiException(response, request, deadline.Token).ConfigureAwait(false); }
                catch (ApiException error)
                {
                    // Never retain server text, echoed bodies, credentials or signed URLs in exception chains.
                    throw new ApiException(SafeAdminResourceCode(error.ErrorCode), "The administrator request was rejected.", error.StatusCode)
                    { ProblemType = SafeAdminResourceCode(error.ProblemType) };
                }
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

    private static string SafeAdminResourceCode(string? code) => code is "permission_denied" or "profile_verification_required" or "profile_unverified"
        or "demo_restricted" or "dependency_unavailable" or "validation_failed" or "not_found" or "conflict" or "precondition_failed"
        or "precondition_required" or "capability_disabled" or "capability_not_configured" or "capability_unsupported" or "rate_limited"
        or "authentication_required" or "job_not_cancelable" ? code : "unknown";
}
