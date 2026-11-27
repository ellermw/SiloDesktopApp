using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using Presentation = SiloPlayer.Core.Services.Admin.AdminMaintenancePresentation;

namespace SiloPlayer.Tests;

/// <summary>
/// Configure › Maintenance contracts (official server and WebUI at 92ee0518): request paths,
/// exact bodies, 202/409/422/400 semantics, no-replay commands, list generations, cursor
/// validation, authority retirement and the ported presentation rules. Synthetic server only.
/// </summary>
public sealed class AdminMaintenanceTests
{
    [Fact]
    public async Task CatalogJobSourceUsesTheExportIdAndFallsBackForOlderRows()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var api = new AdminMaintenanceApi(kit.Client);
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(Page(null,
            Job("import-1", AdminMaintenanceApi.ImportKind, extra: ",\"source_export_job_id\":\"export-4\",\"source_label\":\"received seed\""),
            Job("import-2", AdminMaintenanceApi.ImportKind, extra: ",\"source_label\":\"seed.json.gz\""),
            Job("import-3", AdminMaintenanceApi.ImportKind))));
        var jobs = (await api.ListJobsAsync(lease, AdminMaintenanceApi.ImportKind, 10, null)).Items;
        Assert.Equal("export-4", jobs[0].SourceExportJobId);
        Assert.Null(jobs[1].SourceExportJobId);
        Assert.Equal(["Export job export-4", "seed.json.gz", "Catalog seed"], jobs.Select(Presentation.ImportDescription));
        Assert.Equal(["Export job export-4", "seed.json.gz", "Catalog seed"], jobs.Select(Presentation.Description));
    }

    internal static string Job(string id, string kind = AdminMaintenanceApi.ExportKind, string state = "succeeded", string extra = "") =>
        $$"""{"id":"{{id}}","kind":"{{kind}}","state":"{{state}}","terminal":{{(state is "succeeded" or "failed" or "canceled" ? "true" : "false")}},"cancelable":false,"created_at":"2026-11-16T10:00:00Z","library_ids":[],"artifact_size_bytes":0{{extra}}}""";

    internal static string Page(string? next, params string[] rows) =>
        "{\"items\":[" + string.Join(',', rows) + "],\"page\":{\"has_more\":" + (next == null ? "false}}" : "true,\"next_cursor\":\"" + next + "\"}}");

    internal static HttpResponseMessage Accepted(string json, string? location)
    {
        var response = AdminMonitorKit.Json(json, HttpStatusCode.Accepted);
        if (location != null) response.Headers.Location = new Uri(location, UriKind.Relative);
        response.Headers.TryAddWithoutValidation("Retry-After", "5");
        return response;
    }

    internal static HttpResponseMessage Problem(HttpStatusCode status, string type, string extra = "", string? location = null)
    {
        var response = AdminMonitorKit.Json($$"""{"type":"https://silo.test/docs/api/v2/problems/{{type}}","title":"Problem","status":{{(int)status}},"detail":"secret provider text"{{extra}}}""", status);
        if (location != null) response.Headers.Location = new Uri(location, UriKind.Relative);
        return response;
    }

    /// <summary>A scripted server: empty lists unless <see cref="Override"/> answers; Override may block to hold a response.</summary>
    private sealed class Server
    {
        public Func<HttpRequestMessage, Task<HttpResponseMessage?>>? Override;

        public async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request)
        {
            if (Override != null && await Override(request) is { } answer) return answer;
            return request.RequestUri!.AbsolutePath switch
            {
                "/api/v2/admin/jobs" or "/api/v2/admin/catalog/import-sources" or "/api/v2/admin/catalog/local-import-sources" => AdminMonitorKit.Json(Page(null)),
                _ => AdminMonitorKit.Json("{}", HttpStatusCode.NotFound),
            };
        }
    }

    private static (AdminMonitorKit Kit, AdminMonitorAuthority Authority, AdminMaintenanceApi Api, Server Server) Setup()
    {
        var kit = AdminMonitorKit.Create();
        var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var server = new Server();
        kit.Http.Respond = (request, _) => server.RespondAsync(request);
        return (kit, authority, new AdminMaintenanceApi(kit.Client), server);
    }

    private static async Task Until(Func<bool> condition, int milliseconds = 3000)
    {
        for (var elapsed = 0; elapsed < milliseconds && !condition(); elapsed += 10) await Task.Delay(10);
        Assert.True(condition(), "condition was not reached");
    }

    private static int Count(AdminMonitorKit kit, string request) => kit.Http.Requests.Count(sent => sent == request);
    private static string[] Posts(AdminMonitorKit kit) => kit.Http.Requests.Where(sent => sent.StartsWith("POST ", StringComparison.Ordinal)).ToArray();

    private const string ExportsRead = "GET /api/v2/admin/jobs?kind=catalog_export&limit=10";
    private const string ImportsRead = "GET /api/v2/admin/jobs?kind=catalog_import&limit=10";
    private const string HistoryRead = "GET /api/v2/admin/jobs?limit=30";

    // ── API ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListsUseOfficialKindsPageSizesAndOpaqueCursors()
    {
        var (kit, authority, api, _) = Setup();
        using (kit) using (authority)
        {
            using var lease = authority.Capture();
            await api.ListJobsAsync(lease, AdminMaintenanceApi.ExportKind, AdminMaintenanceApi.CatalogJobPageSize, null);
            await api.ListJobsAsync(lease, null, AdminMaintenanceApi.HistoryPageSize, "opaque/+=");
            await api.ListImportSourcesAsync(lease, local: false, null);
            await api.ListImportSourcesAsync(lease, local: true, "next");
            Assert.Equal([ExportsRead, "GET /api/v2/admin/jobs?limit=30&cursor=opaque%2F%2B%3D",
                "GET /api/v2/admin/catalog/import-sources?limit=50", "GET /api/v2/admin/catalog/local-import-sources?limit=50&cursor=next"], kit.Http.Requests);
        }
    }

    [Fact]
    public async Task AKindFilteredPageWithAForeignKindIsInconsistent()
    {
        var (kit, authority, api, server) = Setup();
        using (kit) using (authority)
        {
            server.Override = _ => Task.FromResult<HttpResponseMessage?>(AdminMonitorKit.Json(Page(null, Job("1", AdminMaintenanceApi.ImportKind))));
            using var lease = authority.Capture();
            await Assert.ThrowsAsync<AdminListInconsistentException>(() => api.ListJobsAsync(lease, AdminMaintenanceApi.ExportKind, 10, null));
        }
    }

    [Theory]
    [InlineData("{\"id\":\"i1\",\"kind\":\"catalog_import\",\"state\":null}")]
    [InlineData("{\"id\":\"i1\",\"kind\":null,\"state\":\"failed\"}")]
    [InlineData("{\"id\":\"i1\",\"kind\":\"catalog_import\",\"state\":\"failed\",\"failure\":{\"type\":null,\"detail\":\"Fixed\"}}")]
    [InlineData("{\"id\":\"i1\",\"kind\":\"catalog_import\",\"state\":\"failed\",\"failure\":{\"type\":\"https://silo.test/job_failed\",\"detail\":null}}")]
    [InlineData("{\"id\":\"i1\",\"kind\":\"catalog_import\",\"state\":\"failed\",\"failure\":{\"type\":null,\"detail\":null}}")]
    public async Task NullPresentationFieldsAreRejectedBeforePublishingRows(string malformed)
    {
        var (kit, authority, api, server) = Setup();
        using (kit) using (authority) using (var controller = new AdminMaintenanceController(api, authority))
        {
            server.Override = request => Task.FromResult<HttpResponseMessage?>(request.RequestUri!.AbsolutePath == "/api/v2/admin/jobs"
                ? AdminMonitorKit.Json(Page(null, malformed)) : null);
            using var lease = authority.Capture();
            await Assert.ThrowsAsync<AdminListInconsistentException>(() => api.ListJobsAsync(lease, null, 30, null));
            await Assert.ThrowsAsync<AdminListInconsistentException>(() => api.ListJobsAsync(lease, AdminMaintenanceApi.ImportKind, 10, null));
            await controller.OpenAsync();
            Assert.Equal(AdminFailureKind.InvalidResponse, controller.State.History.Failure);
            Assert.Equal(AdminFailureKind.InvalidResponse, controller.State.Imports.Failure);
            Assert.Empty(controller.State.History.Items);
            Assert.Empty(controller.State.Imports.Items);
            server.Override = _ => Task.FromResult<HttpResponseMessage?>(AdminMonitorKit.Json(Page(null, Job("valid", AdminMaintenanceApi.ImportKind))));
            await controller.RestartAsync(AdminMaintenanceListKind.Imports);
            Assert.Null(controller.State.Imports.Failure);
            Assert.Equal("valid", Assert.Single(controller.State.Imports.Items).Id);
        }
    }

    [Fact]
    public async Task ExportJobIsAcceptedOnlyForA202ThatNamesItsJob()
    {
        var (kit, authority, api, server) = Setup();
        using (kit) using (authority)
        {
            using var lease = authority.Capture();
            server.Override = _ => Task.FromResult<HttpResponseMessage?>(Accepted(Job("e1", state: "queued"), "/api/v2/admin/jobs/e1"));
            var job = await api.CreateExportJobAsync(lease);
            Assert.Equal("queued", job.State);
            Assert.False(job.Terminal);
            Assert.Equal("POST /api/v2/admin/catalog/export-jobs", kit.Http.Requests.Single());
            Assert.Equal("{}", kit.Http.Bodies.Single());

            foreach (var answer in new Func<HttpResponseMessage>[]
            {
                () => Accepted(Job("e1", state: "queued"), null),
                () => Accepted(Job("e1", state: "queued"), "/api/v2/admin/jobs/other"),
                () => Accepted(Job("e1", AdminMaintenanceApi.ImportKind, "queued"), "/api/v2/admin/jobs/e1"),
                () => AdminMonitorKit.Json(Job("e1", state: "queued")),
                () => Accepted("not json", "/api/v2/admin/jobs/e1"),
            })
            {
                server.Override = _ => Task.FromResult<HttpResponseMessage?>(answer());
                await Assert.ThrowsAsync<AdminCatalogOutcomeUnknownException>(() => api.CreateExportJobAsync(lease));
            }
            Assert.Equal(6, Posts(kit).Length);
        }
    }

    [Theory]
    [InlineData(AdminCatalogImportSourceKind.LocalPath, "local_path", "/catalog-seeds/a.json.gz")]
    [InlineData(AdminCatalogImportSourceKind.ExportJob, "export_job_id", "job-7")]
    [InlineData(AdminCatalogImportSourceKind.BucketArtifact, "artifact_key", "catalog-seeds/b.json.gz")]
    [InlineData(AdminCatalogImportSourceKind.RemoteUrl, "remote_url", "https://seeds.example/c.json.gz")]
    public async Task ImportBodyCarriesExactlyTheSelectedSource(AdminCatalogImportSourceKind source, string member, string value)
    {
        var (kit, authority, api, server) = Setup();
        using (kit) using (authority)
        {
            server.Override = _ => Task.FromResult<HttpResponseMessage?>(AdminMonitorKit.Json("""{"items_created":3,"files_created":4}"""));
            var draft = AdminCatalogImportDraft.Initial() with
            {
                Source = source, LocalPath = source == AdminCatalogImportSourceKind.LocalPath ? "  " + value + " " : "/stale/local.json.gz",
                ExportJobId = source == AdminCatalogImportSourceKind.ExportJob ? value : "stale-job",
                ArtifactKey = source == AdminCatalogImportSourceKind.BucketArtifact ? value : "stale-key",
                RemoteUrl = source == AdminCatalogImportSourceKind.RemoteUrl ? value : "https://stale.example/x.json.gz",
                Execution = AdminCatalogImportExecution.Synchronous, ConflictMode = AdminCatalogConflictMode.OverwriteExisting,
            };
            draft = draft.UpdateRewrite(draft.Rewrites[0].Id, from: "/srv/media", to: "/media").AddRewrite();
            using var lease = authority.Capture();
            var result = await api.ImportAsync(lease, draft.ToRequest());
            Assert.Equal(3, result.ItemsCreated);
            Assert.Equal("POST /api/v2/admin/catalog/import", kit.Http.Requests.Single());
            using var body = JsonDocument.Parse(kit.Http.Bodies.Single());
            var names = body.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
            Assert.Equal([member, "conflict_mode", "path_rewrites"], names);
            Assert.Equal(value, body.RootElement.GetProperty(member).GetString());
            Assert.Equal("overwrite_existing", body.RootElement.GetProperty("conflict_mode").GetString());
            Assert.Equal("""[{"from":"/srv/media","to":"/media"}]""", body.RootElement.GetProperty("path_rewrites").GetRawText());
        }
    }

    [Fact]
    public void DraftFiltersBlankRowsRejectsPartialRowsAndNeverInspectsLocalFiles()
    {
        var draft = AdminCatalogImportDraft.Initial();
        Assert.True(draft.CanSubmit);
        Assert.False(draft.IsDirty);
        Assert.Equal("/catalog-seeds/", draft.ToRequest().SourceValue);
        Assert.Empty(draft.ToRequest().PathRewrites);
        var partial = draft.UpdateRewrite(draft.Rewrites[0].Id, from: "/srv/media");
        Assert.True(partial.IsDirty);
        Assert.Equal([draft.Rewrites[0].Id], partial.PartialRewriteIds);
        Assert.False(partial.CanSubmit);
        Assert.Throws<InvalidOperationException>(() => partial.ToRequest());
        // A Linux server path that does not exist on Windows stays valid.
        var linux = draft with { LocalPath = "/mnt/linux-only/seed.json.gz" };
        Assert.True(linux.CanSubmit);
        Assert.Equal("/mnt/linux-only/seed.json.gz", linux.ToRequest().SourceValue);
        Assert.False((draft with { Source = AdminCatalogImportSourceKind.ExportJob }).CanSubmit);
        Assert.False((draft with { Source = AdminCatalogImportSourceKind.RemoteUrl, RemoteUrl = "   " }).CanSubmit);
        var single = draft.RemoveRewrite(draft.Rewrites[0].Id);
        Assert.Single(single.Rewrites);
        Assert.NotEqual(draft.Rewrites[0].Id, single.Rewrites[0].Id);
        var two = draft.AddRewrite();
        Assert.Equal(two.Rewrites[0], two.RemoveRewrite(two.Rewrites[1].Id).Rewrites.Single());
    }

    [Fact]
    public async Task ConflictCarriesTheActiveJobLocationAndRewriteRootsAreFieldErrors()
    {
        var (kit, authority, api, server) = Setup();
        using (kit) using (authority)
        {
            using var lease = authority.Capture();
            server.Override = _ => Task.FromResult<HttpResponseMessage?>(Problem(HttpStatusCode.Conflict, "conflict", location: "/api/v2/admin/jobs/active-1"));
            var conflict = await Assert.ThrowsAsync<AdminCatalogConflictException>(() => api.CreateExportJobAsync(lease));
            Assert.Equal("active-1", AdminMaintenanceApi.JobIdFromLocation(conflict.JobLocation));
            Assert.Equal(AdminFailureKind.Conflict, AdminFailures.Classify(conflict));

            server.Override = _ => Task.FromResult<HttpResponseMessage?>(Problem(HttpStatusCode.UnprocessableEntity, "validation_failed",
                ""","errors":[{"location":"body.path_rewrites","code":"path_rewrite_required","detail":"Source root requires a rewrite: /srv/media"},{"location":"body.path_rewrites","code":"path_rewrite_required","detail":"Source root requires a rewrite: /srv/tv"}]"""));
            var refused = await Assert.ThrowsAsync<ApiException>(() => api.CreateImportJobAsync(lease, AdminCatalogImportDraft.Initial().ToRequest()));
            Assert.Equal(422, refused.StatusCode);
            Assert.Equal(["/srv/media", "/srv/tv"], Presentation.RequiredRewriteRoots(refused.FieldErrors));
        }
    }

    [Theory]
    [InlineData("/api/v2/admin/jobs/abc", "abc")]
    [InlineData("https://silo.test/api/v2/admin/jobs/a%20b", "a b")]
    [InlineData("/api/v2/admin/jobs/abc/cancel", null)]
    [InlineData("/api/v2/library-jobs/abc", null)]
    [InlineData("/api/v2/admin/jobs/", null)]
    [InlineData(null, null)]
    public void JobLocationsNameOnlyAnAdminJob(string? location, string? expected) => Assert.Equal(expected, AdminMaintenanceApi.JobIdFromLocation(location));

    [Fact]
    public async Task PublishRequiresTheSameJobAndAUrl()
    {
        var (kit, authority, api, server) = Setup();
        using (kit) using (authority)
        {
            using var lease = authority.Capture();
            server.Override = _ => Task.FromResult<HttpResponseMessage?>(AdminMonitorKit.Json("""{"job_id":"e1","url":"https://bucket.example/seed?X-Signature=1","expires_at":"2026-11-23T10:00:00Z"}"""));
            var published = await api.PublishAsync(lease, "e1");
            Assert.Equal("POST /api/v2/admin/catalog/export-jobs/e1/publish", kit.Http.Requests.Single());
            Assert.Equal(DateTimeOffset.Parse("2026-11-23T10:00:00Z"), published.ExpiresAt);
            server.Override = _ => Task.FromResult<HttpResponseMessage?>(AdminMonitorKit.Json("""{"job_id":"other","url":"https://bucket.example/seed"}"""));
            await Assert.ThrowsAsync<AdminCatalogOutcomeUnknownException>(() => api.PublishAsync(lease, "e1"));
        }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"items_created\":0}")]
    [InlineData("{\"files_created\":0}")]
    public async Task IncompleteSynchronousSuccessKeepsTheOutcomeUnknown(string body)
    {
        var (kit, authority, api, server) = Setup();
        using (kit) using (authority)
        {
            using var lease = authority.Capture();
            server.Override = _ => Task.FromResult<HttpResponseMessage?>(AdminMonitorKit.Json(body));
            await Assert.ThrowsAsync<AdminCatalogOutcomeUnknownException>(() => api.ImportAsync(lease, AdminCatalogImportDraft.Initial().ToRequest()));
            Assert.True(api.HasUnconfirmedImport(lease));
            Assert.Equal(["POST /api/v2/admin/catalog/import"], kit.Http.Requests);
        }
    }

    [Fact]
    public async Task CommandsNeverRefreshTokensOrResendAfterUnauthorized()
    {
        var (kit, authority, api, server) = Setup();
        using (kit) using (authority)
        {
            using var lease = authority.Capture();
            server.Override = _ => Task.FromResult<HttpResponseMessage?>(Problem(HttpStatusCode.Unauthorized, "unauthorized"));
            var error = await Assert.ThrowsAsync<ApiException>(() => api.ImportAsync(lease, AdminCatalogImportDraft.Initial().ToRequest()));
            Assert.Equal(401, error.StatusCode);
            Assert.Equal(["POST /api/v2/admin/catalog/import"], kit.Http.Requests);
        }
    }

    // ── Controller ────────────────────────────────────────────────────────

    private static (AdminMonitorKit Kit, AdminMonitorAuthority Authority, AdminMaintenanceController Controller, Server Server) Create()
    {
        var (kit, authority, api, server) = Setup();
        return (kit, authority, new AdminMaintenanceController(api, authority), server);
    }

    [Fact]
    public async Task OpeningMountsFiveListsAndNothingPolls()
    {
        var (kit, authority, controller, _) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            Assert.Equal(5, kit.Http.Requests.Count);
            Assert.Equal(1, Count(kit, ExportsRead));
            Assert.Equal(1, Count(kit, ImportsRead));
            Assert.Equal(1, Count(kit, HistoryRead));
            Assert.Equal(1, Count(kit, "GET /api/v2/admin/catalog/import-sources?limit=50"));
            Assert.Equal(1, Count(kit, "GET /api/v2/admin/catalog/local-import-sources?limit=50"));
            Assert.True(controller.State.Exports.Loaded && controller.State.History.Loaded);
            await Task.Delay(400);
            Assert.Equal(5, kit.Http.Requests.Count);
        }
    }

    [Fact]
    public void MaintenanceSourcesDeclareNoTimer()
    {
        var root = FindRepositoryRoot();
        var files = Directory.EnumerateFiles(Path.Combine(root, "src"), "*AdminMaintenance*.cs", SearchOption.AllDirectories).ToArray();
        Assert.True(files.Length >= 6, "the Maintenance sources were found");
        foreach (var path in files)
        {
            var source = File.ReadAllText(path);
            Assert.DoesNotMatch(new Regex("DispatcherTimer|PeriodicTimer|System\\.Threading\\.Timer|new Timer\\(|SetInterval|RefetchInterval"), source);
        }
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(408)]
    [InlineData(0)]
    public async Task AnUnconfirmedExportIsReconciledByReadingNeverResent(int status)
    {
        var (kit, authority, controller, server) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            var notices = new List<AdminMaintenanceNotice>();
            controller.Notice += notices.Add;
            server.Override = request => request.Method == HttpMethod.Post
                ? status == 0 ? throw new HttpRequestException("synthetic") : Task.FromResult<HttpResponseMessage?>(Problem((HttpStatusCode)status, "internal_error"))
                : Task.FromResult<HttpResponseMessage?>(null);
            await controller.StartExportAsync();
            Assert.Single(Posts(kit));
            Assert.Equal(2, Count(kit, ExportsRead));
            Assert.Equal(1, Count(kit, ImportsRead));
            Assert.False(controller.State.ExportPending);
            Assert.Equal(AdminMaintenanceNoticeKind.Warning, notices.Single().Kind);
            Assert.DoesNotContain("secret", notices.Single().Message, StringComparison.Ordinal);
        }
    }

    public static IEnumerable<object[]> NonObjectCommandErrors()
    {
        foreach (var status in new[] { 408, 500, 503 })
            foreach (var body in new[] { "[]", "null", "\"error\"", "42", "true", "false" })
                yield return [status, body];
    }

    [Theory]
    [MemberData(nameof(NonObjectCommandErrors))]
    public async Task NonObjectCommandErrorsPreserveTheirHttpStatus(int status, string body)
    {
        var (kit, authority, api, server) = Setup();
        using (kit) using (authority)
        {
            using var lease = authority.Capture();
            server.Override = _ => Task.FromResult<HttpResponseMessage?>(AdminMonitorKit.Json(body, (HttpStatusCode)status));
            var error = await Assert.ThrowsAsync<ApiException>(() => api.CreateExportJobAsync(lease));
            Assert.Equal(status, error.StatusCode);
            Assert.Equal("unknown", error.ErrorCode);
            Assert.Equal($"HTTP {status}", error.Message);
            Assert.Single(Posts(kit));
        }
    }

    [Theory]
    [MemberData(nameof(NonObjectCommandErrors))]
    public async Task NonObjectCommandErrorsRemainUnknownAndNeverReplay(int status, string body)
    {
        foreach (var command in new[] { "export", "publish", "queuedImport", "synchronousImport" })
        {
            var (kit, authority, controller, server) = Create();
            using (kit) using (authority) using (controller)
            {
                server.Override = request => Task.FromResult<HttpResponseMessage?>(request.RequestUri!.PathAndQuery == ExportsRead.Split(' ')[1]
                    ? AdminMonitorKit.Json(Page(null, Job("done"))) : null);
                await controller.OpenAsync();
                var notices = new List<AdminMaintenanceNotice>();
                controller.Notice += notices.Add;
                server.Override = request => Task.FromResult<HttpResponseMessage?>(request.Method == HttpMethod.Post
                    ? AdminMonitorKit.Json(body, (HttpStatusCode)status) : null);

                if (command == "export") await controller.StartExportAsync();
                else if (command == "publish") await controller.PublishAsync("done");
                else
                {
                    controller.SetDialogOpen(true);
                    controller.EditDraft(draft => draft with
                    {
                        Execution = command == "queuedImport" ? AdminCatalogImportExecution.Queued : AdminCatalogImportExecution.Synchronous,
                    });
                    await controller.SubmitImportAsync();
                    Assert.True(controller.State.ImportOutcomeUnknown);
                    Assert.False(controller.State.CanSubmitImport);
                    Assert.True(controller.Api.HasUnconfirmedImport(controller.State.Owner!));
                    await controller.SubmitImportAsync();
                    controller.SetDialogOpen(false);
                    controller.SetDialogOpen(true);
                    Assert.True(controller.State.ImportOutcomeUnknown);
                    Assert.False(controller.State.CanSubmitImport);
                    await controller.SubmitImportAsync();
                }

                Assert.Single(Posts(kit));
                Assert.Equal(AdminMaintenanceNoticeKind.Warning, Assert.Single(notices).Kind);
                Assert.False(controller.State.AnyCommandPending);
                Assert.Equal(command is "export" or "publish" ? 2 : 1, Count(kit, ExportsRead));
                Assert.Equal(command == "queuedImport" ? 2 : 1, Count(kit, ImportsRead));
                Assert.Equal(1, Count(kit, HistoryRead));
            }
        }
    }

    [Fact]
    public async Task AConflictAdoptsOnlyTheJobItsLocationNames()
    {
        var (kit, authority, controller, server) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            server.Override = request => Task.FromResult<HttpResponseMessage?>(request.RequestUri!.AbsolutePath switch
            {
                "/api/v2/admin/catalog/export-jobs" => Problem(HttpStatusCode.Conflict, "conflict", location: "/api/v2/admin/jobs/import-9"),
                "/api/v2/admin/jobs/import-9" => AdminMonitorKit.Json(Job("import-9", AdminMaintenanceApi.ImportKind, "running")),
                _ => null,
            });
            await controller.StartExportAsync();
            Assert.Single(Posts(kit));
            Assert.Equal(1, Count(kit, "GET /api/v2/admin/jobs/import-9"));
            Assert.Equal(2, Count(kit, ImportsRead));
            Assert.Equal(1, Count(kit, ExportsRead));
            Assert.Equal("import-9", controller.State.ConflictJob?.Id);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task AnUnconfirmedSynchronousImportBlocksUntilAcknowledgedAndIsNeverResent(int status)
    {
        var (kit, authority, controller, server) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            controller.SetDialogOpen(true);
            controller.EditDraft(draft => draft with { Execution = AdminCatalogImportExecution.Synchronous, ConflictMode = AdminCatalogConflictMode.OverwriteExisting });
            server.Override = request => request.Method != HttpMethod.Post ? Task.FromResult<HttpResponseMessage?>(null)
                : status == 0 ? throw new HttpRequestException("lost")
                : Task.FromResult<HttpResponseMessage?>(Problem((HttpStatusCode)status, "dependency_unavailable"));
            await controller.SubmitImportAsync();
            Assert.True(controller.State.ImportOutcomeUnknown);
            Assert.False(controller.State.CanSubmitImport);
            Assert.Equal(AdminCatalogConflictMode.OverwriteExisting, controller.State.Draft.ConflictMode);
            await controller.SubmitImportAsync();
            controller.SetDialogOpen(false);
            controller.SetDialogOpen(true);
            server.Override = _ => Task.FromResult<HttpResponseMessage?>(Problem(HttpStatusCode.ServiceUnavailable, "dependency_unavailable"));
            await controller.RefreshAsync(AdminMaintenanceListKind.Imports);
            Assert.True(controller.State.ImportOutcomeUnknown);
            await controller.SubmitImportAsync();
            Assert.Single(Posts(kit));
            // Mount, then the explicit refresh and its single read retry; an import-and-wait has no job to reconcile.
            Assert.Equal(3, Count(kit, ImportsRead));
            Assert.Equal(AdminFailureKind.Server, controller.State.Imports.Failure);
            controller.AcknowledgeImportOutcome();
            Assert.False(controller.State.ImportOutcomeUnknown);
            Assert.True(controller.State.CanSubmitImport);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DepartingDuringImportKeepsTheBlockAcrossControllerReplacement(bool queued)
    {
        var (kit, authority, api, server) = Setup();
        using (kit) using (authority)
        {
            var reply = new TaskCompletionSource<HttpResponseMessage?>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var first = new AdminMaintenanceController(api, authority);
            await first.OpenAsync();
            first.SetDialogOpen(true);
            first.EditDraft(draft => draft with
            {
                Execution = queued ? AdminCatalogImportExecution.Queued : AdminCatalogImportExecution.Synchronous,
                Source = AdminCatalogImportSourceKind.RemoteUrl,
                RemoteUrl = "https://seeds.test/seed.json.gz",
            });
            server.Override = request => request.Method == HttpMethod.Post ? reply.Task : Task.FromResult<HttpResponseMessage?>(null);
            var pending = first.SubmitImportAsync();
            try
            {
                await Until(() => Posts(kit).Length == 1);
                first.Dispose();
                using var replacement = new AdminMaintenanceController(api, authority);
                await replacement.OpenAsync();
                replacement.SetDialogOpen(true);
                replacement.EditDraft(draft => draft with { Source = AdminCatalogImportSourceKind.RemoteUrl, RemoteUrl = "https://seeds.test/seed.json.gz" });
                Assert.True(replacement.State.ImportOutcomeUnknown);
                replacement.AcknowledgeImportOutcome();
                Assert.True(replacement.State.ImportOutcomeUnknown);
                await replacement.SubmitImportAsync();
                Assert.Single(Posts(kit));
                reply.TrySetResult(queued
                    ? Accepted(Job("departed", AdminMaintenanceApi.ImportKind, "queued"), "/api/v2/admin/jobs/departed")
                    : AdminMonitorKit.Json("{\"items_created\":1,\"files_created\":1}"));
                await pending;
                Assert.True(replacement.State.ImportOutcomeUnknown);
                replacement.AcknowledgeImportOutcome();
                Assert.False(replacement.State.ImportOutcomeUnknown);
                Assert.True(replacement.State.CanSubmitImport);
                server.Override = request => Task.FromResult<HttpResponseMessage?>(request.Method == HttpMethod.Post
                    ? Accepted(Job("next", AdminMaintenanceApi.ImportKind, "queued"), "/api/v2/admin/jobs/next") : null);
                await replacement.SubmitImportAsync();
                Assert.Equal(2, Posts(kit).Length);
            }
            finally
            {
                reply.TrySetResult(AdminMonitorKit.Json("{}"));
                await pending;
            }
        }
    }

    [Fact]
    public async Task AQueuedImportSuccessResetsOnlyItsDraftAndRefreshesImports()
    {
        var (kit, authority, controller, server) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            var completed = 0;
            controller.ImportCompleted += () => completed++;
            controller.SetDialogOpen(true);
            controller.EditDraft(draft => draft with { LocalPath = "/catalog-seeds/a.json.gz" });
            server.Override = request => Task.FromResult<HttpResponseMessage?>(request.RequestUri!.AbsolutePath == "/api/v2/admin/catalog/import-jobs"
                ? Accepted(Job("i1", AdminMaintenanceApi.ImportKind, "queued"), "/api/v2/admin/jobs/i1") : null);
            await controller.SubmitImportAsync();
            Assert.Equal(1, completed);
            Assert.False(controller.State.Draft.IsDirty);
            Assert.False(controller.State.DialogOpen);
            Assert.Equal(2, Count(kit, ImportsRead));
            Assert.Equal("""{"local_path":"/catalog-seeds/a.json.gz","conflict_mode":"skip_existing","path_rewrites":[]}""",
                kit.Http.Bodies.ElementAt(kit.Http.Requests.ToList().IndexOf("POST /api/v2/admin/catalog/import-jobs")));
        }
    }

    [Theory]
    [InlineData("committed", 1)]
    [InlineData("queued", 0)]
    [InlineData("refused", 0)]
    [InlineData("unknown", 0)]
    public async Task OnlyAConfirmedSynchronousImportPublishesAnOwnerScopedCatalogChange(string outcome, int expected)
    {
        var (kit, authority, controller, server) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            var changes = new List<(ApiRequestContext Context, long Epoch)>();
            controller.Api.CatalogChanged += (changedContext, changedEpoch) => changes.Add((changedContext, changedEpoch));
            controller.EditDraft(draft => draft with
            {
                Execution = outcome == "queued" ? AdminCatalogImportExecution.Queued : AdminCatalogImportExecution.Synchronous,
            });
            server.Override = request => Task.FromResult<HttpResponseMessage?>(request.Method != HttpMethod.Post ? null : outcome switch
            {
                "committed" => AdminMonitorKit.Json("{\"items_created\":1,\"files_created\":2}"),
                "queued" => Accepted(Job("i1", AdminMaintenanceApi.ImportKind, "queued"), "/api/v2/admin/jobs/i1"),
                "refused" => Problem(HttpStatusCode.BadRequest, "bad_request"),
                _ => AdminMonitorKit.Json("{}"),
            });
            var context = kit.Client.CaptureContext();
            var epoch = authority.Epoch;
            await controller.SubmitImportAsync();
            Assert.Equal(expected, changes.Count);
            if (expected != 0)
            {
                Assert.Equal((context, epoch), changes.Single());
                Assert.False(controller.State.ImportOutcomeUnknown);
                Assert.False(controller.State.Draft.IsDirty);
                Assert.Equal(1, Count(kit, ImportsRead));
            }
            Assert.Single(Posts(kit));
        }
    }

    [Fact]
    public async Task ARetiredImportsLateSuccessCannotNotifyTheReplacementOwner()
    {
        var (kit, authority, controller, server) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            var changes = 0;
            controller.Api.CatalogChanged += (_, _) => changes++;
            var reply = new TaskCompletionSource<HttpResponseMessage?>(TaskCreationOptions.RunContinuationsAsynchronously);
            server.Override = request => request.Method == HttpMethod.Post ? reply.Task : Task.FromResult<HttpResponseMessage?>(null);
            controller.EditDraft(draft => draft with { Execution = AdminCatalogImportExecution.Synchronous });
            var pending = controller.SubmitImportAsync();
            try
            {
                await Until(() => Posts(kit).Length == 1);
                kit.SetRole("user");
                kit.SetRole("admin");
                reply.TrySetResult(AdminMonitorKit.Json("{\"items_created\":1,\"files_created\":2}"));
                await pending;
                Assert.Equal(0, changes);
                Assert.Single(Posts(kit));
            }
            finally
            {
                reply.TrySetResult(AdminMonitorKit.Json("{}"));
                await pending;
            }
        }
    }

    [Fact]
    public async Task ANotificationFailureCannotChangeACommittedImportOrReplayIt()
    {
        var (kit, authority, controller, server) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            var notified = 0;
            controller.Api.CatalogChanged += (_, _) => throw new InvalidOperationException("synthetic observer failure");
            controller.Api.CatalogChanged += (_, _) => notified++;
            controller.EditDraft(draft => draft with { Execution = AdminCatalogImportExecution.Synchronous });
            server.Override = request => Task.FromResult<HttpResponseMessage?>(request.Method == HttpMethod.Post
                ? AdminMonitorKit.Json("{\"items_created\":1,\"files_created\":2}") : null);
            await controller.SubmitImportAsync();
            Assert.Equal(1, notified);
            Assert.False(controller.State.ImportOutcomeUnknown);
            Assert.False(controller.State.Draft.IsDirty);
            Assert.Single(Posts(kit));
        }
    }

    [Fact]
    public async Task RefusalsKeepTheDraftWithSafeReasonsAndRewriteFieldErrors()
    {
        var (kit, authority, controller, server) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            controller.SetDialogOpen(true);
            controller.EditDraft(draft => draft with { LocalPath = "/seed.json.gz" });
            server.Override = request => Task.FromResult<HttpResponseMessage?>(request.Method == HttpMethod.Post ? Problem(HttpStatusCode.UnprocessableEntity, "validation_failed",
                ""","errors":[{"location":"body.path_rewrites","code":"path_rewrite_required","detail":"Source root requires a rewrite: /srv/media"}]""") : null);
            await controller.SubmitImportAsync();
            Assert.Equal(["/srv/media"], controller.State.RequiredRewriteRoots);
            Assert.Equal("/seed.json.gz", controller.State.Draft.LocalPath);
            Assert.False(controller.State.ImportOutcomeUnknown);
            controller.EditDraft(draft => draft.UpdateRewrite(draft.Rewrites[0].Id, from: "/srv/media", to: "/media"));
            Assert.Empty(controller.State.RequiredRewriteRoots);

            server.Override = request => Task.FromResult<HttpResponseMessage?>(request.Method == HttpMethod.Post ? Problem(HttpStatusCode.BadRequest, "bad_request") : null);
            await controller.SubmitImportAsync();
            Assert.Equal(AdminMaintenanceText.T("admin.native.maintenance.error.sourceUnavailable"), controller.State.ImportError);
            server.Override = request => Task.FromResult<HttpResponseMessage?>(request.Method == HttpMethod.Post ? Problem(HttpStatusCode.UnprocessableEntity, "validation_failed") : null);
            await controller.SubmitImportAsync();
            Assert.Equal(AdminMaintenanceText.T("admin.native.maintenance.error.invalidImport"), controller.State.ImportError);
            server.Override = request => Task.FromResult<HttpResponseMessage?>(request.Method == HttpMethod.Post ? Problem(HttpStatusCode.Forbidden, "demo_restricted") : null);
            await controller.SubmitImportAsync();
            Assert.Equal(AdminMaintenanceText.T("admin.native.maintenance.error.demo"), controller.State.ImportError);
            Assert.DoesNotContain("secret", controller.State.ImportError, StringComparison.Ordinal);
            Assert.Equal(4, Posts(kit).Length);
            Assert.True(controller.State.Draft.IsDirty);
        }
    }

    [Fact]
    public async Task ARefreshRequestedDuringARefreshIsCoalescedIntoOneMorePass()
    {
        var (kit, authority, controller, server) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            var gate = new TaskCompletionSource();
            var held = 0;
            server.Override = async request =>
            {
                if (request.RequestUri!.PathAndQuery == HistoryRead.Split(' ')[1] && Interlocked.Increment(ref held) == 1) await gate.Task;
                return null;
            };
            var first = controller.RefreshAsync(AdminMaintenanceListKind.History);
            await Until(() => held == 1);
            var second = controller.RefreshAsync(AdminMaintenanceListKind.History);
            var third = controller.RefreshAsync(AdminMaintenanceListKind.History);
            gate.SetResult();
            await Task.WhenAll(first, second, third);
            Assert.Equal(3, Count(kit, HistoryRead));
            Assert.False(controller.State.History.Loading);
        }
    }

    [Fact]
    public async Task ARefreshSupersedesAnInFlightLoadOlderAndDropsItsLateRows()
    {
        var (kit, authority, controller, server) = Create();
        using (kit) using (authority) using (controller)
        {
            var gate = new TaskCompletionSource();
            server.Override = async request =>
            {
                var query = request.RequestUri!.PathAndQuery;
                if (query == "/api/v2/admin/jobs?limit=30") return AdminMonitorKit.Json(Page("c2", Job("a", "library_refresh")));
                if (query == "/api/v2/admin/jobs?limit=30&cursor=c2") { await gate.Task; return AdminMonitorKit.Json(Page(null, Job("late", "library_refresh"))); }
                return null;
            };
            await controller.OpenAsync();
            Assert.True(controller.State.History.HasMore);
            var more = controller.LoadMoreAsync(AdminMaintenanceListKind.History);
            await Until(() => controller.State.History.LoadingMore);
            var refresh = controller.RefreshAsync(AdminMaintenanceListKind.History);
            await Until(() => Count(kit, HistoryRead) == 2);
            gate.SetResult();
            await Task.WhenAll(more, refresh);
            Assert.Equal(["a"], controller.State.History.Items.Select(job => job.Id));
            Assert.Null(controller.State.History.Failure);
        }
    }

    [Theory]
    [InlineData("c1", "a", false)]
    [InlineData("c3", "a", true)]
    public async Task CursorCyclesAndRepeatedRowsAcrossPagesAreRefused(string secondNext, string secondId, bool duplicate)
    {
        var (kit, authority, controller, server) = Create();
        using (kit) using (authority) using (controller)
        {
            server.Override = request => Task.FromResult<HttpResponseMessage?>(request.RequestUri!.PathAndQuery switch
            {
                "/api/v2/admin/jobs?limit=30" => AdminMonitorKit.Json(Page("c1", Job("a", "library_refresh"))),
                "/api/v2/admin/jobs?limit=30&cursor=c1" => AdminMonitorKit.Json(Page(secondNext, Job(duplicate ? secondId : "b", "library_refresh"))),
                _ => null,
            });
            await controller.OpenAsync();
            await controller.LoadMoreAsync(AdminMaintenanceListKind.History);
            Assert.Equal(AdminFailureKind.InvalidResponse, controller.State.History.Failure);
            Assert.Equal(["a"], controller.State.History.Items.Select(job => job.Id));
        }
    }

    [Fact]
    public async Task RestartDropsTheListAndReadsOnlyTheFirstPage()
    {
        var (kit, authority, controller, server) = Create();
        using (kit) using (authority) using (controller)
        {
            server.Override = request => Task.FromResult<HttpResponseMessage?>(request.RequestUri!.PathAndQuery switch
            {
                "/api/v2/admin/jobs?limit=30" => AdminMonitorKit.Json(Page("c1", Job("a", "library_refresh"))),
                "/api/v2/admin/jobs?limit=30&cursor=c1" => AdminMonitorKit.Json(Page(null, Job("b", "library_refresh"))),
                _ => null,
            });
            await controller.OpenAsync();
            await controller.LoadMoreAsync(AdminMaintenanceListKind.History);
            Assert.Equal(["a", "b"], controller.State.History.Items.Select(job => job.Id));
            await controller.RefreshAsync(AdminMaintenanceListKind.History);
            Assert.Equal(2, Count(kit, "GET /api/v2/admin/jobs?limit=30&cursor=c1"));
            await controller.RestartAsync(AdminMaintenanceListKind.History);
            Assert.Equal(["a"], controller.State.History.Items.Select(job => job.Id));
            Assert.Equal(2, Count(kit, "GET /api/v2/admin/jobs?limit=30&cursor=c1"));
        }
    }

    [Fact]
    public async Task AuthorityLossClearsListsDraftsBlocksAndSignedLinks()
    {
        var (kit, authority, controller, server) = Create();
        using (kit) using (authority) using (controller)
        {
            server.Override = request => Task.FromResult<HttpResponseMessage?>(request.RequestUri!.PathAndQuery == ExportsRead.Split(' ')[1]
                ? AdminMonitorKit.Json(Page(null, Job("e1", extra: ",\"download_url\":\"/api/v2/admin/jobs/e1/artifact?sig=abc\",\"public_url\":\"https://bucket.example/s?sig=1\",\"public_link_supported\":true")))
                : request.Method == HttpMethod.Post ? throw new HttpRequestException("lost") : (HttpResponseMessage?)null);
            await controller.OpenAsync();
            var owner = controller.State.Owner!;
            Assert.Equal("https://silo.test/api/v2/admin/jobs/e1/artifact?sig=abc", controller.DownloadUri(owner, "e1")!.AbsoluteUri);
            Assert.Equal("https://bucket.example/s?sig=1", controller.PublicUrl(owner, "e1"));
            controller.SetDialogOpen(true);
            controller.EditDraft(draft => draft with { Execution = AdminCatalogImportExecution.Synchronous });
            await controller.SubmitImportAsync();
            Assert.True(controller.State.ImportOutcomeUnknown);
            kit.SetRole("user");
            await Until(() => controller.State.Owner == null);
            Assert.Empty(controller.State.Exports.Items);
            Assert.False(controller.State.Draft.IsDirty);
            Assert.False(controller.State.ImportOutcomeUnknown);
            Assert.Null(controller.DownloadUri(owner, "e1"));
            Assert.Null(controller.PublicUrl(owner, "e1"));
            Assert.False(controller.RequiresLeaveConfirmation);
            var before = kit.Http.Requests.Count;
            await controller.RefreshAsync(AdminMaintenanceListKind.Exports);
            await controller.StartExportAsync();
            Assert.Equal(before, kit.Http.Requests.Count);
        }
    }

    [Fact]
    public async Task LateAnswersOfARetiredOwnerAreDropped()
    {
        var (kit, authority, controller, server) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            var gate = new TaskCompletionSource();
            server.Override = async request =>
            {
                if (request.RequestUri!.PathAndQuery == HistoryRead.Split(' ')[1]) { await gate.Task; return AdminMonitorKit.Json(Page(null, Job("late", "library_refresh"))); }
                return null;
            };
            var refresh = controller.RefreshAsync(AdminMaintenanceListKind.History);
            await Until(() => controller.State.History.Loading);
            kit.SetRole("user");
            await Until(() => controller.State.Owner == null);
            gate.SetResult();
            await refresh;
            Assert.Empty(controller.State.History.Items);
        }
    }

    [Fact]
    public async Task PublishIsOfferedOnlyForCompletedUnpublishedExportsAndRefreshesAfterward()
    {
        var (kit, authority, controller, server) = Create();
        using (kit) using (authority) using (controller)
        {
            server.Override = request => Task.FromResult<HttpResponseMessage?>(request.RequestUri!.AbsolutePath switch
            {
                "/api/v2/admin/jobs" when request.RequestUri.Query.Contains("catalog_export") => AdminMonitorKit.Json(Page(null,
                    Job("done"), Job("running", state: "running"), Job("local", extra: ",\"public_link_supported\":false"), Job("published", extra: ",\"public_url\":\"https://b.example/x\""))),
                "/api/v2/admin/catalog/export-jobs/done/publish" => AdminMonitorKit.Json("""{"job_id":"done","url":"https://b.example/new"}"""),
                _ => null,
            });
            await controller.OpenAsync();
            Assert.Equal(["done"], controller.State.Exports.Items.Where(Presentation.CanCreateLink).Select(job => job.Id));
            await controller.PublishAsync("running");
            await controller.PublishAsync("published");
            Assert.Empty(Posts(kit));
            await controller.PublishAsync("done");
            Assert.Equal(["POST /api/v2/admin/catalog/export-jobs/done/publish"], Posts(kit));
            Assert.Equal(2, Count(kit, ExportsRead));
            Assert.Null(controller.State.PublishingJobId);
        }
    }

    // ── Presentation ──────────────────────────────────────────────────────

    private static AdminMaintenanceJob Read(string json) => JsonSerializer.Deserialize<AdminMaintenanceJob>(json,
        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!;

    [Fact]
    public void StatesMapToWebUiStatusesAndUnknownKindsStayGeneric()
    {
        Assert.Equal("completed", Presentation.Status(Read(Job("1"))));
        Assert.Equal("cancelled", Presentation.Status(Read(Job("1", state: "canceled"))));
        Assert.Equal("running", Presentation.Status(Read(Job("1", state: "canceling"))));
        Assert.Equal("Cancellation requested", Presentation.Message(Read(Job("1", state: "canceling"))));
        Assert.Equal("completed", Presentation.StatusLabel(Read(Job("1"))));
        Assert.Equal("future_kind", Presentation.KindLabel("future_kind"));
        Assert.Equal("", Presentation.KindLabel("<script>"));
        var future = Read(Job("1", "future_kind", "paused"));
        Assert.Equal("paused", Presentation.StatusLabel(future));
        Assert.Null(Presentation.Result(future));
        Assert.Equal("", Presentation.Description(future));
    }

    [Fact]
    public void ExplicitNullKindAndStateHaveSafePresentationFallbacks()
    {
        var job = Read("{\"id\":\"i1\",\"kind\":null,\"state\":null}");
        Assert.Equal("", Presentation.KindLabel(job.Kind));
        Assert.Equal("", Presentation.Status(job));
        Assert.Equal("", Presentation.StatusWord(job));
        Assert.Equal("", Presentation.StatusLabel(job));
        Assert.Null(Presentation.Result(job));
    }

    [Theory]
    [InlineData("{\"type\":null,\"detail\":null}")]
    [InlineData("{\"type\":null,\"detail\":\"Fixed\"}")]
    [InlineData("{\"type\":\"https://silo.test/job_failed\",\"detail\":null}")]
    public void ExplicitNullFailureFieldsHaveAGenericFallback(string failure)
    {
        var job = Read(Job("i1", AdminMaintenanceApi.ImportKind, "failed", ",\"failure\":" + failure));
        Assert.Equal("Job failed", Presentation.FailureText(job));
    }

    [Fact]
    public void ResultsFollowTheOfficialPerKindSummaries()
    {
        var cleanup = Read(Job("1", "image_cache_cleanup", "canceled", ",\"library_result\":{\"deleted_prefixes\":2,\"deleted_s3_objects\":41}"));
        Assert.Equal("Canceled after deleting 41 cached objects; 2 prefixes fully cleaned", Presentation.Result(cleanup));
        var refresh = Read(Job("2", "library_refresh", extra: ",\"library_name\":\"Movies\",\"library_result\":{\"total_items\":0}"));
        Assert.Equal("No library items to refresh", Presentation.Result(refresh));
        Assert.Equal("\"Movies\"", Presentation.Description(refresh));
        var delete = Read(Job("3", "delete_library", extra: ",\"library_id\":\"7\",\"library_result\":{\"deleted_media_files\":1,\"deleted_orphaned_items\":2}"));
        Assert.Equal("Deleted 1 file and 2 items", Presentation.Result(delete));
        Assert.Equal("Library #7", Presentation.Description(delete));
        var export = Read(Job("4", extra: ",\"library_ids\":[\"1\",\"2\"],\"catalog_result\":{\"items_exported\":5,\"files_exported\":9}"));
        Assert.Equal("Exported 5 items, 9 files", Presentation.Result(export));
        Assert.Equal("Exported 5 items and 9 files", Presentation.CatalogListResult(export));
        Assert.Equal("2 libraries", Presentation.ExportScope(export));
        var import = Read(Job("5", AdminMaintenanceApi.ImportKind, extra: ",\"source_label\":\"https://user:pw@seeds.example/a.json.gz?sig=secret#frag\",\"catalog_result\":{\"items_created\":1,\"files_created\":0}"));
        Assert.Equal("https://seeds.example/a.json.gz", Presentation.ImportDescription(import));
        Assert.Equal("Imported 1 item and 0 files", Presentation.CatalogListResult(import));
        var failed = Read(Job("6", "storage_transition", "failed", ",\"failure\":{\"type\":\"https://x/job_failed\",\"title\":\"Job failed\",\"detail\":\"Fixed\",\"retryable\":false}"));
        Assert.Null(Presentation.FailureText(failed));
        var other = Read(Job("7", state: "failed", extra: ",\"failure\":{\"type\":\"https://x/provider\",\"title\":\"t\",\"detail\":\"token=abc\",\"retryable\":false}"));
        Assert.Equal("Job failed", Presentation.FailureText(other));
    }

    [Fact]
    public void ProgressLabelsFollowTheOfficialFormatters()
    {
        Assert.Equal("Queued", Presentation.ProgressText(Read(Job("1", state: "queued"))));
        Assert.Equal("Starting", Presentation.ProgressText(Read(Job("1", state: "running"))));
        Assert.Equal("Waiting", Presentation.ExportProgressText(Read(Job("1", state: "running"))));
        var counted = Read(Job("1", state: "running", extra: ",\"progress\":{\"current\":3,\"total\":4,\"unit\":\"items\"}"));
        Assert.Equal(75, Presentation.ProgressPercent(counted));
        Assert.Equal(12, Presentation.ProgressPercent(Read(Job("1", state: "running"))));
        Assert.Equal(4, Presentation.ProgressPercent(Read(Job("1", state: "queued"))));
    }

    // ── Routing and text ──────────────────────────────────────────────────

    [Fact]
    public void MaintenanceResolvesToItsRouteAndDescendantsKeepTheCatchAll()
    {
        Assert.True(AdminRouteCatalog.TryResolve("/admin/maintenance", out var page));
        Assert.Equal(AdminRouteResolutionKind.Route, page.Kind);
        Assert.Equal(AdminRoute.Maintenance, page.Request!.Route);
        Assert.False(page.Redirected);
        Assert.True(AdminRouteCatalog.TryResolve("/admin/maintenance/x", out var descendant));
        Assert.True(descendant.IsCatchAll);
        Assert.Equal(AdminRoute.Dashboard, descendant.Request!.Route);
        Assert.True(AdminRouteCatalog.TryResolve("/admin/backups", out var backups));
        Assert.Equal(AdminRouteResolutionKind.Route, backups.Kind);
        Assert.Equal(AdminRoute.Backups, backups.Request!.Route);
    }

    [Fact]
    public void ProductionConfigureRegistrationIncludesTheMaintenancePage()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Services", "AdminConfigureServices.cs"));
        Assert.Contains("new AdminPageRegistration(AdminRoute.Maintenance, typeof(AdminMaintenancePage))", source);
        Assert.Contains("AddSingleton<AdminMaintenanceApi>", source);
        Assert.Contains("AddTransient<AdminMaintenanceController>", source);
        Assert.Contains("'Views/Admin/AdminMaintenancePage.xbf'", File.ReadAllText(Path.Combine(root, "installer", "build.ps1")));
        var registry = new AdminPageRegistry([new(AdminRoute.Maintenance, typeof(object))]);
        Assert.True(registry.CanOpen(new AdminRouteRequest(AdminRoute.Maintenance)));
        Assert.Equal(AdminRoute.Maintenance, Assert.Single(Assert.Single(AdminNavigation.SearchGroups("system maintenance", registry.IsRegistered)).Pages).Route);
    }

    [Fact]
    public void EveryMaintenanceKeyUsedBySourceHasEnglishText()
    {
        var root = FindRepositoryRoot();
        var files = Directory.EnumerateFiles(Path.Combine(root, "src"), "AdminMaintenance*.cs", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith("AdminMaintenanceText.cs", StringComparison.Ordinal)).ToArray();
        Assert.True(files.Length >= 5, "the Maintenance sources were found");
        var keys = files.SelectMany(path => Regex.Matches(File.ReadAllText(path), "\"((?:web\\.admin|admin\\.native\\.maintenance)\\.[A-Za-z0-9_.]+)\"").Select(match => match.Groups[1].Value))
            .Where(key => !key.EndsWith('.')).Distinct().ToArray();
        Assert.NotEmpty(keys);
        Assert.Empty(keys.Where(key => !AdminMaintenanceText.Has(key)));
        Assert.Empty(AdminMaintenanceText.Keys.Where(AdminLibrariesText.Has));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SiloPlayer.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
