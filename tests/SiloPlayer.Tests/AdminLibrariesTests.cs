using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using P = SiloPlayer.Core.Services.Admin.AdminLibraryPresentation;

namespace SiloPlayer.Tests;

/// <summary>
/// Manage › Libraries › Libraries wire contracts (official OpenAPI and WebUI hooks at
/// abd85d0c): request paths, bodies and query strings, response validation, whole-order
/// preconditions, no-replay writes, and the ported presentation rules. Synthetic server only.
/// </summary>
public sealed class AdminLibrariesTests
{
    internal static string Library(string id, string name = "Movies", string type = "movies", string? warning = null, bool trickplaySupported = true) =>
        $$"""{"id":"{{id}}","name":"{{name}}","type":"{{type}}","paths":["/media/{{id}}"],"enabled":true,"metadata_language":"en","auto_translate_metadata":false,"chapter_thumbnails_enabled":false,"chapter_thumbnails_supported":true,"intro_detection_enabled":true,"trickplay_enabled":false,"trickplay_supported":{{(trickplaySupported ? "true" : "false")}},"box_sets_enabled":false,"box_set_min_movies":2,"box_sets_in_search":true,"trailer_kinds":["trailer"],"realtime_monitoring":true,"sort_order":{{id}}{{(warning == null ? "" : $",\"scan_warning_code\":\"{warning}\"")}}}""";

    internal static string Libraries(params string[] rows) => "{\"items\":[" + string.Join(',', rows) + "],\"page\":{\"has_more\":false}}";

    private static (AdminMonitorKit Kit, AdminMonitorAuthority Authority, AdminLibrariesApi Api) Setup(Func<HttpRequestMessage, HttpResponseMessage?> respond)
    {
        var kit = AdminMonitorKit.Create();
        var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (request, _) => Task.FromResult(respond(request) ?? AdminMonitorKit.Json("{}", HttpStatusCode.NotFound));
        return (kit, authority, new AdminLibrariesApi(kit.Client));
    }

    private static HttpResponseMessage NoContent() => new(HttpStatusCode.NoContent);

    // ── List ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListReadsEveryLibraryAndOnlyACompleteReadAuthorizesReorder()
    {
        var (kit, authority, api) = Setup(_ => AdminMonitorKit.Json(Libraries(Library("1"), Library("2", "Shows", "series"))));
        using (kit) using (authority)
        {
            using var lease = authority.Capture();
            var read = await api.ListAsync(lease);
            Assert.Equal("GET /api/v2/libraries", kit.Http.Requests.Single());
            Assert.True(read.IsComplete);
            Assert.NotNull(read.Snapshot);
            Assert.Equal(["1", "2"], read.Items.Select(library => library.Id));
            Assert.Equal("series", read.Items[1].Type);
            Assert.True(read.Snapshot!.IsCurrentFor(lease, AdminLibrariesApi.LibraryScope));
        }
    }

    [Fact]
    public async Task ListThatSaysMoreFollowsIsTruncatedAndCarriesNoSnapshot()
    {
        var (kit, authority, api) = Setup(_ => AdminMonitorKit.Json("{\"items\":[" + Library("1") + "],\"page\":{\"has_more\":true,\"next_cursor\":\"c2\"}}"));
        using (kit) using (authority)
        {
            using var lease = authority.Capture();
            var read = await api.ListAsync(lease);
            Assert.False(read.IsComplete);
            Assert.Null(read.Snapshot);
            Assert.Single(kit.Http.Requests);
        }
    }

    [Theory]
    [InlineData("\"01\"")]
    [InlineData("\"\"")]
    [InlineData("\"x\"")]
    public async Task ListRejectsInvalidLibraryIdentities(string id)
    {
        var (kit, authority, api) = Setup(_ => AdminMonitorKit.Json(Libraries(Library("1").Replace("\"id\":\"1\"", "\"id\":" + id))));
        using (kit) using (authority)
        {
            using var lease = authority.Capture();
            await Assert.ThrowsAsync<AdminListInconsistentException>(() => api.ListAsync(lease));
        }
    }

    [Fact]
    public async Task ListRejectsDuplicateLibraries()
    {
        var (kit, authority, api) = Setup(_ => AdminMonitorKit.Json(Libraries(Library("1"), Library("1"))));
        using (kit) using (authority)
        {
            using var lease = authority.Capture();
            var error = await Assert.ThrowsAsync<AdminListInconsistentException>(() => api.ListAsync(lease));
            Assert.Equal(AdminListProblem.DuplicateIdentity, error.Problem);
        }
    }

    // ── Create / update ──────────────────────────────────────────────────

    [Fact]
    public async Task CreateSendsOnlyTheMembersLibraryCreateDeclares()
    {
        string? body = null;
        var (kit, authority, api) = Setup(request => { body = request.Content!.ReadAsStringAsync().Result; return AdminMonitorKit.Json(Library("7", "New"), HttpStatusCode.Created); });
        using (kit) using (authority)
        {
            using var lease = authority.Capture();
            var created = await api.CreateAsync(lease, new AdminLibraryWrite
            {
                Name = "New", Paths = ["/media/new"], Type = "movies", Enabled = false, MetadataLanguage = "en", AutoTranslateMetadata = true,
                TrailerKinds = ["trailer"], RealtimeMonitoring = true,
            });
            Assert.Equal("7", created.Id);
            Assert.Equal("POST /api/v2/libraries", kit.Http.Requests.Single());
            using var json = JsonDocument.Parse(body!);
            var names = json.RootElement.EnumerateObject().Select(property => property.Name).ToHashSet();
            Assert.DoesNotContain("enabled", names);
            Assert.DoesNotContain("auto_translate_metadata", names);
            Assert.DoesNotContain("trickplay_enabled", names);
            Assert.DoesNotContain("box_sets_enabled", names);
            Assert.Contains("realtime_monitoring", names);
            Assert.Equal("/media/new", json.RootElement.GetProperty("paths")[0].GetString());
            Assert.DoesNotContain(json.RootElement.EnumerateObject(), property => property.Value.ValueKind == JsonValueKind.Null);
        }
    }

