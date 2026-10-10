using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.Json.Serialization;
using SiloPlayer.Core.Models;

namespace SiloPlayer.Core.Api;

public class ApiException(string errorCode, string message, int statusCode, string? errorLocation = null, double? retryAfterSeconds = null) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
    public int StatusCode { get; } = statusCode;
    public string? ErrorLocation { get; } = errorLocation;
    public double? RetryAfterSeconds { get; } = retryAfterSeconds;
}

public readonly record struct ProfileVerificationContext(
    long AuthenticationGeneration,
    long RequestContextGeneration,
    string? ProfileId);

public readonly record struct ApiRequestContext(long AuthenticationGeneration, long RequestContextGeneration, string BaseUrl, string? ProfileId);
/// <summary>Account/profile ownership, stable across read-cache/access refreshes.</summary>
public readonly record struct ApiIdentityContext(long AuthenticationGeneration, long IdentityGeneration, string BaseUrl, string? ProfileId);
public sealed record ApiResponse<T>(T Body, string? ETag);

public class SiloApiClient
{
    public const string DefaultClientName = "Silo for Windows";
    public const string ClientFamily = "desktop";

    private static readonly HttpRequestOptionsKey<long> AuthenticationGenerationOption =
        new("SiloPlayer.AuthenticationGeneration");
    private static readonly HttpRequestOptionsKey<long> RequestContextGenerationOption =
        new("SiloPlayer.RequestContextGeneration");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    private readonly HttpClient _http;
    private readonly object _authStateGate = new();
    private string? _accessToken;
    private string? _profileId;
    private string? _profileToken;
    private string? _deviceId;
    private string? _deviceName;
    private string? _devicePlatform;
    private string _clientName = DefaultClientName;
    private string? _clientVersion;
    private string _baseUrl = "";
    private long _authenticationGeneration;
    private long _requestContextGeneration;
    private long _identityGeneration;
    private Func<CancellationToken, Task<bool>>? _tokenRefresher;
    private Action<ProfileVerificationContext>? _profileVerificationRequired;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public SiloApiClient(HttpClient http) { _http = http; }

    public ApiRequestContext CaptureContext()
    {
        lock (_authStateGate) return new(_authenticationGeneration, _requestContextGeneration, _baseUrl, _profileId);
    }
    public bool IsCurrentContext(ApiRequestContext context) => CaptureContext() == context;

    public ApiIdentityContext CaptureIdentityContext()
    {
        lock (_authStateGate) return new(_authenticationGeneration, _identityGeneration, _baseUrl, _profileId);
    }
    public bool IsCurrentIdentityContext(ApiIdentityContext context) => CaptureIdentityContext() == context;

    public async Task<T> SendIdentityBoundRequestAsync<T>(ApiIdentityContext context, HttpMethod method,
        string path, object? body, CancellationToken ct = default)
    {
        using var request = CreateIdentityBoundRequest(context, method, path, body);
        return await SendAsync<T>(request, ct).ConfigureAwait(false);
    }

    public async Task SendIdentityBoundNoContentAsync(ApiIdentityContext context, HttpMethod method,
        string path, object? body, CancellationToken ct = default)
    {
        using var request = CreateIdentityBoundRequest(context, method, path, body);
        await SendNoContentAsync(request, ct).ConfigureAwait(false);
    }

    private HttpRequestMessage CreateIdentityBoundRequest(ApiIdentityContext context, HttpMethod method, string path, object? body)
    {
        lock (_authStateGate)
        {
            if (!IsCurrentIdentityContext(context)) throw new OperationCanceledException("Playback identity changed.");
            return CreateRequest(method, path, body, null);
        }
    }

    /// <summary>Retire reads authorized by old account access without changing credentials or the selected PIN grant.</summary>
    public ApiRequestContext InvalidateAccessContext()
    {
        lock (_authStateGate)
        {
            _requestContextGeneration++;
            return new(_authenticationGeneration, _requestContextGeneration, _baseUrl, _profileId);
        }
    }

    public bool TryInvalidateAccessContext(ApiRequestContext expected, out ApiRequestContext updated)
    {
        lock (_authStateGate)
        {
            updated = new(_authenticationGeneration, _requestContextGeneration, _baseUrl, _profileId);
            if (updated != expected) return false;
            _requestContextGeneration++;
            updated = new(_authenticationGeneration, _requestContextGeneration, _baseUrl, _profileId);
            return true;
        }
    }

