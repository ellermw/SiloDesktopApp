using System.Net;
using System.Text;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Wire contract for Configure › Search (official admin_catalog_search.go, admin_settings_checks.go, admin_tasks.go at 17d5376c). Synthetic data only.</summary>
public sealed class AdminSearchApiTests
{
    internal const string StatusPath = "/api/v2/admin/catalog/search/status";
    internal const string CheckPath = "/api/v2/admin/settings/check/meilisearch";

    internal static string StatusJson(bool rebuild = false, bool degraded = false, string active = "meilisearch") => $$"""
        {"configured_provider":"meilisearch","active_provider":"{{active}}","degraded":{{(degraded ? "true" : "false")}},{{(degraded ? "\"degraded_reason\":\"Meilisearch is unreachable.\"," : "")}}
         "meilisearch":{"configured":true,"healthy":true,"circuit_state":"closed","timeout_ms":800,"matching_strategy":"last","index_types":["movie","show"],
           "semantic_enabled":true,"binary_quantized":false,"semantic_ratio":0.5,"embedder":"default","last_fallback":"timeout"},
         "index":{"active_index_uid":"silo_media_items_v3","schema_version":3,"expected_schema_version":3,"rebuild_required":{{(rebuild ? "true" : "false")}},
           "document_count":1234,"vector_document_count":1000,"pending_events":7,"dead_lettered_events":2,"last_sync_at":"2026-01-02T03:04:05.678Z","last_processed_event_id":"9007199254740993"},
         "tasks":[{"key":"sync_catalog_search_index","name":"Sync","href":"/admin/tasks/sync_catalog_search_index"}],
         "semantic":{"ready":true,"vector_coverage_ratio":0.81,"coverage_updated_at":"2026-01-02T03:04:05Z",
           "per_type":[{"type":"movie","eligible":10,"vectorized":8,"vector_coverage_ratio":0.8,"ready":true}],
           "capability":{"ok":true,"embedder":"default","dimensions":384} } }
        """;

    private static (AdminMonitorKit Kit, AdminMonitorAuthority Authority, MonitorAuthorityLease Lease, AdminSearchApi Api) Create()
    {
        var kit = AdminMonitorKit.Create();
        var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        return (kit, authority, authority.Capture(), new AdminSearchApi(kit.Client));
    }

    private static HttpResponseMessage Problem(HttpStatusCode status, string type) =>
        new(status) { Content = new StringContent($$"""{"type":"https://siloserver.org/docs/api/v2/problems/{{type}}","title":"x","status":{{(int)status}},"detail":"Fixture detail with secret-token-1."}""", Encoding.UTF8, "application/problem+json") };

