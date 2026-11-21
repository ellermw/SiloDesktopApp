using System.Net;
using System.Text;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>
/// Regression tests for the independent review of the Autoscan Activity page (November 20): request counts, stale reads,
/// the unknown-cancellation way out, walked-page validation and source text encoding.
/// </summary>
public sealed partial class AdminAutoscanActivityTests
{
    private const string HistoryPrefix = "/api/v2/admin/autoscan/";
    private static string StatusJson(int active) =>
        "{\"enabled\":true,\"active_scans\":" + active + ",\"accepted_scans\":0,\"running_scans\":" + active + ",\"sources\":[],\"running_polls\":[]}";
    private static int StatusReads(Harness harness) => harness.Wire.Reads.Count(path => path.StartsWith(HistoryPrefix + "status", StringComparison.Ordinal));
    private static int HistoryReads(Harness harness) => harness.Wire.Reads.Count(path =>
        path.StartsWith(HistoryPrefix + "scans", StringComparison.Ordinal) || path.StartsWith(HistoryPrefix + "events", StringComparison.Ordinal));
    /// <summary>The library, source, connection and plugin name lookups.</summary>
    private static int NameReads(Harness harness) => harness.Wire.Reads.Count(path =>
        !path.StartsWith(HistoryPrefix + "scans", StringComparison.Ordinal) && !path.StartsWith(HistoryPrefix + "events", StringComparison.Ordinal) &&
        !path.StartsWith(HistoryPrefix + "status", StringComparison.Ordinal));
    private const string Running7 = """[{"id":"target","library_id":7,"mode":"file","path":"/synthetic/movie.mkv","trigger":"autoscan","status":"running"}]""";

    // ---- D2: a page, filter, view or size change reads only the history ----------------------------------------------------

    [Fact]
    public async Task PageFilterViewAndSizeChangesSendOnlyTheHistoryRequests()
    {
        using var harness = new Harness(); using var controller = new AdminAutoscanActivityController(harness.Api, harness.Authority, new Channel(), new Clock());
        await controller.OpenAsync();
        var status = StatusReads(harness); var names = NameReads(harness); var history = HistoryReads(harness);
        Assert.Equal(1, status); Assert.True(names >= 4);
        await controller.GoToPageAsync(2); await controller.GoToPageAsync(1);
        await controller.FilterAsync("x", "failed", 25); await controller.FilterAsync("x", "failed", 50);
        await controller.SelectAsync(AdminAutoscanHistory.Polls); await controller.GoToPageAsync(1);
        await controller.ResetFiltersAsync();
        Assert.Equal(status, StatusReads(harness)); Assert.Equal(names, NameReads(harness));
        Assert.True(HistoryReads(harness) > history);
        // The refresh button and the periodic refresh read the status and the shown page again, still not the names.
        await controller.ReloadHistoryAsync(); await controller.RefreshAsync();
        Assert.Equal(status + 2, StatusReads(harness)); Assert.Equal(names, NameReads(harness));
    }

    [Fact]
    public async Task AFailedNameLookupIsRetriedByAnExplicitReloadButNotByAPageChange()
    {
        using var harness = new Harness(); using var controller = new AdminAutoscanActivityController(harness.Api, harness.Authority, new Channel(), new Clock());
        harness.Wire.Reply = request => request.RequestUri!.AbsolutePath.EndsWith("/connections", StringComparison.Ordinal)
            ? Task.FromResult(Json("{\"title\":\"synthetic unavailable\",\"status\":503}", HttpStatusCode.ServiceUnavailable)) : (Task<HttpResponseMessage>)null!;
        await controller.OpenAsync(); Assert.True(controller.State.LabelsFailed);
        var names = NameReads(harness);
        await controller.GoToPageAsync(1); Assert.Equal(names, NameReads(harness));
        await controller.ReloadHistoryAsync(); Assert.True(NameReads(harness) > names); Assert.True(controller.State.LabelsFailed);
        harness.Wire.Reply = null; await controller.ReloadHistoryAsync(); Assert.False(controller.State.LabelsFailed);
        var healthy = NameReads(harness);
        await controller.ReloadHistoryAsync(); Assert.Equal(healthy, NameReads(harness));
    }

    // ---- D5: scan events never become lookup requests ---------------------------------------------------------------------

