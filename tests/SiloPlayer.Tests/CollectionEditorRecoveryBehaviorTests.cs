using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class CollectionEditorRecoveryBehaviorTests
{
    [Theory]
    [InlineData("tmdb", "tmdb_discover", "daily", "tmdb_discover", true)]
    [InlineData("trakt", "trakt_preset", "daily", "trakt", true)]
    [InlineData("trakt", "trakt_preset", "", "trakt", false)]
    public async Task LockedSourceKeepsItsConfigurationAndHonorsExistingSchedule(string type, string mode, string cadence, string source, bool canSchedule)
    {
        using var wire = new LockedSourceWire(type, mode, cadence); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://locked-collection.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SetTokens("fixture", "fixture", 86400); auth.SelectProfile("mine", profile: new() { Id = "mine", IsPrimary = true });
        var vm = new CollectionEditorViewModel(new CollectionsApi(client), new CatalogApi(client), new SettingsApi(client), auth);
        await vm.LoadExistingCommand.ExecuteAsync("fixture");
        Assert.Equal(source, vm.SourceKind);
        Assert.Equal(canSchedule, vm.GetType().GetProperty("CanEditSavedSyncSchedule")?.GetValue(vm) as bool?);
        vm.Name = "Renamed"; vm.SyncSchedule = canSchedule ? "weekly" : "daily";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Null(vm.ErrorMessage);
        Assert.NotNull(wire.Body);
        using var body = System.Text.Json.JsonDocument.Parse(wire.Body!);
        Assert.False(body.RootElement.TryGetProperty("source_config", out _));
        Assert.Equal(canSchedule, body.RootElement.TryGetProperty("sync_schedule", out _));
    }

    private sealed class LockedSourceWire(string type, string mode, string cadence) : HttpMessageHandler
    {
        public string? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Patch) Body = await request.Content!.ReadAsStringAsync(ct);
            var path = request.RequestUri!.AbsolutePath;
            var json = path == "/api/v2/collections/fixture" ? System.Text.Json.JsonSerializer.Serialize(new { id = "fixture", name = "Saved", creator_profile_id = "mine", collection_type = type, item_count = 2, source_config = new { mode }, sync_cadence = cadence }) : path.EndsWith("/capabilities") ? "{\"sync_schedule_editable\":true}" : "{\"items\":[]}";
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"fixture-revision\"");
            return response;
        }
    }

    [Fact]
    public async Task SyncRefreshesSavedStatusWithoutReplacingAnEditableDraft()
    {
        using var wire = new SyncWire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://collection-sync.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SelectProfile("mine", profile: new() { Id = "mine", IsPrimary = true });
        var vm = new CollectionEditorViewModel(new CollectionsApi(client), new CatalogApi(client), new SettingsApi(client), auth);
        await vm.LoadExistingCommand.ExecuteAsync("fixture");
        vm.Name = "Local draft";
        await vm.SyncNowCommand.ExecuteAsync(null);
        Assert.Equal(7, vm.LoadedCollection!.ItemCount);
        Assert.Equal("success", vm.LoadedCollection.LastSyncStatus);
        Assert.Equal("2026-10-09T20:00:00Z", vm.LoadedCollection.LastSyncAt);
        Assert.Equal("Local draft", vm.Name);
        Assert.Equal("7", vm.SourceItemCountText);
    }
    private sealed class SyncWire : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var json = path.EndsWith("/sync") ? "{\"status\":\"success\",\"items_matched\":7,\"items_unmatched\":3,\"completed_at\":\"2026-10-09T20:00:00Z\"}" :
                path == "/api/v2/collections/fixture" ? "{\"id\":\"fixture\",\"name\":\"Saved\",\"creator_profile_id\":\"mine\",\"collection_type\":\"mdblist\",\"item_count\":2}" : "{\"items\":[]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
    [Fact]
    public async Task NewEditorReferencesRemainPendingUntilOptionsComplete()
    {
        using var handler = new PendingReferences(); using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://collection-options.invalid"); client.SetProfile("mine");
        using var auth = new AuthService(client, new AuthApi(client));
        var vm = new CollectionEditorViewModel(new CollectionsApi(client), new CatalogApi(client), new SettingsApi(client), auth);
        var load = vm.LoadReferenceDataCommand.ExecuteAsync(null);
        await handler.Started.Task;
        Assert.True(vm.IsLoading);
        handler.Gate.SetResult(true); await load;
        Assert.False(vm.IsLoading); Assert.False(vm.IsLoadUnavailable);
    }

    [Fact]
    public async Task FailedNewEditorReferencesAreRetryableAndDoNotExposeReadyState()
    {
        using var handler = new PendingReferences { Fail = true }; using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://collection-options.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        var vm = new CollectionEditorViewModel(new CollectionsApi(client), new CatalogApi(client), new SettingsApi(client), auth);
        handler.Gate.SetResult(true); await vm.LoadReferenceDataCommand.ExecuteAsync(null);
        Assert.True(vm.IsLoadUnavailable); Assert.False(vm.IsLoading);
        handler.Fail = false; await vm.LoadReferenceDataCommand.ExecuteAsync(null);
        Assert.False(vm.IsLoadUnavailable); Assert.Null(vm.ErrorMessage);
    }

    private sealed class PendingReferences : HttpMessageHandler
    {
        public bool Fail;
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/capabilities"))
            {
                Started.TrySetResult(true); await Gate.Task.WaitAsync(ct);
                if (Fail) return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{\"message\":\"options unavailable\"}") };
            }
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"items\":[]}") };
        }
    }

    [Fact]
    public async Task ExistingCollectionIsReadOnlyUntilItsOwnerProfileIsKnown()
    {
        using var wire = new Wire { Status = HttpStatusCode.OK }; var client = wire.Client(); using var auth = new AuthService(client, new AuthApi(client));
        var vm = new CollectionEditorViewModel(new CollectionsApi(client), new CatalogApi(client), new SettingsApi(client), auth);
        await vm.LoadExistingCommand.ExecuteAsync("fixture");
        Assert.True(vm.IsReadOnly);
    }
    [Fact]
    public async Task FailedExistingLoadShowsUnavailableAndRetryRestoresEditor()
    {
        using var wire = new Wire(); var client = wire.Client(); using var auth = new AuthService(client, new AuthApi(client));
        var vm = new CollectionEditorViewModel(new CollectionsApi(client), new CatalogApi(client), new SettingsApi(client), auth);
        await vm.LoadExistingCommand.ExecuteAsync("fixture"); Assert.True(vm.IsLoadUnavailable); Assert.False(vm.IsNotFound);
        wire.Status = HttpStatusCode.OK; await vm.LoadExistingCommand.ExecuteAsync("fixture");
        Assert.False(vm.IsLoadUnavailable); Assert.False(vm.IsNotFound); Assert.Equal("Recovered", vm.Name); Assert.Null(vm.ErrorMessage);
    }
    [Fact]
    public async Task MissingCollectionShowsNotFoundWithoutEditableDraft()
    {
        using var wire = new Wire { Status = HttpStatusCode.NotFound }; var client = wire.Client(); using var auth = new AuthService(client, new AuthApi(client));
        var vm = new CollectionEditorViewModel(new CollectionsApi(client), new CatalogApi(client), new SettingsApi(client), auth);
        await vm.LoadExistingCommand.ExecuteAsync("fixture"); Assert.True(vm.IsNotFound); Assert.Equal("Collection not found.", vm.ErrorMessage);
    }
    private sealed class Wire : HttpMessageHandler
    {
        public HttpStatusCode Status = HttpStatusCode.ServiceUnavailable;
        public SiloApiClient Client() { var client = new SiloApiClient(new HttpClient(this)); client.SetBaseUrl("https://collection-editor.invalid"); return client; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var collection = request.RequestUri!.AbsolutePath == "/api/v2/collections/fixture";
            var status = collection ? Status : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(collection && status == HttpStatusCode.OK ? "{\"id\":\"fixture\",\"name\":\"Recovered\",\"collection_type\":\"smart\"}" : status == HttpStatusCode.OK ? "{\"items\":[]}" : "{\"message\":\"collection unavailable\"}") });
        }
    }
}