    [Fact]
    public async Task StatusReadsTheOfficialPathAndKeepsEveryDisplayedField()
    {
        var (kit, authority, lease, api) = Create();
        using (kit) using (authority) using (lease)
        {
            kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(StatusJson(rebuild: true)));
            var status = await api.GetStatusAsync(lease);
            Assert.Equal("GET " + StatusPath, Assert.Single(kit.Http.Requests));
            Assert.Equal(("meilisearch", "meilisearch", false), (status.ConfiguredProvider, status.ActiveProvider, status.Degraded));
            Assert.True(status.Meilisearch.Healthy && status.Meilisearch.Configured && status.Meilisearch.SemanticEnabled);
            Assert.Equal("closed", status.Meilisearch.CircuitState);
            Assert.Equal(["movie", "show"], status.Meilisearch.IndexTypes);
            Assert.Equal("timeout", status.Meilisearch.LastFallback);
            Assert.Equal(0.5, status.Meilisearch.SemanticRatio);
            Assert.True(status.Index.RebuildRequired);
            Assert.Equal(("silo_media_items_v3", 3, 3), (status.Index.ActiveIndexUid, status.Index.SchemaVersion, status.Index.ExpectedSchemaVersion));
            Assert.Equal((1234L, 1000L, 7L, 2L), (status.Index.DocumentCount, status.Index.VectorDocumentCount, status.Index.PendingEvents, status.Index.DeadLetteredEvents));
            Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, 678, TimeSpan.Zero), status.Index.LastSyncAt);
            var semantic = Assert.IsType<AdminSearchSemanticStatus>(status.Semantic);
            Assert.True(semantic.Ready);
            Assert.Equal(0.81, semantic.CoverageRatio);
            var coverage = Assert.Single(semantic.PerType);
            Assert.Equal(("movie", 10L, 8L, true), (coverage.Type, coverage.Eligible, coverage.Vectorized, coverage.Ready));
            Assert.True(semantic.Capability.Ok);
            Assert.Equal("default", semantic.Capability.Embedder);
        }
    }

    [Fact]
    public async Task DegradedStatusUsesASafeReasonAndAMinimalServerFixtureParses()
    {
        var (kit, authority, lease, api) = Create();
        using (kit) using (authority) using (lease)
        {
            kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(StatusJson(degraded: true)));
            var degraded = await api.GetStatusAsync(lease);
            Assert.True(degraded.Degraded);
            Assert.Equal("Additional provider diagnostics are withheld.", degraded.DegradedReason);
            // The published empty fixture: every collection omitted or empty.
            kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""
                {"configured_provider":"","active_provider":"","degraded":false,
                 "meilisearch":{"configured":false,"healthy":false,"circuit_state":"","timeout_ms":0,"matching_strategy":"","semantic_enabled":false,"binary_quantized":false,"semantic_ratio":0,"embedder":""},
                 "index":{"active_index_uid":"","schema_version":0,"expected_schema_version":0,"rebuild_required":false,"document_count":0,"vector_document_count":0,"pending_events":0,"dead_lettered_events":0,"last_processed_event_id":"9007199254740993"},
                 "tasks":[],"semantic":{"ready":false,"vector_coverage_ratio":0,"capability":{"ok":false}}}
                """));
            var empty = await api.GetStatusAsync(lease);
            Assert.Empty(empty.Meilisearch.IndexTypes);
            Assert.Null(empty.Index.LastSyncAt);
            Assert.Empty(Assert.IsType<AdminSearchSemanticStatus>(empty.Semantic).PerType);
        }
    }

    [Theory]
    [InlineData("""{"configured_provider":"x","active_provider":"y","degraded":false}""")]
    [InlineData("""{"active_provider":"y","meilisearch":{},"index":{}}""")]
    public async Task AnIncompleteStatusIsRejectedRatherThanShownAsZeros(string body)
    {
        var (kit, authority, lease, api) = Create();
        using (kit) using (authority) using (lease)
        {
            kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(body));
            await Assert.ThrowsAsync<InvalidDataException>(() => api.GetStatusAsync(lease));
        }
    }

    [Theory]
    [InlineData("https://fixture-user:fixture-password@search.invalid/?api_key=fixture-key")]
    [InlineData("Authorization: Bearer fixture-token")]
    [InlineData("proxy rejected unlabelled fixture-secret")]
    public async Task StatusDiagnosticsNeverExposeProviderResponseText(string diagnostic)
    {
        var (kit, authority, lease, api) = Create();
        using (kit) using (authority) using (lease)
        {
            var body = System.Text.Json.Nodes.JsonNode.Parse(StatusJson(degraded: true))!;
            body["degraded_reason"] = diagnostic;
            body["meilisearch"]!["last_fallback"] = diagnostic;
            body["semantic"]!["disabled_reason"] = diagnostic;
            body["semantic"]!["capability"]!["reason"] = diagnostic;
            kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(body.ToJsonString()));
            var status = await api.GetStatusAsync(lease);
            var semantic = Assert.IsType<AdminSearchSemanticStatus>(status.Semantic);
            foreach (var reason in new[] { status.DegradedReason, status.Meilisearch.LastFallback, semantic.DisabledReason, semantic.Capability.Reason })
            {
                Assert.Equal("Additional provider diagnostics are withheld.", reason);
                Assert.DoesNotContain("fixture-", reason!);
            }
            Assert.True(status.Degraded);
            Assert.Equal(1234, status.Index.DocumentCount);
        }
    }

    [Theory]
    [InlineData("Meilisearch index has not been built; using Postgres search")]
    [InlineData("search index rebuild required")]
    [InlineData("semantic search disabled")]
    public async Task KnownStaticStatusReasonsRemainUseful(string diagnostic)
    {
        var (kit, authority, lease, api) = Create();
        using (kit) using (authority) using (lease)
        {
            var body = System.Text.Json.Nodes.JsonNode.Parse(StatusJson(degraded: true))!;
            body["degraded_reason"] = diagnostic;
            kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(body.ToJsonString()));
            Assert.Equal(diagnostic, (await api.GetStatusAsync(lease)).DegradedReason);
        }
    }

    [Fact]
    public async Task TooManyCoverageRowsIsAnInvalidResponseNotASilentTrim()
    {
        var (kit, authority, lease, api) = Create();
        using (kit) using (authority) using (lease)
        {
            var rows = string.Join(",", Enumerable.Range(0, AdminSearchApi.MaxListed + 1).Select(i => $$"""{"type":"t{{i}}","eligible":1,"vectorized":1,"vector_coverage_ratio":1,"ready":true}"""));
            kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(StatusJson().Replace("\"per_type\":[", "\"per_type\":[" + rows + ",")));
            var error = await Assert.ThrowsAsync<InvalidDataException>(() => api.GetStatusAsync(lease));
            Assert.Equal(AdminFailureKind.InvalidResponse, AdminFailures.Classify(error));
        }
    }

    [Fact]
    public async Task StatusFailuresCarryNoServerDetail()
    {
        var (kit, authority, lease, api) = Create();
        using (kit) using (authority) using (lease)
        {
            kit.Http.Respond = (_, _) => Task.FromResult(Problem(HttpStatusCode.Forbidden, "permission_denied"));
            var error = await Assert.ThrowsAsync<ApiException>(() => api.GetStatusAsync(lease));
            Assert.DoesNotContain("secret-token-1", error.Message);
            Assert.Equal(AdminFailureKind.Forbidden, AdminFailures.Classify(error));
        }
    }

    [Fact]
    public async Task ConnectionCheckPostsTheValuesAndDirtyKeysAndRedactsAnyEchoedCredential()
    {
        var (kit, authority, lease, api) = Create();
        using (kit) using (authority) using (lease)
        {
            kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"success":false,"message":"Rejected key typed-key-9 for http://user:pass9@search.example.test:7700"}"""));
            var values = AdminSearchSettingsRules.MeilisearchKeys.ToDictionary(key => key, _ => "", StringComparer.Ordinal);
            values[AdminSearchSettingsRules.MeilisearchUrl] = "http://user:pass9@search.example.test:7700";
            values[AdminSearchSettingsRules.MeilisearchApiKey] = "typed-key-9";
            var result = await api.CheckMeilisearchAsync(lease, values, [AdminSearchSettingsRules.MeilisearchUrl, AdminSearchSettingsRules.MeilisearchApiKey]);
            Assert.Equal("POST " + CheckPath, Assert.Single(kit.Http.Requests));
            Assert.False(result.Success);
            Assert.DoesNotContain("typed-key-9", result.Message);
            Assert.DoesNotContain("pass9", result.Message);
            using var body = System.Text.Json.JsonDocument.Parse(kit.Http.Bodies.Single());
            Assert.Equal(AdminSearchSettingsRules.MeilisearchKeys.Count, body.RootElement.GetProperty("values").EnumerateObject().Count());
            Assert.Equal([AdminSearchSettingsRules.MeilisearchUrl, AdminSearchSettingsRules.MeilisearchApiKey],
                body.RootElement.GetProperty("dirty_keys").EnumerateArray().Select(value => value.GetString()));
        }
    }

    [Theory]
    [InlineData(false, "Rejected credential saved-key-9", "")]
    [InlineData(true, "Proxy accepted saved-key-9", "")]
    [InlineData(false, "Rejected typed%2Fkey%2B9", "typed/key+9")]
    [InlineData(true, "Accepted dHlwZWQva2V5Kzk=", "typed/key+9")]
    public async Task ConnectionCheckWithholdsUnknownSavedAndEncodedTypedCredentialEchoes(bool success, string diagnostic, string typedKey)
    {
        var (kit, authority, lease, api) = Create();
        using (kit) using (authority) using (lease)
        {
            kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(System.Text.Json.JsonSerializer.Serialize(new { success, message = diagnostic })));
            var values = AdminSearchSettingsRules.MeilisearchKeys.ToDictionary(key => key, _ => "", StringComparer.Ordinal);
            values[AdminSearchSettingsRules.MeilisearchApiKey] = typedKey;
            string[] dirty = typedKey.Length == 0 ? [] : [AdminSearchSettingsRules.MeilisearchApiKey];
            var result = await api.CheckMeilisearchAsync(lease, values, dirty);
            Assert.Equal(success, result.Success);
            Assert.Equal(success ? "Meilisearch is reachable." : "Connection check failed.", result.Message);
            Assert.DoesNotContain(diagnostic, result.Message);
            using var body = System.Text.Json.JsonDocument.Parse(kit.Http.Bodies.Single());
            Assert.Equal(typedKey, body.RootElement.GetProperty("values").GetProperty(AdminSearchSettingsRules.MeilisearchApiKey).GetString());
            Assert.Equal(dirty.Length, body.RootElement.GetProperty("dirty_keys").GetArrayLength());
        }
    }

    [Fact]
    public async Task ConnectionCheckRejectsForeignKeysBeforeAnyRequest()
    {
        var (kit, authority, lease, api) = Create();
        using (kit) using (authority) using (lease)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => api.CheckMeilisearchAsync(lease, new Dictionary<string, string> { ["branding.server_name"] = "x" }, []));
            await Assert.ThrowsAsync<ArgumentException>(() => api.CheckMeilisearchAsync(lease, new Dictionary<string, string>(), [AdminSearchSettingsRules.MeilisearchUrl]));
            Assert.Empty(kit.Http.Requests);
        }
    }

    [Fact]
    public void SearchMaintenanceNamesTaskDestinationsWithoutExposingTaskCommands()
    {
        Assert.Equal("/admin/tasks/rebuild_catalog_search_index", AdminSearchSettingsRules.RebuildTaskHref);
        Assert.Equal("/admin/tasks/sync_catalog_search_index", AdminSearchSettingsRules.SyncTaskHref);
        Assert.Equal([nameof(AdminSearchApi.CheckMeilisearchAsync), nameof(AdminSearchApi.GetStatusAsync)], typeof(AdminSearchApi)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
            .Select(method => method.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ARetiredLeaseSendsNothing()
    {
        var (kit, authority, lease, api) = Create();
        using (kit) using (authority)
        {
            lease.Dispose();
            await Assert.ThrowsAnyAsync<Exception>(() => api.GetStatusAsync(lease));
            Assert.Empty(kit.Http.Requests);
        }
    }
}