    [Fact]
    public async Task TerminalScanEventsSendNoRequestsAtAll()
    {
        using var harness = new Harness(); var channel = new Channel();
        using var controller = new AdminAutoscanActivityController(harness.Api, harness.Authority, channel, new Clock());
        await controller.OpenAsync(); channel.Snapshot(Running7);
        var reads = harness.Wire.Reads.Count;
        foreach (var status in new[] { "completed", "failed", "cancelled", "completed", "failed" })
            channel.Event("scan.completed", "{\"id\":\"run-" + status + "\",\"library_id\":7,\"mode\":\"file\",\"trigger\":\"autoscan\",\"status\":\"" + status + "\"}");
        await Task.Delay(250);
        Assert.Equal(reads, harness.Wire.Reads.Count); Assert.Equal("target", Assert.Single(controller.State.Queue).Id);
    }

    // ---- D3: an older reconciliation read cannot overwrite a newer status -------------------------------------------------

    [Fact]
    public async Task AnOlderReconciliationReadNeverOverwritesANewerStatus()
    {
        using var harness = new Harness(); var channel = new Channel();
        using var controller = new AdminAutoscanActivityController(harness.Api, harness.Authority, channel, new Clock());
        await controller.OpenAsync(); channel.Snapshot(Running7);
        harness.Wire.Reply = request => request.Method == HttpMethod.Post ? throw new HttpRequestException("synthetic uncertain outcome") : (Task<HttpResponseMessage>)null!;
        await controller.CancelAsync("7"); Assert.True(controller.State.CancelReview.ContainsKey("7"));
        var older = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        harness.Wire.Reply = request => request.RequestUri!.AbsolutePath.EndsWith("/status", StringComparison.Ordinal)
            ? Interlocked.Increment(ref calls) == 1 ? older.Task : Task.FromResult(Json(StatusJson(5))) : (Task<HttpResponseMessage>)null!;
        var reconcile = controller.ReconcileCancelAsync("7");   // read A: held
        await controller.RefreshAsync();                         // read B: newer, answers at once
        Assert.Equal(5, controller.State.Status!.ActiveScans);
        older.SetResult(Json(StatusJson(0))); await reconcile;
        Assert.Equal(5, controller.State.Status!.ActiveScans); Assert.False(controller.State.StatusFetching);
        // Read A still started after the unknown outcome and succeeded, so it lifts the block without touching the shown counts.
        Assert.Empty(controller.State.CancelReview); Assert.Equal(1, harness.Wire.Writes);
    }

    // ---- D4: a way out of an unknown cancellation that needs no realtime snapshot ----------------------------------------

    [Fact]
    public async Task ExplicitStatusReadLiftsTheBlockWhenTheScanIsGone()
    {
        using var harness = new Harness(); var channel = new Channel();
        using var controller = new AdminAutoscanActivityController(harness.Api, harness.Authority, channel, new Clock());
        await controller.OpenAsync();
        channel.Snapshot("""[{"id":"target","library_id":7,"mode":"file","trigger":"autoscan","status":"running"},{"id":"other","library_id":8,"mode":"file","trigger":"autoscan","status":"running"}]""");
        harness.Wire.Reply = request => request.Method == HttpMethod.Post ? throw new HttpRequestException("synthetic uncertain outcome")
            : request.RequestUri!.AbsolutePath.EndsWith("/status", StringComparison.Ordinal) ? Task.FromResult(Json(StatusJson(1))) : (Task<HttpResponseMessage>)null!;
        await controller.CancelAsync("7"); Assert.Equal(1, harness.Wire.Writes);
        channel.Event("scan.completed", """{"id":"target","library_id":7,"mode":"file","trigger":"autoscan","status":"cancelled"}""");
        Assert.True(controller.State.CancelReview.ContainsKey("7"));   // an event alone never lifts the block
        await controller.ReconcileCancelAsync("7");
        Assert.Empty(controller.State.CancelReview); Assert.Equal(AdminAutoscanActivityText.T("native.cancelReviewed"), controller.State.Message);
        Assert.Equal("other", Assert.Single(controller.State.Queue).Id);
        await controller.CancelAsync("7"); Assert.Equal(1, harness.Wire.Writes);   // nothing is listed for the library any more
    }

