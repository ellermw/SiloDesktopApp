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
        vm.AddRuleCommand.Execute(null); vm.RuleDefinition.Groups[0].Rules[0].Value = "Drama";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal("created", vm.CollectionId); Assert.True(vm.IsEditing); Assert.NotNull(vm.ErrorMessage);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Null(vm.ErrorMessage); Assert.Equal(1, wire.CreateCount); Assert.Equal(2, wire.PatchCount);
        Assert.Equal("Keep this", wire.LastPatch!.Value.GetProperty("description").GetString());
    }

    [Fact]
    public async Task SmartCollectionWithoutACompleteRuleIsNeitherPreviewedNorSaved()
    {
        using var wire = new Wire(); var client = wire.Client(); using var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("mine");
        var vm = new CollectionEditorViewModel(new CollectionsApi(client), new CatalogApi(client), new SettingsApi(client), auth)
            { Name = "Draft", CollectionType = "smart" };
        Assert.True(vm.SmartNeedsRule);
        await vm.RefreshPreviewAsync();
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal("Add at least one rule.", vm.ErrorMessage); Assert.Equal(0, wire.CreateCount); Assert.False(vm.HasPreview);
        vm.AddRuleCommand.Execute(null);
        Assert.True(vm.SmartNeedsRule); // a blank value is not a rule
        foreach (var value in new object?[] { 0, false, "Drama" })
        {
            vm.RuleDefinition.Groups[0].Rules[0].Value = value;
            Assert.False(vm.SmartNeedsRule);
        }
        vm.RuleDefinition.Groups[0].Rules[0].Value = new object[] { 1990, "" };
        Assert.True(vm.SmartNeedsRule);
        vm.RuleDefinition.Groups[0].Rules[0].Value = "Drama";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Null(vm.ErrorMessage); Assert.Equal(1, wire.CreateCount);
        var manual = new CollectionEditorViewModel(new CollectionsApi(client), new CatalogApi(client), new SettingsApi(client), auth) { Name = "Manual", CollectionType = "manual" };
        Assert.False(manual.SmartNeedsRule);
    }

    [Fact]
    public async Task ConcurrentRenameKeepsRejectingSaveUntilTheEditorReloads()
    {
        using var wire = new Wire();
        var client = wire.Client(); using var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("mine");
        var vm = new CollectionEditorViewModel(new CollectionsApi(client), new CatalogApi(client), new SettingsApi(client), auth);
        await vm.LoadExistingCommand.ExecuteAsync("created");
        Assert.Null(vm.ErrorMessage);
        vm.Name = "Stale edit";
        wire.CurrentName = "Changed on another device"; wire.Revision = "\"concurrent-revision\"";
        for (var retry = 0; retry < 3; retry++)
        {
            await vm.SaveCommand.ExecuteAsync(null);
            Assert.NotNull(vm.ErrorMessage);
            Assert.Equal("Stale edit", vm.Name);
        }
        Assert.Equal(1, wire.PatchCount);   // only the first attempt reaches the server with the stale ETag
        await vm.LoadExistingCommand.ExecuteAsync("created");
        Assert.Null(vm.ErrorMessage); Assert.Equal("Changed on another device", vm.Name);
        vm.Name = "Saved after reload";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Null(vm.ErrorMessage); Assert.Equal(2, wire.PatchCount);
        Assert.Equal("Saved after reload", wire.LastPatch!.Value.GetProperty("name").GetString());
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public async Task SmartSortClearingRequiresCapabilityAndStoredDefaultSort(bool? supported)
    {
        foreach (var storedSort in new string?[] { null, "{}", "{\"mode\":\"manual_pins\"}",
            "{\"field\":\"title\",\"mode\":\"manual_pins\"}", "{\"order\":\"asc\",\"mode\":\"manual_pins\"}",
            "{\"field\":\"title\",\"order\":\"asc\",\"mode\":\"manual_pins\"}" })
        {
            using var wire = new Wire { CollectionDefaultSort = supported,
                SortConfig = storedSort == null ? null : JsonSerializer.Deserialize<Dictionary<string, object>>(storedSort) };
            var client = wire.Client(); using var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("mine");
            var vm = new CollectionEditorViewModel(new CollectionsApi(client), new CatalogApi(client), new SettingsApi(client), auth);
            await vm.LoadExistingCommand.ExecuteAsync("created");
            Assert.Null(vm.ErrorMessage);
            Assert.Equal(supported == true, vm.Capabilities!.CollectionDefaultSort);
            vm.Name = "Renamed"; vm.Description = "Updated description";
            vm.RuleDefinition.Sort = new() { Field = "year", Order = "desc" }; vm.ClearSmartDefaultSort();
            await vm.SaveCommand.ExecuteAsync(null);
            Assert.Null(vm.ErrorMessage); Assert.Equal(1, wire.PatchCount);
            var patch = wire.LastPatch!.Value;
            Assert.Equal("Renamed", patch.GetProperty("name").GetString());
            Assert.Equal("Updated description", patch.GetProperty("description").GetString());
            var querySort = patch.GetProperty("query_definition").GetProperty("sort");
            Assert.Equal("year", querySort.GetProperty("field").GetString());
            Assert.Equal("desc", querySort.GetProperty("order").GetString());
            var shouldClear = supported == true && wire.SortConfig != null &&
                (wire.SortConfig.ContainsKey("field") || wire.SortConfig.ContainsKey("order"));
            Assert.Equal(shouldClear, patch.TryGetProperty("sort_config", out var sort));
            if (shouldClear)
            {
                Assert.False(sort.TryGetProperty("field", out _)); Assert.False(sort.TryGetProperty("order", out _));
                Assert.Equal("manual_pins", sort.GetProperty("mode").GetString());
            }
        }
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
    [InlineData("mdblist")]
    [InlineData("tmdb")]
    [InlineData("tmdb_list")]
    public async Task SyncedCollectionRenamePreservesNumericStringLibraryIds(string kind)
    {
        using var wire = new Wire
        {
            Kind = kind,
            SourceConfig = new() { ["library_ids"] = new object[] { "3", 7, "3", "invalid", "0", -1, "2147483648", null! } },
        };
        var client = wire.Client(); using var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("mine");
        var vm = new CollectionEditorViewModel(new CollectionsApi(client), new CatalogApi(client), new SettingsApi(client), auth);
        await vm.LoadExistingCommand.ExecuteAsync("created");
        Assert.Null(vm.ErrorMessage); Assert.False(vm.IsReadOnly);
        Assert.Equal([3, 7], vm.SelectedLibraryIds);
        vm.Name = "Renamed";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Null(vm.ErrorMessage); Assert.Equal(1, wire.PatchCount);
        var patch = wire.LastPatch!.Value;
        Assert.Equal("Renamed", patch.GetProperty("name").GetString());
        // The v2 write contract uses string IDs, even when legacy source_config
        // mixes numeric and numeric-string values. Invalid IDs stay filtered out.
        Assert.Equal(["3", "7"], patch.GetProperty("library_ids").EnumerateArray().Select(value => value.GetString()));
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
        public string CurrentName = "Draft", Revision = "\"draft-revision\"";
        public Dictionary<string, object>? SourceConfig;
        public Dictionary<string, object>? SortConfig = new() { ["field"] = "title", ["order"] = "asc", ["mode"] = "manual_pins" };
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
                if (request.Headers.IfMatch.Single().Tag != Revision)
                    return new(HttpStatusCode.PreconditionFailed) { Content = new StringContent("{\"status\":412,\"detail\":\"Changed elsewhere\"}") };
                if (FailFirstPatch && PatchCount == 1) return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{\"message\":\"metadata unavailable\"}") };
            }
            var json = path == "/api/v2/collections" || path == "/api/v2/collections/created" ? JsonSerializer.Serialize(new { id = "created", creator_profile_id = "mine", name = CurrentName, collection_type = Kind, source_config = SourceConfig, source_url = Kind == "tmdb_list" ? "https://www.themoviedb.org/list/123" : "https://mdblist.com/lists/fixture/saved/", poster_url = "https://artwork.invalid/poster.jpg", sync_schedule = "0 0 * * *", sync_cadence = "daily", sort_config = SortConfig, query_definition = Kind == "smart" ? (object)new { match = "all", groups = new[] { new { match = "all", rules = new[] { new { field = "genre", op = "is", value = "Drama" } } } } } : null }) : "{}";
            if (path == "/api/v2/collections/capabilities")
                json = CollectionDefaultSort is { } supported ? JsonSerializer.Serialize(new { collection_default_sort = supported }) : "{}";
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) }; response.Headers.ETag = new(Revision); return response;
        }
    }
}
