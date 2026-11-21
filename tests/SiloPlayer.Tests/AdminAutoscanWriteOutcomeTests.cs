using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Definite refusal versus unknown outcome for every Autoscan write.</summary>
public sealed class AdminAutoscanWriteOutcomeTests
{
    private const string Settings = """{"enabled":true,"default_poll_interval_seconds":600,"debounce_seconds":60}""";
    private const string Sources = """{"items":[{"id":"one","plugin_id":"p","capability_id":"c","enabled":true,"delivery_mode":"poll","source_config":{}}],"page":{"has_more":false}}""";
    private static HttpResponseMessage Read(HttpRequestMessage request) => AdminMonitorKit.Json(
        request.RequestUri!.AbsolutePath.EndsWith("/settings") ? Settings :
        request.RequestUri.AbsolutePath.EndsWith("/sources") ? Sources : """{"items":[],"page":{"has_more":false}}""");

    private static HttpResponseMessage Problem(HttpStatusCode code, string type, string detail) =>
        AdminMonitorKit.Json(JsonSerializer.Serialize(new { type = "https://silo.test/docs/api/v2/problems/" + type, title = "Problem", status = (int)code, detail }), code);

    [Fact]
    public async Task RefusedWriteShowsTheRedactedBoundedReasonAndKeepsWritesOpenWithoutAReread()
    {
        using var kit = AdminMonitorKit.Create();
        const string secret = "/hooks/synthetic-webhook-token-123";
        var refuse = true;
        kit.Http.Respond = (r, _) => Task.FromResult(r.Method == HttpMethod.Post && refuse
            ? Problem(HttpStatusCode.UnprocessableEntity, "validation_failed", new string('x', 290) + secret + " rejected")
            : r.Method == HttpMethod.Post ? AdminMonitorKit.Json("""{"id":"two","plugin_id":"p","capability_id":"c"}""", HttpStatusCode.Created)
            : r.RequestUri!.AbsolutePath.EndsWith("/sources")
                ? AdminMonitorKit.Json("""{"items":[{"id":"one","delivery_mode":"webhook","webhook_configured":true,"webhook_url":"/hooks/synthetic-webhook-token-123"}],"page":{"has_more":false}}""")
                : Read(r));
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminAutoscanController(new(kit.Client), authority);
        await controller.OpenAsync(); var owner = controller.State.Owner!; var reads = kit.Http.Requests.Count;
        var refused = await controller.CreateSourceAsync("p", "c", new() { DeliveryMode = "poll" }, owner);
        Assert.Equal(AdminAutoscanOutcome.Refused, refused.Outcome);
        Assert.DoesNotContain("synthetic-webhook-token", refused.Message); Assert.True(refused.Message!.Length <= AdminAutoscanFailures.MessageLimit + 1);
        Assert.Equal(refused.Message, controller.State.Error);
        // A definite refusal changed nothing: no re-read, no unknown outcome, and the admin can correct and submit again.
        Assert.False(controller.State.UnknownOutcome); Assert.False(controller.State.Busy);
        Assert.Equal(reads + 1, kit.Http.Requests.Count);
        refuse = false;
        var created = await controller.CreateSourceAsync("p", "c", new() { DeliveryMode = "poll" }, owner);
        Assert.Equal(AdminAutoscanOutcome.Succeeded, created.Outcome); Assert.Equal("two", created.Value!.Id);
        Assert.Equal(2, kit.Http.Requests.Count(r => r.StartsWith("POST")));
    }

    [Fact]
    public async Task FieldErrorDetailIsShownWhenTheProblemOnlySaysSeeErrors()
    {
        using var kit = AdminMonitorKit.Create();
        kit.Http.Respond = (r, _) => Task.FromResult(r.Method == HttpMethod.Put
            ? AdminMonitorKit.Json("""{"type":"https://silo.test/docs/api/v2/problems/validation_failed","title":"Validation","detail":"See errors","errors":[{"location":"body.label","code":"too_long","detail":"Label is too long."}]}""", HttpStatusCode.UnprocessableEntity)
            : Read(r));
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminAutoscanController(new(kit.Client), authority);
        await controller.OpenAsync();
        var result = await controller.UpdateSourceAsync("one", AdminAutoscanSourceDraft.FromSource(controller.State.Sources[0], true), controller.State.Owner!);
        Assert.Equal(AdminAutoscanOutcome.Refused, result.Outcome); Assert.Contains("Label is too long", result.Message);
    }

