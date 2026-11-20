using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using R = SiloPlayer.Core.Services.Admin.AdminLibrarySettingsRules;

namespace SiloPlayer.Tests;

public sealed class AdminLibrarySettingsControllerTests
{
    private const string EffectiveJson = """
        {"catalog.scope_versions_to_library":"false","metadata.fallback_language":"en","scanner.realtime_monitoring":"false",
         "scanner.workers":"4","matcher.workers":"8","metadata.image_workers":"0","metadata.person_refresh_per_minute":"120",
         "matcher.batch_size":"500","access.unrated_content":"hide","catalog.extra_rating_sources":"rt_critic,letterboxd",
         "subtitles.auto_sync":"true","subtitles.sync_execution":"transcode_nodes_only","subtitles.sync_node_capacity":"2",
         "branding.server_name":"Unowned"}
        """;

    private sealed class Harness : IDisposable
    {
        internal readonly AdminMonitorKit Kit = AdminMonitorKit.Create();
        internal readonly AdminMonitorAuthority Authority;
        internal readonly AdminServerSettingsApi SettingsApi;
        internal readonly AdminLibrarySettingsApi LibraryApi;
        internal readonly AdminMonitorApi MonitorApi;
        internal readonly AdminServerSettingsStore Store;
        internal readonly AdminLibrarySettingsController Controller;
        internal string Effective = EffectiveJson;
        internal string Capability = """{"revision":"r1","state":"available","allowed":true,"plugin_declared_sources":true}""";
        internal string Sources = """{"items":[{"source":"imdb","label":"IMDb","name":"IMDb","always_shown":true},{"source":"rt_critic","label":"Rotten Tomatoes critics","name":"RT","always_shown":false,"provider":"MDBList"}]}""";
        internal string Nodes = """{"items":[{"id":"1","name":"node","type":"transcode","enabled":true,"healthy":true}],"page":{"has_more":false}}""";
        internal HttpStatusCode CapabilityStatus = HttpStatusCode.OK, SourcesStatus = HttpStatusCode.OK, NodesStatus = HttpStatusCode.OK;
        internal Func<HttpRequestMessage, Task<HttpResponseMessage>>? Write;

        internal Harness()
        {
            Authority = new AdminMonitorAuthority(Kit.Client, Kit.Auth);
            SettingsApi = new AdminServerSettingsApi(Kit.Client);
            LibraryApi = new AdminLibrarySettingsApi(Kit.Client);
            MonitorApi = new AdminMonitorApi(Kit.Client);
            Store = new AdminServerSettingsStore(SettingsApi, Authority, AdminLibrarySettingsController.Keys, []);
            Controller = new AdminLibrarySettingsController(SettingsApi, LibraryApi, MonitorApi, Store, Authority);
            Kit.Http.Respond = (request, _) =>
            {
                if (request.Method != HttpMethod.Get)
                    return Write?.Invoke(request) ?? Task.FromResult(AdminMonitorKit.Json("{}", HttpStatusCode.InternalServerError));
                var path = request.RequestUri!.AbsolutePath;
                HttpResponseMessage response = path switch
                {
                    "/api/v2/admin/settings/effective" => AdminMonitorKit.Json(Effective),
                    "/api/v2/admin/settings/sensitive-status" => AdminMonitorKit.Json("""{"configured":[],"managed_by_env":[]}"""),
                    "/api/v2/admin/settings/restart-keys" => AdminMonitorKit.Json("""{"keys":[],"prefixes":[]}"""),
                    "/api/v2/admin/rating-sources/capabilities" => AdminMonitorKit.Json(Capability, CapabilityStatus),
                    "/api/v2/admin/rating-sources" => AdminMonitorKit.Json(Sources, SourcesStatus),
                    "/api/v2/admin/nodes" => AdminMonitorKit.Json(Nodes, NodesStatus),
                    _ => AdminMonitorKit.Json("{}", HttpStatusCode.NotFound),
                };
                response.Headers.ETag = new("\"one\"");
                return Task.FromResult(response);
            };
        }

        internal string[] Writes => Kit.Http.Requests.Where(request => request.StartsWith("PUT ", StringComparison.Ordinal)).ToArray();
        public void Dispose() { Controller.Dispose(); Store.Dispose(); Authority.Dispose(); Kit.Dispose(); }
    }

    private static HttpResponseMessage Saved(string values, string etag = "\"two\"")
    {
        var response = AdminMonitorKit.Json($$"""{"values":{{values}},"restart_required":false}""");
        response.Headers.ETag = new(etag);
        return response;
    }

