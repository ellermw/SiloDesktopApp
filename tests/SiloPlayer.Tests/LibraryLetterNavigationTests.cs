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
    [InlineData("navigation")]
    public async Task LateIndexCannotPublishAfterOwnerChanges(string change)
    {
        using var wire = new Wire { HoldIndex = true }; var model = wire.Create();
        var load = model.LoadCommand.ExecuteAsync(null);
        await wire.IndexStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (change == "profile") wire.Client.SetProfile("new-synthetic-profile");
        else if (change == "language") wire.Client.LocalizedReads.NotifySettingChanged("catalog.metadata_language");
        else if (change == "filter") model.SelectedGenre = "Comedy";
        else model.SuspendCatalogLoads();
        wire.ReleaseIndex.TrySetResult();
        await load;
        Assert.Null(model.LetterIndex);
    }

    [Fact]
    public async Task IndexFailureLeavesCatalogUsableAndDoesNotGuess()
    {
        using var wire = new Wire { FailIndex = true }; var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null);
        await model.JumpToLetterCommand.ExecuteAsync("M");
        Assert.Null(model.LetterIndex); Assert.Null(model.ErrorMessage);
        Assert.NotNull(model.GetWindowItem(0)); Assert.Equal(0, wire.ProbeRequests);
        Assert.Equal(1, wire.IndexRequests);
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
                return Reply(new { total = 1000, letters = new[] { new { letter = "A", count = 742, position = 0 }, new { letter = "M", count = 7, position = 742 }, new { letter = "N", count = 251, position = 749 } }, runs = new[] { new { letter = "A", position = 0 }, new { letter = "M", position = 742 }, new { letter = "N", position = 749 } } });
            }
            if (path == "/api/v2/catalog")
            {
                var q = request.RequestUri.Query.TrimStart('?').Split('&').Select(x => x.Split('=', 2)).ToDictionary(x => x[0], x => Uri.UnescapeDataString(x[1]));
                var limit = int.Parse(q["limit"]); var seek = q.TryGetValue("seek", out var value) ? int.Parse(value) : 0;
                Seeks.Add(seek); if (limit == 1) ProbeRequests++;
                return Reply(new { total = 1000, total_exact = true, items = Enumerable.Range(seek, Math.Min(limit, 1000 - seek)).Select(i => new { content_id = "item" + i, title = (i < 742 ? "A " : i < 749 ? "M " : "N ") + i, type = "movie" }), page = new { has_more = seek + limit < 1000 } });
            }
            return Reply(new { }, HttpStatusCode.NotFound);
        }
        private static HttpResponseMessage Reply(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        protected override void Dispose(bool disposing) { if (disposing) http.Dispose(); base.Dispose(disposing); }
    }
}
