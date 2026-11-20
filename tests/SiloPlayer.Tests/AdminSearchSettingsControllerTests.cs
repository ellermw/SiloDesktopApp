using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Search settings (official SearchSettings.tsx / SearchStatusPanel.tsx at 17d5376c) on synthetic data.</summary>
public sealed class AdminSearchSettingsControllerTests
{
    private const string Provider = AdminSearchSettingsRules.Provider;
    private const string Url = AdminSearchSettingsRules.MeilisearchUrl;
    private const string ApiKey = AdminSearchSettingsRules.MeilisearchApiKey;
    private const string Semantic = AdminSearchSettingsRules.SemanticEnabled;
    private const string Ratio = AdminSearchSettingsRules.SemanticRatio;
    private const string StatusRequest = "GET " + AdminSearchApiTests.StatusPath;

    private sealed class Harness : IDisposable
    {
        internal readonly AdminMonitorKit Kit = AdminMonitorKit.Create();
        internal readonly AdminMonitorAuthority Authority;
        internal readonly DashboardManualTime Time = new();
        internal readonly AdminServerSettingsStore Store;
        internal readonly AdminSearchSettingsController Controller;
        internal string Effective;
        internal string ETag = "\"one\"";
        internal string Sensitive = """{"configured":["catalog.search.meilisearch.api_key"],"managed_by_env":[]}""";
        internal string Status = AdminSearchApiTests.StatusJson();
        internal Func<HttpRequestMessage, Task<HttpResponseMessage>>? Write;
        internal Func<Task<HttpResponseMessage>>? EffectiveResponseAsync;
        internal Func<HttpResponseMessage>? StatusResponse;
        internal Func<Task<HttpResponseMessage>>? StatusResponseAsync;
        internal bool Defer;

