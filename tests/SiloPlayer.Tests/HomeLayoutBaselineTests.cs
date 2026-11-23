using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

/// <summary>
/// Home layout export and import against official silo-server 3b2b32168 (#4648 hide server rows added since an export,
/// #4410 rows of collections). The cases port web/src/lib/homeLayoutBaseline.test.ts, homeLayoutTransfer.test.ts,
/// useHomeLayoutExport.test.tsx and components/sections/HomeLayoutTransfer.test.tsx.
/// </summary>
public sealed class HomeLayoutBaselineTests
{
    private static ProfileSectionBaselineRow Row(string id, string type, string title, string token) => new() { Id = id, SectionType = type, Title = title, Token = token };
    private static ProfileSectionBaseline Baseline(params ProfileSectionBaselineRow[] rows) => new() { SectionTokens = rows.Select(row => row.Token).ToList(), VisibleSections = [.. rows] };
    private static ProfileSectionBaseline OfficialBaseline() => Baseline(Row("default-continue-watching", "continue_watching", "Continue Watching", "tok-continue"),
        Row("s-collections", "collection_posters", "Collections", "tok-collections"));
    private static HomeLayoutPage Page(string[]? tokens, params string[] mentioned)
        => new() { ServerSectionTokens = tokens?.ToList(), Overrides = mentioned.Select(id => new SectionOverride { SectionId = id }).ToList() };
    private static string[] Ids(IEnumerable<ProfileSectionBaselineRow> rows) => rows.Select(row => row.Id).ToArray();

    // ---- rowsAddedSinceExport (homeLayoutBaseline.test.ts)
    [Fact]
    public void OffersRowsAbsentAtExportNotADisabledRowThatExistedThenAndWasReEnabled()
    {
        var baseline = OfficialBaseline(); baseline.VisibleSections.Add(Row("s-reenabled", "hidden_gems", "Hidden Gems", "tok-disabled"));
        Assert.Equal(["s-collections"], Ids(HomeLayoutBaseline.RowsAddedSinceExport(Page(["tok-continue", "tok-disabled"]), baseline)));
    }
    [Fact]
    public void OffersNothingWhenNoRowTheProfileSeesMatchesTheExport()
        => Assert.Empty(HomeLayoutBaseline.RowsAddedSinceExport(Page(["tok-disabled"]), OfficialBaseline()));
    [Fact]
    public void OffersNothingForAFileWithoutTokensOrWithAnEmptyList()
    {
        Assert.Empty(HomeLayoutBaseline.RowsAddedSinceExport(Page(null, "default-continue-watching"), OfficialBaseline()));
        Assert.Empty(HomeLayoutBaseline.RowsAddedSinceExport(Page([]), OfficialBaseline()));
    }
    [Fact]
    public void LeavesOutANewRowTheFileAlreadyHasAChangeFor()
        => Assert.Empty(HomeLayoutBaseline.RowsAddedSinceExport(Page(["tok-continue"], "s-collections"), OfficialBaseline()));

