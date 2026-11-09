using System.Net;
using SiloPlayer.Core.Models.Home;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;
public sealed class BoxSetFoundationTests
{
    [Fact] public void AggregateCannotEnterMetadataMaintenance() => Assert.Equal(default, ItemMaintenanceActionPolicy.Resolve(true, "box_set"));
    [Fact] public void AggregateCannotEnterPersonalLists() => Assert.False(MediaItemActionPolicy.CanAddToPersonalLists("box_set"));
    [Fact] public void ViewerLibraryFlagIsParsedWithoutChangingOldServers()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var library = JsonSerializer.Deserialize<Library>("""{"id":7,"box_sets_enabled":true}""", options)!;
        var flag = typeof(Library).GetProperty("BoxSetsEnabled");
        Assert.NotNull(flag); Assert.Equal(true, flag.GetValue(library));
        Assert.Equal(false, flag.GetValue(new Library()));
    }
    [Fact] public async Task ReadPreservesStringIdentityAndLibrary()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://boxsets.invalid"); client.SetProfile("alpha");
        var api = new CatalogApi(client);
        var result = await api.GetBoxSetAsync("9007199254740993/a", "7");
        Assert.EndsWith("/api/v2/catalog/box-sets/9007199254740993%2Fa?library_id=7", wire.Last);
        Assert.Equal("9007199254740993", result.Id);
    }
    [Theory]
    [InlineData("7", "12")]
    [InlineData("18446744073709551615", "12")]
    [InlineData("\"set-7\"", "\"12\"")]
    public async Task CatalogBoxSetSummaryAcceptsNumericOrStringIdentifiers(string id, string library)
    {
        using var wire = new Wire { Json = "{\"items\":[{\"type\":\"box_set\",\"box_set\":{\"id\":" + id + ",\"library_id\":" + library + "}}]}" };
        using var http = new HttpClient(wire);
        var result = await new CatalogApi(Client(http)).GetCatalogAsync(12);
        Assert.Equal(id.Trim('"'), result.Items.Single().BoxSet!.Id);
        Assert.Equal("12", result.Items.Single().BoxSet!.LibraryId);
    }
    [Theory] [InlineData(403, DetailFailureKind.AccessDenied)] [InlineData(404, DetailFailureKind.NotFound)] [InlineData(503, DetailFailureKind.Transient)]
    public async Task DedicatedReaderPreservesFailureKind(int status, DetailFailureKind expected)
    {
        using var wire = new Wire { Status = (HttpStatusCode)status }; using var http = new HttpClient(wire);
        var error = await Assert.ThrowsAsync<ApiException>(() => new CatalogApi(Client(http)).GetBoxSetAsync("7", "2"));
        Assert.Equal(expected, DetailFailurePolicy.Classify(error));
    }
    [Fact] public async Task MembershipAndSearchHaveTheirOwnEnvelopesAndEscapedScopes()
    {
        using var wire = new Wire { Json = "{}" }; using var http = new HttpClient(wire); var api = new CatalogApi(Client(http));
        Assert.Null((await api.GetItemBoxSetAsync("movie/a?", 9)).BoxSet);
        Assert.EndsWith("/catalog/items/movie%2Fa%3F/box-set?library_id=9", wire.Last);
        wire.Json = """{"box_set":{"id":"4","library_id":"12","items":[{"type":"movie","content_id":"one"}]}}""";
        Assert.Equal("12", (await api.GetItemBoxSetAsync("movie" )).BoxSet!.LibraryId);
        wire.Json = """{"items":[{"id":"4","library_id":"12","movie_count":2}]}""";
        Assert.Equal("4", (await api.SearchBoxSetsAsync("A&B", 9, 100)).Items.Single().Id);
        Assert.EndsWith("/catalog/box-sets?q=A%26B&limit=20&library_id=9", wire.Last);
    }
    [Theory] [InlineData("profile")] [InlineData("language")] [InlineData("access")] [InlineData("cancel")]
    public async Task LateReaderCannotPublishUnderNewOwner(string change)
    {
        using var wire = new Wire { Hold = true }; using var http = new HttpClient(wire); var client = Client(http);
        using var cancellation = new CancellationTokenSource();
        var pending = new CatalogApi(client).GetBoxSetAsync("7", "2", cancellation.Token);
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        if (change == "profile") client.SetProfile("beta");
        if (change == "access") client.InvalidateAccessContext();
        if (change == "language") client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
        if (change == "cancel") cancellation.Cancel();
        wire.Release.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }
    [Fact] public async Task AggregateMutationsNeverReachPersistence()
    {
        var calls = 0; var set = new MediaItem { Type = "box_set", ContentId = "box_set:7" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => MediaItemActionExecutor.ToggleWatchedAsync(set, _ => { calls++; return Task.CompletedTask; }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => MediaItemActionExecutor.ToggleFavoriteAsync(set, _ => { calls++; return Task.CompletedTask; }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => MediaItemActionExecutor.ResetProgressAsync(set, () => { calls++; return Task.CompletedTask; }));
        Assert.Equal(0, calls); Assert.Null(set.UserState);
    }
    [Fact] public void PlaySelectionUsesReleaseOrderAndNeverSyntheticAggregate()
    {
        var detail = new BoxSetDetail { MovieCount = 3, PlayedCount = 1, Items = [new() { Type = "box_set", ContentId = "box_set:7" }, new() { Type = "movie", ContentId = "first", UserState = new() { Played = true } }, new() { Type = "movie", ContentId = "second" }] };
        Assert.Equal("second", detail.PlayTarget!.ContentId); Assert.Equal("Continue", detail.PlayLabel);
        detail.Items[2].UserState = new() { Played = true }; detail.PlayedCount = 3;
        Assert.Equal("first", detail.PlayTarget!.ContentId); Assert.Equal("Play", detail.PlayLabel);
        detail.Items.Clear(); Assert.Null(detail.PlayTarget);
    }
    private static SiloApiClient Client(HttpClient http) { var client = new SiloApiClient(http); client.SetBaseUrl("https://boxsets.invalid"); client.SetProfile("alpha"); return client; }

    private sealed class Wire : HttpMessageHandler
    {
        internal string? Last;
        internal string Json = """{"id":"9007199254740993","library_id":"7","title":"Films","items":[]}""";
        internal HttpStatusCode Status = HttpStatusCode.OK;
        internal bool Hold;
        internal TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request.RequestUri!.AbsoluteUri;
            Started.TrySetResult(); if (Hold) await Release.Task;
            return new HttpResponseMessage(Status) { Content = new StringContent(Json) };
        }
    }
}
