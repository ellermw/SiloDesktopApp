using System.Net;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class LibraryLetterNavigationTests
{
    [Fact]
    public async Task JumpUsesWholeQueryServerIndexWithoutSamplingTitles()
    {
        using var wire = new Wire(); var model = wire.Create();
        var jumped = -1; model.LetterJumpRequested += position => jumped = position;
        await model.LoadCommand.ExecuteAsync(null);
        await model.JumpToLetterCommand.ExecuteAsync("M");
        Assert.Equal(1, wire.IndexRequests);
        Assert.Equal(0, wire.ProbeRequests);
        Assert.NotNull(model.GetWindowItem(742));
        Assert.Contains(wire.Seeks, seek => seek >= 720);
        Assert.Equal(742, jumped);
        Assert.Equal(1000, model.TotalCount);
    }

    [Fact]
    public async Task JumpPreservesDescendingSortAndAdvancedFilters()
    {
        using var wire = new Wire(); var model = wire.Create();
        model.SelectedOrder = "desc"; model.UseAdvancedRules = true; model.AdvancedRulesMatch = "any";
        model.AdvancedGroups.Add(new EditableQueryGroup { Match = "all", Rules = { new QueryRule { Field = "genre", Op = "is", Value = "Drama" } } });
        await model.LoadCommand.ExecuteAsync(null);
        await model.JumpToLetterCommand.ExecuteAsync("M");
        Assert.Equal("desc", model.SelectedOrder);
        using var body = JsonDocument.Parse(wire.IndexBody!);
        Assert.Equal("desc", body.RootElement.GetProperty("order").GetString());
        Assert.Equal("any", body.RootElement.GetProperty("match").GetString());
        Assert.Equal("Drama", body.RootElement.GetProperty("groups")[0].GetProperty("rules")[0].GetProperty("value").GetString());
    }

    [Fact]
    public async Task OldServerDoesNotProbeOrGuessAJump()
    {
        using var wire = new Wire { Supported = false }; var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null); var count = wire.Seeks.Count;
        await model.JumpToLetterCommand.ExecuteAsync("M");
        Assert.Equal(0, wire.IndexRequests); Assert.Equal(0, wire.ProbeRequests);
        Assert.Equal(count, wire.Seeks.Count);
        Assert.Null(model.ErrorMessage);
    }

    [Theory]
    [InlineData("added_at")]
    [InlineData("year")]
    public async Task NonTitleSortDoesNotChangeSortOrReadIndex(string sort)
    {
        using var wire = new Wire(); var model = wire.Create(); model.SelectedSort = sort;
        await model.LoadCommand.ExecuteAsync(null);
        await model.JumpToLetterCommand.ExecuteAsync("M");
        Assert.Equal(sort, model.SelectedSort); Assert.Equal(0, wire.IndexRequests);
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("language")]
    [InlineData("filter")]
    [InlineData("library")]
    [InlineData("navigation")]
    public async Task LateIndexCannotPublishAfterOwnerChanges(string change)
    {
        using var wire = new Wire { HoldIndex = true }; var model = wire.Create();
        var load = model.LoadCommand.ExecuteAsync(null);
        await wire.IndexStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var indexRead = model.EnsureLetterIndexAsync();
        if (change == "profile") wire.Client.SetProfile("new-synthetic-profile");
        else if (change == "language") wire.Client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
        else if (change == "filter") model.SelectedGenre = "Comedy";
        else if (change == "library") model.Library = new Library { Id = 8, Type = "movie" };
        else model.SuspendCatalogLoads();
        wire.ReleaseIndex.TrySetResult();
        await Task.WhenAll(load, indexRead);
        Assert.Null(model.LetterIndex);
    }

    [Fact]
    public async Task FirstCardsDoNotWaitForOptionalLetterIndex()
    {
        using var wire = new Wire { HoldIndex = true }; var model = wire.Create();
        var load = model.LoadCommand.ExecuteAsync(null);
        try
        {
            await wire.IndexStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await load.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.NotNull(model.GetWindowItem(0));
        }
        finally { wire.ReleaseIndex.TrySetResult(); await model.EnsureLetterIndexAsync(); }
    }

    [Fact]
    public async Task LaterCardsCanExtendPastAnOlderIndexTotal()
    {
        using var wire = new Wire { CatalogTotal = 1200, CatalogExact = false }; var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        await model.LoadWindowAsync(960, 100, force: true);
        Assert.True(model.TotalCount >= 1060);
        Assert.NotNull(model.GetWindowItem(1050));
    }

    [Fact]
    public async Task LateSmallerIndexRetainsPreviouslyKnownCatalogTotal()
    {
        using var wire = new Wire { HoldIndex = true, IndexTotal = 800 }; var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null);
        Assert.Equal(1000, model.TotalCount);
        wire.ReleaseIndex.TrySetResult(); await model.EnsureLetterIndexAsync();
        Assert.Equal(800, model.LetterIndex!.Total);
        Assert.Equal(1000, model.TotalCount);
    }

    [Fact]
    public async Task LateExactCatalogCountReplacesLargerIndexTotal()
    {
        using var wire = new Wire { HoldCatalog = true, IndexTotal = 1200, CatalogTotal = 800 }; var model = wire.Create();
        var load = model.LoadCommand.ExecuteAsync(null);
        await model.EnsureLetterIndexAsync(); Assert.Equal(1200, model.LetterIndex!.Total);
        wire.ReleaseCatalog.TrySetResult(); await load;
        Assert.Equal(800, model.TotalCount);
        Assert.Equal(800, model.DisplayTotalCount);
        Assert.Equal(800, model.Items.Count);
        await model.LoadWindowAsync(799, 1, force: true);
        Assert.NotNull(model.GetWindowItem(799));
        Assert.All(wire.Seeks, seek => Assert.InRange(seek, 0, 799));
    }

    [Fact]
    public async Task LateLargerIndexCannotOverrideExactCatalogTotal()
    {
        using var wire = new Wire { HoldIndex = true, IndexTotal = 1200, CatalogTotal = 800 }; var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null);
        Assert.Equal(800, model.TotalCount);
        wire.ReleaseIndex.TrySetResult(); await model.EnsureLetterIndexAsync();
        Assert.Equal(1200, model.LetterIndex!.Total);
        Assert.Equal(800, model.TotalCount);
        Assert.Equal(800, model.Items.Count);
    }

    [Fact]
    public async Task ExactCatalogTotalCanShrinkAfterAnEarlierExactCount()
    {
        using var wire = new Wire { CatalogTotal = 1200, IndexTotal = 1200 }; var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        Assert.Equal(1200, model.TotalCount);
        wire.CatalogTotal = 800;
        await model.LoadWindowAsync(720, 80, force: true);
        Assert.Equal(800, model.TotalCount);
        Assert.Equal(800, model.DisplayTotalCount);
        Assert.Equal(800, model.Items.Count);
    }

    [Fact]
    public async Task ProvisionalIndexDoesNotSuppressAuthoritativeCountRequest()
    {
        using var wire = new Wire { CatalogExact = false, ExactCountTotal = 800, IndexTotal = 1200 }; var model = wire.Create();
        var counted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        model.WindowLoaded += () => { if (model.TotalCount == 800) counted.TrySetResult(); };
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        Assert.Equal(1200, model.TotalCount);
        wire.ReleaseExactCount.TrySetResult();
        await counted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(800, model.DisplayTotalCount);
        Assert.Equal(800, model.Items.Count);
    }

    [Fact]
    public async Task ANewerClickRetiresAnOlderPendingJump()
    {
        using var wire = new Wire(); var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        var jumped = new List<int>(); model.LetterJumpRequested += jumped.Add;
        wire.HoldCatalog = true;
        var oldJump = model.JumpToLetterCommand.ExecuteAsync("M");
        wire.HoldCatalog = false;
        await model.JumpToLetterCommand.ExecuteAsync("A");
        wire.ReleaseCatalog.TrySetResult(); await oldJump;
        Assert.Equal(new[] { 0 }, jumped);
    }

    [Fact]
    public async Task IndexFailureLeavesCatalogUsableAndDoesNotGuess()
    {
        using var wire = new Wire { FailIndex = true }; var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null);
        await model.JumpToLetterCommand.ExecuteAsync("M");
        Assert.Null(model.LetterIndex); Assert.Null(model.ErrorMessage);
        Assert.NotNull(model.GetWindowItem(0)); Assert.Equal(0, wire.ProbeRequests);
        Assert.Equal(2, wire.IndexRequests);
    }

    [Fact]
    public async Task FailedIndexCanBeRetriedAndPublishExactLetters()
    {
        using var wire = new Wire { FailIndex = true }; var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null);
        Assert.Null(model.LetterIndex);
        Assert.Equal(1, wire.IndexRequests);
        wire.FailIndex = false;
        await model.EnsureLetterIndexAsync();
        Assert.Equal(2, wire.IndexRequests);
        Assert.NotNull(model.LetterIndex);
        var jumped = -1; model.LetterJumpRequested += position => jumped = position;
        await model.JumpToLetterCommand.ExecuteAsync("M");
        Assert.Equal(742, jumped);
        Assert.Equal(2, wire.IndexRequests);
        Assert.Null(model.ErrorMessage);
    }

    [Fact]
    public async Task IndexFailureIsLoggedWithScopeWithoutShowingCatalogError()
    {
        var directory = Path.Combine(Path.GetTempPath(), "library-letters-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var operation = SiloPlayer.Core.Services.LocalLog.BeginOperation("letters-test", directory);
            using var wire = new Wire { FailIndex = true }; var model = wire.Create();
            await model.LoadCommand.ExecuteAsync(null);
            var log = File.ReadAllText(Path.Combine(directory, "catalog_errors.txt"));
            Assert.Contains("catalog_letters library=7 order=asc type=all group_count=0 query_fingerprint=", log);
            Assert.Contains("status=503", log);
            Assert.Null(model.ErrorMessage);
            Assert.NotNull(model.GetWindowItem(0));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task EmptyLibraryHasNoInventedJump()
    {
        using var wire = new Wire { IndexTotal = 0, CatalogTotal = 0 }; var model = wire.Create();
        var jumps = 0; model.LetterJumpRequested += _ => jumps++;
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        await model.JumpToLetterCommand.ExecuteAsync("A");
        Assert.Equal(0, model.TotalCount); Assert.Empty(model.LetterIndex!.Letters);
        Assert.Equal(0, jumps); Assert.Null(model.ErrorMessage);
    }

    [Fact]
    public async Task ScalarFiltersRemainConjoinedWithAnyStructuredGroups()
    {
        using var wire = new Wire(); var api = new CatalogApi(wire.Client);
        await api.GetCatalogLettersAsync(7, genre: "Drama", yearMin: "2001", queryGroupsMatch: "any",
            queryGroups: [new() { Match = "any", Rules = [new() { Field = "studio", Op = "is", Value = "One" }, new() { Field = "studio", Op = "is", Value = "Two" }] }]);
        using var body = JsonDocument.Parse(wire.IndexBody!);
        var groups = body.RootElement.GetProperty("groups"); Assert.Equal(2, groups.GetArrayLength());
        foreach (var group in groups.EnumerateArray())
        {
            Assert.Equal("all", group.GetProperty("match").GetString());
            Assert.Contains(group.GetProperty("rules").EnumerateArray(), rule => rule.GetProperty("field").GetString() == "genre" && rule.GetProperty("value").GetString() == "Drama");
        }
    }

    [Fact]
    public void RunsIdentifyRepeatedLetterBlocksAndRejectInvalidPositions()
    {
        var index = new CatalogLettersResponse { Total = 6, Letters = [new() { Letter = "A", Count = 4, Position = 0 }, new() { Letter = "#", Count = 2, Position = 2 }],
            Runs = [new() { Letter = "A", Position = 0 }, new() { Letter = "#", Position = 2 }, new() { Letter = "A", Position = 4 }] };
        index.Validate(); Assert.Equal("A", index.LetterAtIndex(5)); Assert.Equal("#", index.LetterAtIndex(3));
        index.Runs[2].Position = 1;
        Assert.Throws<InvalidDataException>(() => index.Validate());
    }

    private sealed class Wire : HttpMessageHandler
    {
        private readonly HttpClient http;
        internal readonly SiloApiClient Client;
        internal bool Supported = true;
        internal int IndexRequests, ProbeRequests;
        internal string? IndexBody;
        internal bool HoldIndex, FailIndex;
        internal int CatalogTotal = 1000;
        internal int IndexTotal = 1000;
        internal bool CatalogExact = true;
        internal int? ExactCountTotal;
        internal readonly TaskCompletionSource ReleaseExactCount = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool HoldCatalog;
        internal readonly TaskCompletionSource ReleaseCatalog = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource IndexStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleaseIndex = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly List<int> Seeks = [];
        internal Wire() { http = new(this, disposeHandler: false); Client = new(http); Client.SetBaseUrl("https://letter-fixture.invalid"); }
        internal LibraryViewModel Create() => new(new CatalogApi(Client)) { Library = new Library { Id = 7, Type = "movie" } };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/search/capabilities")) return Reply(new { letter_index = Supported });
            if (path.EndsWith("/catalog/letters"))
            {
                IndexRequests++; IndexBody = await request.Content!.ReadAsStringAsync(ct);
                IndexStarted.TrySetResult();
                if (HoldIndex) await ReleaseIndex.Task; // Deliberately ignore cancellation to model a late transport.
                if (FailIndex) return Reply(new { detail = "Synthetic unavailable index" }, HttpStatusCode.ServiceUnavailable);
                if (IndexTotal == 0) return Reply(new { total = 0, letters = Array.Empty<object>(), runs = Array.Empty<object>() });
                return Reply(new { total = IndexTotal, letters = new[] { new { letter = "A", count = 742, position = 0 }, new { letter = "M", count = 7, position = 742 }, new { letter = "N", count = IndexTotal - 749, position = 749 } }, runs = new[] { new { letter = "A", position = 0 }, new { letter = "M", position = 742 }, new { letter = "N", position = 749 } } });
            }
            if (path == "/api/v2/catalog")
            {
                var q = request.RequestUri.Query.TrimStart('?').Split('&').Select(x => x.Split('=', 2)).ToDictionary(x => x[0], x => Uri.UnescapeDataString(x[1]));
                var limit = int.Parse(q["limit"]); var seek = q.TryGetValue("seek", out var value) ? int.Parse(value) : 0;
                Seeks.Add(seek); if (limit == 1) ProbeRequests++;
                if (HoldCatalog) await ReleaseCatalog.Task;
                var countOnly = !q.ContainsKey("skip_total") && ExactCountTotal.HasValue;
                if (countOnly) await ReleaseExactCount.Task;
                var total = countOnly ? ExactCountTotal!.Value : CatalogTotal;
                var exact = countOnly || CatalogExact;
                return Reply(new { total = exact ? total : 0, total_exact = exact, items = Enumerable.Range(seek, Math.Max(0, Math.Min(limit, total - seek))).Select(i => new { content_id = "item" + i, title = (i < 742 ? "A " : i < 749 ? "M " : "N ") + i, type = "movie" }), page = new { has_more = seek + limit < total } });
            }
            return Reply(new { }, HttpStatusCode.NotFound);
        }
        private static HttpResponseMessage Reply(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        protected override void Dispose(bool disposing) { if (disposing) http.Dispose(); base.Dispose(disposing); }
    }
}