        internal Harness(string provider = "meilisearch")
        {
            Effective = $$"""{"catalog.search.provider":"{{provider}}","catalog.search.meilisearch.url":"http://localhost:7700","catalog.search.meilisearch.matching_strategy":"","catalog.search.meilisearch.semantic_enabled":"false","catalog.search.meilisearch.index":"","catalog.search.meilisearch.timeout_ms":"","catalog.search.meilisearch.sync_batch_size":"","catalog.search.meilisearch.semantic_ratio":"","branding.server_name":"Other page"}""";
            Authority = new AdminMonitorAuthority(Kit.Client, Kit.Auth);
            var api = new AdminServerSettingsApi(Kit.Client);
            Store = new AdminServerSettingsStore(api, Authority, AdminSearchSettingsController.Keys, AdminSearchSettingsController.Secrets);
            Controller = new AdminSearchSettingsController(api, new AdminSearchApi(Kit.Client), Store, Authority, Time, () => Defer);
            Kit.Http.Respond = (request, _) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (request.Method != HttpMethod.Get) return Write?.Invoke(request) ?? Task.FromResult(AdminMonitorKit.Json("{}", HttpStatusCode.InternalServerError));
                if (path == AdminSearchApiTests.StatusPath) return StatusResponseAsync?.Invoke() ?? Task.FromResult(StatusResponse?.Invoke() ?? AdminMonitorKit.Json(Status));
                if (path.EndsWith("effective") && EffectiveResponseAsync != null) return EffectiveResponseAsync();
                var response = path.EndsWith("effective") ? (Effective == "fail" ? AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable) : AdminMonitorKit.Json(Effective))
                    : path.EndsWith("sensitive-status") ? (Sensitive == "fail" ? AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable) : AdminMonitorKit.Json(Sensitive))
                    : AdminMonitorKit.Json("""{"keys":[],"prefixes":[]}""");
                response.Headers.ETag = new(ETag);
                return Task.FromResult(response);
            };
        }
        /// <summary>The newest request body that carried content (status reads are bodiless GETs that may follow a write).</summary>
        internal string LastBody => Kit.Http.Bodies.Last(body => body.Length > 0);
        internal string[] Writes => Kit.Http.Requests.Where(r => !r.StartsWith("GET")).ToArray();
        internal int StatusReads => Kit.Http.Requests.Count(r => r == StatusRequest);
        internal async Task<Harness> OpenedAsync(bool active = true, bool advanced = false)
        {
            Controller.SetActive(active);
            if (advanced) Controller.SetAdvancedOpen(true);
            await Controller.OpenAsync();
            return this;
        }
        public void Dispose() { Controller.Dispose(); Store.Dispose(); Authority.Dispose(); Kit.Dispose(); }
    }

    private static async Task Until(Func<bool> condition, string label)
    {
        for (var i = 0; i < 400 && !condition(); i++) await Task.Delay(5);
        Assert.True(condition(), "Timed out: " + label);
    }

    private static HttpResponseMessage Saved(string values, string etag = "\"two\"")
    {
        var response = AdminMonitorKit.Json($$"""{"values":{{values}},"restart_required":false}""");
        response.Headers.ETag = new(etag);
        return response;
    }

    [Fact]
    public void ScopeIsTheNineOfficialKeysWithOneSecretAndNoHiddenTier()
    {
        Assert.Equal(["catalog.search.provider", "catalog.search.meilisearch.url", "catalog.search.meilisearch.api_key",
            "catalog.search.meilisearch.matching_strategy", "catalog.search.meilisearch.semantic_enabled", "catalog.search.meilisearch.index",
            "catalog.search.meilisearch.timeout_ms", "catalog.search.meilisearch.sync_batch_size", "catalog.search.meilisearch.semantic_ratio"], AdminSearchSettingsController.Keys);
        Assert.Equal([ApiKey], AdminSearchSettingsController.Secrets);
        Assert.Equal(["SILO_SEARCH_PROVIDER", "SILO_MEILISEARCH_URL", "SILO_MEILISEARCH_API_KEY"],
            new[] { Provider, Url, ApiKey }.Select(AdminSearchSettingsRules.EnvironmentVariable));
        foreach (var hidden in new[] { "rebuild_batch_size", "rebuild_task_queue_depth", "index_types", "embedder", "binary_quantized" })
            Assert.DoesNotContain("catalog.search.meilisearch." + hidden, AdminSearchSettingsController.Keys);
    }

    [Theory]
    [InlineData("catalog.search.provider", "postgres")]
    [InlineData("catalog.search.meilisearch.matching_strategy", "last")]
    [InlineData("catalog.search.meilisearch.semantic_enabled", "false")]
    [InlineData("catalog.search.meilisearch.index", "silo_media_items")]
    [InlineData("catalog.search.meilisearch.timeout_ms", "800")]
    [InlineData("catalog.search.meilisearch.sync_batch_size", "500")]
    [InlineData("catalog.search.meilisearch.semantic_ratio", "0.50")]
    public void BlankSavedValuesDisplayTheOfficialDefaults(string key, string expected)
    {
        Assert.Equal(expected, AdminSearchSettingsRules.Display(key, ""));
        Assert.Equal("saved", AdminSearchSettingsRules.Display(key, "saved"));
    }

    [Fact]
    public async Task OpenLoadsEveryFieldWithoutAnyStatusReadWhileThePageIsPostgresAndAdvancedIsClosed()
    {
        using var h = new Harness("postgres");
        await h.OpenedAsync();
        var state = h.Controller.State;
        Assert.True(state.Loaded);
        Assert.False(state.MeilisearchSelected);
        Assert.Equal("http://localhost:7700", state.Values[Url]);
        Assert.True(state.ApiKeyConfigured);
        Assert.Equal("", state.Values[ApiKey]);
        Assert.Empty(state.DirtyKeys);
        Assert.Equal(0, h.StatusReads);
        Assert.False(h.Controller.IsPolling);
    }

    [Fact]
    public async Task OpeningSearchRevalidatesAPrimedSharedSnapshotWithinTheCacheWindow()
    {
        using var h = new Harness();
        using (var owner = h.Authority.Capture())
        {
            await h.Store.GetEffectiveAsync(owner);
            await h.Store.GetSensitiveStatusAsync(owner);
        }
        h.Effective = h.Effective.Replace("http://localhost:7700", "http://fresh.example.test:7700").Replace("\"meilisearch\"", "\"postgres\"");
        h.Sensitive = """{"configured":[],"managed_by_env":["catalog.search.meilisearch.url"]}""";
        h.ETag = "\"fresh\"";
        await h.OpenedAsync();
        Assert.Equal("postgres", h.Controller.State.Values[Provider]);
        Assert.Equal("http://fresh.example.test:7700", h.Controller.State.Values[Url]);
        Assert.False(h.Controller.State.ApiKeyConfigured);
        Assert.Contains(Url, h.Controller.State.ManagedByEnv);
        Assert.Equal(2, h.Kit.Http.Requests.Count(r => r.EndsWith("/effective", StringComparison.Ordinal)));
        Assert.Equal(2, h.Kit.Http.Requests.Count(r => r.EndsWith("/sensitive-status", StringComparison.Ordinal)));
        h.Write = request =>
        {
            Assert.Equal("\"fresh\"", Assert.Single(request.Headers.IfMatch).ToString());
            return Task.FromResult(Saved("""{"catalog.search.meilisearch.index":"custom"}"""));
        };
        h.Controller.Set(AdminSearchSettingsRules.Index, "custom");
        await h.Controller.SaveAsync();
        Assert.Single(h.Writes);
    }

    [Fact]
    public async Task StatusIsReadWhenMeilisearchIsSelectedOrAdvancedIsOpenAndNotWhileInactive()
    {
        using (var h = new Harness("meilisearch"))
        {
            await h.OpenedAsync(active: false);
            Assert.Equal(0, h.StatusReads);
            h.Controller.SetActive(true);
            await Until(() => h.Controller.State.Status != null, "status after activation");
            Assert.Equal(1, h.StatusReads);
            Assert.Equal(1234, h.Controller.State.Status!.Index.DocumentCount);
        }
        using (var h = new Harness("postgres"))
        {
            await h.OpenedAsync();
            h.Controller.SetAdvancedOpen(true);
            await Until(() => h.Controller.State.Status != null, "status for the open Advanced panel");
            Assert.Equal(1, h.StatusReads);
            h.Controller.SetAdvancedOpen(false);
            h.Controller.Set(Provider, "meilisearch");
            Assert.Equal(1, h.StatusReads); // fresh within 15 seconds: not read again
        }
    }

    [Fact]
    public async Task SwitchingToMeilisearchReadsStatusAndSwitchingBackKeepsTheStagedMeilisearchEdits()
    {
        using var h = new Harness("postgres");
        await h.OpenedAsync();
        h.Controller.Set(Provider, "meilisearch");
        h.Controller.Set(Url, "http://search.example.test:7700");
        await Until(() => h.Controller.State.Status != null, "status after selecting Meilisearch");
        h.Controller.Set(Provider, "postgres");
        var state = h.Controller.State;
        Assert.False(state.MeilisearchSelected);
        Assert.Contains(Url, state.DirtyKeys);
        Assert.True(AdminSearchSettingsRules.AnyMeilisearchDirty(state.DirtyKeys), "the staged edit keeps the Meilisearch rows reachable");
        Assert.Equal("http://search.example.test:7700", state.Values[Url]);
    }

    [Fact]
    public async Task PollingRunsEveryTwoSecondsOnlyWhileARebuildIsRequiredAndStopsWhenItCompletes()
    {
        using var h = new Harness { Status = AdminSearchApiTests.StatusJson(rebuild: true) };
        await h.OpenedAsync();
        await Until(() => h.Controller.State.Status?.Index.RebuildRequired == true && h.Controller.IsPolling, "polling starts");
        var reads = h.StatusReads;
        h.Time.Advance(TimeSpan.FromSeconds(2));
        await Until(() => h.StatusReads == reads + 1, "one poll read");
        h.Time.Advance(TimeSpan.FromSeconds(2));
        await Until(() => h.StatusReads == reads + 2, "second poll read");
        h.Status = AdminSearchApiTests.StatusJson(rebuild: false);
        h.Time.Advance(TimeSpan.FromSeconds(2));
        await Until(() => h.Controller.State.Status?.Index.RebuildRequired == false && !h.Controller.IsPolling, "polling stops with the rebuild");
        var done = h.StatusReads;
        h.Time.Advance(TimeSpan.FromSeconds(30));
        await Task.Delay(50);
        Assert.Equal(done, h.StatusReads);
    }

    [Theory]
    [InlineData("retry")]
    [InlineData("save")]
    [InlineData("reload")]
    public async Task ExplicitInvalidationsDuringAReadCoalesceIntoOneFreshFollowUp(string operation)
    {
        using var h = new Harness();
        var held = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.StatusResponseAsync = () => held.Task;
        await h.OpenedAsync();
        await Until(() => h.StatusReads == 1, "held initial status");
        try
        {
            if (operation == "save")
            {
                h.Write = _ => Task.FromResult(Saved("""{"catalog.search.meilisearch.index":"custom"}"""));
                h.Controller.Set(AdminSearchSettingsRules.Index, "custom");
                await h.Controller.SaveAsync();
                Assert.Single(h.Writes);
            }
            else if (operation == "reload")
            {
                await h.Controller.ReloadAsync(preserveDraft: true);
                await h.Controller.ReloadAsync(preserveDraft: true);
            }
            else
            {
                h.Controller.RefreshStatus();
                h.Controller.RefreshStatus();
            }
            Assert.Equal(1, h.StatusReads);
            h.Status = AdminSearchApiTests.StatusJson().Replace("\"document_count\":1234", "\"document_count\":4321");
            h.StatusResponseAsync = null;
        }
        finally { held.TrySetResult(AdminMonitorKit.Json(AdminSearchApiTests.StatusJson())); }
        await Until(() => h.StatusReads == 2 && h.Controller.State.Status?.Index.DocumentCount == 4321, "fresh follow-up status");
        h.Controller.Set(AdminSearchSettingsRules.Index, "another");
        Assert.Equal(2, h.StatusReads);
        Assert.False(h.Controller.IsPolling);
    }

    [Fact]
    public async Task AQueuedRefreshSurvivesInactivityUntilActivation()
    {
        using var h = new Harness();
        var held = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.StatusResponseAsync = () => held.Task;
        await h.OpenedAsync();
        await Until(() => h.StatusReads == 1, "held status");
        h.Controller.SetActive(false);
        h.Controller.RefreshStatus();
        h.StatusResponseAsync = null;
        held.TrySetResult(AdminMonitorKit.Json(AdminSearchApiTests.StatusJson()));
        await Until(() => h.Controller.State.Status != null, "inactive read settled");
        Assert.Equal(1, h.StatusReads);
        h.Controller.SetActive(true);
        await Until(() => h.StatusReads == 2, "pending invalidation on activation");
    }

    [Fact]
    public async Task RetirementDropsTheQueuedRefreshAndHeldResult()
    {
        using var h = new Harness();
        var held = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.StatusResponseAsync = () => held.Task;
        await h.OpenedAsync();
        await Until(() => h.StatusReads == 1, "held status");
        h.Controller.RefreshStatus();
        h.Controller.Suspend();
        h.StatusResponseAsync = null;
        held.TrySetResult(AdminMonitorKit.Json(AdminSearchApiTests.StatusJson(rebuild: true)));
        h.Controller.SetActive(false);
        h.Controller.SetActive(true);
        Assert.Equal(1, h.StatusReads);
        Assert.Null(h.Controller.State.Status);
        Assert.False(h.Controller.IsPolling);
    }

    [Fact]
    public async Task SlowPollingDoesNotQueueAnImmediateExtraRead()
    {
        using var h = new Harness { Status = AdminSearchApiTests.StatusJson(rebuild: true) };
        await h.OpenedAsync();
        await Until(() => h.Controller.IsPolling, "poll active");
        var held = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.StatusResponseAsync = () => held.Task;
        h.Time.Advance(TimeSpan.FromSeconds(2));
        await Until(() => h.StatusReads == 2, "slow poll");
        h.Time.Advance(TimeSpan.FromSeconds(8));
        h.StatusResponseAsync = null;
        held.TrySetResult(AdminMonitorKit.Json(AdminSearchApiTests.StatusJson()));
        await Until(() => h.Controller.State.Status?.Index.RebuildRequired == false && !h.Controller.IsPolling, "slow poll completed");
        Assert.Equal(2, h.StatusReads);
    }

    [Fact]
    public async Task AHiddenOrDeactivatedPageDropsThePollAndActivationReadsOnlyStaleStatus()
    {
        using var h = new Harness { Status = AdminSearchApiTests.StatusJson(rebuild: true) };
        await h.OpenedAsync();
        await Until(() => h.Controller.IsPolling, "polling");
        h.Controller.SetActive(false);
        Assert.False(h.Controller.IsPolling);
        var reads = h.StatusReads;
        h.Time.Advance(TimeSpan.FromSeconds(10));
        await Task.Delay(30);
        Assert.Equal(reads, h.StatusReads);
        h.Controller.SetActive(true);
        await Task.Delay(30);
        Assert.Equal(reads, h.StatusReads); // 10 s old: still fresh
        h.Controller.SetActive(false);
        h.Time.Advance(TimeSpan.FromSeconds(10));
        h.Controller.SetActive(true);
        await Until(() => h.StatusReads == reads + 1, "stale status read on activation");
        Assert.True(h.Controller.IsPolling);
    }

    [Fact]
    public async Task APollTickIsSkippedWhilePlaybackIsLoadingAndTriesAgainNextTick()
    {
        using var h = new Harness { Status = AdminSearchApiTests.StatusJson(rebuild: true) };
        await h.OpenedAsync();
        await Until(() => h.Controller.IsPolling, "polling");
        var reads = h.StatusReads;
        h.Defer = true;
        h.Time.Advance(TimeSpan.FromSeconds(2));
        await Task.Delay(30);
        Assert.Equal(reads, h.StatusReads);
        h.Defer = false;
        h.Time.Advance(TimeSpan.FromSeconds(2));
        await Until(() => h.StatusReads == reads + 1, "poll resumes");
    }

    [Fact]
    public async Task AFailedStatusKeepsTheLastStatusAndIsNotRepeatedForEveryKeystroke()
    {
        using var h = new Harness();
        await h.OpenedAsync();
        await Until(() => h.Controller.State.Status != null, "first status");
        h.StatusResponse = () => AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable);
        await Task.Delay(30); // let the first read leave flight
        h.Controller.RefreshStatus();
        await Until(() => h.Controller.State.StatusFailure != null, "failure shown");
        await Task.Delay(30); // let the failed read leave flight
        Assert.NotNull(h.Controller.State.Status);
        var reads = h.StatusReads;
        for (var i = 0; i < 5; i++) h.Controller.Set(Url, "http://h" + i);
        await Task.Delay(30);
        Assert.Equal(reads, h.StatusReads);
        h.StatusResponse = null;
        h.Controller.RefreshStatus();
        await Until(() => h.Controller.State.StatusFailure == null, "retry clears the failure");
    }

    [Fact]
    public async Task SaveSendsOnlyTheDirtySearchKeysUnderTheCapturedETag()
    {
        using var h = new Harness();
        await h.OpenedAsync();
        h.Write = _ => Task.FromResult(Saved("""{"catalog.search.provider":"meilisearch","catalog.search.meilisearch.timeout_ms":"1200"}"""));
        h.Controller.Set(AdminSearchSettingsRules.TimeoutMs, "1200");
        h.Controller.Set(Provider, "meilisearch"); // equal to the saved value: withdrawn
        Assert.Equal([AdminSearchSettingsRules.TimeoutMs], h.Controller.State.DirtyKeys);
        await h.Controller.SaveAsync();
        var write = Assert.Single(h.Writes);
        Assert.StartsWith("PUT ", write);
        using var body = System.Text.Json.JsonDocument.Parse(h.LastBody);
        Assert.Equal([AdminSearchSettingsRules.TimeoutMs], body.RootElement.GetProperty("values").EnumerateObject().Select(p => p.Name));
        Assert.Empty(h.Controller.State.DirtyKeys);
        Assert.Equal("1200", h.Controller.State.PersistedValues[AdminSearchSettingsRules.TimeoutMs]);
    }

    [Fact]
    public async Task SaveReReadsStatusAfterAnAcknowledgedSearchChange()
    {
        using var h = new Harness();
        await h.OpenedAsync();
        await Until(() => h.Controller.State.Status != null, "first status");
        var reads = h.StatusReads;
        h.Write = _ => Task.FromResult(Saved("""{"catalog.search.meilisearch.matching_strategy":"all"}"""));
        h.Controller.Set(AdminSearchSettingsRules.MatchingStrategy, "all");
        await h.Controller.SaveAsync();
        await Until(() => h.StatusReads == reads + 1, "status read after save");
    }

    [Fact]
    public async Task EnvironmentManagedKeysAreDisabledAndNeverWritten()
    {
        using var h = new Harness { Sensitive = """{"configured":[],"managed_by_env":["catalog.search.provider","catalog.search.meilisearch.url","catalog.search.meilisearch.api_key"]}""" };
        await h.OpenedAsync();
        h.Controller.Set(Provider, "postgres");
        h.Controller.Set(Url, "http://other");
        h.Controller.SetApiKey("typed");
        Assert.Empty(h.Controller.State.DirtyKeys);
        Assert.Equal(["catalog.search.provider", "catalog.search.meilisearch.url", "catalog.search.meilisearch.api_key"], h.Controller.State.ManagedByEnv);
        h.Write = _ => Task.FromResult(Saved("""{"catalog.search.meilisearch.index":"custom"}"""));
        h.Controller.Set(AdminSearchSettingsRules.Index, "custom");
        await h.Controller.SaveAsync();
        using var body = System.Text.Json.JsonDocument.Parse(h.LastBody);
        Assert.Equal([AdminSearchSettingsRules.Index], body.RootElement.GetProperty("values").EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task AFailedEnvironmentStatusProtectsProviderUrlAndKeyAndNeverKeepsAKeyDraft()
    {
        using var h = new Harness { Sensitive = "fail" };
        await h.OpenedAsync();
        var state = h.Controller.State;
        Assert.True(state.Loaded);
        Assert.True(state.SensitiveStatusError);
        h.Controller.Set(Provider, "postgres");
        h.Controller.Set(Url, "http://other");
        h.Controller.SetApiKey("typed");
        Assert.Empty(h.Controller.State.DirtyKeys);
        h.Controller.Set(AdminSearchSettingsRules.Index, "custom"); // unrelated tuning stays editable
        Assert.Contains(AdminSearchSettingsRules.Index, h.Controller.State.DirtyKeys);
        h.Sensitive = """{"configured":["catalog.search.meilisearch.api_key"],"managed_by_env":[]}""";
        await h.Controller.ReloadAsync(preserveDraft: true);
        Assert.False(h.Controller.State.SensitiveStatusError);
        Assert.Contains(AdminSearchSettingsRules.Index, h.Controller.State.DirtyKeys);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedEffectiveAndSensitiveReloadErasesTheKeyAndKeepsCredentialWritesProtected(bool preserveDraft)
    {
        using var h = new Harness();
        await h.OpenedAsync();
        var effective = h.Effective;
        var sensitive = h.Sensitive;
        h.Controller.SetApiKey("replacement-draft");
        h.Controller.Set(Url, "http://draft.example.test");
        h.Controller.Set(Provider, "postgres");
        h.Effective = "fail";
        h.Sensitive = "fail";
        await h.Controller.ReloadAsync(preserveDraft);
        Assert.False(h.Controller.State.Loading);
        Assert.NotNull(h.Controller.State.Error);
        Assert.True(h.Controller.State.SensitiveStatusError);
        Assert.Equal("", h.Controller.State.Values[ApiKey]);
        Assert.DoesNotContain(ApiKey, h.Controller.State.DirtyKeys);
        h.Controller.SetApiKey("another-draft");
        h.Controller.Set(Url, "http://blocked.example.test");
        h.Controller.Set(Provider, "meilisearch");
        Assert.Equal("", h.Controller.State.Values[ApiKey]);
        Assert.Equal("http://draft.example.test", h.Controller.State.Values[Url]);
        Assert.Equal("postgres", h.Controller.State.Values[Provider]);
        await h.Controller.SaveAsync();
        Assert.Empty(h.Writes);

        // A successful sensitive read alone must not lift protection after failed reconciliation.
        h.Sensitive = sensitive;
        await h.Controller.ReloadAsync(preserveDraft);
        Assert.True(h.Controller.State.SensitiveStatusError);
        await h.Controller.SaveAsync();
        Assert.Empty(h.Writes);
        h.Effective = effective;
        await h.Controller.ReloadAsync(preserveDraft);
        Assert.False(h.Controller.State.SensitiveStatusError);
        Assert.Equal("", h.Controller.State.Values[ApiKey]);
        h.Controller.SetApiKey("reviewed-draft");
        Assert.Contains(ApiKey, h.Controller.State.DirtyKeys);
    }

    [Fact]
    public async Task ApiKeyTypingReplacesEmptyingKeepsClearIsExplicitAndTheSavedKeyIsNeverLoaded()
    {
        using var h = new Harness();
        await h.OpenedAsync();
        Assert.Equal("", h.Controller.State.Values[ApiKey]);
        h.Controller.SetApiKey("new-key");
        Assert.Contains(ApiKey, h.Controller.State.DirtyKeys);
        h.Controller.SetApiKey("");
        Assert.DoesNotContain(ApiKey, h.Controller.State.DirtyKeys); // emptying keeps the saved key
        h.Controller.ClearApiKey();
        Assert.Contains(ApiKey, h.Controller.State.DirtyKeys);
        Assert.True(h.Controller.State.Values[ApiKey].Length == 0);
        h.Controller.KeepApiKey();
        Assert.DoesNotContain(ApiKey, h.Controller.State.DirtyKeys);
        h.Controller.SetApiKey("sent-once");
        h.Write = _ => Task.FromResult(Saved("""{"catalog.search.provider":"meilisearch"}"""));
        await h.Controller.SaveAsync();
        Assert.Contains("sent-once", h.LastBody);
        Assert.Equal("", h.Controller.State.Values[ApiKey]); // erased after the acknowledged save
        Assert.True(h.Controller.State.ApiKeyConfigured);
    }

    [Fact]
    public async Task ClearingTheApiKeyIsOfferedOnlyWhenOneIsSavedAndWritesAnExplicitEmpty()
    {
        using var h = new Harness { Sensitive = """{"configured":[],"managed_by_env":[]}""" };
        await h.OpenedAsync();
        h.Controller.ClearApiKey();
        Assert.DoesNotContain(ApiKey, h.Controller.State.DirtyKeys);
        using var configured = new Harness();
        await configured.OpenedAsync();
        configured.Write = _ => Task.FromResult(Saved("""{"catalog.search.provider":"meilisearch"}"""));
        configured.Controller.ClearApiKey();
        await configured.Controller.SaveAsync();
        using var body = System.Text.Json.JsonDocument.Parse(configured.LastBody);
        Assert.Equal("", body.RootElement.GetProperty("values").GetProperty(ApiKey).GetString());
        Assert.False(configured.Controller.State.ApiKeyConfigured);
    }

    [Fact]
    public async Task EnablingSemanticSearchWarnsOnlyForAnUnsavedChangeToTrue()
    {
        using var h = new Harness();
        await h.OpenedAsync();
        bool Warns() => AdminSearchSettingsRules.IsEnablingSemanticSearch(h.Controller.State.Values, h.Controller.State.PersistedValues, h.Controller.State.DirtyKeys);
        Assert.False(Warns());
        h.Controller.Set(Semantic, "true");
        Assert.True(Warns());
        h.Controller.Set(Semantic, "false");
        Assert.False(Warns());
    }

    [Fact]
    public async Task ConnectionCheckSendsEveryMeilisearchValueAndOnlyTheDirtyKeysAndHidesAfterAnEdit()
    {
        using var h = new Harness();
        await h.OpenedAsync();
        h.Write = _ => Task.FromResult(AdminMonitorKit.Json("""{"success":true,"message":"Meilisearch is reachable."}"""));
        h.Controller.Set(Url, "http://search.example.test:7700");
        h.Controller.SetApiKey("typed-key");
        await h.Controller.CheckConnectionAsync();
        Assert.Equal("POST " + AdminSearchApiTests.CheckPath, Assert.Single(h.Writes));
        using var body = System.Text.Json.JsonDocument.Parse(h.LastBody);
        var values = body.RootElement.GetProperty("values");
        Assert.Equal(AdminSearchSettingsRules.MeilisearchKeys.Count, values.EnumerateObject().Count());
        Assert.Equal("http://search.example.test:7700", values.GetProperty(Url).GetString());
        Assert.Equal(new[] { Url, ApiKey }.Order(StringComparer.Ordinal), body.RootElement.GetProperty("dirty_keys").EnumerateArray().Select(v => v.GetString()!).Order(StringComparer.Ordinal));
        Assert.True(h.Controller.State.ConnectionResult!.Success);
        h.Controller.Set(AdminSearchSettingsRules.Index, "changed");
        Assert.Null(h.Controller.State.ConnectionResult);
    }

    [Fact]
    public async Task ConnectionCheckIsUnavailableForPostgresAndADelayedAnswerForAnOldDraftIsDropped()
    {
        using (var postgres = new Harness("postgres"))
        {
            await postgres.OpenedAsync();
            await postgres.Controller.CheckConnectionAsync();
            Assert.Empty(postgres.Writes);
        }
        using var h = new Harness();
        await h.OpenedAsync();
        var gate = new TaskCompletionSource();
        h.Write = async _ => { await gate.Task; return AdminMonitorKit.Json("""{"success":true,"message":"ok"}"""); };
        var check = h.Controller.CheckConnectionAsync();
        await Until(() => h.Controller.State.Checking, "check in flight");
        h.Controller.Set(Url, "http://new.example.test");
        gate.SetResult();
        await check;
        Assert.False(h.Controller.State.Checking);
        Assert.Null(h.Controller.State.ConnectionResult);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReloadClearsAnExistingConnectionResultEvenWhenOnlyEffectiveSettingsFail(bool preserveDraft)
    {
        using var h = new Harness();
        await h.OpenedAsync(active: false);
        h.Write = _ => Task.FromResult(AdminMonitorKit.Json("""{"success":true,"message":"ok"}"""));
        await h.Controller.CheckConnectionAsync();
        Assert.True(h.Controller.State.ConnectionResult!.Success);
        var effective = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.EffectiveResponseAsync = () => effective.Task;
        var reload = h.Controller.ReloadAsync(preserveDraft);
        try
        {
            Assert.True(h.Controller.State.Loading);
            Assert.Null(h.Controller.State.ConnectionResult);
        }
        finally { effective.TrySetResult(AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable)); }
        await reload;
        Assert.False(h.Controller.State.Loading);
        Assert.NotNull(h.Controller.State.Error);
        Assert.False(h.Controller.State.SensitiveStatusError);
        Assert.Null(h.Controller.State.ConnectionResult);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task ReloadDropsAnOlderCheckDuringOrAfterAFailedEffectiveRead(bool completeDuringReload, bool success)
    {
        using var h = new Harness();
        await h.OpenedAsync(active: false);
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Write = _ => response.Task;
        var check = h.Controller.CheckConnectionAsync();
        Assert.True(h.Controller.State.Checking);
        var effective = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.EffectiveResponseAsync = () => effective.Task;
        var reload = h.Controller.ReloadAsync(preserveDraft: true);
        try
        {
            Assert.True(h.Controller.State.Loading);
            if (!completeDuringReload)
            {
                effective.TrySetResult(AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable));
                await reload;
            }
            response.TrySetResult(success
                ? AdminMonitorKit.Json("""{"success":true,"message":"ok"}""")
                : AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable));
            await check;
            Assert.False(h.Controller.State.Checking);
            Assert.Null(h.Controller.State.ConnectionResult);
        }
        finally
        {
            response.TrySetResult(AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable));
            effective.TrySetResult(AdminMonitorKit.Json("{}", HttpStatusCode.ServiceUnavailable));
        }
        await reload;
        Assert.False(h.Controller.State.Loading);
        Assert.NotNull(h.Controller.State.Error);
        Assert.False(h.Controller.State.SensitiveStatusError);
        Assert.False(h.Controller.State.Checking);
        Assert.Null(h.Controller.State.ConnectionResult);
    }

    [Fact]
    public async Task ARejectedCheckShowsTheGenericFailureNotServerDetail()
    {
        using var h = new Harness();
        await h.OpenedAsync();
        h.Write = _ => Task.FromResult(AdminMonitorKit.Json("""{"title":"x","detail":"secret-detail"}""", HttpStatusCode.UnprocessableEntity));
        await h.Controller.CheckConnectionAsync();
        var result = h.Controller.State.ConnectionResult!;
        Assert.False(result.Success);
        Assert.DoesNotContain("secret-detail", result.Message);
    }


    [Fact]
    public async Task SuspendRetiresReadsPollAndDraftsAndNothingIsRequestedAfterwards()
    {
        using var h = new Harness { Status = AdminSearchApiTests.StatusJson(rebuild: true) };
        await h.OpenedAsync();
        await Until(() => h.Controller.IsPolling, "polling");
        h.Controller.Set(AdminSearchSettingsRules.Index, "custom");
        h.Controller.Suspend();
        Assert.False(h.Controller.IsPolling);
        Assert.Null(h.Controller.State.Owner);
        Assert.Empty(h.Controller.State.DirtyKeys);
        Assert.Equal("", h.Controller.State.Values.GetValueOrDefault(AdminSearchSettingsRules.Index, ""));
        var requests = h.Kit.Http.Requests.Count;
        h.Time.Advance(TimeSpan.FromSeconds(30));
        await h.Controller.SaveAsync();
        await h.Controller.CheckConnectionAsync();
        await Task.Delay(30);
        Assert.Equal(requests, h.Kit.Http.Requests.Count);
    }

    [Fact]
    public async Task ASemanticRatioEditStaysAnEditUntilItEqualsTheSavedValue()
    {
        using var h = new Harness();
        await h.OpenedAsync();
        h.Controller.Set(Ratio, "0.75");
        Assert.Contains(Ratio, h.Controller.State.DirtyKeys);
        h.Controller.Set(Ratio, "");
        Assert.DoesNotContain(Ratio, h.Controller.State.DirtyKeys);
        Assert.Equal("0.50", AdminSearchSettingsRules.Display(Ratio, h.Controller.State.Values[Ratio]));
    }

    [Theory]
    [InlineData(0.5, "0.50")]
    [InlineData(1.0, "1.00")]
    [InlineData(0.333, "0.33")]
    public void SemanticRatioIsShownWithTwoDecimals(double value, string expected)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            Assert.Equal(expected, AdminSearchSettingsRules.FormatSemanticRatio(value));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = culture; }
    }

    [Theory]
    [InlineData(0.805, 81)]
    [InlineData(0, 0)]
    [InlineData(double.NaN, 0)]
    public void PercentRoundsWholePercentAndTreatsAMissingValueAsZero(double ratio, long expected) =>
        Assert.Equal(expected, AdminSearchSettingsRules.Percent(ratio));

    [Fact]
    public void FormatDateTimeIsEmptyForAMissingInstantAndLocalOtherwise()
    {
        Assert.Equal("", AdminSearchSettingsRules.FormatDateTime(null));
        var text = AdminSearchSettingsRules.FormatDateTime(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), TimeZoneInfo.Utc);
        Assert.Contains(", ", text);
        Assert.Contains("2026", text);
    }

    // ---------------------------------------------------------------- catalog, navigation and registration

    [Theory]
    [InlineData("meilisearch")]
    [InlineData("typo")]
    [InlineData("reindex")]
    public void SearchIsAServerLeafSearchableOnlyWhenRegistered(string query)
    {
        Assert.True(AdminRouteCatalog.TryResolve("/admin/settings/search", out var resolution));
        Assert.Equal(AdminRoute.SearchSettings, resolution.Request!.Route);
        Assert.Equal("/admin/settings/search", resolution.CanonicalHref);
        Assert.False(new AdminPageRegistry([]).CanOpen(resolution.Request));
        Assert.Empty(AdminNavigation.SearchGroups(query, new AdminPageRegistry([]).IsRegistered));
        var registry = new AdminPageRegistry([new(AdminRoute.SearchSettings, typeof(object))]);
        Assert.True(registry.CanOpen(resolution.Request));
        Assert.Equal(AdminRoute.SearchSettings, Assert.Single(Assert.Single(AdminNavigation.SearchGroups(query, registry.IsRegistered)).Pages).Route);
        Assert.Empty(AdminRouteCatalog.MissingTextKeys(AdminRoute.SearchSettings));
    }

    [Fact]
    public void EveryPageAndStatusMessageResolvesToCatalogTextNotAKey()
    {
        var keys = new List<string>
        {
            "web.admin.settings.search.title", "web.admin.settings.search.group", "web.admin.settings.search.description", "web.admin.settings.search.search.description",
            "web.admin.settings.search.managedByEnv", "web.admin.settings.search.credentialStatusError", "web.admin.settings.search.degradedFallback",
            "web.admin.settings.search.rebuildInBackground", "web.admin.settings.search.provider.label", "web.admin.settings.search.provider.description",
            "web.admin.settings.search.provider.postgres", "web.admin.settings.search.meili.url.label", "web.admin.settings.search.meili.apiKey.label",
            "web.admin.settings.search.meili.apiKey.hint", "web.admin.settings.search.meili.matching.label", "web.admin.settings.search.meili.matching.last",
            "web.admin.settings.search.meili.matching.all", "web.admin.settings.search.meili.semantic.label", "web.admin.settings.search.meili.semantic.description",
            "web.admin.settings.search.meili.semantic.enablingWarning", "web.admin.settings.search.meili.index.label", "web.admin.settings.search.meili.index.description",
            "web.admin.settings.search.meili.timeout.label", "web.admin.settings.search.meili.timeout.description", "web.admin.settings.search.meili.batch.label",
            "web.admin.settings.search.meili.batch.description", "web.admin.settings.search.meili.ratio.label", "web.admin.settings.search.meili.ratio.description",
            "web.admin.common.units.milliseconds", "web.common.format.percent", "web.settings.advancedSection.title", "web.settings.advancedSection.accessibleLabel",
            "web.admin.common.connectionCheck.check", "web.admin.common.connectionCheck.checking", "web.settings.secretField.clear", "web.settings.secretField.keep",
            "web.settings.secretField.clearedDescription", "web.settings.secretField.willClear", "web.settings.secretField.notConfigured",
        };
        keys.AddRange(AdminText.Keys.Where(key => key.StartsWith("web.admin.settings.search.status.", StringComparison.Ordinal) || key.StartsWith("configure.native.search.", StringComparison.Ordinal)));
        Assert.True(keys.Count > 80, "the status and native-only keys are part of the check");
        Assert.All(keys, key => Assert.NotEqual(key, AdminText.T(key)));
        Assert.Equal("ms", AdminText.T("web.admin.common.units.milliseconds"));
        Assert.Equal("81%", AdminText.T("web.common.format.percent", new { percent = 81 }));
        Assert.Equal("Managed by SILO_SEARCH_PROVIDER.", AdminText.T("web.admin.settings.search.managedByEnv", new { env = "SILO_SEARCH_PROVIDER" }));
        Assert.Equal("schema 3/4", AdminText.T("web.admin.settings.search.status.schema", new { version = 3, expected = 4 }));
        Assert.Equal("Last sync 1/2/2026 · 1 item indexed", AdminText.T("web.admin.settings.search.status.summary",
            new Dictionary<string, object?> { ["count"] = 1L, ["count_text"] = "1", ["last_sync"] = "1/2/2026" }));
        Assert.Equal("movie: 8/10 (80%)", AdminText.T("web.admin.settings.search.status.typeCoverage", new { type = "movie", vectorized = 8, eligible = 10, percent = "80%" }));
        var rich = AdminNetworkAccessText.Rich("web.admin.settings.search.rebuildInBackground");
        Assert.Contains(rich, run => run is { Tag: "link", Text: "Open maintenance task" });
    }

    [Fact]
    public void TheDisclosureRemembersItsStateUnderASearchSection()
    {
        var preferences = new LoggingDisclosurePreferences();
        Assert.False(preferences.Read("search"));
        preferences.Write("search", true);
        Assert.True(preferences.Read("search"));
    }

    [Fact]
    public void ProductionConfigureRegistrationIncludesTheSearchPageAndItsKeys()
    {
        var source = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Services", "AdminConfigureServices.cs"));
        Assert.Contains("new AdminPageRegistration(AdminRoute.SearchSettings, typeof(AdminSearchSettingsPage))", source);
        Assert.Contains("AddSingleton<AdminSearchApi>", source);
        Assert.Contains("AdminSearchSettingsController.Keys", source);
        Assert.Contains("AdminSearchSettingsController.Secrets", source);
        foreach (var file in new[] { "AdminSearchSettingsPage.cs", "AdminSearchSettingsPage.Status.cs", "AdminSearchSettingsPage.xaml" })
            Assert.True(File.Exists(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Views", "Admin", file)), file);
        var installer = File.ReadAllText(Path.Combine(TestRepository.Root, "installer", "build.ps1"));
        Assert.Contains("'Views/Admin/AdminSearchSettingsPage.xbf'", installer);
        var manifest = File.ReadAllText(Path.Combine(TestRepository.Root, "docs", "parity", "admin-monitor-surfaces.json"));
        foreach (var path in new[] { "Api/AdminSearchApi.cs", "Models/Admin/AdminSearchModels.cs", "Services/Admin/AdminSearchSettingsController.cs",
                     "Services/Admin/AdminSearchSettingsRules.cs", "Views/Admin/AdminSearchSettingsPage.cs", "Views/Admin/AdminSearchSettingsPage.Status.cs", "Views/Admin/AdminSearchSettingsPage.xaml" })
            Assert.Contains(path, manifest);
    }

    [Fact]
    public void SearchSourcesNeverLogOrPrintSettingValues()
    {
        foreach (var path in new[]
        {
            Path.Combine(TestRepository.Root, "src", "SiloPlayer.Core", "Api", "AdminSearchApi.cs"),
            Path.Combine(TestRepository.Root, "src", "SiloPlayer.Core", "Services", "Admin", "AdminSearchSettingsController.cs"),
            Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Views", "Admin", "AdminSearchSettingsPage.cs"),
            Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Views", "Admin", "AdminSearchSettingsPage.Status.cs"),
        })
        {
            var source = File.ReadAllText(path);
            Assert.DoesNotContain("LocalLog.", source.Replace("LocalLog.RedactDiagnosticText", ""), StringComparison.Ordinal);
            Assert.DoesNotContain("Console.Write", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Debug.Write", source, StringComparison.Ordinal);
        }
    }
}
