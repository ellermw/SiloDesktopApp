using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class CollectionEditorCurrentDraftTests
{
    [Fact]
    public async Task FailedMetadataAfterCreationRetriesTheCreatedIdentity()
    {
        using var wire = new Wire { FailFirstPatch = true };
        var client = wire.Client(); using var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("mine");
        var vm = new CollectionEditorViewModel(new CollectionsApi(client), new CatalogApi(client), new SettingsApi(client), auth)
            { Name = "Draft", Description = "Keep this", CollectionType = "smart" };
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal("created", vm.CollectionId); Assert.True(vm.IsEditing); Assert.NotNull(vm.ErrorMessage);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Null(vm.ErrorMessage); Assert.Equal(1, wire.CreateCount); Assert.Equal(2, wire.PatchCount);
        Assert.Equal("Keep this", wire.LastPatch!.Value.GetProperty("description").GetString());
    }

    [Fact]
    public async Task RemovingArtworkIsADraftUntilSave()
    {
        using var wire = new Wire(); var client = wire.Client(); using var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("mine");
        var vm = new CollectionEditorViewModel(new CollectionsApi(client), new CatalogApi(client), new SettingsApi(client), auth);
        await vm.LoadExistingCommand.ExecuteAsync("created"); Assert.False(vm.IsReadOnly); Assert.NotNull(vm.CurrentPosterUrl);
        await vm.RemovePosterCommand.ExecuteAsync(null);
        Assert.Null(vm.CurrentPosterUrl); Assert.True(vm.RemovePosterOnSave); Assert.Equal(0, wire.DeletePosterCount);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Null(vm.ErrorMessage); Assert.Equal(1, wire.DeletePosterCount); Assert.False(vm.RemovePosterOnSave);
    }

    [Fact]
    public void MissingSourcesNeverEnablesAnImport() => Assert.False(CollectionImportPolicy.CanCreate(new() { Imports = true }, "mdblist"));

    [Fact]
    public async Task ChangingSmartOrderClearsOnlyStoredDefaultOrder()
    {
        using var wire = new Wire(); var client = wire.Client(); using var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("mine");
        var vm = new CollectionEditorViewModel(new CollectionsApi(client), new CatalogApi(client), new SettingsApi(client), auth);
        await vm.LoadExistingCommand.ExecuteAsync("created");
        vm.RuleDefinition.Sort = new() { Field = "year", Order = "desc" }; vm.ClearSmartDefaultSort();
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Null(vm.ErrorMessage);
        var sort = wire.LastPatch!.Value.GetProperty("sort_config"); Assert.False(sort.TryGetProperty("field", out _)); Assert.False(sort.TryGetProperty("order", out _)); Assert.Equal("manual_pins", sort.GetProperty("mode").GetString());
        Assert.Equal("year", wire.LastPatch.Value.GetProperty("query_definition").GetProperty("sort").GetProperty("field").GetString());
    }

    [Fact]
    public async Task ManualCollectionRenameOmitsSourceFields()
    {
        using var wire = new Wire { Kind = "manual" }; var client = wire.Client(); using var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("mine");
        var vm = new CollectionEditorViewModel(new CollectionsApi(client), new CatalogApi(client), new SettingsApi(client), auth);
        await vm.LoadExistingCommand.ExecuteAsync("created"); Assert.False(vm.IsImportedCollection); Assert.False(vm.CanEditSavedSyncSchedule);
        vm.Name = "Rename only"; await vm.SaveCommand.ExecuteAsync(null); Assert.Null(vm.ErrorMessage);
        Assert.False(wire.LastPatch!.Value.TryGetProperty("max_items", out _)); Assert.False(wire.LastPatch.Value.TryGetProperty("library_ids", out _));
        Assert.Equal("Rename only", wire.LastPatch.Value.GetProperty("name").GetString());
    }

    [Theory]
    [InlineData(true, "title:desc")]
    [InlineData(true, "year:asc")]
    [InlineData(true, "")]
    [InlineData(false, "title:desc")]
    [InlineData(false, "year:asc")]
    [InlineData(false, "")]
    [InlineData(null, "title:desc")]
    [InlineData(null, "year:asc")]
    [InlineData(null, "")]
    public async Task ImportedSortWritesRequireDefaultSortCapability(bool? supported, string editedSort)
    {
        using var wire = new Wire { Kind = "mdblist", CollectionDefaultSort = supported };
        var client = wire.Client(); using var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("mine");
        var vm = new CollectionEditorViewModel(new CollectionsApi(client), new CatalogApi(client), new SettingsApi(client), auth);
        await vm.LoadExistingCommand.ExecuteAsync("created");
        Assert.Null(vm.ErrorMessage); Assert.Equal("title:asc", vm.DefaultSortValue);
        Assert.Equal(supported == true, vm.Capabilities!.CollectionDefaultSort);
        // Bypass the disabled UI to verify the write boundary independently.
        vm.DefaultSortValue = editedSort; vm.Name = "Renamed";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Null(vm.ErrorMessage); Assert.Equal(1, wire.PatchCount);
        var patch = wire.LastPatch!.Value;
        Assert.Equal("Renamed", patch.GetProperty("name").GetString());
        Assert.Equal(supported == true, patch.TryGetProperty("sort_config", out var sort));
        if (supported == true)
        {
            Assert.Equal("manual_pins", sort.GetProperty("mode").GetString());
            if (editedSort.Length == 0)
            {
                Assert.False(sort.TryGetProperty("field", out _)); Assert.False(sort.TryGetProperty("order", out _));
            }
            else
            {
                var parts = editedSort.Split(':', 2);
                Assert.Equal(parts[0], sort.GetProperty("field").GetString());
                Assert.Equal(parts[1], sort.GetProperty("order").GetString());
            }
        }
    }

    [Fact]
    public async Task UndoUsesStoredPositionRatherThanAppendPosition()
    {
        using var wire = new Wire(); var client = wire.Client();
        await new CollectionsApi(client).RestoreCollectionItemAsync("created", "removed", 5);
        Assert.Equal(5, wire.RestoredPosition);
    }

    [Theory]
    [InlineData("https://mdblist.com/lists/example/favorites/?sort=rank#top", true)]
    [InlineData("https://mdblist.com.evil.invalid/lists/example/favorites", false)]
    [InlineData("https://mdblist.com/movie/1", false)]
    public void MDBListInputIsARealListLink(string value, bool expected) => Assert.Equal(expected, CollectionImportPolicy.IsMDBListUrl(value));

    private sealed class Wire : HttpMessageHandler
    {
        public bool FailFirstPatch; public string Kind = "smart"; public int CreateCount, PatchCount, DeletePosterCount; public JsonElement? LastPatch; public int? RestoredPosition;
        public bool? CollectionDefaultSort = true;
        public SiloApiClient Client() { var client = new SiloApiClient(new HttpClient(this)); client.SetBaseUrl("https://collection-draft.invalid"); return client; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Put && path.EndsWith("/items/removed")) RestoredPosition = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement.GetProperty("position").GetInt32();
            if (request.Method == HttpMethod.Post && path == "/api/v2/collections") CreateCount++;
            if (request.Method == HttpMethod.Delete && path.EndsWith("/image") && request.RequestUri.Query.Contains("type=poster")) DeletePosterCount++;
            if (request.Method == HttpMethod.Patch)
            {
                PatchCount++; LastPatch = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement.Clone();
                if (FailFirstPatch && PatchCount == 1) return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{\"message\":\"metadata unavailable\"}") };
            }
            var json = path == "/api/v2/collections" || path == "/api/v2/collections/created" ? JsonSerializer.Serialize(new { id = "created", creator_profile_id = "mine", name = "Draft", collection_type = Kind, source_url = "https://mdblist.com/lists/fixture/saved/", poster_url = "https://artwork.invalid/poster.jpg", sync_schedule = "0 0 * * *", sync_cadence = "daily", sort_config = new { field = "title", order = "asc", mode = "manual_pins" } }) : "{}";
            if (path == "/api/v2/collections/capabilities")
                json = CollectionDefaultSort is { } supported ? JsonSerializer.Serialize(new { collection_default_sort = supported }) : "{}";
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) }; response.Headers.ETag = new("\"draft-revision\""); return response;
        }
    }
}
