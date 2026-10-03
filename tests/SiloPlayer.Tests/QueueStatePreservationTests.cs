using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public class QueueStatePreservationTests
{
    [Fact]
    public void DropSnapshotRestoresBothRowsAfterRefreshWithoutDuplicates()
    {
        var continueRow = new HomeSectionWithItems { Id = "continue", SectionType = "continue_watching",
            Items = [new() { ContentId = "episode", SeriesId = "show" }, new() { ContentId = "movie" }] };
        var next = new HomeSectionWithItems { Id = "next", SectionType = "next_up", Items = [new() { ContentId = "show", Type = "series" }] };
        var snapshot = HomeDismissalSnapshot.Capture([continueRow, next], "episode", "show");
        continueRow.Items.RemoveAt(0); next.Items.Clear();
        snapshot.Restore([continueRow, next]); snapshot.Restore([continueRow, next]);
        Assert.Equal(new[] { "episode", "movie" }, continueRow.Items.Select(i => i.ContentId));
        Assert.Single(next.Items); Assert.Equal("show", next.Items[0].ContentId);
    }

    [Theory]
    [InlineData("12", true, true, 12)]
    [InlineData("future-ceiling", false, false, 12)]
    [InlineData("16", true, false, 16)]
    [InlineData("", true, true, null)]
    public async Task ProfileWritesPreserveCeilingAndOnlyAdvertisedAdvisoryFields(string rating, bool age, bool require, int? value)
    {
        JsonElement body = default;
        using var client = new HttpClient(new Wire(async request =>
        {
            body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync()).RootElement.Clone();
            return Json("{\"id\":\"fixture\"}");
        }));
        var apiClient = new SiloApiClient(client); apiClient.SetBaseUrl("https://fixture.invalid");
        await new AuthApi(apiClient).UpdateProfileAsync("fixture", new() { Name = "Renamed", MaxContentRating = rating,
            MaxAdvisoryAgeSupported = age, RequireAdvisoryAgeSupported = require, MaxAdvisoryAge = value, RequireAdvisoryAge = true });
        Assert.Equal(rating.Length == 0 ? null : rating, body.GetProperty("max_content_rating").GetString());
        Assert.Equal(age, body.TryGetProperty("max_advisory_age", out var stored));
        if (age) Assert.Equal(value, stored.ValueKind == JsonValueKind.Null ? null : stored.GetInt32());
        Assert.Equal(age && require, body.TryGetProperty("require_advisory_age", out var required));
        if (age && require) Assert.Equal(value.HasValue, required.GetBoolean());
    }

    [Fact]
    public async Task ProviderNewValuesReadFromProtectedRevisionAndPatchOnlyChangedField()
    {
        JsonElement patch = default;
        using var http = new HttpClient(new Wire(async request =>
        {
            if (request.Method == HttpMethod.Patch)
            {
                Assert.Equal("\"r1\"", request.Headers.GetValues("If-Match").Single());
                patch = JsonDocument.Parse(await request.Content!.ReadAsStringAsync()).RootElement.Clone();
            }
            var response = Json(request.RequestUri!.AbsolutePath.EndsWith("/settings")
                ? """{"import_ratings_enabled":true,"export_ratings_enabled":false,"sync_dropped_enabled":true}"""
                : """{"provider":"trakt","connected":true,"capabilities":{"import_ratings":true,"sync_dropped":true}}""");
            response.Headers.ETag = new("\"r1\""); return response;
        }));
        var client = new SiloApiClient(http); client.SetBaseUrl("https://fixture.invalid");
        var api = new WatchProvidersApi(client); var connection = await api.GetConnectionAsync("trakt");
        Assert.True(connection.ImportRatingsEnabled); Assert.True(connection.SyncDroppedEnabled);
        await api.UpdateConnectionAsync("trakt", new Dictionary<string, object?> { ["export_ratings_enabled"] = true });
        Assert.Single(patch.EnumerateObject()); Assert.True(patch.GetProperty("export_ratings_enabled").GetBoolean());
    }
    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
    private sealed class Wire(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => send(r); }
}