    [Fact]
    public async Task ExplicitStatusReadLiftsTheBlockWhileTheScanKeepsRunningAndALaterCancelIsANewRequest()
    {
        using var harness = new Harness(); var channel = new Channel();
        using var controller = new AdminAutoscanActivityController(harness.Api, harness.Authority, channel, new Clock());
        await controller.OpenAsync(); channel.Snapshot(Running7);
        harness.Wire.Reply = request => request.Method == HttpMethod.Post ? throw new HttpRequestException("synthetic uncertain outcome")
            : request.RequestUri!.AbsolutePath.EndsWith("/status", StringComparison.Ordinal) ? Task.FromResult(Json(StatusJson(1))) : (Task<HttpResponseMessage>)null!;
        await controller.CancelAsync("7"); Assert.Equal(1, harness.Wire.Writes);
        await controller.CancelAsync("7"); Assert.Equal(1, harness.Wire.Writes);               // never replayed blindly
        await controller.RefreshAsync(); Assert.True(controller.State.CancelReview.ContainsKey("7"));   // the periodic read does not lift it
        // No realtime snapshot or event arrives; the admin's own re-read is the way out.
        await controller.ReconcileCancelAsync("7"); Assert.Equal(1, harness.Wire.Writes);
        Assert.Empty(controller.State.CancelReview); Assert.Equal(AdminAutoscanActivityText.T("native.cancelStillListed"), controller.State.Message);
        Assert.Equal("target", Assert.Single(controller.State.Queue).Id);
        harness.Wire.Reply = request => request.Method == HttpMethod.Post ? Task.FromResult(Json("""{"library_id":"7","cancelled":1}""")) : (Task<HttpResponseMessage>)null!;
        await controller.CancelAsync("7"); Assert.Equal(2, harness.Wire.Writes);               // a new, deliberate request
        Assert.Empty(controller.State.CancelReview);
    }

    [Fact]
    public async Task AFailedReconciliationReadKeepsTheBlockEvenWhenTheScanIsListedAsGone()
    {
        using var harness = new Harness(); var channel = new Channel();
        using var controller = new AdminAutoscanActivityController(harness.Api, harness.Authority, channel, new Clock());
        await controller.OpenAsync(); channel.Snapshot(Running7);
        harness.Wire.Reply = request => request.Method == HttpMethod.Post ? throw new HttpRequestException("synthetic uncertain outcome")
            : request.RequestUri!.AbsolutePath.EndsWith("/status", StringComparison.Ordinal) ? throw new HttpRequestException("synthetic status failure") : (Task<HttpResponseMessage>)null!;
        await controller.CancelAsync("7"); channel.Snapshot("[]");
        await controller.ReconcileCancelAsync("7");
        Assert.True(controller.State.CancelReview.ContainsKey("7")); Assert.Contains("could not be reloaded", controller.State.Message); Assert.Equal(1, harness.Wire.Writes);
    }

    // ---- D9: only the target page has its rows validated -----------------------------------------------------------------

    [Fact]
    public async Task AnOddRowOnAWalkedPageDoesNotFailTheTargetPage()
    {
        using var harness = new Harness(); using var controller = new AdminAutoscanActivityController(harness.Api, harness.Authority, new Channel(), new Clock());
        await controller.OpenAsync(); await controller.GoToPageAsync(1);
        var oddScan = Scan.Replace("\"status\":\"completed\"", "\"status\":\"future\"");
        harness.Wire.Reply = request => Task.FromResult(Json(request.RequestUri!.AbsolutePath switch
        {
            "/api/v2/admin/autoscan/scans" => request.RequestUri.Query.Contains("cursor=o25") ? Wire.Rows(request.RequestUri, Scan, 75) : Page(oddScan, "o25"),
            "/api/v2/admin/autoscan/status" => StatusJson(0),
            _ => """{"items":[],"page":{"has_more":false}}"""
        }));
        // The reload walks from the first page, whose row has a status this client does not know; page 2 is the one being shown.
        await controller.ReloadHistoryAsync();
        Assert.Null(controller.State.Scans.Failure); Assert.Equal(1, controller.State.Scans.PageIndex);
        Assert.Equal("scan-25", controller.State.Scans.Items[0].Id); Assert.Equal(25, controller.State.Scans.Items.Count);
        // The same row on the page being shown still fails that page visibly.
        await controller.GoToPageAsync(0); Assert.NotNull(controller.State.Scans.Failure);
    }

    [Fact]
    public async Task AnOddEventOnAWalkedPageDoesNotFailTheTargetPage()
    {
        using var harness = new Harness(); using var controller = new AdminAutoscanActivityController(harness.Api, harness.Authority, new Channel(), new Clock());
        await controller.OpenAsync(); await controller.SelectAsync(AdminAutoscanHistory.Polls); await controller.GoToPageAsync(1);
        var oddPoll = Poll.Replace("\"status\":\"success\"", "\"status\":\"future\"");
        harness.Wire.Reply = request => Task.FromResult(Json(request.RequestUri!.AbsolutePath switch
        {
            "/api/v2/admin/autoscan/events" => request.RequestUri.Query.Contains("cursor=o25") ? Wire.Rows(request.RequestUri, Poll, 75) : Page(oddPoll, "o25"),
            "/api/v2/admin/autoscan/status" => StatusJson(0),
            _ => """{"items":[],"page":{"has_more":false}}"""
        }));
        await controller.ReloadHistoryAsync();
        Assert.Null(controller.State.Polls.Failure); Assert.Equal(1, controller.State.Polls.PageIndex); Assert.Equal("1025", controller.State.Polls.Items[0].Id);
    }

