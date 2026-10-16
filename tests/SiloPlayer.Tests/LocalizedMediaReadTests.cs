using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class LocalizedMediaReadTests
{
    [Theory]
    [InlineData("/api/v2/catalog/items/movie")]
    [InlineData("/api/v2/home/sections/featured/items")]
    [InlineData("/api/v2/library/1/sections/featured/items")]
    [InlineData("/api/v2/collections")]
    [InlineData("/api/v2/calendar")]
    [InlineData("/api/v2/recommendations/rows")]
    [InlineData("/api/v2/favorites")]
    [InlineData("/api/v2/watchlist")]
    [InlineData("/api/v2/history")]
    [InlineData("/api/v2/progress")]
    [InlineData("/api/v2/shuffles/session")]
    [InlineData("/api/v2/watch-together/rooms/room/picker")]
    public async Task MetadataChangeRetiresOldLanguageResponseEvenWhenHandlerIgnoresCancellation(string path)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler((_, _) => { started.TrySetResult(); return gate.Task; }));
        var client = Client(http);
        var pending = client.GetAsync<Dictionary<string, string>>(path);
        await started.Task;
        Notify(client, "catalog.metadata_language");
        gate.SetResult(Json("{\"title\":\"old language\"}"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Theory]
    [InlineData("/api/v2/playback/sessions/session", "GET")]
    [InlineData("/api/v2/settings/values", "GET")]
    [InlineData("/api/v2/catalog/items/movie", "PATCH")]
    public async Task MetadataChangeDoesNotCancelPlaybackSettingsOrMutations(string path, string method)
    {
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler((_, _) => { started.TrySetResult(); return gate.Task; }));
        var client = Client(http);
        var identity = client.CaptureIdentityContext();
        var context = client.CaptureContext();
        var pending = client.SendRequestAsync<Dictionary<string, string>>(new HttpMethod(method), path, null, null);
        await started.Task;
        Notify(client, "catalog.metadata_language_overrides");
        gate.SetResult(Json("{\"status\":\"ok\"}"));
        Assert.Equal("ok", (await pending)["status"]);
        Assert.Equal(identity, client.CaptureIdentityContext());
        Assert.Equal(context, client.CaptureContext());
    }

    [Fact]
    public async Task UnrelatedSettingKeepsLocalizedReadAndNewReadAfterLanguageChangeSucceeds()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Json("{\"title\":\"current\"}"))));
        var client = Client(http);
        Notify(client, "playback.audio_language");
        Assert.Equal("current", (await client.GetAsync<Dictionary<string, string>>("/api/v2/catalog/items/movie"))["title"]);
        Notify(client, "catalog.metadata_language");
        Assert.Equal("current", (await client.GetAsync<Dictionary<string, string>>("/api/v2/catalog/items/movie"))["title"]);
    }

    [Fact]
    public async Task LargeStructuredCatalogQueryIsLocalizedButTranslateMutationIsNot()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler(async (_, _) => { await gate.Task; return Json("{\"status\":\"ok\"}"); }));
        var client = Client(http);
        var query = client.PostAsync<Dictionary<string, string>>("/api/v2/catalog/query", new { });
        var translate = client.PostAsync<Dictionary<string, string>>("/api/v2/catalog/items/movie/translate-description", new { });
        Notify(client, "catalog.metadata_language");
        gate.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query);
        Assert.Equal("ok", (await translate)["status"]);
    }

    [Fact]
    public void RealtimeChangeOnlyRetiresTheSelectedProfilesMetadata()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Json("{}"))));
        var client = Client(http);
        var changes = 0;
        client.LocalizedReads.Invalidated += _ => changes++;
        using var other = JsonDocument.Parse("{\"key\":\"catalog.metadata_language\",\"profile_id\":\"other\"}");
        client.LocalizedReads.NotifyRealtime("user_settings", "user_settings.changed", other.RootElement, "profile");
        Assert.Equal(0, changes);
        using var current = JsonDocument.Parse("{\"key\":\"catalog.metadata_language\",\"profile_id\":\"profile\"}");
        client.LocalizedReads.NotifyRealtime("user_settings", "user_settings.changed", current.RootElement, "profile");
        Assert.Equal(1, changes);
        client.LocalizedReads.NotifyRealtime("catalog", "user_settings.changed", current.RootElement, "profile");
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task LanguageChangeInvalidatesFilterAndPrefetchedDetailCaches()
    {
        var requests = 0;
        using var http = new HttpClient(new Handler((_, _) => { requests++; return Task.FromResult(Json("{}")); }));
        var client = Client(http);
        var catalog = new CatalogApi(client);
        var auth = new AuthService(client, new AuthApi(client), new EmptyCredentials());
        var details = new ItemDetailPrefetchCache(catalog, auth);
        var firstFilters = await catalog.GetFiltersAsync();
        var firstDetail = await details.GetAsync("movie", CancellationToken.None);
        Assert.Same(firstFilters, await catalog.GetFiltersAsync());
        Assert.Same(firstDetail, await details.GetAsync("movie", CancellationToken.None));
        var generation = catalog.FilterCacheGeneration;
        Assert.Equal(2, requests);
        Notify(client, "catalog.metadata_language");
        Assert.NotEqual(generation, catalog.FilterCacheGeneration);
        Assert.NotSame(firstFilters, await catalog.GetFiltersAsync());
        Assert.NotSame(firstDetail, await details.GetAsync("movie", CancellationToken.None));
        Assert.Equal(4, requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LocalizedEtagReadCannotPublishOldLanguage(bool contextBound)
    {
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler((_, _) => gate.Task));
        var client = Client(http);
        var pending = contextBound
            ? client.GetWithETagAsync<Dictionary<string, string>>(client.CaptureContext(), "/api/v2/collections/one")
            : client.GetWithETagAsync<Dictionary<string, string>>("/api/v2/collections/one");
        Notify(client, "catalog.metadata_language_overrides");
        gate.SetResult(Json("{\"title\":\"old\"}"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    private static void Notify(SiloApiClient client, string key)
        => new SettingsApi(client).NotifyLocalizedSettingChanged(key);

    private static SiloApiClient Client(HttpClient http)
    {
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://localized.invalid");
        client.SetProfile("profile");
        return client;
    }
    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handle(request, cancellationToken);
    }
    private sealed class EmptyCredentials : ICredentialStore
    {
        public void SaveCredential(string serverUrl, string key, string value) { }
        public string? LoadCredential(string serverUrl, string key) => null;
        public void DeleteCredential(string serverUrl, string key) { }
        public void DeleteAllForServer(string serverUrl) { }
    }
}
