using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Api;

/// <summary>A layout write the server refused because the validator was stale (412) or missing (428).</summary>
public sealed class DashboardLayoutConflictException(int statusCode, string? currentETag, string message)
    : ApiException(statusCode == 428 ? "precondition_required" : "stale_version", message, statusCode)
{
    /// <summary>The server's current validator when it supplied one; never applied automatically.</summary>
    public string? CurrentETag { get; } = currentETag;
}

/// <summary>
/// Transport the Admin Monitor dashboard needs beyond the shared client:
/// conditional layout reads (304), validator-guarded 204 writes that return a
/// new ETag without a body, and context-bound reads/actions that re-check the
/// captured request context after every response.
/// </summary>
public partial class SiloApiClient
{
    /// <summary>Context-bound JSON read. Token refresh is allowed; a changed context cancels.</summary>
    public async Task<T> GetDashboardJsonAsync<T>(ApiRequestContext context, string path, CancellationToken ct)
    {
        using var request = CreateContextBoundRequest(context, HttpMethod.Get, path, null);
        using var response = await SendWithRetryAsync(request, ct).ConfigureAwait(false);
        if (!IsCurrentContext(context)) throw new OperationCanceledException("API context changed.", ct);
        if (!response.IsSuccessStatusCode) await ThrowApiException(response, request, ct).ConfigureAwait(false);
        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct).ConfigureAwait(false);
        if (!IsCurrentContext(context)) throw new OperationCanceledException("API context changed.", ct);
        return value ?? throw new InvalidDataException("Empty API response.");
    }

    /// <summary>Context-bound read that also returns a strong validator.</summary>
    public async Task<(T Value, string ETag)> GetDashboardJsonWithETagAsync<T>(ApiRequestContext context, string path, CancellationToken ct)
    {
        using var request = CreateContextBoundRequest(context, HttpMethod.Get, path, null);
        using var response = await SendWithRetryAsync(request, ct).ConfigureAwait(false);
        if (!IsCurrentContext(context)) throw new OperationCanceledException("API context changed.", ct);
        if (!response.IsSuccessStatusCode) await ThrowApiException(response, request, ct).ConfigureAwait(false);
        var etag = StrongValidator(response);
        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct).ConfigureAwait(false);
        if (!IsCurrentContext(context)) throw new OperationCanceledException("API context changed.", ct);
        if (etag == null) throw new InvalidDataException("Server response is missing its version.");
        return (value ?? throw new InvalidDataException("Empty API response."), etag);
    }

    /// <summary>GET /admin/dashboard/layout, optionally conditional on the displayed validator.</summary>
    public async Task<DashboardLayoutRead> GetDashboardLayoutAsync(ApiRequestContext context, string path,
        string? ifNoneMatch, CancellationToken ct)
    {
        var headers = string.IsNullOrWhiteSpace(ifNoneMatch) ? null
            : new Dictionary<string, string> { ["If-None-Match"] = ifNoneMatch };
        using var request = CreateContextBoundRequest(context, HttpMethod.Get, path, null, headers);
        using var response = await SendWithRetryAsync(request, ct).ConfigureAwait(false);
        if (!IsCurrentContext(context)) throw new OperationCanceledException("API context changed.", ct);
        if (response.StatusCode == HttpStatusCode.NotModified) return new DashboardLayoutRead(true, null);
        if (!response.IsSuccessStatusCode) await ThrowApiException(response, request, ct).ConfigureAwait(false);
        var etag = StrongValidator(response) ?? throw new InvalidDataException("Server layout validator missing.");
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);
        if (!IsCurrentContext(context)) throw new OperationCanceledException("API context changed.", ct);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid layout response.");
        JsonElement? layout = root.TryGetProperty("layout", out var layoutElement) && layoutElement.ValueKind == JsonValueKind.Object
            ? layoutElement.Clone() : null;
        DateTimeOffset? updatedAt = root.TryGetProperty("updated_at", out var updated) && updated.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(updated.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var parsed) ? parsed : null;
        return new DashboardLayoutRead(false, new DashboardServerLayout(layout, updatedAt, etag));
    }

    /// <summary>
    /// PUT a layout under the captured validator. Never replays on token refresh;
    /// 204 must carry the committed validator, otherwise the outcome is unknown.
    /// </summary>
    public async Task<string> PutDashboardLayoutAsync(ApiRequestContext context, string path, string layoutJson,
        string ifMatch, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ifMatch) || ifMatch == "*" || ifMatch.StartsWith("W/", StringComparison.Ordinal))
            throw new ArgumentException("A strong layout validator is required.", nameof(ifMatch));
        using var request = CreateContextBoundRequest(context, HttpMethod.Put, path, null,
            new Dictionary<string, string> { ["If-Match"] = ifMatch });
        request.Content = new StringContent("{\"layout\":" + layoutJson + "}", Encoding.UTF8, "application/json");
        using var response = await SendWithRetryAsync(request, ct, allowTokenRefresh: false).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.PreconditionFailed or (HttpStatusCode)428)
            throw new DashboardLayoutConflictException((int)response.StatusCode, StrongValidator(response),
                "The server layout changed since it was read.");
        if (!response.IsSuccessStatusCode) await ThrowApiException(response, request, ct).ConfigureAwait(false);
        var etag = StrongValidator(response);
        if (!IsCurrentContext(context)) throw new OperationCanceledException("API context changed.", ct);
        return etag ?? throw new InvalidDataException("Server layout write revision missing.");
    }

    /// <summary>Context-bound mutation without token-refresh replay. Accepts an empty 2xx body.</summary>
    public async Task<T?> SendDashboardActionAsync<T>(ApiRequestContext context, HttpMethod method, string path,
        object? body, CancellationToken ct) where T : class
    {
        using var request = CreateContextBoundRequest(context, method, path, body);
        using var response = await SendWithRetryAsync(request, ct, allowTokenRefresh: false).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) await ThrowApiException(response, request, ct).ConfigureAwait(false);
        T? value = null;
        if (response.StatusCode != HttpStatusCode.NoContent && response.Content.Headers.ContentLength != 0)
            value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct).ConfigureAwait(false);
        if (!IsCurrentContext(context)) throw new OperationCanceledException("API context changed.", ct);
        return value;
    }

    /// <summary>A single setting write guarded by the displayed settings validator; never replayed.</summary>
    public async Task<T> PutDashboardSettingAsync<T>(ApiRequestContext context, string path, object body, string ifMatch,
        CancellationToken ct)
    {
        var response = await PutWithETagWithoutRefreshAsync<T>(context, path, body, ifMatch, ct).ConfigureAwait(false);
        return response.Body;
    }

    private static string? StrongValidator(HttpResponseMessage response)
    {
        var tag = response.Headers.ETag;
        if (tag == null || tag.IsWeak) return null;
        var text = tag.ToString();
        return string.IsNullOrWhiteSpace(text) || text == "*" ? null : text;
    }
}