    // ---- file format (homeLayoutTransfer.test.ts "round-trips server_section_tokens ...")
    private static string FileJson(string pages, string server = "srv-1", string libraries = "[]")
        => $$"""{"format":"silo-home-layout","version":1,"exported_at":"2026-11-20T12:00:00.000Z","server_id":"{{server}}","libraries":{{libraries}},"pages":[{{pages}}]}""";
    private static List<string>? TokensOf(string pageJson) => HomeLayoutTransfer.Parse(FileJson(pageJson)).Pages.Single().ServerSectionTokens;
    [Fact]
    public void RoundTripsServerSectionTokensAndDegradesMalformedOrTruncatedListsToEmpty()
    {
        var exported = new HomeLayoutFile { ServerId = "server-a", Pages = [new() { ServerSectionTokens = ["tok-1", "tok-2"] }] };
        Assert.Equal(["tok-1", "tok-2"], HomeLayoutTransfer.Parse(HomeLayoutTransfer.Serialize(exported)).Pages.Single().ServerSectionTokens);
        string Many(int count) => $$"""{"scope":"home","overrides":[],"server_section_tokens":[{{string.Join(",", Enumerable.Range(0, count).Select(i => $"\"tok-{i}\""))}}]}""";
        // A page can have more server rows than one save takes overrides.
        Assert.Equal(501, TokensOf(Many(501))!.Count);
        Assert.Empty(TokensOf(Many(10_001))!);
        Assert.Null(TokensOf("""{"scope":"home","overrides":[]}"""));
        Assert.Empty(TokensOf("""{"scope":"home","overrides":[],"server_section_tokens":"tok"}""")!);
        Assert.Empty(TokensOf("""{"scope":"home","overrides":[],"server_section_tokens":null}""")!);
        Assert.Empty(TokensOf("""{"scope":"home","overrides":[],"server_section_tokens":["a",""]}""")!);
        Assert.Empty(TokensOf("""{"scope":"home","overrides":[],"server_section_tokens":["a",7]}""")!);
        Assert.Empty(TokensOf($$"""{"scope":"home","overrides":[],"server_section_tokens":["{{new string('x', 129)}}"]}""")!);
        Assert.Equal(["a", "b"], TokensOf("""{"scope":"home","overrides":[],"server_section_tokens":["a","a","b"]}"""));
    }
    [Fact]
    public void ExportWritesTheOfficialFieldAndLeavesUnsetFlagsOut()
    {
        var raw = new RawSectionOverride { Id = "saved", SectionId = "admin-1", Position = 2, Hidden = false, Removed = false, IsUserAdded = false };
        var json = HomeLayoutTransfer.Serialize(new HomeLayoutFile { ServerId = "s", ExportedAt = "2026-11-20T12:00:00.000Z",
            Pages = [new() { Overrides = [HomeLayoutTransfer.ToWrite(raw)], ServerSectionTokens = ["t1"] }] });
        using var document = JsonDocument.Parse(json);
        var page = document.RootElement.GetProperty("pages")[0];
        Assert.Equal("t1", page.GetProperty("server_section_tokens")[0].GetString());
        var row = page.GetProperty("overrides")[0];
        Assert.Equal("admin-1", row.GetProperty("section_id").GetString()); Assert.Equal(2, row.GetProperty("position").GetInt32());
        foreach (var absent in new[] { "id", "hidden", "removed", "is_user_added", "title", "config" }) Assert.False(row.TryGetProperty(absent, out _), absent);
        Assert.Equal("silo-home-layout", document.RootElement.GetProperty("format").GetString()); Assert.Equal(1, document.RootElement.GetProperty("version").GetInt32());
        var hidden = HomeLayoutTransfer.ToWrite(new RawSectionOverride { SectionId = "x", Hidden = true });
        Assert.True(hidden.Hidden);
    }
    [Fact]
    public void ParseReportsTheOfficialMessages()
    {
        Assert.Equal("This isn't valid JSON.", Assert.Throws<InvalidDataException>(() => HomeLayoutTransfer.Parse("{nope")).Message);
        Assert.Equal("This isn't a Silo home layout export.", Assert.Throws<InvalidDataException>(() => HomeLayoutTransfer.Parse("[]")).Message);
        Assert.Equal("This isn't a Silo home layout export.", Assert.Throws<InvalidDataException>(() => HomeLayoutTransfer.Parse("""{"format":"other","version":1}""")).Message);
        Assert.Equal("This export comes from a newer version of Silo.", Assert.Throws<InvalidDataException>(() => HomeLayoutTransfer.Parse("""{"format":"silo-home-layout","version":2,"pages":[]}""")).Message);
        Assert.Equal("This home layout export is incomplete or damaged.", Assert.Throws<InvalidDataException>(() => HomeLayoutTransfer.Parse("""{"format":"silo-home-layout","version":1,"pages":[]}""")).Message);
    }

