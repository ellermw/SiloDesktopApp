using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.MediaMaintenance;

namespace SiloPlayer.Tests;

public sealed class MediaMaintenanceApiTests
{
    [Fact]
    public async Task SearchMatchesUsesTheCurrentServerContract()
    {
        var handler = new RecordingHandler("{\"candidates\":[]}");
        var api = CreateApi(handler);

        await api.SearchMatchesAsync("movie/1", new ItemMatchSearchRequest
        {
            Title = "Arrival",
            Year = 2016,
        });

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("/api/v1/admin/items/movie%2F1/match/search", handler.LastUri!.AbsolutePath);
        using var json = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("Arrival", json.RootElement.GetProperty("title").GetString());
        Assert.Equal(2016, json.RootElement.GetProperty("year").GetInt32());
    }

    [Fact]
    public async Task ApplyMatchUsesTheCurrentServerContract()
    {
        var handler = new RecordingHandler("{}", HttpStatusCode.NoContent);
        var api = CreateApi(handler);

        await api.ApplyMatchAsync("movie/1", new ItemMatchApplyRequest
        {
            ProviderIds = new Dictionary<string, string> { ["tmdb"] = "329865" },
        });

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("/api/v1/admin/items/movie%2F1/match/apply", handler.LastUri!.AbsolutePath);
        using var json = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("329865", json.RootElement.GetProperty("provider_ids").GetProperty("tmdb").GetString());
    }

    [Theory]
    [InlineData("quick")]
    [InlineData("complete")]
    public async Task RefreshMetadataUsesOnlyTheApprovedModes(string mode)
    {
        var handler = new RecordingHandler("{\"id\":\"job-1\",\"status\":\"queued\"}");
        var api = CreateApi(handler);

        var receipt = await api.RefreshMetadataAsync("movie/1", mode);

        Assert.Equal("job-1", receipt.Id);
        Assert.Equal("/api/v1/admin/items/movie%2F1/refresh-metadata", handler.LastUri!.AbsolutePath);
        using var json = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal(mode, json.RootElement.GetProperty("mode").GetString());
    }

    [Fact]
    public async Task RefreshMetadataRejectsUnknownModesBeforeSending()
    {
        var handler = new RecordingHandler("{}");
        var api = CreateApi(handler);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => api.RefreshMetadataAsync("movie-1", "deep"));

        Assert.Null(handler.LastUri);
    }

    [Fact]
    public async Task PermissionFailuresRemainApiExceptions()
    {
        var handler = new RecordingHandler(
            "{\"error\":\"forbidden\",\"message\":\"Metadata permission required\"}",
            HttpStatusCode.Forbidden);
        var api = CreateApi(handler);

        var error = await Assert.ThrowsAsync<ApiException>(
            () => api.RefreshMetadataAsync("movie-1", "quick"));

        Assert.Equal(403, error.StatusCode);
        Assert.Equal("forbidden", error.ErrorCode);
    }

    [Theory]
    [InlineData("search")]
    [InlineData("apply")]
    [InlineData("refresh")]
    public async Task MaintenanceRequestsRejectHttpBeforeDispatchingCredentials(string operation)
    {
        var handler = new RecordingHandler("{\"candidates\":[]}");
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("http://example.test");
        client.SetAccessToken("secret-access-token");
        client.SetProfile("profile-1", "secret-profile-token");
        var api = new MediaMaintenanceApi(client);

        Task Request() => operation switch
        {
            "search" => api.SearchMatchesAsync("movie-1", new ItemMatchSearchRequest()),
            "apply" => api.ApplyMatchAsync("movie-1", new ItemMatchApplyRequest()),
            "refresh" => api.RefreshMetadataAsync("movie-1", "quick"),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(Request);

        Assert.Contains("HTTPS", error.Message, StringComparison.Ordinal);
        Assert.Null(handler.LastUri);
    }

    private static MediaMaintenanceApi CreateApi(RecordingHandler handler)
    {
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://example.test");
        return new MediaMaintenanceApi(client);
    }

    private sealed class RecordingHandler(
        string responseJson,
        HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        public HttpMethod? LastMethod { get; private set; }
        public Uri? LastUri { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastMethod = request.Method;
            LastUri = request.RequestUri;
            LastBody = request.Content == null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseJson),
            };
        }
    }
}
