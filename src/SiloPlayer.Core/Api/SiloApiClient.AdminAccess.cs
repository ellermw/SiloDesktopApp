using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

public partial class SiloApiClient
{
    private static readonly HttpRequestOptionsKey<bool> AdminAccessSensitiveOption = new("Silo.AdminAccessSensitive");
    // The security pages never use the ordinary diagnostic/problem-detail path: a hostile server
    // can echo submitted credentials in it. Only a known problem identifier crosses this boundary.
    internal async Task<ApiResponse<T>> AdminAccessSendAsync<T>(MonitorAuthorityLease owner, HttpMethod method, string path,
        object? body = null, string? etag = null, HttpStatusCode expected = HttpStatusCode.OK)
    {
        owner.ThrowIfRetired();
        if (etag != null) AdminValidators.RequireStrong(etag);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(owner.Token); deadline.CancelAfter(TimeSpan.FromSeconds(20));
        using var request = CreateContextBoundRequest(owner.Context, method, path, body,
            etag == null ? null : new Dictionary<string, string> { ["If-Match"] = etag });
        request.Options.Set(AdminAccessSensitiveOption, true);
        try
        {
            using var response = await SendWithRetryAsync(request, deadline.Token, allowTokenRefresh: false).ConfigureAwait(false);
            owner.ThrowIfRetired();
            var bytes = await MonitorLogsReadBoundedAsync(response.Content, owner, 4 * 1024 * 1024, deadline.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                string code = "request_failed";
                try
                {
                    using var problem = JsonDocument.Parse(bytes);
                    if (problem.RootElement.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String)
                    {
                        var name = type.GetString()!.Split('/').Last();
                        if (name is "precondition_failed" or "precondition_required" or "permission_denied" or "authentication_required" or
                            "profile_verification_required" or "break_glass_required" or "provider_already_enabled" or "validation_failed" or "conflict" or
                            "not_found" or "dependency_unavailable" or "invalid_cursor" or "rate_limited") code = name;
                    }
                    // A change to a plugin field the deployment owns is a 422 validation problem whose error entry carries the code managed_by_environment. Only that identifier is read; no server text crosses.
                    if (code == "validation_failed" && problem.RootElement.TryGetProperty("errors", out var entries) && entries.ValueKind == JsonValueKind.Array
                        && entries.EnumerateArray().Any(e => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("code", out var entryCode) && entryCode.ValueKind == JsonValueKind.String && entryCode.GetString() == "managed_by_environment")) code = "managed_by_environment";
                }
                catch (JsonException) { }
                throw new AdminAccessException(code, (int)response.StatusCode, uncertain: method != HttpMethod.Get && ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout));
            }
            if (response.StatusCode != expected) throw new AdminAccessException("invalid_response", uncertain: method != HttpMethod.Get);
            if (expected == HttpStatusCode.NoContent)
            {
                if (bytes.Length != 0) throw new AdminAccessException("invalid_response", uncertain: true);
                return new(default!, null);
            }
            try
            {
                var value = JsonSerializer.Deserialize<T>(bytes, JsonOptions) ?? throw new JsonException();
                owner.ThrowIfRetired(); return new(value, response.Headers.ETag?.ToString());
            }
            catch (JsonException) { throw new AdminAccessException("invalid_response", uncertain: method != HttpMethod.Get); }
        }
        catch (OperationCanceledException) when (!owner.IsCurrent) { throw; }
        catch (AdminAccessException) { throw; }
        catch (Exception) { throw new AdminAccessException("unreachable", uncertain: method != HttpMethod.Get); }
        finally { owner.ThrowIfRetired(); }
    }
}

public sealed class AdminAccessException(string code, int status = 0, bool uncertain = false)
    : Exception(MessageFor(code, status, uncertain))
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public bool Uncertain { get; } = uncertain;
    /// <summary>A definite answer that changed nothing and leaves the screen valid (for example 409 conflict): the admin may correct the cause and try again without a reload.</summary>
    public bool Refusal => !Uncertain && Code is "conflict" or "policy_domain_enabled";
    private static string MessageFor(string code, int status, bool uncertain) => uncertain
        ? "The outcome is unknown. Reload and review the server state before making another change. This request was not resent."
        : code switch
        {
            // Not the official sentence (web.admin.settings.lib.externalSignIn.problem.breakGlassRequired): an existing regression test pins this wording.
            "break_glass_required" => "Keep an enabled break-glass administrator with password sign-in before turning off Silo passwords.",
            "provider_already_enabled" => AdminText.T("web.admin.settings.lib.externalSignIn.problem.providerAlreadyEnabled"),
            "provider_required" => AdminText.T("web.admin.settings.signIn.page.noProviderError"),
            // The server's own sentence for this refusal (updateAdminPluginAuthBinding / updateAdminPluginInstallationConfig).
            "managed_by_environment" => "Set by the environment. Change it there and restart the affected service.",
            "policy_domain_enabled" => "Another override is already enabled for this domain. Disable it first.",
            "conflict" => "The server refused this change because of the current state of the resource. Review it, then try again.",
            _ when status is 412 or 428 => "This resource changed or its revision is unavailable. Reload and review before saving again.",
            _ when status == 401 => "Sign in again, then reload this page.",
            _ when status == 403 => "Your current account and profile cannot make this change. Reload after access is restored.",
            _ when status == 404 => "This resource no longer exists. Reload the list.",
            _ when status == 429 => "The server asked you to wait. Try again later.",
            "validation_failed" => "The server rejected these values. Review the fields before trying again.",
            "invalid_cursor" => "The list changed. Reload from its first page.",
            "invalid_response" => "The server returned an invalid response. Reload before editing.",
            _ => "The request could not be completed. Reload and try again.",
        };
}
