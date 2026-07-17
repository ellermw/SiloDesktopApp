using System.Net.Http.Json;
using SiloPlayer.Core.Models.Settings;

namespace SiloPlayer.Core.Api;

public sealed class PlexBrowserAuthApi : IDisposable
{
    private readonly HttpClient _client = new() { BaseAddress = new Uri("https://plex.tv") };
    private readonly string _clientIdentifier = $"silo-desktop-{Guid.NewGuid():N}";

    public async Task<PlexBrowserPin> CreatePinAsync(CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Post, "/api/v2/pins");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["strong"] = "true" });
        using var response = await _client.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<PlexBrowserPin>(cancellationToken: ct)
               ?? throw new InvalidOperationException("Plex returned an empty PIN response.");
    }

    public Uri BuildAuthenticationUri(PlexBrowserPin pin)
    {
        var query = $"clientID={Uri.EscapeDataString(_clientIdentifier)}&code={Uri.EscapeDataString(pin.Code)}" +
                    $"&context%5Bdevice%5D%5Bproduct%5D={Uri.EscapeDataString("Silo")}";
        return new Uri($"https://app.plex.tv/auth#?{query}");
    }

    public async Task<string?> CheckPinAsync(PlexBrowserPin pin, CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Get,
            $"/api/v2/pins/{pin.Id}?code={Uri.EscapeDataString(pin.Code)}");
        using var response = await _client.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
        var result = await response.Content.ReadFromJsonAsync<PlexBrowserPin>(cancellationToken: ct);
        return result?.AuthToken;
    }

    public async Task<List<PlexBrowserResource>> GetServersAsync(string token, CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Get, "/api/v2/resources?includeHttps=1&includeRelay=1", token);
        using var response = await _client.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
        var resources = await response.Content.ReadFromJsonAsync<List<PlexBrowserResource>>(cancellationToken: ct) ?? [];
        return resources.Where(resource => resource.Provides.Split(',').Contains("server", StringComparer.OrdinalIgnoreCase)).ToList();
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, string? token = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.TryAddWithoutValidation("X-Plex-Client-Identifier", _clientIdentifier);
        request.Headers.TryAddWithoutValidation("X-Plex-Product", "Silo");
        request.Headers.TryAddWithoutValidation("X-Plex-Version", "1.0.0");
        request.Headers.TryAddWithoutValidation("X-Plex-Platform", "Windows");
        request.Headers.TryAddWithoutValidation("X-Plex-Device", "Desktop");
        request.Headers.TryAddWithoutValidation("X-Plex-Device-Name", "Silo Desktop");
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.TryAddWithoutValidation("X-Plex-Token", token);
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var body = (await response.Content.ReadAsStringAsync(ct)).Trim();
        throw new HttpRequestException(string.IsNullOrWhiteSpace(body)
            ? $"Plex request failed with status {(int)response.StatusCode}."
            : body);
    }

    public void Dispose() => _client.Dispose();
}