    [Fact]
    public async Task UnknownOutcomeBlocksFurtherWritesUntilTheCanonicalStateIsReadAgain()
    {
        using var kit = AdminMonitorKit.Create(); var readsFail = false;
        kit.Http.Respond = (r, _) => r.Method == HttpMethod.Post ? throw new HttpRequestException("lost")
            : Task.FromResult(readsFail ? AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable) : Read(r));
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminAutoscanController(new(kit.Client), authority);
        await controller.OpenAsync(); var owner = controller.State.Owner!;
        readsFail = true;
        Assert.Equal(AdminAutoscanOutcome.Unknown, (await controller.CreateSourceAsync("p", "c", new() { DeliveryMode = "poll" }, owner)).Outcome);
        Assert.True(controller.State.UnknownOutcome); Assert.True(controller.State.Stale);
        Assert.Equal(AdminAutoscanOutcome.Skipped, (await controller.CreateSourceAsync("p", "c", new() { DeliveryMode = "poll" }, owner)).Outcome);
        Assert.Equal(1, kit.Http.Requests.Count(r => r.StartsWith("POST")));
        readsFail = false; await controller.ReloadAsync();
        Assert.False(controller.State.UnknownOutcome); Assert.False(controller.State.Stale);
    }

    [Fact]
    public async Task CreateAnsweredWithoutAUsableIdIsUnknownNotNothingSent()
    {
        using var kit = AdminMonitorKit.Create();
        kit.Http.Respond = (r, _) => Task.FromResult(r.Method == HttpMethod.Post ? AdminMonitorKit.Json("""{"plugin_id":"p","capability_id":"c"}""", HttpStatusCode.Created) : Read(r));
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminAutoscanController(new(kit.Client), authority);
        await controller.OpenAsync();
        var result = await controller.CreateSourceAsync("p", "c", new() { DeliveryMode = "poll" }, controller.State.Owner!);
        Assert.Equal(AdminAutoscanOutcome.Unknown, result.Outcome); Assert.Contains("could not be confirmed", result.Message);
        // The re-read that clears it covers the whole unfiltered list.
        Assert.Equal(2, kit.Http.Requests.Count(r => r == "GET /api/v2/admin/autoscan/sources?limit=100"));
    }

    [Fact]
    public async Task ConnectionCreateAnsweredWithoutAnIdIsUnknown()
    {
        using var kit = AdminMonitorKit.Create();
        kit.Http.Respond = (r, _) => Task.FromResult(r.Method == HttpMethod.Post ? AdminMonitorKit.Json("""{"name":"n","kind":"sonarr"}""", HttpStatusCode.Created) : Read(r));
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminAutoscanController(new(kit.Client), authority);
        await controller.OpenAsync();
        var result = await controller.CreateConnectionAsync(new() { Name = "n", Kind = "sonarr", BaseUrl = "http://fixture.invalid", ApiKeyRef = "synthetic-key" }, controller.State.Owner!);
        Assert.Equal(AdminAutoscanOutcome.Unknown, result.Outcome);
        Assert.Equal(1, kit.Http.Requests.Count(r => r.StartsWith("POST")));
    }

    [Fact]
    public async Task TriggerAnsweredWithConflictMeansAPollIsAlreadyRunning()
    {
        using var kit = AdminMonitorKit.Create();
        kit.Http.Respond = (r, _) => Task.FromResult(r.Method == HttpMethod.Post ? Problem(HttpStatusCode.Conflict, "conflict", "task running") : Read(r));
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminAutoscanController(new(kit.Client), authority);
        await controller.OpenAsync();
        var result = await controller.TriggerAsync(controller.State.Owner!);
        Assert.Equal(AdminAutoscanOutcome.Refused, result.Outcome); Assert.Contains("already running", result.Message);
        Assert.False(controller.State.UnknownOutcome); Assert.Single(kit.Http.Requests.Where(r => r.StartsWith("POST")));
    }

    [Fact]
    public async Task LocalRefusalsSendNothing()
    {
        using var kit = AdminMonitorKit.Create(); kit.Http.Respond = (r, _) => Task.FromResult(Read(r));
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminAutoscanController(new(kit.Client), authority);
        await controller.OpenAsync(); var owner = controller.State.Owner!;
        var invalid = await controller.CreateSourceAsync("p", "c", new() { DeliveryMode = "poll", PollIntervalSeconds = 0 }, owner);
        Assert.Equal(AdminAutoscanOutcome.Refused, invalid.Outcome);
        var baseline = new AdminAutoscanSource { Id = "one", PluginId = "p", CapabilityId = "c", DeliveryMode = "poll", Label = "stale" };
        var changed = await controller.UpdateSourceAsync("one", new() { DeliveryMode = "poll" }, owner, baseline);
        Assert.Equal(AdminAutoscanOutcome.Refused, changed.Outcome); Assert.Contains("changed", changed.Message);
        Assert.DoesNotContain(kit.Http.Requests, r => r.StartsWith("POST") || r.StartsWith("PUT"));
        Assert.False(controller.State.UnknownOutcome);
    }

    [Fact]
    public async Task DeleteRefusalAndTimeoutAreDistinguished()
    {
        using var kit = AdminMonitorKit.Create(); var mode = "refuse";
        kit.Http.Respond = (r, _) => r.Method == HttpMethod.Delete
            ? mode == "refuse" ? Task.FromResult(Problem(HttpStatusCode.NotFound, "not_found", "Autoscan source not found.")) : throw new TaskCanceledException("late", new TimeoutException())
            : Task.FromResult(Read(r));
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminAutoscanController(new(kit.Client), authority);
        await controller.OpenAsync(); var owner = controller.State.Owner!;
        var refused = await controller.DeleteAsync("one", owner);
        Assert.Equal(AdminAutoscanOutcome.Refused, refused.Outcome); Assert.False(controller.State.UnknownOutcome);
        mode = "timeout";
        var unknown = await controller.DeleteAsync("one", owner);
        Assert.Equal(AdminAutoscanOutcome.Unknown, unknown.Outcome); Assert.Contains("could not be confirmed", unknown.Message);
        Assert.Equal(2, kit.Http.Requests.Count(r => r.StartsWith("DELETE")));
    }

    [Fact]
    public void RefusalTextIsRedactedBeforeItIsTruncated()
    {
        const string secret = "synthetic-credential-value-000";
        var text = AdminAutoscanFailures.Redact(new string('y', AdminAutoscanFailures.MessageLimit - 8) + secret + " tail", [secret]);
        Assert.DoesNotContain("synthetic", text); Assert.DoesNotContain("credential", text);
        Assert.True(text.Length <= AdminAutoscanFailures.MessageLimit + 1);
    }

    [Fact]
    public async Task ConnectionTestFailureShowsTheServersOwnTextWithoutTheDraftKey()
    {
        using var kit = AdminMonitorKit.Create();
        kit.Http.Respond = (r, _) => Task.FromResult(r.RequestUri!.AbsolutePath.EndsWith("/connections/test")
            ? Problem(HttpStatusCode.BadRequest, "validation_failed", "Probe rejected synthetic-draft-key") : Read(r));
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminAutoscanController(new(kit.Client), authority);
        await controller.OpenAsync();
        var result = await controller.TestConnectionAsync(new() { BaseUrl = "http://fixture.invalid", ApiKeyRef = "synthetic-draft-key" }, controller.State.Owner!);
        Assert.False(result!.Ok); Assert.DoesNotContain("synthetic-draft-key", result.Error);
    }

    [Fact]
    public async Task CreateBodyIsSentAsJsonWithTheOfficialFieldsAndNestedRewrites()
    {
        using var kit = AdminMonitorKit.Create();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"id":"new","plugin_id":"p","capability_id":"c","delivery_mode":"webhook"}""", HttpStatusCode.Created));
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        await new AdminAutoscanApi(kit.Client).CreateSourceAsync(lease, "p", "c", new()
        { ConnectionId = null, Enabled = true, DeliveryMode = "webhook", PollIntervalSeconds = null, Label = "label", SourceConfig = new() { ["webhook_provider"] = "sonarr" }, PathRewrites = [new() { From = "/arr", To = "/silo" }] });
        using var body = JsonDocument.Parse(Assert.Single(kit.Http.Bodies));
        var root = body.RootElement;
        Assert.Equal(new[] { "capability_id", "connection_id", "delivery_mode", "enabled", "label", "path_rewrites", "plugin_id", "poll_interval_seconds", "source_config" }, root.EnumerateObject().Select(p => p.Name).OrderBy(s => s).ToArray());
        Assert.Equal("p", root.GetProperty("plugin_id").GetString()); Assert.True(root.GetProperty("enabled").GetBoolean());
        var rewrite = root.GetProperty("path_rewrites")[0];
        Assert.Equal("/arr", rewrite.GetProperty("from").GetString()); Assert.Equal("/silo", rewrite.GetProperty("to").GetString());
        Assert.Equal("sonarr", root.GetProperty("source_config").GetProperty("webhook_provider").GetString());
    }

    [Fact]
    public void FeedsColumnAndEditSummaryFollowTheOfficialWording()
    {
        var libraries = new[] { new AdminLibrary { Id = "1", Name = "Movies", Enabled = true, Type = "movies", Paths = ["/media/movies"] } };
        var matched = AdminAutoscanPresentation.Targets(new() { DeliveryMode = "poll", PathRewrites = [new() { From = "/r", To = "/media/movies/x" }] }, new(), libraries);
        Assert.Equal("Movies", AdminAutoscanPresentation.TargetsText(matched)); Assert.Equal("Movies", AdminAutoscanPresentation.DescribeTargets(matched));
        var unmatched = AdminAutoscanPresentation.Targets(new() { DeliveryMode = "poll", PathRewrites = [new() { From = "/r", To = "/elsewhere" }] }, new(), libraries);
        Assert.Equal("None", AdminAutoscanPresentation.TargetsText(unmatched)); Assert.Equal("No matching library", AdminAutoscanPresentation.DescribeTargets(unmatched));
        var webhook = AdminAutoscanPresentation.Targets(new() { DeliveryMode = "webhook" }, new(), libraries);
        Assert.Equal("Determined at scan time", AdminAutoscanPresentation.TargetsText(webhook));
        // A local watcher with a path field but no paths can never match: the row says None, the edit summary says why.
        var watcher = AdminAutoscanPresentation.Targets(new() { DeliveryMode = "poll" }, new() { Connection = "none", DeliveryModes = ["poll"], ConfigForm = new() { Fields = [new() { Key = "roots" }] } }, libraries);
        Assert.True(watcher.Unresolvable); Assert.Equal("None", AdminAutoscanPresentation.TargetsText(watcher)); Assert.Equal("No paths configured", AdminAutoscanPresentation.DescribeTargets(watcher));
    }
}
