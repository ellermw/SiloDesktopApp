using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public class HomeLayoutMergeTests
{
    [Fact]
    public void Cross_server_import_keeps_target_admin_overrides_and_unrecreatable_custom_rows()
    {
        var rows = new List<RawSectionOverride> { new() { Id = "admin-id", SectionId = "target-admin", Hidden = true },
            new() { Id = "legacy-id", UserConfig = "{\"source\":\"trakt\"}" }, new() { Id = "replaceable-custom" } };
        var merged = HomeLayoutMerge.Build(new() { Overrides = [new() { Id = "imported" }] }, rows, false, new HashSet<string>());
        Assert.Equal(new[] { "admin-id", "legacy-id", "imported" }, merged.Select(x => x.Id));
        Assert.True(merged[0].Hidden);
    }
    [Fact]
    public void Same_server_import_reuses_last_admin_id_and_keeps_saved_legacy_hide_unchanged()
    {
        var rows = new List<RawSectionOverride> { new() { Id = "old", SectionId = "a" }, new() { Id = "last", SectionId = "a" },
            new() { Id = "legacy-id", SectionId = "legacy", Hidden = true } };
        var merged = HomeLayoutMerge.Build(new() { Overrides = [new() { SectionId = "a" }, new() { SectionId = "a" },
            new() { SectionId = "legacy", Hidden = false }] }, rows, true, new HashSet<string> { "legacy" });
        Assert.Equal(new[] { "last", "legacy-id" }, merged.Select(x => x.Id)); Assert.True(merged[1].Hidden);
    }
    [Fact]
    public async Task Section_writes_are_ordered_and_nested_import_transaction_does_not_deadlock()
    {
        using var wire = new Wire(); var client = new SiloApiClient(new HttpClient(wire)); client.SetBaseUrl("https://fixture.invalid");
        var api = new SettingsApi(client);
        var first = api.UpdateProfileSectionsAsync(new() { Scope = "home" }); await wire.Started.Task;
        var second = api.RunSectionMutationAsync(() => api.ResetProfileSectionsAsync());
        Assert.Equal(1, wire.Requests); wire.Release.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5)); Assert.Equal(2, wire.Requests);
    }
    private sealed class Wire : HttpMessageHandler
    {
        public int Requests; public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { if (++Requests == 1) { Started.SetResult(); await Release.Task.WaitAsync(ct); } return new(HttpStatusCode.NoContent); }
    }
}
