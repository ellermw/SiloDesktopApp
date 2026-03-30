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

    public ContinuumApiClient(HttpClient http) { _http = http; }

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
        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) await ThrowApiException(response, ct);
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
        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) await ThrowApiException(response, ct);
    }

    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, BuildUrl(path));
        AddHeaders(request);
        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) await ThrowApiException(response, ct);
    }

    public async Task<T> PatchAsync<T>(string path, object body, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, BuildUrl(path));
        AddHeaders(request);
        request.Content = CreateJsonContent(body);
        return await SendAsync<T>(request, ct);
    }

    /// <summary>
    /// Serialize using property reflection that works in all build modes.
    /// .NET 8 self-contained publish disables System.Text.Json reflection by default,
    /// so we build a dictionary manually from the object's properties.
    /// </summary>
    private static StringContent CreateJsonContent(object body)
    {
        // Build a dictionary from the object's public properties,
        // applying snake_case naming manually. This bypasses System.Text.Json's
        // broken reflection path in .NET 8 self-contained publish builds.
        var props = body.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        var dict = new Dictionary<string, object?>();
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

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken ct)
    {
        var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) await ThrowApiException(response, ct);
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct))!;
    }

    private static async Task ThrowApiException(HttpResponseMessage response, CancellationToken ct)
    {
        ApiError? error = null;
        try { error = await response.Content.ReadFromJsonAsync<ApiError>(JsonOptions, ct); } catch { }
        throw new ApiException(error?.Error ?? "unknown", error?.Message ?? $"HTTP {(int)response.StatusCode}", (int)response.StatusCode);
    }
}