    // ---- merge (homeLayoutTransfer.test.ts "preserves existing destination overrides ..." and importPage)
    private static RawSectionOverride Stored(string id, string? sectionId = null, int? position = null, bool removed = false, string title = "", bool userAdded = false, string userType = "")
        => new() { Id = id, SectionId = sectionId ?? "", Position = position, Removed = removed, Title = title, IsUserAdded = userAdded, UserSectionType = userType };
    [Fact]
    public void HidingRowsOnAPageWhoseFileOverridesWereAllSkippedKeepsTheExistingOverrides()
    {
        var existing = new[] { Stored("saved-1", "admin-1", 0), Stored("saved-col", "s-collections", 2, title: "My Collections"), Stored("saved-gone", "s-removed", removed: true),
            Stored("saved-custom", userAdded: true, userType: "hidden_gems") };
        var merged = HomeLayoutMerge.Build(new() { ReplaceExisting = false }, existing, true, ["s-collections", "s-removed", "s-new"]);
        Assert.Equal(5, merged.Count);
        Assert.Equal(["saved-1", "saved-col", "saved-gone", "saved-custom"], merged.Take(4).Select(row => row.Id));
        Assert.True(merged[1].Hidden); Assert.Equal("My Collections", merged[1].Title); Assert.Equal(2, merged[1].Position);
        Assert.NotEqual(true, merged[0].Hidden); Assert.NotEqual(true, merged[2].Hidden); Assert.True(merged[2].Removed);
        Assert.Equal("s-new", merged[4].SectionId); Assert.True(merged[4].Hidden); Assert.False(string.IsNullOrEmpty(merged[4].Id));
    }
    [Fact]
    public void HidingRowsOnAReplacedPageAddsHiddenOverridesForRowsTheFileDoesNotMention()
    {
        var page = new HomeLayoutPage { Overrides = [new() { SectionId = "admin-1", Position = 1, Id = "new" }, new() { UserSectionType = "hidden_gems", Id = "new-custom" }] };
        var merged = HomeLayoutMerge.Build(page, [Stored("saved-collections", "s-collections")], true, ["s-collections", "admin-1", "s-collections"]);
        Assert.Equal(3, merged.Count);
        Assert.Equal("admin-1", merged[0].SectionId); Assert.NotEqual(true, merged[0].Hidden);
        Assert.Equal("new-custom", merged[1].Id);
        Assert.Equal(("saved-collections", "s-collections", true), (merged[2].Id, merged[2].SectionId, merged[2].Hidden == true));
        // Another server never hides this server's rows.
        Assert.Empty(HomeLayoutMerge.Build(new(), [], false, ["s-collections"]));
    }