    [Fact]
    public async Task WalkedPagesKeepTheirEnvelopeChecks()
    {
        using var harness = new Harness(); using var lease = harness.Authority.Capture();
        var odd = Scan.Replace("\"status\":\"completed\"", "\"status\":\"future\"");
        harness.Wire.Reply = _ => Task.FromResult(Json(Page(odd, "next")));
        var walked = await harness.Api.ReadScansAsync(lease, new(), null, default, targetPage: false);
        Assert.Equal("next", walked.NextCursor);
        await Assert.ThrowsAnyAsync<Exception>(() => harness.Api.ReadScansAsync(lease, new()));
        foreach (var json in new[] { "{\"items\":[],\"total\":-1,\"page\":{\"has_more\":false}}", "{\"items\":null,\"total\":0,\"page\":{\"has_more\":false}}",
                     "{\"items\":[],\"total\":0,\"page\":{\"has_more\":true}}", Page(string.Join(',', Enumerable.Repeat(Scan, 26))) })
        {
            harness.Wire.Reply = _ => Task.FromResult(Json(json));
            await Assert.ThrowsAnyAsync<Exception>(() => harness.Api.ReadScansAsync(lease, new(), null, default, targetPage: false));
        }
    }

    [Fact]
    public async Task AnEventWithMoreRunsThanTheBoundStaysOnThePageAndStatesTheOmission()
    {
        using var harness = new Harness(); using var lease = harness.Authority.Capture();
        var extra = 3; var total = AdminAutoscanActivityApi.MaxEventRuns + extra;
        var runs = string.Join(',', Enumerable.Range(0, total).Select(index => "{\"id\":\"run-" + index + "\",\"library_id\":\"7\",\"mode\":\"file\",\"trigger\":\"autoscan\",\"status\":\"completed\"}"));
        var big = Poll.Replace("\"scan_runs\":[]", "\"scan_runs\":[" + runs + "]");
        var other = Poll.Replace("\"id\":\"11\"", "\"id\":\"12\"");
        harness.Wire.Reply = _ => Task.FromResult(Json("{\"items\":[" + big + "," + other + "],\"total\":2,\"page\":{\"has_more\":false}}"));
        var window = await harness.Api.ReadEventsAsync(lease, new(Limit: 25));
        Assert.Equal(2, window.Items.Count);
        Assert.Equal(AdminAutoscanActivityApi.MaxEventRuns, window.Items[0].ScanRuns!.Count); Assert.Equal(extra, window.Items[0].ScanRunsOmitted);
        Assert.Equal(0, window.Items[1].ScanRunsOmitted); Assert.Equal("12", window.Items[1].Id);
        Assert.Contains(AdminAutoscanActivityText.Number(total), AdminAutoscanActivityDisplay.DetailsSummary(window.Items[0]));
    }

    // ---- D7: the rows and pager stay while the next page loads ------------------------------------------------------------

    [Fact]
    public async Task ThePreviousRowsStayOnScreenWhileTheNextPageLoadsAndAFailureDropsThem()
    {
        using var harness = new Harness(); using var controller = new AdminAutoscanActivityController(harness.Api, harness.Authority, new Channel(), new Clock());
        await controller.OpenAsync();
        var held = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Wire.Reply = request => request.RequestUri!.AbsolutePath.EndsWith("/scans", StringComparison.Ordinal) && request.RequestUri.Query.Contains("cursor=o25") ? held.Task : (Task<HttpResponseMessage>)null!;
        var next = controller.GoToPageAsync(1);
        var loading = controller.State.Scans;
        Assert.True(loading.Fetching); Assert.True(loading.Stale); Assert.True(loading.Loaded); Assert.Equal(1, loading.PageIndex);
        Assert.Equal("scan-0", loading.Items[0].Id); Assert.Equal(75, loading.Total);
        held.SetResult(Json("{\"title\":\"synthetic unavailable\",\"status\":503}", HttpStatusCode.ServiceUnavailable)); await next;
        Assert.NotNull(controller.State.Scans.Failure); Assert.Empty(controller.State.Scans.Items); Assert.False(controller.State.Scans.Stale);
        harness.Wire.Reply = null; await controller.ReloadHistoryAsync();
        Assert.Null(controller.State.Scans.Failure); Assert.Equal("scan-25", controller.State.Scans.Items[0].Id); Assert.False(controller.State.Scans.Stale);
        // A change of the number of rows keeps the shown rows until the new page arrives; a new search does not (those rows no longer match).
        var sized = controller.FilterAsync("", "", 50); Assert.True(controller.State.Scans.Stale); Assert.Equal(25, controller.State.Scans.Items.Count); await sized;
        Assert.Equal(50, controller.State.Scans.Items.Count); Assert.False(controller.State.Scans.Stale);
        var searched = controller.FilterAsync("zoo", "", 50); Assert.Empty(controller.State.Scans.Items); Assert.False(controller.State.Scans.Loaded); await searched;
    }

