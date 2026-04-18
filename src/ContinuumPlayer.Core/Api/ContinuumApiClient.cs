using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ContinuumPlayer.Core.Models;

namespace ContinuumPlayer.Core.Api;

public class ApiException(string errorCode, string message, int statusCode) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
    public int StatusCode { get; } = statusCode;
}

public class ContinuumApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    private readonly HttpClient _http;
    private string? _accessToken;
    private string? _profileId;
    private string? _profileToken;
    private string _baseUrl = "";
    private Func<CancellationToken, Task<bool>>? _tokenRefresher;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public ContinuumApiClient(HttpClient http) { _http = http; }

    /// <summary>
    /// Registers a callback that attempts to refresh the access token.
    /// Called automatically on 401 responses before retrying the request.
    /// </summary>
    public void SetTokenRefresher(Func<CancellationToken, Task<bool>> refresher) => _tokenRefresher = refresher;

    public void SetBaseUrl(string baseUrl) => _baseUrl = baseUrl.TrimEnd('/');
    public string BaseUrl => _baseUrl;

    public void SetAccessToken(string token) => _accessToken = token;
    public void SetProfile(string profileId, string? profileToken = null)
    {
        _profileId = profileId;
        _profileToken = profileToken;
    }
    public void ClearAuth()
    {
        _accessToken = null;
        _profileId = null;
        _profileToken = null;
    }

    public string? AccessToken => _accessToken;
    public string? ProfileId => _profileId;
    public string? ProfileToken => _profileToken;

    private string BuildUrl(string path) => _baseUrl + path;

    public async Task<T> GetAsync<T>(string path, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUrl(path));
        AddHeaders(request);
        return await SendAsync<T>(request, ct);
    }

    public async Task<T> PostAsync<T>(string path, object body, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl(path));
        AddHeaders(request);
        request.Content = CreateJsonContent(body);
        return await SendAsync<T>(request, ct);
    }

    public async Task PostNoContentAsync(string path, object body, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl(path));
        AddHeaders(request);
        request.Content = CreateJsonContent(body);
        await SendNoContentAsync(request, ct);
    }

    public async Task<T> PutAsync<T>(string path, object body, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, BuildUrl(path));
        AddHeaders(request);
        request.Content = CreateJsonContent(body);
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
        if (_accessToken != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        if (_profileId != null) request.Headers.Add("X-Profile-Id", _profileId);
        if (_profileToken != null) request.Headers.Add("X-Profile-Token", _profileToken);
    }

    // Buffer content before sending so we can replay on 401 retry
    private async Task<HttpResponseMessage> SendWithRetryAsync(HttpRequestMessage request, CancellationToken ct)
    {
        // Pre-buffer content — after SendAsync the content stream is consumed
        byte[]? contentBytes = null;
        System.Net.Http.Headers.MediaTypeHeaderValue? contentType = null;
        if (request.Content != null)
        {
            contentBytes = await request.Content.ReadAsByteArrayAsync(ct);
            contentType = request.Content.Headers.ContentType;
        }

        var response = await _http.SendAsync(request, ct);

        // On 401, try refreshing the token and retry once
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && _tokenRefresher != null)
        {
            bool refreshed = false;
            await _refreshLock.WaitAsync(ct);
            try { refreshed = await _tokenRefresher(ct); }
            finally { _refreshLock.Release(); }

            if (refreshed)
            {
                using var retry = new HttpRequestMessage(request.Method, request.RequestUri);
                AddHeaders(retry);
                if (contentBytes != null)
                {
                    retry.Content = new ByteArrayContent(contentBytes);
                    retry.Content.Headers.ContentType = contentType;
                }
                response = await _http.SendAsync(retry, ct);
            }
        }

        return response;
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken ct)
    {
        // ConfigureAwait(false) on both awaits so the HTTP and JSON work
        // doesn't capture the calling SynchronizationContext — on WinUI 3
        // the UI thread was the one doing deserialization, which caused
        // multi-second freezes on larger catalog/home-section responses.
        var response = await SendWithRetryAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) await ThrowApiException(response, ct).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct).ConfigureAwait(false))!;
    }

    private async Task SendNoContentAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var response = await SendWithRetryAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) await ThrowApiException(response, ct).ConfigureAwait(false);
    }

    private static async Task ThrowApiException(HttpResponseMessage response, CancellationToken ct)
    {
        ApiError? error = null;
        try { error = await response.Content.ReadFromJsonAsync<ApiError>(JsonOptions, ct); } catch { }
        throw new ApiException(error?.Error ?? "unknown", error?.Message ?? $"HTTP {(int)response.StatusCode}", (int)response.StatusCode);
    }
}