    [Fact]
    public async Task UpdateSendsEnabledAndAutoTranslateAndChangeOnlyMembersWhenSet()
    {
        string? body = null;
        var (kit, authority, api) = Setup(request => { body = request.Content!.ReadAsStringAsync().Result; return AdminMonitorKit.Json(Library("3")); });
        using (kit) using (authority)
        {
            using var lease = authority.Capture();
            await api.UpdateAsync(lease, "3", new AdminLibraryWrite { Name = "Movies", Paths = ["/m"], Type = "movies", Enabled = false, TrickplayEnabled = true, BoxSetMinMovies = 4 });
            Assert.Equal("PATCH /api/v2/libraries/3", kit.Http.Requests.Single());
            using var json = JsonDocument.Parse(body!);
            Assert.False(json.RootElement.GetProperty("enabled").GetBoolean());
            Assert.False(json.RootElement.GetProperty("auto_translate_metadata").GetBoolean());
            Assert.True(json.RootElement.GetProperty("trickplay_enabled").GetBoolean());
            Assert.Equal(4, json.RootElement.GetProperty("box_set_min_movies").GetInt32());
            Assert.False(json.RootElement.TryGetProperty("box_sets_in_search", out _));
        }
    }

    [Fact]
    public async Task UpdateAnsweringAnotherLibraryIsAnIdentityChange()
    {
        var (kit, authority, api) = Setup(_ => AdminMonitorKit.Json(Library("4")));
        using (kit) using (authority)
        {
            using var lease = authority.Capture();
            await Assert.ThrowsAsync<AdminLibraryIdentityException>(() => api.UpdateAsync(lease, "3", new AdminLibraryWrite { Name = "x", Paths = ["/x"], Type = "movies" }));
        }
    }

    [Theory]
    [InlineData("", "/m", "movies", null, null)]
    [InlineData("A", "", "movies", null, null)]
    [InlineData("A", "/m", "", null, null)]
    [InlineData("A", "/m", "movies", "pt-BR", null)]
    [InlineData("A", "/m", "movies", "en", 11)]
    public async Task InvalidBodiesAreRefusedBeforeSending(string name, string path, string type, string? language, int? boxSet)
    {
        var (kit, authority, api) = Setup(_ => AdminMonitorKit.Json(Library("1")));
        using (kit) using (authority)
        {
            using var lease = authority.Capture();
            var body = new AdminLibraryWrite { Name = name, Paths = path.Length == 0 ? [] : [path], Type = type, MetadataLanguage = language, BoxSetMinMovies = boxSet };
            await Assert.ThrowsAsync<ArgumentException>(() => api.CreateAsync(lease, body));
            Assert.Empty(kit.Http.Requests);
        }
    }

    [Fact]
    public async Task WritesAreSentOnceAndNeverReplayedAfterUnauthorized()
    {
        var (kit, authority, api) = Setup(_ => AdminMonitorKit.Json("{\"type\":\"https://siloserver.org/docs/api/v2/problems/authentication_required\",\"title\":\"x\",\"status\":401}", HttpStatusCode.Unauthorized));
        using (kit) using (authority)
        {
            var refreshes = 0;
            kit.Client.SetTokenRefresher(_ => { refreshes++; return Task.FromResult(true); });
            using var lease = authority.Capture();
            await Assert.ThrowsAnyAsync<Exception>(() => api.StartScanAsync(lease, "1"));
            Assert.Single(kit.Http.Requests);
            Assert.Equal(0, refreshes);
        }
    }

    // ── Reorder ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ReorderSendsTheWholeOrderWithStringIdsAndPositions()
    {
        string? body = null;
        var (kit, authority, api) = Setup(request =>
            request.Method == HttpMethod.Get ? AdminMonitorKit.Json(Libraries(Library("1"), Library("2"), Library("3")))
                : (body = request.Content!.ReadAsStringAsync().Result) == null ? null : NoContent());
        using (kit) using (authority)
        {
            using var lease = authority.Capture();
            var read = await api.ListAsync(lease);
            await api.ReorderAsync(lease, read.Snapshot!, ["3", "1", "2"]);
            Assert.Equal("POST /api/v2/libraries/reorder", kit.Http.Requests.Last());
            Assert.Equal("{\"entries\":[{\"id\":\"3\",\"position\":0},{\"id\":\"1\",\"position\":1},{\"id\":\"2\",\"position\":2}]}", body);
        }
    }

    [Theory]
    [InlineData("1,2")]
    [InlineData("1,1,2")]
    [InlineData("1,2,9")]
    [InlineData("1,2,3,4")]
    public async Task ReorderRefusesAnythingButAnExactPermutation(string order)
    {
        var (kit, authority, api) = Setup(_ => AdminMonitorKit.Json(Libraries(Library("1"), Library("2"), Library("3"))));
        using (kit) using (authority)
        {
            using var lease = authority.Capture();
            var read = await api.ListAsync(lease);
            await Assert.ThrowsAsync<ArgumentException>(() => api.ReorderAsync(lease, read.Snapshot!, order.Split(',')));
            Assert.Single(kit.Http.Requests);
        }
    }