    // ---- D1: no mis-encoded text in this family's files -------------------------------------------------------------------

    /// <summary>The characters a Windows-1252 reader shows for the continuation bytes 0x80 to 0xBF of UTF-8.</summary>
    private static readonly HashSet<char> Windows1252Continuations = BuildContinuations();

    private static HashSet<char> BuildContinuations()
    {
        var set = new HashSet<char>();
        for (var code = 0xA0; code <= 0xBF; code++) set.Add((char)code);
        foreach (var code in new[] { 0x20AC, 0x201A, 0x0192, 0x201E, 0x2026, 0x2020, 0x2021, 0x02C6, 0x2030, 0x0160, 0x2039, 0x0152, 0x017D, 0x2018, 0x2019,
                     0x201C, 0x201D, 0x2022, 0x2013, 0x2014, 0x02DC, 0x2122, 0x0161, 0x203A, 0x0153, 0x017E, 0x0178 }) set.Add((char)code);
        for (var code = 0x80; code <= 0x9F; code++) set.Add((char)code);
        return set;
    }

    /// <summary>The index of UTF-8 text that was decoded as Windows-1252 and saved again (a lead byte letter followed by such a character), or -1.</summary>
    private static int MisEncodedAt(string text)
    {
        for (var index = 0; index + 1 < text.Length; index++)
            if (text[index] is >= (char)0xC2 and <= (char)0xF4 && Windows1252Continuations.Contains(text[index + 1])) return index;
        return text.IndexOf((char)0xFFFD);
    }

    private static IEnumerable<string> Walk(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory)) yield return file;
        foreach (var child in Directory.EnumerateDirectories(directory))
            if (Path.GetFileName(child) is not ("bin" or "obj" or ".vs" or "node_modules"))
                foreach (var file in Walk(child)) yield return file;
    }

    private static IReadOnlyList<string> FamilyFiles() => new[] { "src", "tests", "docs" }.SelectMany(top => Walk(Path.Combine(TestRepository.Root, top)))
        .Where(file => Path.GetFileName(file).Contains("AdminAutoscanActivity", StringComparison.OrdinalIgnoreCase) ||
                       Path.GetFileName(file).Contains("admin-autoscan-activity", StringComparison.OrdinalIgnoreCase)).ToArray();

    [Fact]
    public void FamilyFilesAreValidUtf8WithoutMisEncodedText()
    {
        var detector = "a" + (char)0xE2 + (char)0x20AC + (char)0xA6 + "b";
        Assert.Equal(1, MisEncodedAt(detector)); Assert.Equal(-1, MisEncodedAt("Showing 1" + (char)0x2013 + "20 " + (char)0xB7 + " " + (char)0x2026));
        var files = FamilyFiles(); Assert.True(files.Count >= 12);
        var strict = new UTF8Encoding(false, true);
        foreach (var file in files)
        {
            var text = strict.GetString(File.ReadAllBytes(file));
            var at = MisEncodedAt(text);
            Assert.True(at < 0, Path.GetFileName(file) + " has mis-encoded text near: " + (at < 0 ? "" : text.Substring(Math.Max(0, at - 20), Math.Min(48, text.Length - Math.Max(0, at - 20)))));
        }
    }

    [Fact]
    public void ThePagerGapIsWrittenAsAnEllipsisEscape()
    {
        var ui = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Views", "Admin", "AdminAutoscanActivityUi.cs"));
        var escape = ((char)92).ToString() + "u2026";
        Assert.Contains("var gap = Text(\"" + escape + "\"", ui);
    }
}
