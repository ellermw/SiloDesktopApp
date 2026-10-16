using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class TraktRetirementBehaviorTests
{
    [Fact]
    public async Task ServerConvertedManualCollectionKeepsTitleAndMembersWithoutSync()
    {
        using var wire = new Wire();
        var client = wire.Client();
        using var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("mine");
        var vm = new CollectionEditorViewModel(new CollectionsApi(client), new CatalogApi(client), new SettingsApi(client), auth);
        await vm.LoadExistingCommand.ExecuteAsync("converted");
        Assert.Equal("My imported favorites", vm.Name);
        Assert.False(vm.IsImportedCollection);
        Assert.Equal("member", Assert.Single(vm.ManualItems).MediaItemId);
        vm.Name = "My renamed favorites";
        await vm.SaveCommand.ExecuteAsync(null);
        await vm.SyncNowCommand.ExecuteAsync(null);
        Assert.Null(vm.ErrorMessage);
        Assert.Equal("My renamed favorites", wire.Patch!.Value.GetProperty("name").GetString());
        Assert.DoesNotContain(wire.Paths, path => path.EndsWith("/sync"));
        Assert.DoesNotContain(wire.Methods, method => method == HttpMethod.Delete);
    }

    [Fact]
    public async Task HomeImportRejectedByServerDoesNotRetryOrReadView()
    {
        using var wire = new Wire { RejectSave = true }; var client = wire.Client();
        var settings = new SettingsApi(client);
        var transfer = new HomeLayoutTransferService(settings, new CatalogApi(client), new CollectionsApi(client), new AuthApi(client));
        var file = new HomeLayoutFile { ServerId = "same", Pages = [new() { Overrides = [new() { SectionId = "admin", Hidden = false }] }] };
        var plan = await transfer.PreviewAsync(HomeLayoutTransfer.Serialize(file));
        await Assert.ThrowsAsync<ApiException>(() => transfer.ApplyAsync(plan));
        Assert.Equal(1, wire.SaveCount);
        Assert.DoesNotContain(wire.Paths, path => path.Contains("/sections/settings"));
        Assert.Equal(0, transfer.AppliedPages);
    }

    [Fact]
    public async Task SameServerImportReplacesLegacyHideAndKeepsSavedAdminIdentity()
    {
        using var wire = new Wire(); var client = wire.Client();
        var transfer = Transfer(client);
        var file = new HomeLayoutFile { ServerId = "same", Pages = [new() { Overrides = [new() { SectionId = "admin", Hidden = false, Title = "Imported" }] }] };
        await transfer.ApplyAsync(await transfer.PreviewAsync(HomeLayoutTransfer.Serialize(file)));
        var saved = Assert.Single(wire.SaveBody!.Value.GetProperty("overrides").EnumerateArray());
        Assert.Equal("existing", saved.GetProperty("id").GetString());
        Assert.False(saved.GetProperty("hidden").GetBoolean());
        Assert.Equal("Imported", saved.GetProperty("title").GetString());
        Assert.Equal(1, transfer.AppliedPages);
    }

    [Fact]
    public async Task ProfileSwitchAfterPreviewRejectsImportBeforeAnyWrite()
    {
        using var wire = new Wire(); var client = wire.Client(); client.SetProfile("first");
        var transfer = Transfer(client);
        var file = new HomeLayoutFile { ServerId = "same", Pages = [new() { Overrides = [new() { SectionId = "admin" }] }] };
        var plan = await transfer.PreviewAsync(HomeLayoutTransfer.Serialize(file));
        client.SetProfile("replacement");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transfer.ApplyAsync(plan));
        Assert.Equal(0, wire.SaveCount);
    }

    [Fact]
    public async Task ProfileSwitchWhileReadingExistingOverridesCannotWriteReplacementProfile()
    {
        using var wire = new Wire { PauseRead = true }; var client = wire.Client(); client.SetProfile("first");
        var transfer = Transfer(client);
        var file = new HomeLayoutFile { ServerId = "same", Pages = [new() { Overrides = [new() { SectionId = "admin" }] }] };
        var plan = await transfer.PreviewAsync(HomeLayoutTransfer.Serialize(file));
        var applying = transfer.ApplyAsync(plan); await wire.ReadStarted.Task;
        client.SetProfile("replacement"); wire.ReleaseRead.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => applying);
        Assert.Equal(0, wire.SaveCount);
    }

    private static HomeLayoutTransferService Transfer(SiloApiClient client)
        => new(new SettingsApi(client), new CatalogApi(client), new CollectionsApi(client), new AuthApi(client));

    private sealed class Wire : HttpMessageHandler
    {
        public bool RejectSave;
        public bool PauseRead;
        public TaskCompletionSource ReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int SaveCount;
        public JsonElement? Patch;
        public JsonElement? SaveBody;
        public List<string> Paths = [];
        public List<HttpMethod> Methods = [];
        public SiloApiClient Client() { var client = new SiloApiClient(new HttpClient(this)); client.SetBaseUrl("https://retirement.invalid"); return client; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath; Paths.Add(path); Methods.Add(request.Method);
            if (request.Method == HttpMethod.Patch) Patch = JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(ct));
            if (PauseRead && request.Method == HttpMethod.Get && path == "/api/v2/profile/sections")
            { ReadStarted.SetResult(); await ReleaseRead.Task.WaitAsync(ct); }
            if (request.Method == HttpMethod.Put && path.Contains("sections"))
            {
                SaveCount++;
                SaveBody = JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(ct));
                if (RejectSave) return new(HttpStatusCode.UnprocessableEntity) { Content = new StringContent("{\"message\":\"not allowed\"}") };
            }
            var json = path switch
            {
                "/api/v2/collections/converted" => "{\"id\":\"converted\",\"name\":\"My imported favorites\",\"collection_type\":\"manual\",\"creator_profile_id\":\"mine\",\"source_config\":{},\"source_url\":\"\",\"item_count\":1}",
                "/api/v2/collections/converted/items" => "{\"items\":[{\"media_item_id\":\"member\",\"position\":0}],\"total\":1}",
                "/api/v2/system/identity" => "{\"server_id\":\"same\"}",
                _ when path.Contains("sections") && !path.Contains("settings") => "{\"items\":[{\"id\":\"existing\",\"section_id\":\"admin\",\"hidden\":true}]}",
                _ => "{}"
            };
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
            response.Headers.ETag = new("\"fixture\""); return response;
        }
    }
}