    [Fact]
    public async Task ReorderRefusesASnapshotFromAnEarlierAuthority()
    {
        var (kit, authority, api) = Setup(_ => AdminMonitorKit.Json(Libraries(Library("1"), Library("2"))));
        using (kit) using (authority)
        {
            AdminCompleteSnapshot<AdminLibrary> snapshot;
            using (var first = authority.Capture()) snapshot = (await api.ListAsync(first)).Snapshot!;
            kit.Auth.SelectProfile("p2", null, new SiloPlayer.Core.Models.Auth.Profile { Id = "p2", Name = "Other", IsPrimary = true });
            using var second = authority.Capture();
            await Assert.ThrowsAsync<MonitorAuthorityRetiredException>(() => api.ReorderAsync(second, snapshot, ["2", "1"]));
            Assert.Single(kit.Http.Requests);
        }
    }

    // ── Commands ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RowCommandsUseTheirExactOperations()
    {
        var bodies = new List<string>();
        var (kit, authority, api) = Setup(request =>
        {
            bodies.Add(request.Content?.ReadAsStringAsync().Result ?? "");
            var path = request.RequestUri!.AbsolutePath;
            return path switch
            {
                "/api/v2/scan" => AdminMonitorKit.Json("{\"library_id\":\"5\",\"mode\":\"library\",\"status\":\"accepted\"}", HttpStatusCode.Accepted),
                "/api/v2/scan/cancel" => AdminMonitorKit.Json("{\"library_id\":\"5\",\"cancelled\":2}"),
                "/api/v2/admin/tasks/scan_libraries/run" => AdminMonitorKit.Json("{\"key\":\"scan_libraries\",\"name\":\"Scan\",\"state\":\"running\"}"),
                "/api/v2/libraries/5/refresh-metadata" => AdminMonitorKit.Json("{\"id\":\"j1\",\"kind\":\"library_refresh\",\"state\":\"queued\",\"terminal\":false,\"cancelable\":true,\"created_at\":\"2026-11-15T10:00:00Z\"}", HttpStatusCode.Accepted),
                "/api/v2/library-jobs/j%2F1/cancel" => AdminMonitorKit.Json("{\"id\":\"j/1\",\"kind\":\"library_refresh\",\"state\":\"canceling\",\"terminal\":false,\"cancelable\":false,\"created_at\":\"2026-11-15T10:00:00Z\"}", HttpStatusCode.Accepted),
                "/api/v2/libraries/5" => AdminMonitorKit.Json("{\"id\":\"d1\",\"kind\":\"delete_library\",\"state\":\"queued\",\"terminal\":false,\"cancelable\":false,\"created_at\":\"2026-11-15T10:00:00Z\"}", HttpStatusCode.Accepted),
                "/api/v2/libraries/5/check-mount" => AdminMonitorKit.Json("{\"library_id\":\"5\",\"library_name\":\"M\",\"healthy\":false,\"status\":\"unhealthy\",\"summary\":\"1 of 1 roots unreachable\",\"checked_at\":\"2026-11-15T10:00:00Z\",\"roots\":[{\"path\":\"/m\",\"reachable\":false,\"suspect_empty\":false,\"error_code\":\"not_found\",\"error_message\":\"missing\"}]}"),
                "/api/v2/libraries/5/confirm-empty-root-cleanup" => AdminMonitorKit.Json("{\"status\":\"queued\",\"message\":\"ok\"}"),
                _ => null,
            };
        });
        using (kit) using (authority)
        {
            using var lease = authority.Capture();
            Assert.Equal("accepted", (await api.StartScanAsync(lease, "5")).Status);
            Assert.Equal("{\"library_id\":\"5\"}", bodies[^1]);
            Assert.Equal(2, (await api.CancelScansAsync(lease, "5")).Cancelled);
            await api.ScanAllAsync(lease);
            var refresh = await api.RefreshMetadataAsync(lease, "5", "full");
            Assert.Equal("{\"mode\":\"full\"}", bodies[^1]);
            Assert.Equal("queued", refresh.State);
            Assert.False(refresh.Terminal);
            var cancel = await api.CancelJobAsync(lease, "j/1");
            Assert.Equal("running", P.JobStatus(cancel));
            Assert.Equal("Cancellation requested", P.JobMessage(cancel));
            var deletion = await api.DeleteAsync(lease, "5");
            Assert.Equal("delete_library", deletion.Kind);
            var mount = await api.CheckMountAsync(lease, "5");
            Assert.Equal("1 of 1 roots unreachable: /m (missing)", P.MountCheckMessage(mount));
            await api.ConfirmEmptyRootCleanupAsync(lease, "5");
            Assert.Equal(
            [
                "POST /api/v2/scan", "POST /api/v2/scan/cancel", "POST /api/v2/admin/tasks/scan_libraries/run", "POST /api/v2/libraries/5/refresh-metadata",
                "POST /api/v2/library-jobs/j%2F1/cancel", "DELETE /api/v2/libraries/5", "POST /api/v2/libraries/5/check-mount", "POST /api/v2/libraries/5/confirm-empty-root-cleanup",
            ], kit.Http.Requests);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => api.RefreshMetadataAsync(lease, "5", "reset"));
            await Assert.ThrowsAsync<ArgumentException>(() => api.StartScanAsync(lease, "05"));
        }
    }

    [Fact]
    public async Task ScanThatIsNotAcceptedIsNotReportedAsStarted()
    {
        var (kit, authority, api) = Setup(_ => AdminMonitorKit.Json("{\"library_id\":\"5\",\"mode\":\"library\",\"status\":\"rejected\"}", HttpStatusCode.Accepted));
        using (kit) using (authority)
        {
            using var lease = authority.Capture();
            await Assert.ThrowsAsync<InvalidDataException>(() => api.StartScanAsync(lease, "5"));
        }
    }

    [Fact]
    public async Task RefreshJobsAreReadAsOneValidatedPageOfLibraryRefreshJobs()
    {
        var (kit, authority, api) = Setup(_ => AdminMonitorKit.Json("{\"items\":[{\"id\":\"j1\",\"kind\":\"library_refresh\",\"state\":\"running\",\"terminal\":false,\"cancelable\":true,\"created_at\":\"2026-11-15T10:00:00Z\",\"library_id\":\"5\",\"library_ids\":[],\"artifact_size_bytes\":0,\"public_link_supported\":false,\"progress\":{\"current\":3,\"total\":10,\"unit\":\"items\"}}],\"page\":{\"has_more\":true,\"next_cursor\":\"n2\"}}"));
        using (kit) using (authority)
        {
            using var lease = authority.Capture();
            var page = await api.ListRefreshJobsAsync(lease, null);
            Assert.Equal("GET /api/v2/admin/jobs?kind=library_refresh&limit=50", kit.Http.Requests.Single());
            Assert.Equal("n2", page.NextCursor);
            Assert.Equal("5", P.JobLibraryId(page.Items[0]));
            Assert.Equal("3 / 10", P.JobProgress(page.Items[0]));
            await Assert.ThrowsAsync<AdminListInconsistentException>(() => api.ListRefreshJobsAsync(lease, "n2"));
        }
    }

    // ── Providers and poster ─────────────────────────────────────────────

    [Fact]
    public async Task ProviderChainsAreReadAndWrittenInListOrder()
    {
        string? body = null;
        var (kit, authority, api) = Setup(request =>
        {
            if (request.Method == HttpMethod.Put) { body = request.Content!.ReadAsStringAsync().Result; return NoContent(); }
            return AdminMonitorKit.Json("{\"levels\":[{\"content_level\":\"movie\",\"entries\":[{\"plugin_installation_id\":\"9\",\"capability_id\":\"meta\",\"provider_slug\":\"tmdb\",\"priority\":1,\"enabled\":true,\"local\":false},{\"plugin_installation_id\":\"0\",\"capability_id\":\"local\",\"provider_slug\":\"nfo\",\"priority\":5,\"enabled\":true,\"local\":true}]}]}");
        });
        using (kit) using (authority)
        {
            using var lease = authority.Capture();
            var chains = P.LevelChains(await api.GetProvidersAsync(lease, "5"));
            Assert.Equal(["nfo", "tmdb"], chains["movie"].Select(item => item.ProviderSlug));
            await api.GetProviderDefaultsAsync(lease, "mixed");
            await api.SetProvidersAsync(lease, "5", chains);
            Assert.Equal(["GET /api/v2/libraries/5/providers", "GET /api/v2/libraries/provider-defaults?library_type=mixed", "PUT /api/v2/libraries/5/providers"], kit.Http.Requests);
            Assert.Equal("{\"levels\":[{\"content_level\":\"movie\",\"entries\":[{\"plugin_installation_id\":\"0\",\"capability_id\":\"local\",\"priority\":0,\"enabled\":true},{\"plugin_installation_id\":\"9\",\"capability_id\":\"meta\",\"priority\":1,\"enabled\":true}]}]}", body);
        }
    }

    [Fact]
    public async Task PosterUploadIsOneBoundedMultipartPartAndRemovalExpectsNoContent()
    {
        string? contentType = null;
        string? disposition = null;
        var (kit, authority, api) = Setup(request =>
        {
            if (request.Method == HttpMethod.Delete) return NoContent();
            contentType = request.Content!.Headers.ContentType!.MediaType;
            disposition = request.Content.ReadAsStringAsync().Result;
            return AdminMonitorKit.Json(Library("5").Replace("\"sort_order\"", "\"poster_url\":\"/img/p.png\",\"sort_order\""));
        });
        using (kit) using (authority)
        {
            using var lease = authority.Capture();
            var updated = await api.UploadPosterAsync(lease, "5", [1, 2, 3], "image/png");
            Assert.Equal("/img/p.png", updated.PosterUrl);
            Assert.Equal("multipart/form-data", contentType);
            Assert.Contains("name=poster", disposition);
            await api.DeletePosterAsync(lease, "5");
            Assert.Equal(["PUT /api/v2/libraries/5/poster", "DELETE /api/v2/libraries/5/poster"], kit.Http.Requests);
            await Assert.ThrowsAsync<AdminLibraryPosterException>(() => api.UploadPosterAsync(lease, "5", new byte[AdminLibrariesApi.MaxPosterBytes + 1], "image/png"));
            await Assert.ThrowsAsync<AdminLibraryPosterException>(() => api.UploadPosterAsync(lease, "5", [], "image/png"));
            await Assert.ThrowsAsync<ArgumentException>(() => api.UploadPosterAsync(lease, "5", [1], "image/gif"));
            Assert.Equal(2, kit.Http.Requests.Count);
        }
    }

    [Fact]
    public async Task NoContentWritesRefuseAnUnexpectedSuccessBody()
    {
        var (kit, authority, api) = Setup(_ => AdminMonitorKit.Json("{}"));
        using (kit) using (authority)
        {
            using var lease = authority.Capture();
            var error = await Assert.ThrowsAsync<InvalidDataException>(() => api.DeletePosterAsync(lease, "5"));
            Assert.True(P.IsUnknownOutcome(error));
        }
    }

    // ── Diagnostics ──────────────────────────────────────────────────────

    [Fact]
    public async Task UnmatchedListSendsTheWebUiQueryAndValidatesContinuation()
    {
        var (kit, authority, api) = Setup(request => request.RequestUri!.Query.Contains("cursor=same")
            ? AdminMonitorKit.Json("{\"items\":[{\"content_id\":\"c1\",\"content_type\":\"movie\",\"library_id\":\"1\",\"library_name\":\"M\",\"status\":\"unmatched\",\"title\":\"T\",\"year\":0}],\"page\":{\"has_more\":true,\"next_cursor\":\"same\"},\"total\":20}")
            : AdminMonitorKit.Json("{\"items\":[{\"content_id\":\"c1\",\"content_type\":\"movie\",\"library_id\":\"1\",\"library_name\":\"M\",\"status\":\"unmatched\",\"title\":\"T\",\"year\":2001}],\"page\":{\"has_more\":true,\"next_cursor\":\"n2\"},\"total\":20}"));
        using (kit) using (authority)
        {
            using var lease = authority.Capture();
            var page = await api.ListUnmatchedAsync(lease, "  heat ", ignored: true, cursor: "a/b");
            Assert.Equal("GET /api/v2/libraries/unmatched-items?limit=10&q=heat&ignored=true&cursor=a%2Fb", kit.Http.Requests.Single());
            Assert.Equal(20, page.Total);
            Assert.Equal("n2", page.NextCursor);
            await api.ListUnmatchedAsync(lease, " ", ignored: false, cursor: null);
            Assert.Equal("GET /api/v2/libraries/unmatched-items?limit=10", kit.Http.Requests.Last());
            var error = await Assert.ThrowsAsync<AdminListInconsistentException>(() => api.ListUnmatchedAsync(lease, null, false, "same"));
            Assert.Equal(AdminListProblem.RepeatedContinuation, error.Problem);
        }
    }

    [Fact]
    public async Task IgnoreAndRestoreAreOneNoContentWriteEach()
    {
        var (kit, authority, api) = Setup(_ => NoContent());
        using (kit) using (authority)
        {
            using var lease = authority.Capture();
            await api.SetUnmatchedIgnoredAsync(lease, "local-a b", true);
            await api.SetUnmatchedIgnoredAsync(lease, "local-a b", false);
            Assert.Equal(["PUT /api/v2/libraries/unmatched-items/local-a%20b/ignore", "DELETE /api/v2/libraries/unmatched-items/local-a%20b/ignore"], kit.Http.Requests);
        }
    }

    [Fact]
    public async Task RootsListIsScopedToItsLibraryAndOverrideBodiesCarryOnlySetValues()
    {
        string? body = null;
        var (kit, authority, api) = Setup(request =>
        {
            if (request.Method == HttpMethod.Put) { body = request.Content!.ReadAsStringAsync().Result; return NoContent(); }
            if (request.Method == HttpMethod.Delete) return NoContent();
            var other = request.RequestUri!.Query.Contains("q=other");
            return AdminMonitorKit.Json("{\"items\":[{\"library_id\":\"" + (other ? "9" : "2") + "\",\"library_name\":\"S\",\"root_path\":\"/s/Heat (1995)\",\"state\":\"ambiguous\",\"inferred_type\":\"movie\",\"type_confidence\":\"low\",\"title\":\"Heat\",\"year\":1995,\"observed_file_count\":3,\"first_seen_at\":\"2026-11-15T10:00:00Z\",\"last_seen_at\":\"2026-11-15T10:00:00Z\",\"evidence_json\":{\"has_folder_ids\":false,\"movie_evidence_files\":2}}],\"page\":{\"has_more\":false},\"total\":1}");
        });
        using (kit) using (authority)
        {
            using var lease = authority.Capture();
            var page = await api.ListRootsAsync(lease, "2", "ambiguous", "heat", null);
            Assert.Equal("GET /api/v2/libraries/roots?library_id=2&limit=50&state=ambiguous&q=heat", kit.Http.Requests.Single());
            Assert.Equal("no folder IDs · 2 movie-shaped files", P.RootEvidence(page.Items[0]));
            await Assert.ThrowsAsync<AdminLibraryIdentityException>(() => api.ListRootsAsync(lease, "2", "ambiguous", "other", null));
            await api.SetRootOverrideAsync(lease, new AdminRootOverrideWrite("2", "/s/Heat (1995)") { ForcedType = "movie", ForcedYear = 1995, ForcedTmdbId = "949" });
            Assert.Equal("{\"library_id\":\"2\",\"root_path\":\"/s/Heat (1995)\",\"forced_type\":\"movie\",\"forced_year\":1995,\"forced_tmdb_id\":\"949\"}", body);
            await api.DeleteRootOverrideAsync(lease, "2", "/s/Heat (1995)");
            Assert.Equal("DELETE /api/v2/libraries/roots/override?library_id=2&root_path=%2Fs%2FHeat%20%281995%29", kit.Http.Requests.Last());
            await Assert.ThrowsAsync<ArgumentException>(() => api.SetRootOverrideAsync(lease, new AdminRootOverrideWrite("2", "/x") { ForcedType = "episode" }));
        }
    }

    [Fact]
    public async Task SkippedAndStaleListsAreFiftyRowPages()
    {
        var (kit, authority, api) = Setup(request => AdminMonitorKit.Json(request.RequestUri!.AbsolutePath.EndsWith("skipped-roots", StringComparison.Ordinal)
            ? "{\"items\":[],\"total\":0}"
            : "{\"items\":[{\"content_id\":\"c\",\"content_type\":\"movie\",\"library_id\":\"1\",\"library_name\":\"M\",\"provider\":\"tmdb\",\"provider_id\":\"1\",\"title\":\"T\",\"year\":1,\"first_seen_at\":\"2026-11-15T10:00:00Z\",\"last_seen_at\":\"2026-11-15T10:00:00Z\"},{\"content_id\":\"c\",\"content_type\":\"movie\",\"library_id\":\"1\",\"library_name\":\"M\",\"provider\":\"imdb\",\"provider_id\":\"t1\",\"title\":\"T\",\"year\":1,\"first_seen_at\":\"2026-11-15T10:00:00Z\",\"last_seen_at\":\"2026-11-15T10:00:00Z\"}],\"page\":{\"has_more\":false},\"total\":2}"));
        using (kit) using (authority)
        {
            using var lease = authority.Capture();
            Assert.Empty((await api.ListSkippedRootsAsync(lease, "x", null)).Items);
            Assert.Equal(2, (await api.ListStaleIdsAsync(lease, null, null)).Items.Count);
            Assert.Equal(["GET /api/v2/libraries/skipped-roots?limit=50&q=x", "GET /api/v2/libraries/stale-ids?limit=50"], kit.Http.Requests);
        }
    }

    [Fact]
    public async Task MatcherDetailBoundsEachListAndRequiresAFreshCursor()
    {
        var oversized = string.Join(',', Enumerable.Range(1, 11).Select(i => "{\"library_id\":\"1\",\"media_file_id\":\"" + i + "\",\"created_at\":\"2026-11-15T10:00:00Z\",\"updated_at\":\"2026-11-15T10:00:00Z\",\"file_path\":\"/m/" + i + "\"}"));
        var (kit, authority, api) = Setup(request => request.RequestUri!.Query.Contains("cursor=big")
            ? AdminMonitorKit.Json("{\"library_id\":\"1\",\"movies\":[],\"series\":[],\"raw_files\":[" + oversized + "],\"page\":{\"has_more\":false},\"total_count\":11}")
            : request.RequestUri!.Query.Contains("cursor=empty")
                ? AdminMonitorKit.Json("{\"library_id\":\"1\",\"movies\":[],\"series\":[],\"raw_files\":[],\"page\":{\"has_more\":true,\"next_cursor\":\"x\"},\"total_count\":0}")
                : AdminMonitorKit.Json("{\"library_id\":\"1\",\"movies\":[{\"library_id\":\"1\",\"media_file_id\":\"5\",\"file_path\":\"/m/a.mkv\",\"state\":\"parked\",\"failure_kind\":\"no_match\",\"failure_detail\":{\"decision\":{\"outcome\":\"candidate_rejected\",\"rejection\":\"year_mismatch\",\"top_candidates\":[{\"title\":\"A\",\"year\":2001,\"score\":0.82,\"reasons\":[\"title\"],\"sources\":[\"tmdb\"]}]}}}],\"series\":[],\"raw_files\":[],\"page\":{\"has_more\":true,\"next_cursor\":\"p2\"},\"total_count\":3}"));
        using (kit) using (authority)
        {
            using var lease = authority.Capture();
            var page = await api.GetMatchQueueDetailAsync(lease, "1", null);
            Assert.Equal("GET /api/v2/libraries/1/metadata-match-queue?limit=10", kit.Http.Requests.Single());
            Assert.Equal("p2", page.NextCursor);
            var entry = P.MatcherEntries(page.Detail).Single();
            Assert.Equal("parked", entry.State);
            Assert.NotNull(entry.Rejection);
            Assert.Equal("A (2001)", entry.Candidates.Single().Title);
            await Assert.ThrowsAsync<AdminListInconsistentException>(() => api.GetMatchQueueDetailAsync(lease, "1", "big"));
            await Assert.ThrowsAsync<AdminListInconsistentException>(() => api.GetMatchQueueDetailAsync(lease, "1", "empty"));
        }
    }

    [Fact]
    public async Task FolderBrowsingIsAReadOnlyBoundedPage()
    {
        var (kit, authority, api) = Setup(_ => AdminMonitorKit.Json("{\"path\":\"/mnt\",\"parent\":\"/\",\"items\":[{\"name\":\"movies\",\"path\":\"/mnt/movies\"}],\"page\":{\"has_more\":false}}"));
        using (kit) using (authority)
        {
            using var lease = authority.Capture();
            var page = await api.BrowseAsync(lease, "/mnt", "mo", null, 999);
            Assert.Equal("GET /api/v2/admin/filesystem/browse?path=%2Fmnt&name_prefix=mo&limit=200", kit.Http.Requests.Single());
            Assert.Equal("/", page.Parent);
            Assert.Equal("vies", AdminPathCompletion.Suffix("/mnt/mo", AdminPathCompletion.ContextOf("/mnt/mo"), page.Entries));
        }
    }

    [Fact]
    public async Task TranslationPrewarmStartsCancelsAndListsJobs()
    {
        string? body = null;
        var (kit, authority, api) = Setup(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/cancel", StringComparison.Ordinal)) return NoContent();
            if (request.Method == HttpMethod.Post) { body = request.Content!.ReadAsStringAsync().Result; return AdminMonitorKit.Json("{\"id\":\"t1\",\"status\":\"pending\",\"target_language\":\"de\",\"progress_message\":\"\",\"progress\":0,\"fields_done\":0,\"fields_total\":0,\"updated_at\":\"2026-11-15T10:00:00Z\"}", HttpStatusCode.Accepted); }
            return AdminMonitorKit.Json("{\"jobs\":[{\"id\":\"t1\",\"status\":\"running\",\"target_language\":\"de\",\"progress_message\":\"Translating\",\"progress\":0.5,\"fields_done\":5,\"fields_total\":10,\"updated_at\":\"2026-11-15T10:00:00Z\"}]}");
        });
        using (kit) using (authority)
        {
            using var lease = authority.Capture();
            Assert.True((await api.StartTranslationAsync(lease, "4", "de")).IsActive);
            Assert.Equal("{\"target_language\":\"de\"}", body);
            Assert.True((await api.ListTranslationJobsAsync(lease, "4")).Single().IsActive);
            await api.CancelTranslationAsync(lease, "4", "t1");
            Assert.Equal(["POST /api/v2/admin/libraries/4/metadata-translation", "GET /api/v2/admin/libraries/4/metadata-translation/jobs",
                "POST /api/v2/admin/libraries/4/metadata-translation/jobs/t1/cancel"], kit.Http.Requests);
            await Assert.ThrowsAsync<ArgumentException>(() => api.StartTranslationAsync(lease, "4", "not a language"));
        }
    }

    [Fact]
    public async Task ReadsStopWhenTheLeaseRetires()
    {
        var (kit, authority, api) = Setup(_ => AdminMonitorKit.Json(Libraries(Library("1"))));
        using (kit) using (authority)
        {
            var lease = authority.Capture();
            kit.SetRole("user");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.ListAsync(lease));
            Assert.Empty(kit.Http.Requests);
        }
    }

    // ── Presentation ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("movies", true, true, true, true, true, true)]
    [InlineData("series", true, true, true, true, false, false)]
    [InlineData("mixed", true, true, true, true, false, true)]
    [InlineData("audiobooks", false, false, false, false, false, false)]
    [InlineData("custom", true, true, false, false, false, false)]
    public void SettingSupportFollowsTheLibraryType(string type, bool trailers, bool chapters, bool trickplay, bool intro, bool creditsOnly, bool boxSets)
    {
        var support = P.Support(type);
        Assert.Equal((trailers, chapters, trickplay, intro, creditsOnly, boxSets),
            (support.Trailers, support.ChapterThumbnails, support.Trickplay, support.IntroDetection, support.CreditsOnlyDetection, support.BoxSets));
    }

    [Fact]
    public void TypesContentLevelsAndLabelsMatchTheWebUi()
    {
        Assert.Equal(["movies", "series", "mixed", "audiobooks", "ebooks", "manga", "podcasts"], P.Types.Select(type => type.Value));
        Assert.Equal("series", P.FindType("TVShows")!.Value);
        Assert.Equal("weird", P.TypeLabel("weird"));
        Assert.Equal(["movie", "series", "season", "episode", "audiobook", "ebook"], P.ContentLevels("mixed"));
        Assert.Equal("Podcast Episode", P.ContentLevelLabel("podcast_episode"));
        Assert.Equal("Some Level", P.ContentLevelLabel("some_level"));
        Assert.Equal([2, 3, 4, 5, 7], P.BoxSetOptions(7));
        Assert.DoesNotContain("deleted_scene", P.ProviderTrailerKinds);
        Assert.All(P.MetadataLanguages(), option => Assert.Matches("^[a-z]{2}$", option.Code));
    }

    [Fact]
    public void ProviderChainTransformsKeepLocalFirstAndFillMissingLevels()
    {
        var local = new AdminProviderChainItem("0", "local", "nfo", true, true);
        var tmdb = new AdminProviderChainItem("9", "meta", "tmdb", true, false);
        var tvdb = new AdminProviderChainItem("8", "meta", "tvdb", false, false);
        var chains = new Dictionary<string, IReadOnlyList<AdminProviderChainItem>> { ["series"] = [local, tmdb, tvdb], ["season"] = [] };
        Assert.Equal([tmdb, tvdb], P.RemoteChains(chains)["series"]);
        Assert.Equal([local, tvdb, tmdb], P.WithLocalFirst(chains["series"], P.MoveProvider(P.RemoteChains(chains)["series"], 0, 1)!));
        Assert.Null(P.MoveProvider([tmdb], 0, -1));
        Assert.False(P.SetLocalEnabled(chains, false)["series"][0].Enabled);
        var merged = P.MergeWithDefaults(chains, new Dictionary<string, IReadOnlyList<AdminProviderChainItem>> { ["season"] = [tmdb], ["episode"] = [tvdb] }, "series");
        Assert.Equal([tmdb], merged["season"]);
        Assert.Equal([tvdb], merged["episode"]);
        Assert.Equal(3, merged["series"].Count);
    }

    [Fact]
    public void OrderMovesAreSinglePlaceOrDragMoves()
    {
        string[] ids = ["a", "b", "c"];
        Assert.Equal(["b", "a", "c"], P.MoveByOne(ids, id => id, "b", -1));
        Assert.Null(P.MoveByOne(ids, id => id, "a", -1));
        Assert.Null(P.MoveByOne(ids, id => id, "z", 1));
        Assert.Equal(["b", "c", "a"], P.ArrayMove(ids, 0, 2));
        Assert.Null(P.ArrayMove(ids, 1, 1));
    }

    [Fact]
    public void ScanRunsParseFromTheChannelAndFormatLikeTheWebUi()
    {
        var scan = P.ParseScan(JsonDocument.Parse("{\"id\":\"s1\",\"library_id\":4,\"mode\":\"subtree\",\"path\":\"/m/Show/\",\"status\":\"running\",\"trigger\":\"realtime_monitor\",\"started_at\":\"2026-11-15T09:55:00Z\",\"result\":{\"total_files\":200,\"files_processed\":50,\"message\":\"Scanning\"}}").RootElement)!;
        Assert.Equal("4", scan.LibraryId);
        Assert.True(scan.IsActive);
        Assert.Equal("Show", P.ScanTarget(scan.Path));
        Assert.Equal("Entire library", P.ScanTarget(null));
        Assert.Equal("File change", P.ScanTrigger("realtime_monitor"));
        Assert.Equal("odd trigger", P.ScanTrigger("odd_trigger"));
        Assert.Equal("Started 5m ago", P.ScanStarted(scan.StartedAt, DateTimeOffset.Parse("2026-11-15T10:00:30Z")));
        Assert.Contains("25", P.ScanProgress(scan));
        Assert.Null(P.ParseScan(JsonDocument.Parse("{\"id\":\"s2\",\"library_id\":4}").RootElement));
        var queued = scan with { Status = "accepted", Id = "s0", Mode = "library" };
        Assert.True(P.CompareScans(scan, queued) < 0);
    }

    [Fact]
    public void MonitoringAndSeekPreviewBadgesFollowTheWebUiRules()
    {
        Assert.True(P.MonitoringNeedsAttention(new AdminLibraryRealtimeMonitoringEntry { State = "limit_reached" }));
        Assert.False(P.MonitoringNeedsAttention(new AdminLibraryRealtimeMonitoringEntry { State = "not_reporting" }));
        Assert.Equal("Monitoring 3 folders (inotify)", P.MonitoringStatusText(new AdminLibraryRealtimeMonitoringEntry { State = "monitoring", Directories = 3, Backend = "inotify", Detail = "" }));
        Assert.Equal("Previews ready", P.TrickplayLabel(new AdminTrickplayLibrary { Ready = 4 }));
        Assert.Equal("Previews 50%", P.TrickplayLabel(new AdminTrickplayLibrary { Ready = 1, Pending = 1 }));
        Assert.Contains("1 failed", P.TrickplayDetail(new AdminTrickplayLibrary { Ready = 1, Unusable = 1, SheetBytes = 2048 }));
    }

    [Fact]
    public void JobStatesNeverTreatAReceiptAsCompletion()
    {
        Assert.Equal("completed", P.JobStatus(new AdminLibraryJob { State = "succeeded" }));
        Assert.Equal("cancelled", P.JobStatus(new AdminLibraryJob { State = "canceled" }));
        Assert.True(P.IsActiveJob(new AdminLibraryJob { State = "canceling" }));
        Assert.True(P.IsActiveJob(new AdminLibraryJob { State = "queued" }));
        Assert.False(P.IsActiveJob(new AdminLibraryJob { State = "failed" }));
        Assert.Equal("Queued", P.JobProgress(new AdminLibraryJob { State = "queued" }));
    }

    [Fact]
    public void UnknownOutcomesAreSeparatedFromDefiniteFailures()
    {
        Assert.True(P.IsUnknownOutcome(new HttpRequestException("reset")));
        Assert.True(P.IsUnknownOutcome(new OperationCanceledException("timeout", new TimeoutException())));
        Assert.True(P.IsUnknownOutcome(new InvalidDataException("no body")));
        Assert.False(P.IsUnknownOutcome(new ApiException("conflict", "already queued", 409)));
        Assert.False(P.IsUnknownOutcome(new AdminLibraryPosterException(false)));
        Assert.False(P.IsUnknownOutcome(new ArgumentException("bad")));
    }

    [Fact]
    public void PathCompletionContextMatchesTheWebUi()
    {
        Assert.Null(AdminPathCompletion.ContextOf("media"));
        Assert.Equal(new AdminPathCompletion.Context("/", ""), AdminPathCompletion.ContextOf("/"));
        Assert.Equal(new AdminPathCompletion.Context("/mnt", ""), AdminPathCompletion.ContextOf("/mnt/"));
        Assert.Equal(new AdminPathCompletion.Context("/", "mn"), AdminPathCompletion.ContextOf("/mn"));
        Assert.Equal(new AdminPathCompletion.Context("/mnt/media", "mo"), AdminPathCompletion.ContextOf("/mnt/media/mo"));
        Assert.Equal("", AdminPathCompletion.Suffix("/mnt", AdminPathCompletion.ContextOf("/mnt"), [new AdminFilesystemEntry { Name = "mnt", Path = "/mnt" }]));
    }

    // ── Text ─────────────────────────────────────────────────────────────

    [Fact]
    public void TextFormatsWebUiPatternsAndSplitsRichTags()
    {
        Assert.Equal("1 folder", AdminLibrariesText.T("web.admin.libraries.list.folderCount", ("count", 1)));
        Assert.Equal("3 folders", AdminLibrariesText.T("web.admin.libraries.list.folderCount", ("count", 3)));
        Assert.Equal("2 scans", AdminLibrariesText.T("web.admin.libraries.scanQueue.trigger", AdminLibrariesText.Count(2)));
        var segments = AdminLibrariesText.Segments(AdminLibrariesText.T("web.admin.libraries.rootOverride.description", ("path", "/m/a")));
        Assert.Contains(segments, segment => segment is { Tag: "code", Text: "/m/a" });
        Assert.Equal("Jobs could not load. Restart jobs", AdminLibrariesText.Plain(AdminLibrariesText.T("web.admin.common.jobPageControls.loadFailed")));
    }

    /// <summary>Every web.admin / admin.native key the Libraries sources format is in this domain's English table.</summary>
    [Fact]
    public void EveryLibrariesKeyUsedBySourceHasEnglishText()
    {
        var root = FindRepositoryRoot();
        var files = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path) is var name && (name.StartsWith("AdminLibrar", StringComparison.Ordinal) || name.StartsWith("AdminFolderBrowser", StringComparison.Ordinal)))
            .Where(path => !path.EndsWith("AdminLibrariesText.cs", StringComparison.Ordinal)).ToArray();
        Assert.True(files.Length >= 15, "the Libraries sources were found");
        var keys = files.SelectMany(path => Regex.Matches(File.ReadAllText(path), "\"((?:web\\.admin|admin\\.native\\.libraries|web\\.common\\.sortable)\\.[A-Za-z0-9_.]+)\"").Select(match => match.Groups[1].Value))
            .Where(key => !key.EndsWith('.')).Distinct().ToArray();
        Assert.NotEmpty(keys);
        // web.admin keys are this domain's; shared web.common keys may instead come from the generated interface catalog.
        var generated = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer.Core", "Localization", "Generated", "en.json"));
        Assert.Empty(keys.Where(key => !AdminLibrariesText.Has(key) &&
            (!key.StartsWith("web.common.", StringComparison.Ordinal) || !generated.Contains("\"" + key + "\"", StringComparison.Ordinal))));
        Assert.True(AdminLibrariesText.Keys.Count >= 400);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SiloPlayer.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