    [Fact]
    public async Task LoadProjectsOnlyOwnedSettingsAndLoadsConditionalFeeds()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        var state = h.Controller.State;
        Assert.True(state.Loaded);
        Assert.Equal(R.Keys.Order(), state.Values.Keys.Order());
        Assert.DoesNotContain("branding.server_name", state.Values.Keys);
        Assert.True(state.RatingSourcesSupported);
        Assert.True(state.RatingSourcesLoaded);
        Assert.Equal("rt_critic", Assert.Single(state.RatingSources.Where(source => !source.AlwaysShown)).Source);
        Assert.True(state.NodesKnown);
        Assert.True(state.TranscodeNodeAvailable);
        Assert.Empty(state.DirtyKeys);
    }

    [Fact]
    public async Task OlderServerHidesRatingsAndTransientNodeFailureLeavesNodeModesReachable()
    {
        using var h = new Harness { CapabilityStatus = HttpStatusCode.NotFound, NodesStatus = HttpStatusCode.ServiceUnavailable };
        await h.Controller.OpenAsync();
        Assert.False(h.Controller.State.RatingSourcesSupported);
        Assert.False(h.Controller.State.RatingSourcesLoaded);
        Assert.DoesNotContain(h.Kit.Http.Requests, request => request == "GET /api/v2/admin/rating-sources");
        Assert.False(h.Controller.State.NodesKnown);
        Assert.False(h.Controller.State.TranscodeNodeAvailable);
        Assert.True(h.Controller.State.Loaded);
    }

    [Fact]
    public async Task RatingListFailureKeepsTheSupportedGroupWithItsOwnRecoveryState()
    {
        using var h = new Harness { SourcesStatus = HttpStatusCode.ServiceUnavailable };
        await h.Controller.OpenAsync();
        Assert.True(h.Controller.State.RatingSourcesSupported);
        Assert.True(h.Controller.State.RatingSourcesError);
        Assert.Empty(h.Controller.State.RatingSources);
        Assert.True(h.Controller.State.Loaded);
    }

    [Fact]
    public async Task RestoreDefaultsAndRatingToggleStageOneGuardedSettingsWrite()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        h.Controller.RestoreWorkerDefaults();
        h.Controller.SetRatingSource("rt_critic", false);
        h.Controller.SetRatingSource("kinopoisk", true);
        Assert.Equal([R.ExtraRatingSources, R.ScannerWorkers], h.Controller.State.DirtyKeys.Order());
        string? validator = null, body = null;
        h.Write = async request =>
        {
            validator = request.Headers.IfMatch.Single().ToString();
            body = await request.Content!.ReadAsStringAsync();
            return Saved("""{"scanner.workers":"8","catalog.extra_rating_sources":"letterboxd,kinopoisk"}""");
        };

        await h.Controller.SaveAsync();

        Assert.Equal("\"one\"", validator);
        Assert.Equal(["PUT /api/v2/admin/settings"], h.Writes);
        using var json = JsonDocument.Parse(body!);
        var values = json.RootElement.GetProperty("values");
        Assert.Equal("8", values.GetProperty(R.ScannerWorkers).GetString());
        Assert.Equal("letterboxd,kinopoisk", values.GetProperty(R.ExtraRatingSources).GetString());
        Assert.Empty(h.Controller.State.DirtyKeys);
    }

    [Fact]
    public async Task InvalidNumberNeverReachesTheServerAndARepairCanSave()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        h.Controller.Set(R.ImageWorkers, "257");
        Assert.Equal(AdminLibrarySettingProblem.OutOfRange, h.Controller.State.Errors[R.ImageWorkers].Problem);
        await h.Controller.SaveAsync();
        Assert.Empty(h.Writes);
        h.Controller.Set(R.ImageWorkers, "16");
        h.Write = _ => Task.FromResult(Saved("""{"metadata.image_workers":"16"}"""));
        await h.Controller.SaveAsync();
        Assert.Single(h.Writes);
    }

    [Fact]
    public async Task ConflictAndUnknownOutcomeRetainDraftAndNeverReplay()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        h.Controller.Set(R.MatcherWorkers, "12");
        h.Write = _ => Task.FromResult(AdminMonitorKit.Json("""{"type":"precondition_failed","status":412}""", HttpStatusCode.PreconditionFailed));
        await h.Controller.SaveAsync(); await h.Controller.SaveAsync();
        Assert.True(h.Controller.State.Conflict);
        Assert.Single(h.Writes);
        Assert.Equal("12", h.Controller.State.Values[R.MatcherWorkers]);

        h.Effective = EffectiveJson.Replace("\"matcher.workers\":\"8\"", "\"matcher.workers\":\"10\"");
        await h.Controller.ReloadAsync(preserveDraft: true);
        Assert.False(h.Controller.State.Conflict);
        Assert.Equal("10", h.Controller.State.PersistedValues[R.MatcherWorkers]);
        Assert.Equal("12", h.Controller.State.Values[R.MatcherWorkers]);
        h.Write = _ => Task.FromException<HttpResponseMessage>(new HttpRequestException());
        await h.Controller.SaveAsync(); await h.Controller.SaveAsync();
        Assert.True(h.Controller.State.OutcomeUnknown);
        Assert.Equal(2, h.Writes.Length);
    }

    [Fact]
    public async Task LateEditSurvivesAcknowledgementAndAuthorityRetirementClearsAllData()
    {
        using var h = new Harness();
        await h.Controller.OpenAsync();
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Write = _ => pending.Task;
        h.Controller.Set(R.MatcherBatchSize, "600");
        var save = h.Controller.SaveAsync();
        h.Controller.Set(R.MatcherBatchSize, "700");
        pending.SetResult(Saved("""{"matcher.batch_size":"600"}"""));
        await save;
        Assert.Equal("600", h.Controller.State.PersistedValues[R.MatcherBatchSize]);
        Assert.Equal("700", h.Controller.State.Values[R.MatcherBatchSize]);
        Assert.Equal([R.MatcherBatchSize], h.Controller.State.DirtyKeys);
        h.Kit.Auth.SetCurrentUser(new() { Id = "1", Username = "former-admin", Role = "user" });
        Assert.Empty(h.Controller.State.Values);
        Assert.Empty(h.Controller.State.RatingSources);
    }
}
