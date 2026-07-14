using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using SiloPlayer.Core.Models;

namespace SiloPlayer.Core.Api;

public class ApiException(string errorCode, string message, int statusCode) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
    public int StatusCode { get; } = statusCode;
}

public readonly record struct ProfileVerificationContext(
    long AuthenticationGeneration,
    long RequestContextGeneration,
    string? ProfileId);

public class SiloApiClient
{
    private static readonly HttpRequestOptionsKey<long> AuthenticationGenerationOption =
        new("SiloPlayer.AuthenticationGeneration");
    private static readonly HttpRequestOptionsKey<long> RequestContextGenerationOption =
        new("SiloPlayer.RequestContextGeneration");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
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
    private string _baseUrl = "";
    private long _authenticationGeneration;
    private long _requestContextGeneration;
    private Func<CancellationToken, Task<bool>>? _tokenRefresher;
    private Action<ProfileVerificationContext>? _profileVerificationRequired;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public SiloApiClient(HttpClient http) { _http = http; }

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
            _accessToken = null;
            _profileId = null;
            _profileToken = null;
        }
    }
    public string BaseUrl
    {
        get { lock (_authStateGate) return _baseUrl; }
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
            _profileId = profileId;
            _profileToken = profileToken;
        }
    }
    public void ClearProfile()
    {
        lock (_authStateGate)
        {
            _requestContextGeneration++;
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
    public void ClearAuth()
    {
        lock (_authStateGate)
        {
            _authenticationGeneration++;
            _requestContextGeneration++;
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

    public async Task<T> PostAsync<T>(string path, object body, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl(path));
        AddHeaders(request);
        request.Content = CreateJsonContent(body);
        return await SendAsync<T>(request, ct);
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
    /// Serialize using property reflection that works in all build modes.
    /// .NET 8 self-contained publish disables System.Text.Json reflection by default,
    /// so we build a dictionary manually from the object's properties.
    /// </summary>
    private static StringContent CreateJsonContent(object body)
    {
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
        var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

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
                // change represents logout, login, OAuth completion, or impersonation.
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
                using var retry = new HttpRequestMessage(request.Method, request.RequestUri);
                AddHeaders(retry);
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
                response = await _http.SendAsync(retry, ct).ConfigureAwait(false);
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
        bool allowTokenRefresh = true)
    {
        // ConfigureAwait(false) on both awaits so the HTTP and JSON work
        // doesn't capture the calling SynchronizationContext — on WinUI 3
        // the UI thread was the one doing deserialization, which caused
        // multi-second freezes on larger catalog/home-section responses.
        using var response = await SendWithRetryAsync(request, ct, allowTokenRefresh).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) await ThrowApiException(response, request, ct).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct).ConfigureAwait(false))!;
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
        ApiError? error = null;
        try { error = await response.Content.ReadFromJsonAsync<ApiError>(JsonOptions, ct).ConfigureAwait(false); } catch { }
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden &&
            string.Equals(error?.Error, "profile_unverified", StringComparison.Ordinal))
        {
            var context = CreateProfileVerificationContext(request);
            try { _profileVerificationRequired?.Invoke(context); }
            catch { }
        }
        throw new ApiException(error?.Error ?? "unknown", error?.Message ?? $"HTTP {(int)response.StatusCode}", (int)response.StatusCode);
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
