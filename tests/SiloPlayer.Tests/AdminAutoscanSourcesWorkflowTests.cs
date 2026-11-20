using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Plugins;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminAutoscanSourcesWorkflowTests
{
    private const string Settings = """{"enabled":true,"default_poll_interval_seconds":600,"debounce_seconds":60}""";
    private const string Sources = """{"items":[{"id":"one","plugin_id":"p","capability_id":"c","enabled":true,"delivery_mode":"poll","source_config":{"untouched":"keep"}}],"page":{"has_more":false}}""";
    private static HttpResponseMessage Read(HttpRequestMessage request) => AdminMonitorKit.Json(
        request.RequestUri!.AbsolutePath.EndsWith("/settings") ? Settings :
        request.RequestUri.AbsolutePath.EndsWith("/sources") ? Sources : """{"items":[],"page":{"has_more":false}}""");

    [Fact]
    public async Task MutationAdmissionStaysClosedUntilItsReadbackSettles()
    {
        using var kit = AdminMonitorKit.Create();
        var readback = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var wrote = false;
        kit.Http.Respond = (r, _) =>
        {
            if (r.Method == HttpMethod.Put)
            { wrote = true; return Task.FromResult(AdminMonitorKit.Json("""{"settings":{"enabled":false,"default_poll_interval_seconds":600,"debounce_seconds":60},"reschedule_state":"applied"}""")); }
            if (wrote && r.RequestUri!.AbsolutePath.EndsWith("/sources")) { entered.TrySetResult(true); return readback.Task; }
            return Task.FromResult(Read(r));
        };
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminAutoscanController(new(kit.Client), authority);
        await controller.OpenAsync(); var owner = controller.State.Owner!;
        var write = controller.SetEnabledAsync(false, owner); await entered.Task;
        Assert.True(controller.State.Busy); Assert.True(controller.State.Loading);
        Assert.Null(await controller.DeleteAsync("one", owner));
        readback.SetResult(AdminMonitorKit.Json(Sources)); await write;
        Assert.False(controller.State.Busy);
        Assert.Single(kit.Http.Requests.Where(r => r.StartsWith("PUT") || r.StartsWith("DELETE")));
    }

    [Fact]
    public async Task FailedRefreshRetainsRowsButBlocksWritesUntilReadRecovery()
    {
        using var kit = AdminMonitorKit.Create();
        var fail = false;
        kit.Http.Respond = (r, _) => Task.FromResult(fail ? AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable) : Read(r));
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminAutoscanController(new(kit.Client), authority);
        await controller.OpenAsync(); var owner = controller.State.Owner!;
        fail = true; await controller.ReloadAsync();
        Assert.True(controller.State.Loaded);
        Assert.Equal("one", Assert.Single(controller.State.Sources).Id);
        Assert.True(controller.State.Stale);
        var before = kit.Http.Requests.Count;
        await controller.DeleteAsync("one", owner);
        Assert.Equal(before, kit.Http.Requests.Count);
        fail = false; await controller.ReloadAsync(); Assert.False(controller.State.Stale);
    }

    [Fact]
    public async Task CapturedOldVisitCannotDeleteUnderNewVisitEvenWithSameIdentity()
    {
        using var kit = AdminMonitorKit.Create(); kit.Http.Respond = (r, _) => Task.FromResult(Read(r));
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminAutoscanController(new(kit.Client), authority);
        await controller.OpenAsync(); var old = controller.State.Owner!;
        controller.Close(); await controller.OpenAsync(); var before = kit.Http.Requests.Count;
        await controller.DeleteAsync("one", old);
        Assert.Equal(before, kit.Http.Requests.Count);
    }

    [Fact]
    public async Task TriggerValidatesTaskReceiptAndNeverClaimsScanCompletion()
    {
        using var kit = AdminMonitorKit.Create();
        kit.Http.Respond = (r, _) => Task.FromResult(r.Method == HttpMethod.Post
            ? AdminMonitorKit.Json("""{"key":"autoscan_poll","state":"running","execution_scope":"process"}""") : Read(r));
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminAutoscanController(new(kit.Client), authority);
        await controller.OpenAsync(); await controller.TriggerAsync(controller.State.Owner!);
        Assert.Contains("process", controller.State.Notice!);
        Assert.DoesNotContain("completed", controller.State.Notice!, StringComparison.OrdinalIgnoreCase);
        Assert.Single(kit.Http.Requests.Where(r => r.StartsWith("POST")));
    }

    [Fact]
    public async Task AmbiguousCreateIsNotReplayedAndReadbackDoesNotInventAReceipt()
    {
        using var kit = AdminMonitorKit.Create();
        kit.Http.Respond = (r, _) => r.Method == HttpMethod.Post ? throw new HttpRequestException("lost") : Task.FromResult(Read(r));
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminAutoscanController(new(kit.Client), authority);
        await controller.OpenAsync();
        var created = await controller.CreateSourceAsync("p", "c", new() { DeliveryMode = "poll" }, controller.State.Owner!);
        Assert.Null(created); Assert.Single(kit.Http.Requests.Where(r => r.StartsWith("POST")));
        Assert.Contains("could not be confirmed", controller.State.Error!);
    }

    [Fact]
    public async Task WebhookIsWithheldDuringRotationAndFailedReconciliation()
    {
        using var kit = AdminMonitorKit.Create();
        var rotating = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failedRead = false;
        kit.Http.Respond = (r, _) => r.Method == HttpMethod.Post ? rotating.Task : Task.FromResult(
            failedRead ? AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable) :
            r.RequestUri!.AbsolutePath.EndsWith("/sources") ? AdminMonitorKit.Json("""{"items":[{"id":"one","delivery_mode":"webhook","webhook_configured":true,"webhook_url":"/synthetic-secret"}],"page":{"has_more":false}}""") : Read(r));
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminAutoscanController(new(kit.Client), authority);
        await controller.OpenAsync(); var owner = controller.State.Owner!;
        var write = controller.ConfigureWebhookAsync("one", true, owner);
        Assert.Null(controller.SafeWebhookUrl("one", owner));
        failedRead = true; rotating.SetResult(AdminMonitorKit.Json("{}", HttpStatusCode.InternalServerError)); await write;
        Assert.Null(controller.SafeWebhookUrl("one", owner));
        failedRead = false; await controller.ReloadAsync();
        Assert.NotNull(controller.SafeWebhookUrl("one", owner));
    }

    [Fact]
    public void TypedConfigRoundTripsAndUntouchedEditorPreservesUnknownFields()
    {
        var d = new AdminAutoscanDescriptor { ConfigForm = new() { Fields = [
            new() { Key = "switch", Control = "SWITCH", DefaultValue = true },
            new() { Key = "many", Control = "MULTI_SELECT" },
            new() { Key = "hidden", ShowWhen = [new() { Field = "switch", Equals = ["true"] }] }
        ] } };
        var source = new AdminAutoscanSource { DeliveryMode = "poll", SourceConfig = new() { ["switch"] = "false", ["many"] = "a,b", ["unknown"] = "retained" } };
        var draft = new AdminAutoscanEditorDraft(source, d);
        Assert.Equal(false, draft.Config["switch"]); Assert.Equal(new[] { "a", "b" }, draft.Config["many"]);
        Assert.Equal("retained", draft.Build(source).SourceConfig["unknown"]);
        draft.ConfigDirty = true; draft.Config["hidden"] = "discard";
        var config = draft.Build(source).SourceConfig;
        Assert.Equal("false", config["switch"]); Assert.Equal("a,b", config["many"]); Assert.False(config.ContainsKey("hidden"));
    }

    [Fact]
    public void LegacyConfigMigrationIsScopedAndMergesLines()
    {
        var source = new AdminAutoscanSource { PluginId = "silo.autoscan.cephfs", CapabilityId = "cephfs", SourceConfig = new() { ["movie_nested_paths"] = "/one\n/two", ["movie_flat_paths"] = "/one" } };
        var draft = new AdminAutoscanEditorDraft(source, new());
        Assert.Equal("/one\n/two", draft.Build(source).SourceConfig["movie_flat_paths"]);
        Assert.False(draft.Build(source).SourceConfig.ContainsKey("movie_nested_paths"));
        source.PluginId = "other"; Assert.True(new AdminAutoscanEditorDraft(source, new()).Build(source).SourceConfig.ContainsKey("movie_nested_paths"));
    }

    [Fact]
    public void WebhookMappingsSeedByProviderAndSplitAtPathBoundaries()
    {
        var libraries = new[] { new AdminLibrary { Id = "1", Enabled = true, Type = "movies", Paths = ["/mnt/media/movies/a", "/mnt/media/movies/b"] },
            new AdminLibrary { Id = "2", Enabled = true, Type = "series", Paths = ["/mnt/media/tv"] } };
        var rows = AdminAutoscanPresentation.SeedMappings("radarr", libraries);
        Assert.Equal("/mnt/media/movies", Assert.Single(rows).To); Assert.Equal("", rows[0].From);
        Assert.Equal(["/mnt/media/movies/a", "/mnt/media/movies/b"], AdminAutoscanPresentation.ExpandedRoots(rows[0].To, libraries.SelectMany(l => l.Paths)));
        var source = new AdminAutoscanSource { PathRewrites = [new() { From = "/remote", To = "/mnt/media/tv2" }] };
        Assert.Empty(AdminAutoscanPresentation.Targets(source, new(), libraries).Libraries);
    }
}
