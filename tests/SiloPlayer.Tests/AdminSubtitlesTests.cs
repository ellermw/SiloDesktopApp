using System.Net;
using System.Net.Http.Headers;
using System.Text;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>
/// Libraries › Subtitle Files: request shapes, response validation, cursor
/// scoping, authority retirement, guarded metadata writes, one-shot deletion,
/// bounded downloads and the bounded uploader picker. Synthetic server only.
/// </summary>
public sealed class AdminSubtitlesTests
{
    private const string ListPath = "/api/v2/admin/subtitles";

    private static string Row(string id, string language = "en", string provider = "upload", string media = "42", string? contentId = "c1") =>
        $$"""{"id":"{{id}}","media_file_id":"{{media}}"{{(contentId == null ? "" : $",\"media_content_id\":\"{contentId}\"")}},"provider":"{{provider}}","language":"{{language}}","format":"srt","release_name":"Release {{id}}","score":0,"hearing_impaired":false,"created_at":"2026-01-02T03:04:05.678Z","uploader_username":"u","media_title":"Title {{id}}","media_type":"movie","file_path":"/data/subs/{{id}}.srt"}""";

    private static string Page(IEnumerable<string> rows, string? next = null, int total = 3, int uploads = 1, int providers = 2) =>
        $$"""{"items":[{{string.Join(',', rows)}}],"page":{"has_more":{{(next != null ? "true" : "false")}}{{(next != null ? $",\"next_cursor\":\"{next}\"" : "")}}},"total":{{total}},"uploads":{{uploads}},"provider_downloads":{{providers}}}""";

    private static string Metadata(string id = "9", string language = "en", string release = "Original", bool hearing = true, string media = "42") =>
        $$"""{"id":"{{id}}","media_file_id":"{{media}}","provider":"upload","language":"{{language}}","format":"srt","release_name":"{{release}}","score":0,"hearing_impaired":{{(hearing ? "true" : "false")}},"created_at":"2026-01-02T03:04:05.678Z","downloaded_by":"2"}""";

    private static HttpResponseMessage Tagged(string json, string etag, bool weak = false, HttpStatusCode status = HttpStatusCode.OK)
    {
        var response = AdminMonitorKit.Json(json, status);
        response.Headers.ETag = new EntityTagHeaderValue(etag, weak);
        return response;
    }

    private static HttpResponseMessage Problem(HttpStatusCode status, string type, string detail) =>
        AdminMonitorKit.Json($$"""{"type":"https://siloserver.org/docs/api/v2/problems/{{type}}","title":"{{type}}","status":{{(int)status}},"detail":"{{detail}}"}""", status);

    private static HttpResponseMessage Users(int start, int count, string? next)
    {
        var items = string.Join(',', Enumerable.Range(start, count).Select(i => "{\"id\":\"" + i + "\",\"username\":\"user" + i + "\",\"email\":\"\",\"role\":\"user\"}"));
        var page = next == null ? "{\"has_more\":false}" : "{\"has_more\":true,\"next_cursor\":\"" + next + "\"}";
        return AdminMonitorKit.Json("{\"items\":[" + items + "],\"page\":" + page + "}");
    }

    private static AdminStoredSubtitle StoredRow(string id = "9", string media = "42", string format = "srt") => new()
    {
        Id = id, MediaFileId = media, Provider = "upload", Language = "en", Format = format, ReleaseName = "Original", MediaTitle = "Title",
    };

    private static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Server(Func<HttpRequestMessage, HttpResponseMessage?> subtitles) =>
        (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v2/admin/users") return Task.FromResult(Users(1, 2, null));
            return Task.FromResult(subtitles(request) ?? AdminMonitorKit.Json("{}", HttpStatusCode.NotFound));
        };

    // ── Requests and validation ──────────────────────────────────────────