    // ---- fakes
    private static HomeLayoutTransferService Service(SiloApiClient client) => new(new SettingsApi(client), new CatalogApi(client), new CollectionsApi(client), new AuthApi(client));
    private sealed class Wire : HttpMessageHandler
    {
        public string ServerId = "srv-1";
        public Dictionary<string, string> Baselines = new() { ["home:"] = BaselineJson(OfficialBaseline()), ["library:1"] = BaselineJson(Baseline(Row("lib-new", "recently_added", "Recently Added in Movies", "lib-tok"))) };
        public Dictionary<string, string> Stored = [];
        public HashSet<string> FailBaseline = [];
        public bool FailAllBaselines, FailSaveOn;
        public string? FailSavePage;
        public List<string> BaselineReads = []; public List<(string Page, JsonElement Body)> Saved = [];
        public List<string> PersonalCollections = [];
        public (string Path, string? Scope, bool Value)? HideWatchedWrite;
        public SiloApiClient Client() { var client = new SiloApiClient(new HttpClient(this)); client.SetBaseUrl("https://layout-baseline.fixture.invalid"); client.SetProfile("fixture"); return client; }
        public static string BaselineJson(ProfileSectionBaseline baseline) => JsonSerializer.Serialize(new
        { section_tokens = baseline.SectionTokens, visible_sections = baseline.VisibleSections.Select(row => new { id = row.Id, section_type = row.SectionType, title = row.Title, token = row.Token }) });
        private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
        private static string PageKey(HttpRequestMessage request)
        {
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
            return $"{query["scope"]}:{query["library_id"]}";
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v2/system/identity") return Ok($$"""{"server_id":"{{ServerId}}"}""");
            if (path == "/api/v2/user/libraries") return Ok("""{"items":[{"id":1,"name":"Movies","type":"movie"}]}""");
            if (path == "/api/v2/collections") return Ok(JsonSerializer.Serialize(new { items = PersonalCollections.Select(id => new { id, title = id }) }));
            if (path == "/api/v2/profile/sections/baseline")
            {
                var key = PageKey(request); BaselineReads.Add(key);
                if (FailAllBaselines || FailBaseline.Contains(key)) return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("""{"title":"down"}""") };
                return Ok(Baselines.GetValueOrDefault(key, """{"section_tokens":[],"visible_sections":[]}"""));
            }
            if (path == "/api/v2/profile/sections")
            {
                var key = PageKey(request);
                if (request.Method == HttpMethod.Get) return Ok($$"""{"items":{{Stored.GetValueOrDefault(key, "[]")}}}""");
                if (FailSavePage == key) return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("""{"title":"unavailable"}""") };
                Saved.Add((key, JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement.Clone())); return new(HttpStatusCode.NoContent);
            }
            if (path.StartsWith("/api/v2/settings/values/") && request.Method == HttpMethod.Put)
            {
                var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement;
                HideWatchedWrite = (path, System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["scope"], body.GetProperty("value").GetBoolean());
                return Ok("""{"key":"home.hide_watched_items","value":true,"source":"profile"}""");
            }
            if (path == "/api/v2/sections/recipes") return Ok("""{"categories":[{"category":"Browse","recipes":[{"type":"recently_added","admin_only":false},{"type":"collection_posters","admin_only":false},{"type":"collection","admin_only":false},{"type":"hidden_gems","admin_only":false}]}]}""");
            return Ok("""{"items":[],"settings":[],"categories":[]}""");
        }
    }
    private static (Wire Wire, HomeLayoutTransferService Service) Setup(Action<Wire>? configure = null)
    { var wire = new Wire(); configure?.Invoke(wire); return (wire, Service(wire.Client())); }
    private static JsonElement[] Overrides(JsonElement body) => body.GetProperty("overrides").EnumerateArray().ToArray();

    // ---- export (useHomeLayoutExport.test.tsx)
    [Fact]
    public async Task ExportRecordsEachPagesServerSectionTokensFromTheBaseline()
    {
        var (wire, service) = Setup();
        var file = await service.ExportAsync();
        Assert.Equal(["home", "library"], file.Pages.Select(page => page.Scope));
        Assert.Equal(["tok-continue", "tok-collections"], file.Pages[0].ServerSectionTokens);
        Assert.Equal(["lib-tok"], file.Pages[1].ServerSectionTokens);
        Assert.Equal(["home:", "library:1"], wire.BaselineReads);
        Assert.Matches(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{3}Z$", file.ExportedAt);
        // The file the WebUI imports: parse it the way the WebUI's parser does.
        var parsed = HomeLayoutTransfer.Parse(HomeLayoutTransfer.Serialize(file));
        Assert.Equal(file.Pages.Select(page => page.ServerSectionTokens), parsed.Pages.Select(page => page.ServerSectionTokens));
    }
    [Fact]
    public async Task ExportStillWritesAPageWhoseBaselineCantBeReadWithoutItsTokens()
    {
        var (_, service) = Setup(wire => wire.FailBaseline.Add("library:1"));
        var file = await service.ExportAsync();
        Assert.Equal(2, file.Pages.Count);
        Assert.NotNull(file.Pages[0].ServerSectionTokens); Assert.Null(file.Pages[1].ServerSectionTokens);
        Assert.DoesNotContain("server_section_tokens", JsonSerializer.Serialize(file.Pages[1], HomeLayoutTransfer.Json));
    }
    [Fact]
    public async Task ExportThenImportOnTheSameServerOffersTheRowsAddedBetween()
    {
        var (wire, service) = Setup();
        var file = await service.ExportAsync();
        wire.Baselines["home:"] = WireBaseline("tok-continue", "tok-collections", "tok-new-a", "tok-new-b");
        var plan = await service.PreviewAsync(HomeLayoutTransfer.Serialize(file));
        Assert.Equal(["s-tok-new-a", "s-tok-new-b"], plan.HideOptions.Select(option => option.Row.Id));
        Assert.All(plan.HideOptions, option => Assert.True(option.Checked));
        Assert.Equal(2, plan.PagesWithTokens); Assert.Equal(2, plan.Pages.Count);
    }
    private static string WireBaseline(params string[] tokens) => Wire.BaselineJson(Baseline(tokens.Select(token => Row("s-" + token, "recently_added", "Row " + token, token)).ToArray()));

    // ---- import (HomeLayoutTransfer.test.tsx)
    private const string OfficialFile = """
        {"format":"silo-home-layout","version":1,"exported_at":"2026-11-10T00:00:00.000Z","server_id":"srv-1","libraries":[],
         "pages":[{"scope":"home","overrides":[],"server_section_tokens":["tok-continue"]}]}
        """;
    [Fact]
    public async Task PrechecksServerRowsAddedAfterExportAndHidesThemOnImport()
    {
        var (wire, service) = Setup();
        var plan = await service.PreviewAsync(OfficialFile);
        var option = Assert.Single(plan.HideOptions);
        Assert.Equal(("s-collections", "Collections", "home:", "Home", true), (option.Row.Id, option.Row.DisplayTitle, option.PageKey, option.PageLabel, option.Checked));
        Assert.DoesNotContain(plan.HideOptions, o => o.Row.Id == "default-continue-watching");
        await service.ApplyAsync(plan);
        var (page, body) = Assert.Single(wire.Saved);
        Assert.Equal("home:", page);
        var row = Assert.Single(Overrides(body));
        Assert.Equal("s-collections", row.GetProperty("section_id").GetString()); Assert.True(row.GetProperty("hidden").GetBoolean()); Assert.True(row.TryGetProperty("id", out _));
    }
    [Fact]
    public async Task UncheckingEveryRowLeavesTheEmptyFileSavingTheRestoredServerRows()
    {
        var (wire, service) = Setup();
        var plan = await service.PreviewAsync(OfficialFile);
        plan.HideOptions.Single().Checked = false;
        await service.ApplyAsync(plan);
        Assert.Empty(Overrides(Assert.Single(wire.Saved).Body));
    }
    [Fact]
    public async Task ImportsAFileFromBeforeTokensExistedAsBeforeWithoutAnyBaselineRequest()
    {
        var (wire, service) = Setup();
        var older = """{"format":"silo-home-layout","version":1,"exported_at":"2026-10-01T00:00:00.000Z","server_id":"srv-1","libraries":[],"pages":[{"scope":"home","overrides":[]}]}""";
        var plan = await service.PreviewAsync(older);
        Assert.Empty(plan.HideOptions); Assert.True(plan.HasChanges); Assert.Empty(wire.BaselineReads);
        await service.ApplyAsync(plan);
        Assert.Empty(Overrides(Assert.Single(wire.Saved).Body)); Assert.Empty(wire.BaselineReads);
    }
    [Fact]
    public async Task AFileExportedByAnEarlierVersionOfThisAppStillImports()
    {
        // The earlier native export: every flag written, no tokens, an ISO "O" timestamp.
        var older = """
            {"format":"silo-home-layout","version":1,"exported_at":"2026-10-30T12:00:00.1234567+00:00","server_id":"srv-1","libraries":[{"id":1,"name":"Movies","type":"movie"}],"hide_watched_items":true,
             "pages":[{"scope":"home","overrides":[{"section_id":"admin-1","position":0,"hidden":false,"removed":false,"is_user_added":false},
               {"is_user_added":true,"user_section_type":"recently_added","user_title":"Mine","user_config":{"filter_library_ids":[1]},"hidden":false,"removed":false}]},
              {"scope":"library","library_id":1,"overrides":[]}]}
            """;
        var (wire, service) = Setup();
        var plan = await service.PreviewAsync(older);
        Assert.Equal(["Home", "Movies"], plan.Pages.Select(page => page.Label)); Assert.Equal(true, plan.HideWatchedItems);
        await service.ApplyAsync(plan);
        Assert.Equal(2, wire.Saved.Count); Assert.Equal(2, Overrides(wire.Saved[0].Body).Length); Assert.Empty(Overrides(wire.Saved[1].Body));
        Assert.Equal(("/api/v2/settings/values/home.hide_watched_items", "profile", true), wire.HideWatchedWrite);
    }
    [Fact]
    public async Task StillImportsTheFilesRowsWhenTheServerBaselineCantBeRead()
    {
        var (wire, service) = Setup(w => w.FailAllBaselines = true);
        var file = """{"format":"silo-home-layout","version":1,"exported_at":"2026-11-10T00:00:00.000Z","server_id":"srv-1","libraries":[],"pages":[{"scope":"home","overrides":[{"section_id":"default-continue-watching","position":1}],"server_section_tokens":["tok-continue"]}]}""";
        var plan = await service.PreviewAsync(file);
        Assert.Empty(plan.HideOptions); Assert.True(plan.HasChanges);
        await service.ApplyAsync(plan);
        var row = Assert.Single(Overrides(Assert.Single(wire.Saved).Body));
        Assert.Equal("default-continue-watching", row.GetProperty("section_id").GetString()); Assert.Equal(1, row.GetProperty("position").GetInt32());
    }
    [Fact]
    public async Task WhenNoTokenMatchesTheServersRowsNothingIsOfferedAndTheFileImportsAsBefore()
    {
        var (wire, service) = Setup();
        var file = """{"format":"silo-home-layout","version":1,"exported_at":"2026-11-10T00:00:00.000Z","server_id":"srv-1","libraries":[],"pages":[{"scope":"home","overrides":[{"section_id":"admin-1","hidden":true}],"server_section_tokens":["tok-from-another-key"]}]}""";
        var plan = await service.PreviewAsync(file);
        Assert.Empty(plan.HideOptions); Assert.Equal(["home:"], wire.BaselineReads);
        await service.ApplyAsync(plan);
        Assert.Equal("admin-1", Assert.Single(Overrides(Assert.Single(wire.Saved).Body)).GetProperty("section_id").GetString());
    }
    [Fact]
    public async Task ACrossServerFileNeverReadsABaselineOrOffersRows()
    {
        var (wire, service) = Setup();
        var plan = await service.PreviewAsync(OfficialFile.Replace("srv-1", "other-server"));
        Assert.False(plan.SameServer); Assert.Empty(plan.HideOptions); Assert.Empty(wire.BaselineReads); Assert.Empty(plan.Pages); Assert.False(plan.HasChanges);
    }

    [Fact]
    public async Task HidingRowsOnAPageWhoseFileOverridesWereAllSkippedOffersThePageAndKeepsItsSavedOverrides()
    {
        // The file's only override is a personal collection row this profile can't see, so the page has nothing to replace.
        var file = """{"format":"silo-home-layout","version":1,"exported_at":"2026-11-10T00:00:00.000Z","server_id":"srv-1","libraries":[],"pages":[{"scope":"home","overrides":[{"is_user_added":true,"user_section_type":"collection","user_config":{"user_collection_id":"c-private"}}],"server_section_tokens":["tok-continue"]}]}""";
        var (wire, service) = Setup(w => w.Stored["home:"] = """[{"id":"saved-1","section_id":"default-continue-watching","position":0},{"id":"saved-custom","is_user_added":true,"user_section_type":"hidden_gems"}]""");
        var plan = await service.PreviewAsync(file);
        Assert.Empty(plan.Pages); var unapplied = Assert.Single(plan.UnappliedPages); Assert.False(unapplied.ReplaceExisting);
        Assert.Equal(HomeLayoutSkipReason.Collection, Assert.Single(plan.SkippedSections).Reason);
        Assert.Single(plan.HideOptions); Assert.True(plan.HasChanges); Assert.Equal(["Home"], plan.CheckedUnappliedPages.Select(page => page.Label));
        plan.HideOptions[0].Checked = false; Assert.False(plan.HasChanges);
        plan.HideOptions[0].Checked = true;
        await service.ApplyAsync(plan);
        var saved = Overrides(Assert.Single(wire.Saved).Body);
        Assert.Equal(3, saved.Length);
        Assert.Equal("saved-1", saved[0].GetProperty("id").GetString()); Assert.Equal("saved-custom", saved[1].GetProperty("id").GetString());
        Assert.True(saved[2].GetProperty("hidden").GetBoolean()); Assert.Equal("s-collections", saved[2].GetProperty("section_id").GetString());
    }
    [Fact]
    public async Task SavesNothingForAHideOnlyPageWhenEveryCheckedRowIsGoneByApplyTime()
    {
        var file = """{"format":"silo-home-layout","version":1,"exported_at":"2026-11-10T00:00:00.000Z","server_id":"srv-1","libraries":[],"pages":[{"scope":"home","overrides":[{"is_user_added":true,"user_section_type":"collection","user_config":{"user_collection_id":"c-private"}}],"server_section_tokens":["tok-continue"]}]}""";
        var (wire, service) = Setup();
        var plan = await service.PreviewAsync(file);
        wire.Baselines["home:"] = WireBaseline();
        await service.ApplyAsync(plan);
        Assert.Empty(wire.Saved);
    }
    [Fact]
    public async Task ApplyKeepsOnlyCheckedRowsStillPresentOnAFreshBaselineAndCountsThemTowardTheLimit()
    {
        var (wire, service) = Setup();
        var plan = await service.PreviewAsync(OfficialFile);
        wire.Baselines["home:"] = Wire.BaselineJson(Baseline(Row("default-continue-watching", "continue_watching", "Continue Watching", "tok-continue")));
        await service.ApplyAsync(plan);
        Assert.Empty(Overrides(Assert.Single(wire.Saved).Body));
        // A file page of 500 overrides cannot also take the hidden row.
        var (full, fullService) = Setup();
        var rows = string.Join(",", Enumerable.Range(0, 500).Select(_ => """{"user_section_type":"hidden_gems","user_title":"Gems"}"""));
        var oversized = $$"""{"format":"silo-home-layout","version":1,"exported_at":"x","server_id":"srv-1","libraries":[],"pages":[{"scope":"home","overrides":[{{rows}}],"server_section_tokens":["tok-continue"]}]}""";
        var big = await fullService.PreviewAsync(oversized);
        Assert.Single(big.HideOptions);
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => fullService.ApplyAsync(big));
        Assert.Equal("this page would have more than 500 saved section changes", failure.Message); Assert.Equal("Home", fullService.FailedPage); Assert.Empty(full.Saved);
    }
    [Fact]
    public async Task AFailedPageStopsTheImportNamingThePageAndKeepsEarlierPagesSaved()
    {
        var file = """{"format":"silo-home-layout","version":1,"exported_at":"x","server_id":"srv-1","libraries":[{"id":1,"name":"Movies","type":"movie"}],"pages":[{"scope":"home","overrides":[]},{"scope":"library","library_id":1,"overrides":[]}]}""";
        var (wire, service) = Setup(w => w.FailSavePage = "library:1");
        var plan = await service.PreviewAsync(file);
        await Assert.ThrowsAnyAsync<Exception>(() => service.ApplyAsync(plan));
        Assert.Equal("Movies", service.FailedPage); Assert.False(service.FailedHideWatched); Assert.Equal(1, service.AppliedPages); Assert.Equal("home:", Assert.Single(wire.Saved).Page);
    }
    [Fact]
    public async Task AProfileSwitchBetweenPreviewAndApplyWritesNothing()
    {
        var (wire, service) = Setup(); var client = wire.Client(); service = Service(client);
        var plan = await service.PreviewAsync(OfficialFile);
        client.SetProfile("other");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ApplyAsync(plan));
        Assert.Empty(wire.Saved);
    }

    // ---- collection rows (#4410)
    private static string CollectionRowFile(string server, string config, string recipe = "collection_posters")
        => $$"""{"format":"silo-home-layout","version":1,"exported_at":"x","server_id":"{{server}}","libraries":[],"pages":[{"scope":"home","overrides":[{"is_user_added":true,"user_section_type":"{{recipe}}","user_title":"Picks","user_config":{{config}}}]}]}""";
    private const string MixedCollections = """{"collections":[{"library_collection_id":"lib-c1"},{"user_collection_id":"mine"}]}""";
    private static async Task<HomeLayoutPlan> PlanAsync(string text, params string[] personal)
    { var (_, service) = Setup(w => { w.PersonalCollections = [.. personal]; }); return await service.PreviewAsync(text); }
    [Fact]
    public async Task ARowOfCollectionsImportsOnTheSameServerWhenEveryPersonalCollectionItListsIsVisible()
    {
        var plan = await PlanAsync(CollectionRowFile("srv-1", MixedCollections), "mine");
        Assert.Empty(plan.SkippedSections); Assert.Single(Assert.Single(plan.Pages).Overrides);
    }
    [Fact]
    public async Task ARowOfCollectionsListingAPersonalCollectionTheProfileCantSeeIsSkippedAsACollection()
    {
        var plan = await PlanAsync(CollectionRowFile("srv-1", MixedCollections));
        var skipped = Assert.Single(plan.SkippedSections);
        Assert.Equal((HomeLayoutSkipReason.Collection, "Picks", "Home"), (skipped.Reason, skipped.Title, skipped.Page));
        Assert.Equal("Uses a collection this profile can't see here", skipped.ReasonText); Assert.Empty(plan.Pages);
        Assert.Equal("Picks (Home): Uses a collection this profile can't see here", skipped.ToString());
    }
    [Fact]
    public async Task ARowOfOnlyServerCollectionsNeedsNoPersonalCollectionLookup()
    {
        var plan = await PlanAsync(CollectionRowFile("srv-1", """{"collections":[{"library_collection_id":"lib-c1"}]}"""));
        Assert.Empty(plan.SkippedSections); Assert.Single(plan.Pages);
    }
    [Fact]
    public async Task ARowOfCollectionsFromAnotherServerIsSkippedAsACollection()
    {
        var plan = await PlanAsync(CollectionRowFile("other-server", MixedCollections));
        Assert.Equal(HomeLayoutSkipReason.Collection, Assert.Single(plan.SkippedSections).Reason); Assert.Empty(plan.Pages);
    }
    [Fact]
    public void ReadsCollectionRefsInOrderSkippingEntriesItCantRead()
    {
        var config = JsonSerializer.Deserialize<Dictionary<string, object>>("""{"collections":[{"library_collection_id":"a"},7,{"user_collection_id":"b"},{"library_collection_id":"c","user_collection_id":"d"},{"user_collection_id":""},null]}""")!;
        Assert.Equal([(false, "a"), (true, "b"), (false, "c")], HomeLayoutTransfer.CollectionRefs(config));
        Assert.Empty(HomeLayoutTransfer.CollectionRefs(null)); Assert.Empty(HomeLayoutTransfer.CollectionRefs(new() { ["collections"] = "x" }));
    }
    [Fact]
    public async Task RowsOfCollectionsRoundTripThroughAnExportAndImportWithoutLosingTheirList()
    {
        var (wire, service) = Setup(w => { w.PersonalCollections = ["mine"]; w.Stored["home:"] = $$"""[{"id":"r1","is_user_added":true,"user_section_type":"collection_posters","user_title":"Picks","user_config":{{MixedCollections}}}]"""; });
        var file = await service.ExportAsync();
        var plan = await service.PreviewAsync(HomeLayoutTransfer.Serialize(file));
        await service.ApplyAsync(plan);
        var row = Overrides(wire.Saved.First(saved => saved.Page == "home:").Body).Single();
        Assert.Equal("collection_posters", row.GetProperty("user_section_type").GetString());
        Assert.Equal(2, row.GetProperty("user_config").GetProperty("collections").GetArrayLength());
    }

    [Fact]
    public async Task SkippedSectionsAndPagesUseTheOfficialLines()
    {
        var file = """{"format":"silo-home-layout","version":1,"exported_at":"x","server_id":"other","libraries":[{"id":9,"name":"Shows","type":"show"}],"pages":[{"scope":"home","overrides":[{"section_id":"admin-1"},{"is_user_added":true,"user_section_type":"next_up"},{"is_user_added":true,"user_section_type":"mystery"},{"is_user_added":true,"user_section_type":"recently_added","user_config":{"profile_id":"p9"}}]},{"scope":"library","library_id":9,"overrides":[]}]}""";
        var plan = await PlanAsync(file);
        Assert.Equal(1, plan.SkippedServerSectionChanges); Assert.Equal(["Shows"], plan.SkippedPages);
        Assert.Equal([HomeLayoutSkipReason.UnknownRecipe, HomeLayoutSkipReason.UnknownRecipe, HomeLayoutSkipReason.Profile], plan.SkippedSections.Select(row => row.Reason));
        Assert.Equal("1 change to a section defined by the other server's admin doesn't apply here.", HomeLayoutTransfer.ServerChangesText(1));
        Assert.Equal("3 changes to sections defined by the other server's admin don't apply here.", HomeLayoutTransfer.ServerChangesText(3));
        Assert.Equal("No library here matches: Shows.", HomeLayoutTransfer.NoLibraryMatchesText("Shows"));
    }
}
