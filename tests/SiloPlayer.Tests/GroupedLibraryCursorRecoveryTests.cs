using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class GroupedLibraryCursorRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecoveryCountLetterJumpAndContinuationKeepSignedGroupedScope(bool largePost)
    {
        using var wire = new SignedCatalog(); var model = wire.Create(largePost);
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        wire.PolicyEpoch++;
        await model.LoadWindowAsync(623, 60, force: true); await model.EnsureLetterIndexAsync();
        Assert.Null(model.ErrorMessage); Assert.Equal(1, wire.InvalidCursors);
        Assert.All(wire.Catalog, request => Assert.Equal("box_set", request.Values.GetValueOrDefault("group")));
        Assert.All(wire.Letters, body => Assert.Equal("box_set", body.GetProperty("group").GetString()));
        Assert.Equal(1000, model.DisplayTotalCount); Assert.Equal(1000, model.LetterIndex!.Total);
        Assert.NotNull(model.GetWindowItem(623)!.BoxSet); Assert.Null(model.GetWindowItem(0));
        var recovery = wire.Catalog.SkipWhile(request => !request.Rejected).Skip(1).ToArray();
        Assert.Contains(recovery, request => !request.Values.ContainsKey("cursor"));
        Assert.Contains(recovery, request => request.Values.GetValueOrDefault("skip_total") != "true");
        await model.JumpToLetterCommand.ExecuteAsync("M"); await model.LoadWindowAsync(842, 60);
        Assert.NotNull(model.GetWindowItem(842)!.BoxSet); Assert.Equal(1, wire.InvalidCursors);
        Assert.All(wire.Catalog, request => Assert.Equal("box_set", request.Values.GetValueOrDefault("group")));
        Assert.All(wire.Catalog, request => Assert.Equal(largePost ? "POST" : "GET", request.Method));
        model.CancelCatalogLoads();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StateRefreshRecoversRetiredSignedScopeAndRefreshesGroupedCounts(bool largePost)
    {
        using var wire = new SignedCatalog(); var model = wire.Create();
        // A large guided rule uses POST without making state affect ordering.
        if (largePost) model.SelectedActor = new string('x', 34000);
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        var reads = wire.Catalog.Count;
        wire.PolicyEpoch++; wire.GroupedTotal = 900; wire.PlayedCount = 3;
        try
        {
            Assert.Equal(LibraryViewModel.GroupedRefreshResult.Completed,
                await model.RefreshGroupedCatalogStateAsync(623, 60));
            await model.EnsureLetterIndexAsync();
            Assert.Equal(1, wire.InvalidCursors);
            var recovery = wire.Catalog.Skip(reads).ToArray();
            Assert.True(recovery[0].Rejected);
            Assert.Contains(recovery, request => !request.Values.ContainsKey("cursor"));
            Assert.Contains(recovery, request => request.Values.GetValueOrDefault("skip_total") != "true");
            Assert.Equal(900, model.DisplayTotalCount); Assert.Equal(900, model.LetterIndex!.Total);
            Assert.Equal(3, model.GetWindowItem(623)!.BoxSet!.PlayedCount);
            Assert.Null(model.GetWindowItem(0));
            Assert.Equal(LibraryViewModel.GroupedRefreshResult.Completed,
                await model.RefreshGroupedCatalogStateAsync(623, 60));
            await model.EnsureRangeLoadedAsync(842, 859);
            Assert.NotNull(model.GetWindowItem(842)!.BoxSet);
            Assert.Equal(1, wire.InvalidCursors); Assert.Null(model.ErrorMessage);
            Assert.All(wire.Catalog, request =>
            {
                Assert.Equal("box_set", request.Values.GetValueOrDefault("group"));
                Assert.Equal(largePost ? "POST" : "GET", request.Method);
            });
            Assert.All(wire.Letters, body => Assert.Equal("box_set", body.GetProperty("group").GetString()));
        }
        finally { model.CancelCatalogLoads(); }
    }

    [Fact]
    public async Task StateRefreshRecoveryIsBoundedAndLeavesNoDeadCursorAfterFailure()
    {
        using var wire = new SignedCatalog(); var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        var reads = wire.Catalog.Count; var indexes = wire.Letters.Count;
        wire.PolicyEpoch++; wire.RejectFresh = true;
        try
        {
            Assert.Equal(LibraryViewModel.GroupedRefreshResult.Failed,
                await model.RefreshGroupedCatalogStateAsync(623, 60));
            Assert.Equal(2, wire.Catalog.Count - reads); Assert.Equal(indexes, wire.Letters.Count);
            Assert.Null(model.GetWindowItem(0)); Assert.Null(model.ErrorMessage);
            wire.RejectFresh = false;
            Assert.Equal(LibraryViewModel.GroupedRefreshResult.Completed,
                await model.RefreshGroupedCatalogStateAsync(623, 60));
            Assert.NotNull(model.GetWindowItem(623)!.BoxSet);
            Assert.Equal(1, wire.InvalidCursors);
            Assert.All(wire.Catalog, request => Assert.Equal("box_set", request.Values.GetValueOrDefault("group")));
        }
        finally { model.CancelCatalogLoads(); }
    }

    [Fact]
    public async Task RecoveryRemainsOneAttemptWhenFreshGroupedScopeIsRejected()
    {
        using var wire = new SignedCatalog(); var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        var reads = wire.Catalog.Count; var indexes = wire.Letters.Count;
        wire.PolicyEpoch++; wire.RejectFresh = true;
        await model.LoadWindowAsync(623, 60, force: true);
        Assert.Contains("Failed to load catalog", model.ErrorMessage);
        Assert.Equal(2, wire.Catalog.Count - reads); Assert.Equal(indexes, wire.Letters.Count);
        Assert.All(wire.Catalog, request => Assert.Equal("box_set", request.Values.GetValueOrDefault("group")));
        Assert.Null(model.GetWindowItem(0)); model.CancelCatalogLoads();
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("library")]
    [InlineData("disable")]
    [InlineData("navigation")]
    public async Task DepartureDuringRecoveryGroupingPreflightCannotDispatchOrPublish(string change)
    {
        using var wire = new SignedCatalog(); var model = wire.Create();
        await model.LoadCommand.ExecuteAsync(null); await model.EnsureLetterIndexAsync();
        // Model a capability cache expiry without a five-minute wall-clock wait.
        typeof(CatalogApi).GetField("_searchRuleCache", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(wire.Api, null);
        wire.HoldCapability = true; wire.PolicyEpoch++;
        var reads = wire.Catalog.Count;
        var pending = model.LoadWindowAsync(800, 60, force: true);
        int readsDuringPreflight;
        try
        {
            await wire.CapabilityStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            readsDuringPreflight = wire.Catalog.Count;
            if (change == "profile") wire.Client.SetProfile("replacement");
            else if (change == "library") model.Library = new Library { Id = 2, Type = "movie", BoxSetsEnabled = true };
            else if (change == "disable") model.Library!.BoxSetsEnabled = false;
            else model.SuspendCatalogLoads();
        }
        finally { wire.ReleaseCapability.TrySetResult(); }
        await pending; await Task.Delay(30);
        Assert.Equal(reads + 1, readsDuringPreflight); // Only the rejected old cursor, no fresh recovery yet.
        Assert.Equal(readsDuringPreflight, wire.Catalog.Count);
        Assert.Null(model.GetWindowItem(800));
        Assert.All(wire.Catalog, request => Assert.Equal("box_set", request.Values.GetValueOrDefault("group")));
        model.CancelCatalogLoads();
    }

    private sealed class SignedCatalog : HttpMessageHandler
    {
        private static readonly byte[] Key = Encoding.UTF8.GetBytes("synthetic-grouped-recovery-key");
        public readonly SiloApiClient Client;
        public readonly CatalogApi Api;
        public int PolicyEpoch, InvalidCursors, PlayedCount;
        public int GroupedTotal = 1000;
        public bool RejectFresh, HoldCapability;
        public readonly TaskCompletionSource CapabilityStarted = new(TaskCreationOptions.RunContinuationsAsynchronously), ReleaseCapability = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly List<Read> Catalog = [];
        public readonly List<JsonElement> Letters = [];
        public sealed record Read(Dictionary<string, string> Values, string Method, bool Rejected);
        public SignedCatalog()
        {
            Client = new(new HttpClient(this)); Client.SetBaseUrl("https://grouped-recovery.invalid"); Client.SetProfile("synthetic"); Api = new(Client);
        }
        public LibraryViewModel Create(bool largePost = false)
        {
            var model = new LibraryViewModel(Api) { Library = new Library { Id = 1, Type = "movie", BoxSetsEnabled = true } };
            if (largePost)
            {
                model.UseAdvancedRules = true;
                model.AdvancedRules.Add(new QueryRule { Field = "title", Op = "contains", Value = new string('x', 34000) });
            }
            return model;
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("capabilities"))
            {
                if (HoldCapability) { CapabilityStarted.TrySetResult(); await ReleaseCapability.Task; }
                return Reply(new { box_sets = true, letter_index = true });
            }
            if (path.EndsWith("letters"))
            {
                var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement.Clone(); Letters.Add(body);
                var total = body.TryGetProperty("group", out var group) && group.GetString() == "box_set" ? GroupedTotal : 1800;
                return Reply(new { total, letters = new[] { new { letter = "A", count = 742, position = 0 }, new { letter = "M", count = total - 742, position = 742 } }, runs = new[] { new { letter = "A", position = 0 }, new { letter = "M", position = 742 } } });
            }
            Dictionary<string, string> values;
            if (request.Method == HttpMethod.Post)
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                values = body.RootElement.EnumerateObject().ToDictionary(member => member.Name, member => member.Value.ValueKind == JsonValueKind.String ? member.Value.GetString()! : member.Value.GetRawText());
            }
            else values = request.RequestUri.Query.TrimStart('?').Split('&').Select(pair => pair.Split('=', 2)).ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1]));
            var scope = (request.Headers.TryGetValues("X-Profile-ID", out var profiles) ? profiles.First() : "none") + "|" + string.Join('&', values.Where(pair => pair.Key is not ("seek" or "cursor" or "skip_total")).OrderBy(pair => pair.Key).Select(pair => pair.Key + "=" + pair.Value)) + "|policy=" + PolicyEpoch;
            var offset = values.TryGetValue("seek", out var seek) ? int.Parse(seek) : 0;
            if (values.TryGetValue("cursor", out var cursor))
            {
                var parts = cursor.Split('.');
                if (parts.Length != 2 || parts[1] != Sign(scope, parts[0]))
                {
                    Catalog.Add(new(values, request.Method.Method, true)); InvalidCursors++;
                    return Invalid();
                }
                if (!values.ContainsKey("seek")) offset = int.Parse(parts[0]);
            }
            Catalog.Add(new(values, request.Method.Method, false));
            if (RejectFresh && !values.ContainsKey("cursor")) return Invalid();
            var grouped = values.GetValueOrDefault("group") == "box_set"; var countTotal = grouped ? GroupedTotal : 1800;
            var count = Math.Min(int.Parse(values["limit"]), countTotal - offset);
            return Reply(new { items = Enumerable.Range(offset, count).Select(index => new { content_id = grouped ? "box_set:1:" + index : "movie-" + index, title = "Title " + index, type = grouped ? "box_set" : "movie", box_set = grouped ? new { id = index.ToString(), library_id = "1", movie_count = 3, played_count = PlayedCount } : null }), total = countTotal, total_exact = values.GetValueOrDefault("skip_total") != "true", window_cursor = "0." + Sign(scope, "0"), page = new { has_more = offset + count < countTotal, next_cursor = (offset + count) + "." + Sign(scope, (offset + count).ToString()) } });
        }
        private static string Sign(string scope, string position) => Convert.ToHexString(HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes(scope + "\0" + position)));
        private static HttpResponseMessage Invalid() => Reply(new { type = "https://fixture.invalid/docs/api/v2/problems/invalid_cursor", status = 400, detail = "Synthetic retired cursor." }, HttpStatusCode.BadRequest);
        private static HttpResponseMessage Reply(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
    }
}