    [Fact]
    public async Task ListSendsOnlyActiveTrimmedFiltersAndThePageSize()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(Page([Row("9")])));
        using var lease = authority.Capture();
        var api = new AdminSubtitlesApi(kit.Client);

        await api.ListAsync(lease, new AdminSubtitleListQuery { Provider = "all", Language = "all", UserId = "all", Search = "   " });
        Assert.Equal("GET " + ListPath + "?limit=25", kit.Http.Requests.Last());

        await api.ListAsync(lease, new AdminSubtitleListQuery { Provider = "opensubtitles", Language = "pt-BR", UserId = "7", Search = "  Blu Ray%  ", PageSize = 100 }, "a/b+c");
        Assert.Equal("GET " + ListPath + "?provider=opensubtitles&language=pt-BR&user_id=7&q=Blu%20Ray%25&limit=100&cursor=a%2Fb%2Bc", kit.Http.Requests.Last());

        await api.ListAsync(lease, new AdminSubtitleListQuery { UserId = "07", PageSize = 33 });
        Assert.Equal("GET " + ListPath + "?limit=25", kit.Http.Requests.Last());
    }

    [Theory]
    [InlineData("""{"items":[],"page":{"has_more":true,"next_cursor":"x"},"total":0,"uploads":0,"provider_downloads":0}""")]
    [InlineData("""{"items":[ROW],"page":{"has_more":true},"total":1,"uploads":0,"provider_downloads":0}""")]
    [InlineData("""{"items":[ROW],"page":{"has_more":true,"next_cursor":"same"},"total":1,"uploads":0,"provider_downloads":0}""")]
    [InlineData("""{"items":[ROW],"page":{"has_more":false,"next_cursor":"x"},"total":1,"uploads":0,"provider_downloads":0}""")]
    [InlineData("""{"items":[ROW],"total":1,"uploads":0,"provider_downloads":0}""")]
    [InlineData("""{"items":[ROW,ROW],"page":{"has_more":false},"total":2,"uploads":0,"provider_downloads":0}""")]
    [InlineData("""{"items":[BADID],"page":{"has_more":false},"total":1,"uploads":0,"provider_downloads":0}""")]
    public async Task InconsistentPagesAreRejectedAsInvalidLists(string template)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var json = template.Replace("ROW", Row("9")).Replace("BADID", Row("09"));
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(json));
        using var lease = authority.Capture();
        var error = await Assert.ThrowsAsync<AdminListInconsistentException>(() => new AdminSubtitlesApi(kit.Client).ListAsync(lease, new(), "same"));
        Assert.Equal(AdminFailureKind.InvalidResponse, AdminFailures.Classify(error));
        Assert.Equal(AdminSubtitlesText.T("web.admin.errors.subtitleListInvalid"), AdminSubtitlesFailures.Describe(error, "web.admin.errors.subtitleListInvalid"));
    }

    [Fact]
    public async Task OversizedPagesAreRejected()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(Page(Enumerable.Range(1, 26).Select(i => Row(i.ToString())), total: 26)));
        using var lease = authority.Capture();
        var error = await Assert.ThrowsAsync<AdminListInconsistentException>(() => new AdminSubtitlesApi(kit.Client).ListAsync(lease, new()));
        Assert.Equal(AdminListProblem.OversizedPage, error.Problem);
    }

    [Fact]
    public async Task LargeIdsStayExactAndMissingContentIdsStayNull()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(Page([Row("9007199254740993", contentId: null)], total: 1)));
        using var lease = authority.Capture();
        var page = await new AdminSubtitlesApi(kit.Client).ListAsync(lease, new());
        Assert.Equal("9007199254740993", page.Items[0].Id);
        Assert.Null(page.Items[0].MediaContentId);
        Assert.Equal(1, page.Total);
    }

    // ── List controller ──────────────────────────────────────────────────

    [Fact]
    public async Task FilterChangesRestartAtTheFirstPageAndNeverSendAnOldCursor()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = Server(request => AdminMonitorKit.Json(request.RequestUri!.Query.Contains("cursor=")
            ? Page([Row("8")], total: 2) : Page([Row("9")], next: "older", total: 2)));
        using var controller = new AdminSubtitlesController(new AdminSubtitlesApi(kit.Client), authority);
        await controller.OpenAsync(new AdminSubtitleListQuery { Provider = "upload" });
        Assert.True(controller.State.Loaded);
        Assert.True(controller.State.CanNext);
        Assert.False(controller.State.CanPrevious);
        Assert.Equal(2, controller.State.Total);

        await controller.NextAsync();
        Assert.Contains("cursor=older", kit.Http.Requests.Last());
        Assert.Equal(1, controller.State.PageIndex);
        Assert.True(controller.State.CanPrevious);
        Assert.False(controller.State.CanNext);

        await controller.SetQueryAsync(new AdminSubtitleListQuery { Provider = "subdl" });
        Assert.Equal("GET " + ListPath + "?provider=subdl&limit=25", kit.Http.Requests.Last());
        Assert.Equal(0, controller.State.PageIndex);

        await controller.NextAsync();
        await controller.SetQueryAsync(new AdminSubtitleListQuery { Provider = "subdl", PageSize = 50 });
        Assert.Equal("GET " + ListPath + "?provider=subdl&limit=50", kit.Http.Requests.Last());

        await controller.NextAsync();
        await controller.PreviousAsync();
        Assert.DoesNotContain("cursor=", kit.Http.Requests.Last());
        Assert.Equal(0, controller.State.PageIndex);
    }

    [Fact]
    public async Task ALateResponseForEarlierFiltersIsDiscarded()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var slow = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        kit.Http.Respond = (request, _) => request.RequestUri!.AbsolutePath == "/api/v2/admin/users" ? Task.FromResult(Users(1, 1, null))
            : request.RequestUri.Query.Contains("provider=upload") ? slow.Task
            : Task.FromResult(AdminMonitorKit.Json(Page([Row("5", provider: "subdl")], total: 1)));
        using var controller = new AdminSubtitlesController(new AdminSubtitlesApi(kit.Client), authority);
        await controller.OpenAsync(new());
        var stale = controller.SetQueryAsync(new AdminSubtitleListQuery { Provider = "upload" });
        Assert.True(controller.State.Loading);
        await controller.SetQueryAsync(new AdminSubtitleListQuery { Provider = "subdl" });
        slow.SetResult(AdminMonitorKit.Json(Page([Row("1")], total: 1)));
        await stale;
        Assert.Equal("5", Assert.Single(controller.State.Items).Id);
        Assert.Equal("subdl", controller.State.Query.Provider);
    }

    [Fact]
    public async Task LoadFailuresAreDistinctRetryableStatesAndStaleCursorsRestart()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var failCursor = true;
        kit.Http.Respond = Server(request => request.RequestUri!.Query.Contains("cursor=")
            ? failCursor ? Problem(HttpStatusCode.BadRequest, "invalid_cursor", "Invalid subtitle cursor.") : AdminMonitorKit.Json(Page([Row("8")], total: 2))
            : AdminMonitorKit.Json(Page([Row("9")], next: "older", total: 2)));
        using var controller = new AdminSubtitlesController(new AdminSubtitlesApi(kit.Client), authority);
        await controller.OpenAsync(new());
        await controller.NextAsync();
        Assert.NotNull(controller.State.Error);
        Assert.Empty(controller.State.Items);
        Assert.True(controller.State.CanPrevious);
        Assert.True(controller.State.ShowPagination);
        await controller.ReloadAsync();
        Assert.Null(controller.State.Error);
        Assert.Equal(0, controller.State.PageIndex);
        Assert.DoesNotContain("cursor=", kit.Http.Requests.Last());

        kit.Http.Respond = Server(_ => Problem(HttpStatusCode.ServiceUnavailable, "dependency_unavailable", "Subtitle administration is unavailable."));
        await controller.ReloadAsync();
        Assert.Equal(AdminFailureKind.Server, controller.State.ErrorKind);
        kit.Http.Respond = Server(_ => Problem(HttpStatusCode.Forbidden, "permission_denied", "Denied."));
        await controller.ReloadAsync();
        Assert.Equal(AdminFailureKind.Forbidden, controller.State.ErrorKind);
        Assert.DoesNotContain(kit.Http.Requests, request => !request.StartsWith("GET "));
    }

    [Fact]
    public async Task AuthorityChangesClearRowsAndDiscardLateResponses()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = Server(_ => AdminMonitorKit.Json(Page([Row("9")], next: "older")));
        using var controller = new AdminSubtitlesController(new AdminSubtitlesApi(kit.Client), authority);
        var retired = 0;
        controller.Retired += () => retired++;
        await controller.OpenAsync(new());
        Assert.NotEmpty(controller.State.Uploaders);
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        kit.Http.Respond = (_, _) => pending.Task;
        var next = controller.NextAsync();
        kit.SetRole("user");
        pending.SetResult(AdminMonitorKit.Json(Page([Row("1")])));
        await next;
        Assert.Equal(1, retired);
        Assert.Null(controller.State.Owner);
        Assert.Empty(controller.State.Items);
        Assert.Empty(controller.State.Uploaders);
        Assert.Throws<MonitorAuthorityRetiredException>(() => controller.CaptureAction());
        await controller.OpenAsync(new());
        Assert.Null(controller.State.Owner);
    }

    [Fact]
    public async Task UploaderPickerIsBoundedAndTruthfulWhenTruncated()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var userPages = 0;
        kit.Http.Respond = (request, _) =>
        {
            if (request.RequestUri!.AbsolutePath != "/api/v2/admin/users") return Task.FromResult(AdminMonitorKit.Json(Page([Row("9")])));
            Assert.Contains("limit=200", request.RequestUri.Query);
            var page = Interlocked.Increment(ref userPages);
            return Task.FromResult(Users((page - 1) * 200 + 1, 200, "u" + page));
        };
        using var controller = new AdminSubtitlesController(new AdminSubtitlesApi(kit.Client), authority);
        await controller.OpenAsync(new());
        Assert.Equal(5, userPages);
        Assert.Equal(1000, controller.State.Uploaders.Count);
        Assert.True(controller.State.UploadersTruncated);
        Assert.Contains("1,000", AdminSubtitlesText.T("admin.native.subtitles.uploadersTruncated", ("count_text", 1000.ToString("N0", System.Globalization.CultureInfo.InvariantCulture))));
    }

    [Fact]
    public async Task UploaderFailureDoesNotHideTheListAndRetriesWithIt()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == "/api/v2/admin/users"
            ? Problem(HttpStatusCode.InternalServerError, "internal_error", "x") : AdminMonitorKit.Json(Page([Row("9")])));
        using var controller = new AdminSubtitlesController(new AdminSubtitlesApi(kit.Client), authority);
        await controller.OpenAsync(new());
        Assert.NotNull(controller.State.UploadersError);
        Assert.Single(controller.State.Items);
        kit.Http.Respond = Server(_ => AdminMonitorKit.Json(Page([Row("9")])));
        await controller.ReloadAsync();
        Assert.Null(controller.State.UploadersError);
        Assert.Equal(2, controller.State.Uploaders.Count);
    }

    // ── Metadata editor ──────────────────────────────────────────────────

    [Fact]
    public async Task EditorSendsOnlyChangedFieldsWithTheExactValidatorAndRefreshesTheList()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = Server(request => request.Method == HttpMethod.Patch
            ? Tagged(Metadata(release: "New", hearing: true), "\"v2\"")
            : request.RequestUri!.AbsolutePath == ListPath + "/9" ? Tagged(Metadata(), "\"v1\"") : AdminMonitorKit.Json(Page([Row("9")])));
        using var controller = new AdminSubtitlesController(new AdminSubtitlesApi(kit.Client), authority);
        var notices = new List<string>();
        controller.Notice += (message, _) => notices.Add(message);
        await controller.OpenAsync(new());
        using var editor = controller.BeginEdit(controller.State.Items[0]);
        await editor.LoadAsync();
        Assert.Equal("Original", editor.State.ReleaseName);
        Assert.False(editor.State.CanSave);
        editor.SetReleaseName("New");
        Assert.True(editor.State.IsDirty);
        Assert.False(editor.State.LanguageChanged);
        Assert.True(editor.State.CanSave);
        var listReads = kit.Http.Requests.Count(request => request.StartsWith("GET " + ListPath + "?"));
        Assert.True(await editor.SaveAsync());
        Assert.Equal("""{"release_name":"New"}""", kit.Http.Bodies.ElementAt(kit.Http.Requests.ToList().FindIndex(request => request.StartsWith("PATCH"))));
        Assert.True(editor.State.Saved);
        Assert.False(editor.State.IsDirty);
        Assert.Contains(AdminSubtitlesText.T("web.admin.subtitles.toast.updated"), notices);
        await WaitFor(() => kit.Http.Requests.Count(request => request.StartsWith("GET " + ListPath + "?")) > listReads);
    }

    [Fact]
    public async Task PreconditionFailureKeepsTheDraftDisablesSaveAndOnlyAnExplicitReloadReenablesIt()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        string? sentIfMatch = null;
        var version = 1;
        kit.Http.Respond = Server(request =>
        {
            if (request.Method == HttpMethod.Patch)
            {
                sentIfMatch = request.Headers.IfMatch.Single().ToString();
                return Problem(HttpStatusCode.PreconditionFailed, "precondition_failed", "Subtitle metadata changed; review the current value before saving.");
            }
            return request.RequestUri!.AbsolutePath == ListPath + "/9" ? Tagged(Metadata(release: version == 1 ? "Original" : "Other"), $"\"v{version}\"") : AdminMonitorKit.Json(Page([Row("9")]));
        });
        using var controller = new AdminSubtitlesController(new AdminSubtitlesApi(kit.Client), authority);
        await controller.OpenAsync(new());
        using var editor = controller.BeginEdit(controller.State.Items[0]);
        await editor.LoadAsync();
        editor.SetLanguage("fr");
        Assert.True(editor.State.LanguageChanged);
        Assert.False(await editor.SaveAsync());
        Assert.Equal("\"v1\"", sentIfMatch);
        Assert.NotNull(editor.State.Failure);
        Assert.Equal("fr", editor.State.Language);
        Assert.False(editor.State.CanSave);
        Assert.True(editor.State.CanReload);
        Assert.False(await editor.SaveAsync());
        Assert.Single(kit.Http.Requests, request => request.StartsWith("PATCH"));

        version = 2;
        await editor.LoadAsync();
        Assert.Null(editor.State.Failure);
        Assert.Equal("fr", editor.State.Language);
        Assert.Equal("Other", editor.State.Current!.Value.ReleaseName);
        Assert.Equal("Other", editor.State.ReleaseName);
        Assert.Equal(new AdminSubtitlePatch("fr", null, null), editor.State.Patch);
        Assert.Equal("\"v2\"", editor.State.Current.ETag);
        Assert.True(editor.State.CanSave);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task EditorRefusesWeakOrMissingValidatorsAndChangedIdentities(bool weak, bool wrongIdentity)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = Server(request => request.RequestUri!.AbsolutePath == ListPath + "/9"
            ? wrongIdentity ? Tagged(Metadata(media: "43"), "\"v1\"")
            : weak ? Tagged(Metadata(), "\"v1\"", weak: true) : AdminMonitorKit.Json(Metadata())
            : AdminMonitorKit.Json(Page([Row("9")])));
        using var controller = new AdminSubtitlesController(new AdminSubtitlesApi(kit.Client), authority);
        await controller.OpenAsync(new());
        using var editor = controller.BeginEdit(controller.State.Items[0]);
        await editor.LoadAsync();
        Assert.Null(editor.State.Current);
        Assert.Equal(wrongIdentity ? AdminSubtitlesText.T("web.admin.errors.subtitleIdentityChanged") : AdminSubtitlesText.T("web.admin.errors.subtitleValidatorRequired"), editor.State.LoadError);
        Assert.False(editor.State.CanSave);
        Assert.DoesNotContain(kit.Http.Requests, request => request.StartsWith("PATCH"));
    }

    [Fact]
    public async Task OverlongReleaseNamesAreValidatedBeforeSending()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = Server(request => request.RequestUri!.AbsolutePath == ListPath + "/9" ? Tagged(Metadata(), "\"v1\"") : AdminMonitorKit.Json(Page([Row("9")])));
        using var controller = new AdminSubtitlesController(new AdminSubtitlesApi(kit.Client), authority);
        await controller.OpenAsync(new());
        using var editor = controller.BeginEdit(controller.State.Items[0]);
        await editor.LoadAsync();
        editor.SetReleaseName(new string('a', AdminSubtitlePatch.MaxReleaseNameLength + 1));
        Assert.NotNull(editor.State.Validation);
        Assert.False(editor.State.CanSave);
        Assert.False(await editor.SaveAsync());
        editor.Discard();
        Assert.False(editor.State.IsDirty);
        Assert.DoesNotContain(kit.Http.Requests, request => request.StartsWith("PATCH"));
    }

    [Fact]
    public async Task EditorRetiresWithTheOwnerAndCannotSave()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = Server(request => request.RequestUri!.AbsolutePath == ListPath + "/9" ? Tagged(Metadata(), "\"v1\"") : AdminMonitorKit.Json(Page([Row("9")])));
        using var controller = new AdminSubtitlesController(new AdminSubtitlesApi(kit.Client), authority);
        await controller.OpenAsync(new());
        using var editor = controller.BeginEdit(controller.State.Items[0]);
        await editor.LoadAsync();
        editor.SetReleaseName("New");
        kit.Auth.SelectProfile("p2", null, new SiloPlayer.Core.Models.Auth.Profile { Id = "p2", Name = "Kid", IsPrimary = false });
        Assert.False(await editor.SaveAsync());
        Assert.False(editor.ActionLease.IsCurrent);
        Assert.DoesNotContain(kit.Http.Requests, request => request.StartsWith("PATCH"));
    }

    [Fact]
    public void PatchBodiesOmitUnchangedFieldsAndLanguagesFollowTheServerPattern()
    {
        var baseline = new AdminSubtitleMetadata { Id = "9", MediaFileId = "42", Language = "en", ReleaseName = "R", HearingImpaired = false };
        Assert.True(AdminSubtitlePatch.Between(baseline, "en", "R", false).IsEmpty);
        var patch = AdminSubtitlePatch.Between(baseline, "pt-BR", "R", true);
        Assert.Equal(new[] { "language", "hearing_impaired" }, patch.ToBody().Keys);
        Assert.True(AdminSubtitleIds.IsPatchLanguage("pt-BR"));
        Assert.True(AdminSubtitleIds.IsPatchLanguage("x-klingon"));
        Assert.False(AdminSubtitleIds.IsPatchLanguage("english!"));
        Assert.False(AdminSubtitleIds.IsPatchLanguage(""));
    }

    // ── Delete ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeletionReadsTheCurrentRecordAndOwnsExactlyOneGuardedDelete()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        string? ifMatch = null;
        kit.Http.Respond = Server(request =>
        {
            if (request.Method == HttpMethod.Delete) { ifMatch = request.Headers.IfMatch.Single().ToString(); return new HttpResponseMessage(HttpStatusCode.NoContent); }
            return request.RequestUri!.AbsolutePath == ListPath + "/9" ? Tagged(Metadata(release: ""), "\"v7\"") : AdminMonitorKit.Json(Page([Row("9")]));
        });
        using var controller = new AdminSubtitlesController(new AdminSubtitlesApi(kit.Client), authority);
        await controller.OpenAsync(new());
        using var deletion = controller.BeginDelete(controller.State.Items[0]);
        Assert.False(deletion.State.CanConfirm);
        await deletion.PrepareAsync();
        Assert.True(deletion.State.CanConfirm);
        Assert.True(await deletion.ConfirmAsync());
        Assert.Equal("\"v7\"", ifMatch);
        Assert.True(deletion.State.Deleted);
        Assert.False(await deletion.ConfirmAsync());
        Assert.Equal(AdminSubtitlesText.T("web.admin.errors.subtitleDeleteAttempted"), deletion.State.Error);
        Assert.Single(kit.Http.Requests, request => request.StartsWith("DELETE"));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "unexpected_success")]
    [InlineData(HttpStatusCode.Accepted, "unexpected_success")]
    [InlineData(HttpStatusCode.NotFound, "not_found")]
    [InlineData(HttpStatusCode.PreconditionFailed, "precondition_failed")]
    [InlineData(HttpStatusCode.InternalServerError, "internal_error")]
    public async Task FailedDeletesAreNeitherSuccessNorReplayed(HttpStatusCode status, string type)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = Server(request => request.Method == HttpMethod.Delete ? Problem(status, type, "Unable to confirm subtitle deletion.")
            : request.RequestUri!.AbsolutePath == ListPath + "/9" ? Tagged(Metadata(), "\"v1\"") : AdminMonitorKit.Json(Page([Row("9")])));
        using var controller = new AdminSubtitlesController(new AdminSubtitlesApi(kit.Client), authority);
        await controller.OpenAsync(new());
        using var deletion = controller.BeginDelete(controller.State.Items[0]);
        await deletion.PrepareAsync();
        Assert.False(await deletion.ConfirmAsync());
        Assert.False(deletion.State.Deleted);
        Assert.NotNull(deletion.State.Error);
        Assert.False(deletion.State.CanConfirm);
        Assert.False(await deletion.ConfirmAsync());
        Assert.Single(kit.Http.Requests, request => request.StartsWith("DELETE"));
    }

    [Fact]
    public async Task DeletionWithoutAStrongValidatorCannotConfirm()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = Server(request => request.RequestUri!.AbsolutePath == ListPath + "/9" ? Tagged(Metadata(), "\"v1\"", weak: true) : AdminMonitorKit.Json(Page([Row("9")])));
        using var controller = new AdminSubtitlesController(new AdminSubtitlesApi(kit.Client), authority);
        await controller.OpenAsync(new());
        using var deletion = controller.BeginDelete(controller.State.Items[0]);
        await deletion.PrepareAsync();
        Assert.NotNull(deletion.State.Error);
        Assert.False(deletion.State.CanConfirm);
        Assert.False(await deletion.ConfirmAsync());
        Assert.DoesNotContain(kit.Http.Requests, request => request.StartsWith("DELETE"));
    }

    // ── Download ─────────────────────────────────────────────────────────

    private static HttpResponseMessage Bytes(byte[] bytes, string mediaType, long? declaredLength = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        response.Content.Headers.TryAddWithoutValidation("Content-Disposition", "attachment; filename=\"..\\..\\evil.exe\"");
        if (declaredLength is { } length) response.Content.Headers.ContentLength = length;
        return response;
    }

    [Fact]
    public async Task DownloadsPublishTheExactBytesUnderADeterministicName()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var payload = Encoding.UTF8.GetBytes("1\n00:00:01,000 --> 00:00:02,000\nHello\n");
        kit.Http.Respond = Server(request => request.RequestUri!.AbsolutePath == ListPath + "/9/download" ? Bytes(payload, "application/x-subrip") : AdminMonitorKit.Json(Page([Row("9")])));
        using var controller = new AdminSubtitlesController(new AdminSubtitlesApi(kit.Client), authority);
        await controller.OpenAsync(new());
        var directory = Directory.CreateTempSubdirectory("silo-subtitle-download-");
        try
        {
            var row = controller.State.Items[0];
            Assert.Equal("subtitle-9.srt", AdminSubtitleIds.DownloadFileName(row));
            Assert.Equal("subtitle-9.bin", AdminSubtitleIds.DownloadFileName(StoredRow(format: "../x")));
            var target = Path.Combine(directory.FullName, AdminSubtitleIds.DownloadFileName(row));
            using var action = controller.CaptureAction();
            var outcome = await controller.DownloadAsync(action, row, target);
            Assert.True(outcome.Success);
            Assert.Equal(payload, await File.ReadAllBytesAsync(target));
            Assert.Single(directory.GetFiles());
            Assert.Null(controller.State.DownloadingId);
            Assert.Contains(kit.Http.Requests, request => request == "GET " + ListPath + "/9/download");
        }
        finally { directory.Delete(true); }
    }

    [Theory]
    [InlineData("text/html", 0L, AdminSubtitleDownloadProblem.UnexpectedType)]
    [InlineData("application/x-subrip", AdminSubtitlesApi.MaxDownloadBytes + 1L, AdminSubtitleDownloadProblem.TooLarge)]
    [InlineData("text/vtt", 999L, AdminSubtitleDownloadProblem.Incomplete)]
    public async Task RefusedDownloadsPublishNothing(string mediaType, long declared, AdminSubtitleDownloadProblem problem)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var bytes = Encoding.UTF8.GetBytes("WEBVTT\n");
        kit.Http.Respond = Server(request => request.RequestUri!.AbsolutePath == ListPath + "/9/download"
            ? Bytes(bytes, mediaType, declared == 0 ? null : declared) : AdminMonitorKit.Json(Page([Row("9")])));
        using var lease = authority.Capture();
        var api = new AdminSubtitlesApi(kit.Client);
        var refused = await Assert.ThrowsAsync<AdminSubtitleDownloadException>(() => api.DownloadAsync(lease, StoredRow()));
        Assert.Equal(problem, refused.Problem);
        using var controller = new AdminSubtitlesController(api, authority);
        await controller.OpenAsync(new());
        var directory = Directory.CreateTempSubdirectory("silo-subtitle-download-");
        try
        {
            using var action = controller.CaptureAction();
            var outcome = await controller.DownloadAsync(action, controller.State.Items[0], Path.Combine(directory.FullName, "subtitle-9.srt"));
            Assert.False(outcome.Success);
            Assert.NotNull(outcome.Error);
            Assert.Empty(directory.GetFileSystemInfos());
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public async Task ARetirementDuringDownloadPublishesNothing()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var retire = false;
        kit.Http.Respond = Server(request =>
        {
            if (request.RequestUri!.AbsolutePath != ListPath + "/9/download") return AdminMonitorKit.Json(Page([Row("9")]));
            if (retire) kit.SetRole("user");
            return Bytes(Encoding.UTF8.GetBytes("1"), "application/octet-stream");
        });
        using var controller = new AdminSubtitlesController(new AdminSubtitlesApi(kit.Client), authority);
        await controller.OpenAsync(new());
        var directory = Directory.CreateTempSubdirectory("silo-subtitle-download-");
        try
        {
            var row = controller.State.Items[0];
            using var action = controller.CaptureAction();
            retire = true;
            var outcome = await controller.DownloadAsync(action, row, Path.Combine(directory.FullName, "subtitle-9.srt"));
            Assert.False(outcome.Success);
            Assert.Null(outcome.Error);
            Assert.Empty(directory.GetFileSystemInfos());
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public async Task DownloadHttpFailuresUseTheWebUiStatusMessage()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = Server(request => request.RequestUri!.AbsolutePath == ListPath + "/9/download"
            ? Problem(HttpStatusCode.NotFound, "not_found", "Stored subtitle not found.") : AdminMonitorKit.Json(Page([Row("9")])));
        using var controller = new AdminSubtitlesController(new AdminSubtitlesApi(kit.Client), authority);
        await controller.OpenAsync(new());
        var directory = Directory.CreateTempSubdirectory("silo-subtitle-download-");
        try
        {
            using var action = controller.CaptureAction();
            var outcome = await controller.DownloadAsync(action, controller.State.Items[0], Path.Combine(directory.FullName, "subtitle-9.srt"));
            Assert.Equal(AdminSubtitlesText.T("web.admin.errors.subtitleDownloadFailed", ("status", 404)), outcome.Error);
            Assert.Equal("Subtitle download failed (404).", outcome.Error);
        }
        finally { directory.Delete(true); }
    }

    // ── Text and presentation ────────────────────────────────────────────

    [Fact]
    public void TextMatchesTheWebUiCatalogAndFormatsPlurals()
    {
        Assert.Equal(77, AdminSubtitlesText.Keys.Count(key => key.StartsWith("web.admin.", StringComparison.Ordinal)));
        Assert.All(AdminSubtitlesText.Keys, key => Assert.False(string.IsNullOrWhiteSpace(AdminSubtitlesText.T(key))));
        Assert.Equal("1 subtitle on this page; 3 currently match", AdminSubtitlesText.T("web.admin.subtitles.page.summary", ("count", 1), ("total", 3)));
        Assert.Equal("25 subtitles on this page; 40 currently match", AdminSubtitlesText.T("web.admin.subtitles.page.summary", ("count", 25), ("total", 40)));
        Assert.Equal("50 / page", AdminSubtitlesText.PageSizeOption(50));
        Assert.Equal("Remove EN subtitle “Original”? Metadata is removed first; stored-file cleanup is best effort.",
            AdminSubtitlesText.T("web.admin.common.subtitles.delete.description", ("language", "EN"), ("name", "Original")));
        Assert.Equal(new[] { "all", "upload", "opensubtitles", "subdl", "subsource" }, AdminSubtitlesText.ProviderOptions.Select(option => option.Value));
        Assert.Equal("OpenSubtitles", AdminSubtitlesText.ProviderLabel("opensubtitles"));
        Assert.Equal("legacy", AdminSubtitlesText.ProviderLabel("legacy"));
        Assert.Equal(39, AdminSubtitlesText.LanguageOptions().Count);
        Assert.Equal("evil.srt", AdminSubtitlesText.Basename(@"C:\subs\..\evil.srt"));
        Assert.Equal("—", AdminSubtitlesText.Basename(""));
    }

    [Fact]
    public void RelativeTimesFloorAndSwitchToAnAbsoluteDateAfterThirtyDays()
    {
        var now = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
        string Absolute(DateTimeOffset value) => "abs";
        Assert.Equal("just now", AdminSubtitlesText.Relative(now.AddSeconds(-59), now, Absolute));
        Assert.Equal("59m ago", AdminSubtitlesText.Relative(now.AddMinutes(-59.9), now, Absolute));
        Assert.Equal("1h ago", AdminSubtitlesText.Relative(now.AddMinutes(-119), now, Absolute));
        Assert.Equal("29d ago", AdminSubtitlesText.Relative(now.AddDays(-29.9), now, Absolute));
        Assert.Equal("abs", AdminSubtitlesText.Relative(now.AddDays(-30), now, Absolute));
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var waited = 0; waited < 4000 && !condition(); waited += 20) await Task.Delay(20);
        Assert.True(condition());
    }
}