    public HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string path)
    {
        lock (_authStateGate)
        {
            var origin = new Uri(_baseUrl.TrimEnd('/') + "/");
            var target = new Uri(origin, path);
            if (target.Scheme != origin.Scheme || target.Host != origin.Host || target.Port != origin.Port)
                throw new ArgumentException("Authenticated downloads must use the connected server origin.", nameof(path));
            var request = new HttpRequestMessage(method, target);
            AddHeaders(request);
            return request;
        }
    }

    public async Task<T> SendRequestAsync<T>(HttpMethod method, string path, object? body,
        IReadOnlyDictionary<string, string>? headers, CancellationToken ct = default)
    {
        using var request = CreateRequest(method, path, body, headers);
        return await SendAsync<T>(request, ct).ConfigureAwait(false);
    }

    public async Task SendNoContentRequestAsync(HttpMethod method, string path, object? body,
        IReadOnlyDictionary<string, string>? headers, CancellationToken ct = default)
    {
        using var request = CreateRequest(method, path, body, headers);
        await SendNoContentAsync(request, ct).ConfigureAwait(false);
    }

    public async Task<T> SendRequestAsync<T>(ApiRequestContext context, HttpMethod method, string path,
        object? body, CancellationToken ct = default)
    {
        using var request = CreateContextBoundRequest(context, method, path, body);
        return await SendAsync<T>(request, ct).ConfigureAwait(false);
    }

    public async Task SendNoContentRequestAsync(ApiRequestContext context, HttpMethod method, string path,
        object? body, CancellationToken ct = default)
    {
        using var request = CreateContextBoundRequest(context, method, path, body);
        await SendNoContentAsync(request, ct).ConfigureAwait(false);
    }

    public async Task<T> SendRequestWithoutRefreshAsync<T>(ApiRequestContext context, HttpMethod method,
        string path, object? body, CancellationToken ct = default, bool allowNoContent = false)
    {
        using var request = CreateContextBoundRequest(context, method, path, body);
        return await SendAsync<T>(request, ct, allowTokenRefresh: false, allowNoContent: allowNoContent).ConfigureAwait(false);
    }

    public async Task SendNoContentRequestWithoutRefreshAsync(ApiRequestContext context, HttpMethod method,
        string path, object? body, CancellationToken ct = default)
    {
        using var request = CreateContextBoundRequest(context, method, path, body);
        await SendNoContentAsync(request, ct, allowTokenRefresh: false).ConfigureAwait(false);
    }

    private HttpRequestMessage CreateContextBoundRequest(ApiRequestContext context, HttpMethod method,
        string path, object? body, IReadOnlyDictionary<string, string>? headers = null)
    {
        // Check and capture identity atomically. Refresh retries also check these
        // request generations, so a profile switch cannot re-author this write.
        lock (_authStateGate)
        {
            if (!IsCurrentContext(context)) throw new OperationCanceledException("API context changed.");
            return CreateRequest(method, path, body, headers);
        }
    }

    public async Task<ApiResponse<T>> GetWithETagAsync<T>(ApiRequestContext context, string path, CancellationToken ct = default)
    {
        using var request = CreateContextBoundRequest(context, HttpMethod.Get, path, null);
        using var response = await SendWithRetryAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) await ThrowApiException(response, request, ct).ConfigureAwait(false);
        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct).ConfigureAwait(false);
        if (!IsCurrentContext(context)) throw new OperationCanceledException("API context changed.", ct);
        return new(value ?? throw new InvalidDataException("Empty API response."), response.Headers.ETag?.ToString());
    }

    public async Task<ApiResponse<T>> PutWithETagWithoutRefreshAsync<T>(ApiRequestContext context, string path,
        object body, string etag, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(etag) || etag.StartsWith("W/", StringComparison.Ordinal))
            throw new ArgumentException("A strong validator is required.", nameof(etag));
        using var request = CreateContextBoundRequest(context, HttpMethod.Put, path, body,
            new Dictionary<string, string> { ["If-Match"] = etag });
        using var response = await SendWithRetryAsync(request, ct, allowTokenRefresh: false).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) await ThrowApiException(response, request, ct).ConfigureAwait(false);
        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct).ConfigureAwait(false);
        if (!IsCurrentContext(context)) throw new OperationCanceledException("API context changed.", ct);
        return new(value ?? throw new InvalidDataException("Empty API response."), response.Headers.ETag?.ToString());
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, object? body, IReadOnlyDictionary<string, string>? headers)
    {
        var request = new HttpRequestMessage(method, BuildUrl(path));
        AddHeaders(request);
        if (body != null) request.Content = CreateJsonContent(body);
        if (headers != null) foreach (var (key, value) in headers)
        {
            // Request-local capability/validator headers must never replace identity.
            if (key is not ("X-Room-Token" or "If-Match" or "If-None-Match"))
                throw new ArgumentException("Unsupported request-local header.", nameof(headers));
            request.Headers.Add(key, value);
        }
        return request;
    }

    public async Task<List<T>> GetAllItemsAsync<T>(string path, CancellationToken ct = default)
    {
        var context = CaptureContext();
        var result = new List<T>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pagePath = path;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (!IsCurrentContext(context)) throw new OperationCanceledException("API context changed.", ct);
            var page = await GetAsync<ApiCollectionPage<T>>(pagePath, ct).ConfigureAwait(false);
            if (!IsCurrentContext(context)) throw new OperationCanceledException("API context changed.", ct);
            result.AddRange(page.Items);
            if (page.Page?.HasMore != true) return result;
            var cursor = page.Page.NextCursor;
            if (string.IsNullOrWhiteSpace(cursor) || !seen.Add(cursor)) throw new InvalidDataException("Invalid API pagination cursor.");
            pagePath = path + (path.Contains('?') ? "&" : "?") + "cursor=" + Uri.EscapeDataString(cursor);
        }
    }

    /// <summary>
    /// Registers a callback that attempts to refresh the access token.
    /// Called automatically on 401 responses before retrying the request.
    /// </summary>
    public void SetTokenRefresher(Func<CancellationToken, Task<bool>> refresher) => _tokenRefresher = refresher;
    public void SetProfileVerificationRequiredHandler(Action<ProfileVerificationContext> handler) =>
        _profileVerificationRequired = handler;

    public bool IsCurrentProfileContext(ProfileVerificationContext context)
    {
        lock (_authStateGate)
        {
            return _authenticationGeneration == context.AuthenticationGeneration &&
                _requestContextGeneration == context.RequestContextGeneration &&
                string.Equals(_profileId, context.ProfileId, StringComparison.Ordinal);
        }
    }

    public bool TryClearProfile(ProfileVerificationContext context)
    {
        lock (_authStateGate)
        {
            if (_authenticationGeneration != context.AuthenticationGeneration ||
                _requestContextGeneration != context.RequestContextGeneration ||
                !string.Equals(_profileId, context.ProfileId, StringComparison.Ordinal))
            {
                return false;
            }

            _requestContextGeneration++;
            _identityGeneration++;
            _profileId = null;
            _profileToken = null;
            return true;
        }
    }

    public void SetBaseUrl(string baseUrl)
    {
        var normalized = baseUrl.TrimEnd('/');
        lock (_authStateGate)
        {
            if (string.Equals(_baseUrl, normalized, StringComparison.OrdinalIgnoreCase))
                return;

            _baseUrl = normalized;
            _authenticationGeneration++;
            _requestContextGeneration++;
            _identityGeneration++;
            _accessToken = null;
            _profileId = null;
            _profileToken = null;
        }
    }
    public string BaseUrl
    {
        get { lock (_authStateGate) return _baseUrl; }
    }

    /// <summary>Resolves a server-returned absolute or root-relative asset URL.</summary>
    public string? ResolveServerUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute))
            return absolute.ToString();

        string baseUrl;
        lock (_authStateGate) baseUrl = _baseUrl;
        if (!Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var origin))
            return null;
        return Uri.TryCreate(origin, trimmed, out var resolved) ? resolved.ToString() : null;
    }

    public void SetAccessToken(string? token)
    {
        lock (_authStateGate)
            _accessToken = string.IsNullOrWhiteSpace(token) ? null : token;
    }
    public void BeginAuthenticationSession(string? accessToken)
    {
        lock (_authStateGate)
        {
            _authenticationGeneration++;
            _requestContextGeneration++;
            _identityGeneration++;
            _accessToken = string.IsNullOrWhiteSpace(accessToken) ? null : accessToken;
            _profileId = null;
            _profileToken = null;
        }
    }
    public void SetProfile(string profileId, string? profileToken = null)
    {
        lock (_authStateGate)
        {
            _requestContextGeneration++;
            _identityGeneration++;
            _profileId = profileId;
            _profileToken = profileToken;
        }
    }
    public void ClearProfile()
    {
        lock (_authStateGate)
        {
            _requestContextGeneration++;
            _identityGeneration++;
            _profileId = null;
            _profileToken = null;
        }
    }
    public void SetDeviceMetadata(string deviceId, string deviceName, string devicePlatform)
    {
        lock (_authStateGate)
        {
            _deviceId = deviceId;
            _deviceName = deviceName;
            _devicePlatform = devicePlatform;
        }
    }
    public void SetClientMetadata(string clientName, string? clientVersion = null)
    {
        lock (_authStateGate)
        {
            _clientName = string.IsNullOrWhiteSpace(clientName) ? DefaultClientName : clientName.Trim();
            _clientVersion = string.IsNullOrWhiteSpace(clientVersion) ? null : clientVersion.Trim();
        }
    }
    public void ClearAuth()
    {
        lock (_authStateGate)
        {
            _authenticationGeneration++;
            _requestContextGeneration++;
            _identityGeneration++;
            _accessToken = null;
            _profileId = null;
            _profileToken = null;
        }
    }

    public string? AccessToken
    {
        get { lock (_authStateGate) return _accessToken; }
    }
    public string? ProfileId
    {
        get { lock (_authStateGate) return _profileId; }
    }
    public string? ProfileToken
    {
        get { lock (_authStateGate) return _profileToken; }
    }

    private string BuildUrl(string path)
    {
        lock (_authStateGate)
            return _baseUrl + path;
    }

    public async Task<T> GetAsync<T>(string path, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUrl(path));
        AddHeaders(request);
        return await SendAsync<T>(request, ct);
    }

    public async Task<T> GetUnauthenticatedAsync<T>(string path, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUrl(path));
        AddDeviceHeaders(request);
        return await SendAsync<T>(request, ct, allowTokenRefresh: false);
    }

    public async Task<T> GetWithBearerAsync<T>(
        string baseUrl,
        string path,
        string accessToken,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            baseUrl.TrimEnd('/') + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        AddDeviceHeaders(request);
        return await SendAsync<T>(request, ct, allowTokenRefresh: false);
    }

    public async Task<T> PostAsync<T>(string path, object body, CancellationToken ct = default, bool allowTokenRefresh = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl(path));
        AddHeaders(request);
        request.Content = CreateJsonContent(body);
        return await SendAsync<T>(request, ct, allowTokenRefresh);
    }

    /// <summary>
    /// Sends an authentication bootstrap request without an access/profile token and
    /// without invoking the automatic 401 refresh callback. Refresh, login, signup,
    /// setup, and device bootstrap endpoints must use this path: retrying any of them
    /// through the refresh callback can recurse into the refresh operation itself or
    /// accidentally revive a previous user's session.
    /// </summary>
    public async Task<T> PostUnauthenticatedAsync<T>(string path, object body, CancellationToken ct = default)
        => await PostUnauthenticatedAsync<T>(BaseUrl, path, body, ct);

    public async Task<T> PostUnauthenticatedAsync<T>(
        string baseUrl,
        string path,
        object body,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + path);
        AddDeviceHeaders(request);
        request.Content = CreateJsonContent(body);
        return await SendAsync<T>(request, ct, allowTokenRefresh: false);
    }

    public async Task<T> PostMultipartAsync<T>(
        string path,
        IReadOnlyDictionary<string, string?> fields,
        string fileFieldName,
        string fileName,
        byte[] fileBytes,
        string contentType,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl(path));
        AddHeaders(request);

        var form = new MultipartFormDataContent();
        foreach (var (key, value) in fields)
        {
            if (!string.IsNullOrWhiteSpace(value))
                form.Add(new StringContent(value), key);
        }

        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, fileFieldName, fileName);
        request.Content = form;

        return await SendAsync<T>(request, ct);
    }

    public async Task PostNoContentAsync(string path, object body, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl(path));
        AddHeaders(request);
        request.Content = CreateJsonContent(body);
        await SendNoContentAsync(request, ct);
    }

    public async Task PostNoContentWithoutRefreshAsync(string path, object body, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl(path));
        AddHeaders(request);
        request.Content = CreateJsonContent(body);
        await SendNoContentAsync(request, ct, allowTokenRefresh: false);
    }

    public async Task PostUnauthenticatedNoContentAsync(string baseUrl, string path, object body, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + path);
        AddDeviceHeaders(request);
        request.Content = CreateJsonContent(body);
        await SendNoContentAsync(request, ct, allowTokenRefresh: false);
    }

    public async Task PostNoContentWithBearerAsync(
        string baseUrl,
        string path,
        string accessToken,
        object body,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            baseUrl.TrimEnd('/') + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        AddDeviceHeaders(request);
        request.Content = CreateJsonContent(body);
        await SendNoContentAsync(request, ct, allowTokenRefresh: false);
    }

    public async Task<T> PutAsync<T>(string path, object body, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, BuildUrl(path));
        AddHeaders(request);
        request.Content = CreateJsonContent(body);
        return await SendAsync<T>(request, ct);
    }

    public async Task<T> PutBytesAsync<T>(string path, ReadOnlyMemory<byte> body,
        string contentType = "application/octet-stream", CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, BuildUrl(path));
        AddHeaders(request);
        request.Content = new ByteArrayContent(body.ToArray());
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return await SendAsync<T>(request, ct);
    }

    public async Task PutNoContentAsync(string path, object? body = null, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, BuildUrl(path));
        AddHeaders(request);
        if (body != null) request.Content = CreateJsonContent(body);
        await SendNoContentAsync(request, ct);
    }

    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, BuildUrl(path));
        AddHeaders(request);
        await SendNoContentAsync(request, ct);
    }

    public async Task<T> DeleteReturningAsync<T>(string path, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, BuildUrl(path));
        AddHeaders(request);
        return await SendAsync<T>(request, ct);
    }

    public Task<T> PutJsonWithFileAsync<T>(
        string path, object body, string fileFieldName, string fileName,
        byte[] fileBytes, string contentType, CancellationToken ct = default)
        => SendJsonWithFileAsync<T>(HttpMethod.Put, path, body, fileFieldName, fileName, fileBytes, contentType, ct);

    public Task<T> PostJsonWithFileAsync<T>(
        string path, object body, string fileFieldName, string fileName,
        byte[] fileBytes, string contentType, CancellationToken ct = default)
        => SendJsonWithFileAsync<T>(HttpMethod.Post, path, body, fileFieldName, fileName, fileBytes, contentType, ct);

    private async Task<T> SendJsonWithFileAsync<T>(
        HttpMethod method, string path, object body, string fileFieldName,
        string fileName, byte[] fileBytes, string contentType, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, BuildUrl(path));
        AddHeaders(request);
        using var jsonContent = CreateJsonContent(body);
        var json = await jsonContent.ReadAsStringAsync(ct).ConfigureAwait(false);
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(json, System.Text.Encoding.UTF8, "application/json"), "data");
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, fileFieldName, fileName);
        request.Content = form;
        return await SendAsync<T>(request, ct).ConfigureAwait(false);
    }

    public async Task<byte[]> GetBytesAsync(string path, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUrl(path));
        AddHeaders(request);
        using var response = await SendWithRetryAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) await ThrowApiException(response, request, ct).ConfigureAwait(false);
        return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteWithBodyAsync(string path, object body, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, BuildUrl(path));
        AddHeaders(request);
        request.Content = CreateJsonContent(body);
        await SendNoContentAsync(request, ct);
    }

    public async Task<T> DeleteWithBodyAsync<T>(string path, object body, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, BuildUrl(path));
        AddHeaders(request);
        request.Content = CreateJsonContent(body);
        return await SendAsync<T>(request, ct).ConfigureAwait(false);
    }

    public async Task<ApiResponse<T>> GetWithETagAsync<T>(string path, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUrl(path));
        AddHeaders(request);
        using var response = await SendWithRetryAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) await ThrowApiException(response, request, ct).ConfigureAwait(false);
        var body = (await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct).ConfigureAwait(false))!;
        return new(body, response.Headers.ETag?.ToString());
    }

    public async Task<T> PutMultipartFieldsAsync<T>(string path, IReadOnlyDictionary<string, string> fields, CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Put, path, null, null);
        using var form = new MultipartFormDataContent();
        foreach (var (name, value) in fields) form.Add(new StringContent(value), name);
        request.Content = form;
        return await SendAsync<T>(request, ct).ConfigureAwait(false);
    }

    public Task<T> PatchWithETagAsync<T>(string path, object body, string etag, CancellationToken ct = default)
        => SendConditionalAsync<T>(HttpMethod.Patch, path, body, etag, ct);

    public Task<T> PutWithETagAsync<T>(string path, object body, string etag, CancellationToken ct = default)
        => SendConditionalAsync<T>(HttpMethod.Put, path, body, etag, ct);

    public Task<ApiResponse<T>> PutWithETagResponseAsync<T>(string path, object body, string etag, CancellationToken ct = default)
        => SendConditionalResponseAsync<T>(HttpMethod.Put, path, body, etag, ct);

    public Task<ApiResponse<T>> PatchWithETagResponseAsync<T>(string path, object body, string etag, CancellationToken ct = default)
        => SendConditionalResponseAsync<T>(HttpMethod.Patch, path, body, etag, ct);

    private async Task<ApiResponse<T>> SendConditionalResponseAsync<T>(HttpMethod method, string path, object body, string etag, CancellationToken ct)
    {
        using var request = CreateRequest(method, path, body, new Dictionary<string, string> { ["If-Match"] = etag });
        using var response = await SendWithRetryAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) await ThrowApiException(response, request, ct).ConfigureAwait(false);
        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct).ConfigureAwait(false);
        return new(value ?? throw new InvalidDataException("Empty API response."), response.Headers.ETag?.ToString());
    }

    private async Task<T> SendConditionalAsync<T>(HttpMethod method, string path, object body, string etag, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, BuildUrl(path));
        AddHeaders(request);
        request.Headers.IfMatch.ParseAdd(etag);
        request.Content = CreateJsonContent(body);
        return await SendAsync<T>(request, ct).ConfigureAwait(false);
    }

    public async Task DeleteWithETagAsync(string path, string etag, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, BuildUrl(path));
        AddHeaders(request);
        request.Headers.IfMatch.ParseAdd(etag);
        await SendNoContentAsync(request, ct).ConfigureAwait(false);
    }

    public async Task<T> PatchAsync<T>(string path, object body, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, BuildUrl(path));
        AddHeaders(request);
        request.Content = CreateJsonContent(body);
        return await SendAsync<T>(request, ct);
    }

    /// <summary>
    /// PUT a multipart/form-data request with a single file field. Used for endpoints
    /// that expect file uploads (e.g., library poster upload).
    /// </summary>
    public async Task PutMultipartNoContentAsync(string path, string fieldName, string fileName, byte[] fileBytes, string contentType, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, BuildUrl(path));
        AddHeaders(request);
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(fileContent, fieldName, fileName);
        request.Content = form;
        await SendNoContentAsync(request, ct);
    }

    /// <summary>
    /// PUT a multipart/form-data request with a single file field and deserialize
    /// the JSON response. Profile-avatar uploads return the updated profile.
    /// </summary>
    public async Task<T> PutMultipartAsync<T>(
        string path,
        string fieldName,
        string fileName,
        byte[] fileBytes,
        string contentType,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, BuildUrl(path));
        AddHeaders(request);
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
        form.Add(fileContent, fieldName, fileName);
        request.Content = form;
        return await SendAsync<T>(request, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Serialize using property reflection that works in all build modes.
    /// .NET 8 self-contained publish disables System.Text.Json reflection by default,
    /// so we build a dictionary manually from the object's properties.
    /// </summary>
    private static StringContent CreateJsonContent(object body)
    {
        if (body is JsonElement element)
            return new StringContent(element.GetRawText(), System.Text.Encoding.UTF8, "application/json");
        // Callers can pass an IDictionary<string, object?> directly when they
        // want to bypass the reflection path entirely. This is REQUIRED for
        // mutation bodies containing fields whose names must be preserved
        // exactly (e.g. admin section updates, settings PUTs), because
        // .NET 8 Release publish + trimming strips anonymous type property
        // names and silently serializes them as {}. When a dictionary is
        // passed, we forward its entries verbatim — no snake_case rewrite,
        // no reflection.
        Dictionary<string, object?> dict;
        if (body is IDictionary<string, object?> nullableDict)
        {
            dict = new Dictionary<string, object?>(nullableDict);
        }
        else if (body is IDictionary<string, object> nonNullableDict)
        {
            dict = new Dictionary<string, object?>(nonNullableDict.Count);
            foreach (var kvp in nonNullableDict)
                dict[kvp.Key] = kvp.Value;
        }
        else
        {
            // Fallback: reflect on the body's public properties and apply
            // snake_case naming manually. Works when trimming is off OR when
            // the type is trim-rooted; unreliable for anonymous types under
            // publish trimming (hence the dictionary path above).
            dict = new Dictionary<string, object?>();
            var props = body.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            foreach (var prop in props)
            {
                if (!prop.CanRead) continue;
                try
                {
                    var value = prop.GetValue(body);
                    if (value == null) continue;
                    var name = ToSnakeCase(prop.Name);
                    dict[name] = value;
                }
                catch { /* skip properties whose getter was trimmed */ }
            }
        }

        var json = JsonSerializer.Serialize(dict, JsonOptions);
        return new StringContent(json, System.Text.Encoding.UTF8, "application/json");
    }

    private static string ToSnakeCase(string name)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0) sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private void AddHeaders(HttpRequestMessage request)
    {
        lock (_authStateGate)
        {
            request.Options.Set(AuthenticationGenerationOption, _authenticationGeneration);
            request.Options.Set(RequestContextGenerationOption, _requestContextGeneration);
            if (_accessToken != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            if (_profileId != null) request.Headers.Add("X-Profile-Id", _profileId);
            if (_profileToken != null) request.Headers.Add("X-Profile-Token", _profileToken);
            AddDeviceHeadersUnsafe(request);
        }
    }

    private void AddDeviceHeaders(HttpRequestMessage request)
    {
        lock (_authStateGate)
            AddDeviceHeadersUnsafe(request);
    }

    private void AddDeviceHeadersUnsafe(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("X-Silo-Client", _clientName);
        // Silo's typed settings contract never infers the like-client family
        // from the free-form platform string.  Native Windows belongs to the
        // desktop family, so advertise that identity on every request.  This
        // lets effective settings resolve profile_client values and makes
        // writes such as nav.primary_menu unambiguous.
        request.Headers.TryAddWithoutValidation("X-Silo-Client-Family", ClientFamily);
        if (!string.IsNullOrWhiteSpace(_clientVersion))
            request.Headers.TryAddWithoutValidation("X-Silo-Client-Version", _clientVersion);
        if (!string.IsNullOrWhiteSpace(_deviceId)) request.Headers.Add("X-Silo-Device-Id", _deviceId);
        if (!string.IsNullOrWhiteSpace(_deviceName)) request.Headers.Add("X-Silo-Device-Name", _deviceName);
        if (!string.IsNullOrWhiteSpace(_devicePlatform)) request.Headers.Add("X-Silo-Device-Platform", _devicePlatform);
    }

    // Buffer content before sending so we can replay on 401 retry
    private async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpRequestMessage request,
        CancellationToken ct,
        bool allowTokenRefresh = true)
    {
        // HttpClient defaults apply to its convenience methods, not to the
        // explicit messages constructed by this API client. Carry the policy
        // onto each message so HTTP/2 is actually offered to the server.
        request.Version = _http.DefaultRequestVersion;
        request.VersionPolicy = _http.DefaultVersionPolicy;
        // Pre-buffer content — after SendAsync the content stream is consumed
        byte[]? contentBytes = null;
        System.Net.Http.Headers.MediaTypeHeaderValue? contentType = null;
        if (request.Content != null)
        {
            contentBytes = await request.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            contentType = request.Content.Headers.ContentType;
        }

        var sentAccessToken = request.Headers.Authorization?.Parameter;
        var sentAuthenticationGeneration = request.Options.TryGetValue(
            AuthenticationGenerationOption,
            out var requestAuthenticationGeneration)
                ? requestAuthenticationGeneration
                : GetRequestContextSnapshot().AuthenticationGeneration;
        var sentRequestContextGeneration = request.Options.TryGetValue(
            RequestContextGenerationOption,
            out var requestContextGeneration)
                ? requestContextGeneration
                : GetRequestContextSnapshot().RequestContextGeneration;
        var response = await _http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct).ConfigureAwait(false);

        // On 401, try refreshing the token and retry once
        if (allowTokenRefresh &&
            response.StatusCode == System.Net.HttpStatusCode.Unauthorized &&
            _tokenRefresher != null)
        {
            bool refreshed = false;
            await _refreshLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // Never replay an old request in a replacement user's session. Token
                // changes within the same generation are refreshes; a generation
        // change represents logout, login, or OAuth completion.
                var currentContext = GetRequestContextSnapshot();
                if (currentContext.AuthenticationGeneration == sentAuthenticationGeneration &&
                    currentContext.RequestContextGeneration == sentRequestContextGeneration)
                {
                    refreshed = !string.Equals(currentContext.AccessToken, sentAccessToken, StringComparison.Ordinal);
                    if (!refreshed)
                    {
                        refreshed = await _tokenRefresher(ct).ConfigureAwait(false);
                        currentContext = GetRequestContextSnapshot();
                        refreshed = refreshed &&
                            currentContext.AuthenticationGeneration == sentAuthenticationGeneration &&
                            currentContext.RequestContextGeneration == sentRequestContextGeneration;
                    }
                }
            }
            finally { _refreshLock.Release(); }

            if (refreshed)
            {
                using var retry = new HttpRequestMessage(request.Method, request.RequestUri)
                {
                    Version = request.Version,
                    VersionPolicy = request.VersionPolicy,
                };
                AddHeaders(retry);
                // A token refresh must replay the same conditional mutation.
                foreach (var validator in request.Headers.IfMatch) retry.Headers.IfMatch.Add(validator);
                foreach (var validator in request.Headers.IfNoneMatch) retry.Headers.IfNoneMatch.Add(validator);
                if (request.Headers.TryGetValues("X-Room-Token", out var roomTokens))
                    retry.Headers.Add("X-Room-Token", roomTokens);
                retry.Options.TryGetValue(AuthenticationGenerationOption, out var retryAuthenticationGeneration);
                retry.Options.TryGetValue(RequestContextGenerationOption, out var retryRequestContextGeneration);
                if (retryAuthenticationGeneration != sentAuthenticationGeneration ||
                    retryRequestContextGeneration != sentRequestContextGeneration)
                {
                    return response;
                }

                if (contentBytes != null)
                {
                    retry.Content = new ByteArrayContent(contentBytes);
                    retry.Content.Headers.ContentType = contentType;
                }
                response.Dispose();
                response = await _http.SendAsync(
                    retry,
                    HttpCompletionOption.ResponseHeadersRead,
                    ct).ConfigureAwait(false);
            }
        }

        return response;
    }

    private (long AuthenticationGeneration, long RequestContextGeneration, string? AccessToken)
        GetRequestContextSnapshot()
    {
        lock (_authStateGate)
            return (_authenticationGeneration, _requestContextGeneration, _accessToken);
    }

    private async Task<T> SendAsync<T>(
        HttpRequestMessage request,
        CancellationToken ct,
        bool allowTokenRefresh = true,
        bool allowNoContent = false)
    {
        // ConfigureAwait(false) on both awaits so the HTTP and JSON work
        // doesn't capture the calling SynchronizationContext — on WinUI 3
        // the UI thread was the one doing deserialization, which caused
        // multi-second freezes on larger catalog/home-section responses.
        var browseResponse = typeof(T).Name switch
        {
            "CatalogResponse" or "CatalogFiltersResponse" or "HomeLayoutResponse"
                or "HomeSectionItemsResponse" or "HomeSectionsResponse" => typeof(T).Name,
            _ => null,
        };
        HttpResponseMessage response;
        using (Services.LibraryPerformanceTrace.Measure(
            browseResponse == null ? null : $"{browseResponse}-response-headers"))
        {
            response = await SendWithRetryAsync(request, ct, allowTokenRefresh).ConfigureAwait(false);
        }
        using (response)
        using (Services.LibraryPerformanceTrace.Measure(
            browseResponse == null ? null : $"{browseResponse}-body-and-json"))
        {
            if (!response.IsSuccessStatusCode) await ThrowApiException(response, request, ct).ConfigureAwait(false);
            if (allowNoContent && response.StatusCode == System.Net.HttpStatusCode.NoContent) return default!;
            return (await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct).ConfigureAwait(false))!;
        }
    }

    private async Task SendNoContentAsync(
        HttpRequestMessage request,
        CancellationToken ct,
        bool allowTokenRefresh = true)
    {
        using var response = await SendWithRetryAsync(request, ct, allowTokenRefresh).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) await ThrowApiException(response, request, ct).ConfigureAwait(false);
    }

    private async Task ThrowApiException(
        HttpResponseMessage response,
        HttpRequestMessage request,
        CancellationToken ct)
    {
        string? code = null, detail = null, location = null;
        try
        {
            using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);
            var root = body.RootElement;
            string? Text(string key) => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            code = Text("error") ?? Text("code");
            detail = Text("detail") ?? Text("message") ?? Text("title");
            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0
                && errors[0].ValueKind == JsonValueKind.Object && errors[0].TryGetProperty("location", out var rejectedField)
                && rejectedField.ValueKind == JsonValueKind.String)
                location = rejectedField.GetString();
            if (code == null && Uri.TryCreate(Text("type"), UriKind.Absolute, out var type) &&
                type.AbsolutePath.StartsWith("/docs/api/v2/problems/", StringComparison.Ordinal))
                code = type.Segments.LastOrDefault()?.TrimEnd('/');
        }
        catch (JsonException) { }
        catch (NotSupportedException) { }
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden &&
            string.Equals(code, "profile_unverified", StringComparison.Ordinal))
        {
            var context = CreateProfileVerificationContext(request);
            try { _profileVerificationRequired?.Invoke(context); }
            catch { }
        }
        var retryAfter = response.Headers.RetryAfter;
        double? retrySeconds = retryAfter?.Delta?.TotalSeconds;
        if (retrySeconds == null && retryAfter?.Date is { } retryAt)
            retrySeconds = (retryAt - DateTimeOffset.UtcNow).TotalSeconds;
        throw new ApiException(code ?? "unknown", detail ?? $"HTTP {(int)response.StatusCode}", (int)response.StatusCode, location,
            retrySeconds is { } seconds ? Math.Max(0, seconds) : null);
    }

    private static ProfileVerificationContext CreateProfileVerificationContext(HttpRequestMessage request)
    {
        request.Options.TryGetValue(AuthenticationGenerationOption, out var authenticationGeneration);
        request.Options.TryGetValue(RequestContextGenerationOption, out var requestContextGeneration);
        string? profileId = null;
        if (request.Headers.TryGetValues("X-Profile-Id", out var values))
            profileId = values.FirstOrDefault();
        return new ProfileVerificationContext(authenticationGeneration, requestContextGeneration, profileId);
    }
}
