using System.Net;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

/// <summary>
/// Seek-bar previews (trickplay). Official silo-server 3b2b32168: the tile maths of
/// web/src/player/trickplay.ts, the adapter of web/src/api/v2/trickplay.ts, and the
/// manifest read/retry/poll cases of web/src/hooks/queries/trickplay.test.tsx.
/// </summary>
public sealed class SeekPreviewTests
{
    private static WatchTrickplayResponse Response(int sheets = 3, string expires = "2026-11-23T13:00:00Z") => new()
    {
        FileId = "42",
        IntervalMs = 10_000,
        ThumbnailWidth = 300,
        ThumbnailHeight = 168,
        TileColumns = 10,
        TileRows = 10,
        ThumbnailCount = 250,
        ExpiresAt = expires,
        // Deliberately out of order: the adapter sorts by index.
        Sheets = Enumerable.Range(0, sheets).Reverse().Select(i => new WatchTrickplaySheet { Index = i, Url = $"https://cdn.example/s{i}.jpg?sig=x" }).ToList(),
    };

    private static SeekPreviewManifest Manifest(int sheets = 3) => SeekPreviewManifest.FromResponse(Response(sheets), 42)!;

    [Fact]
    public void AdapterSortsSheetsByIndexAndReadsTheGrid()
    {
        var manifest = Manifest();
        Assert.Equal(new[] { "https://cdn.example/s0.jpg?sig=x", "https://cdn.example/s1.jpg?sig=x", "https://cdn.example/s2.jpg?sig=x" }, manifest.Sheets);
        Assert.Equal((10_000L, 300, 168, 10, 10, 250), (manifest.IntervalMs, manifest.Width, manifest.Height, manifest.Columns, manifest.Rows, manifest.Count));
        Assert.Equal(new DateTimeOffset(2026, 11, 23, 13, 0, 0, TimeSpan.Zero), manifest.ExpiresAt);
    }

    [Fact]
    public void AdapterResolvesRootRelativeSheetUrlsAgainstTheServer()
    {
        var response = Response(1);
        response.Sheets![0].Url = "/api/v2/trickplay/sheets/0.jpg?token=abc";
        var manifest = SeekPreviewManifest.FromResponse(response, 42, url => SiloApiClient.ResolveServerUrl(url, "https://silo.example"));
        Assert.Equal("https://silo.example/api/v2/trickplay/sheets/0.jpg?token=abc", manifest!.Sheets[0]);
    }

    [Theory]
    [InlineData("other-file")]
    [InlineData("interval")]
    [InlineData("width")]
    [InlineData("columns")]
    [InlineData("count")]
    [InlineData("oversized")]
    public void UnusableManifestsAreRefused(string defect)
    {
        var response = Response();
        switch (defect)
        {
            case "other-file": response.FileId = "43"; break;
            case "interval": response.IntervalMs = 0; break;
            case "width": response.ThumbnailWidth = 0; break;
            case "columns": response.TileColumns = -1; break;
            case "count": response.ThumbnailCount = 0; break;
            case "oversized": response.ThumbnailWidth = SeekPreviewManifest.MaxThumbnailEdge + 1; break;
        }
        Assert.Null(SeekPreviewManifest.FromResponse(response, 42));
    }

    [Theory]
    // seconds, index, sheet, x, y
    [InlineData(0, 0, 0, 0, 0)]
    [InlineData(9.999, 0, 0, 0, 0)]
    [InlineData(10, 1, 0, 300, 0)]
    [InlineData(105, 10, 0, 0, 168)]
    [InlineData(999, 99, 0, 2700, 1512)]
    [InlineData(1000, 100, 1, 0, 0)]
    [InlineData(1234, 123, 1, 900, 336)]
    // Before the start clamps to the first thumbnail, past the end to the last.
    [InlineData(-5, 0, 0, 0, 0)]
    [InlineData(1_000_000, 249, 2, 2700, 672)]
    public void TileMathMatchesOfficialTrickplayTile(double seconds, int index, int sheet, int x, int y)
    {
        var tile = Manifest().TileAt(seconds);
        Assert.NotNull(tile);
        Assert.Equal((index, sheet, x, y), (tile!.Index, tile.Sheet, tile.X, tile.Y));
        Assert.Equal($"https://cdn.example/s{sheet}.jpg?sig=x", tile.Url);
    }

    [Fact]
    public void NoTileForNonFiniteTimesOrMissingSheets()
    {
        Assert.Null(Manifest().TileAt(double.NaN));
        Assert.Null(Manifest().TileAt(double.PositiveInfinity));
        // Thumbnails 100+ have no sheet when only the first is listed (official: `if (!url) return null`).
        Assert.Null(Manifest(sheets: 1).TileAt(1000));
        Assert.NotNull(Manifest(sheets: 1).TileAt(999));
    }

    [Fact]
    public void SheetsAreInterchangeableOnlyForTheSameFileAndGrid()
    {
        var renewed = SeekPreviewManifest.FromResponse(Response(expires: "2026-11-23T14:00:00Z"), 42)!;
        Assert.True(Manifest().SameSheetsAs(renewed));
        var regenerated = Response(); regenerated.IntervalMs = 5_000;
        Assert.False(Manifest().SameSheetsAs(SeekPreviewManifest.FromResponse(regenerated, 42)));
        Assert.False(Manifest().SameSheetsAs(Manifest(sheets: 2)));
    }

    [Fact]
    public void ExpiryAndScheduledRefreshFollowOfficialMargin()
    {
        var now = new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
        var manifest = Manifest();
        Assert.False(manifest.IsExpired(now));
        Assert.True(manifest.IsExpired(now.AddHours(1)));
        // Read again five minutes before the URLs expire...
        Assert.Equal(TimeSpan.FromMinutes(55), SeekPreviewRefreshPolicy.AfterSuccess(manifest.ExpiresAt, now));
        // ...but never sooner than a minute, even for URLs already near or past expiry.
        Assert.Equal(TimeSpan.FromSeconds(60), SeekPreviewRefreshPolicy.AfterSuccess(now.AddMinutes(3), now));
        Assert.Equal(TimeSpan.FromSeconds(60), SeekPreviewRefreshPolicy.AfterSuccess(now.AddMinutes(-1), now));
        // No usable expiry: no scheduled read (official refetchInterval false).
        Assert.Null(SeekPreviewRefreshPolicy.AfterSuccess(null, now));
        var unparsable = SeekPreviewManifest.FromResponse(Response(expires: "soon"), 42)!;
        Assert.Null(unparsable.ExpiresAt);
        Assert.False(unparsable.IsExpired(now));
    }

    [Fact]
    public void FailedSheetRereadsTheManifestAtMostOnceAMinute()
    {
        var now = new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(TimeSpan.Zero, SeekPreviewRefreshPolicy.AfterSheetError(null, now));
        Assert.Equal(TimeSpan.Zero, SeekPreviewRefreshPolicy.AfterSheetError(now.AddSeconds(-61), now));
        Assert.Equal(TimeSpan.FromSeconds(45), SeekPreviewRefreshPolicy.AfterSheetError(now.AddSeconds(-15), now));
    }

    // Official trickplay.test.tsx: each case runs the read schedule against a fake clock
    // and counts the requests made by a point in time.

    private static readonly Exception Network = new HttpRequestException("network unavailable");
    private static readonly Exception NoAnswer = new TaskCanceledException("timed out", new TimeoutException());
    private static readonly Exception RefreshNoAnswer = new OperationCanceledException("API context changed.");
    private static ApiException Problem(int status, double? retryAfter = null) => new("problem", "problem", status, retryAfterSeconds: retryAfter);

    /// <summary>Request times (ms) up to <paramref name="untilMs"/> for answers drawn from <paramref name="answer"/>.</summary>
    private static List<double> Schedule(Func<int, Exception?> answer, double untilMs)
    {
        var times = new List<double>();
        var now = 0d; var attempt = 0;
        while (now <= untilMs)
        {
            times.Add(now);
            var error = answer(times.Count);
            if (error == null) break; // A good manifest; its next read is minutes away.
            var retry = SeekPreviewRefreshPolicy.AfterFailure(attempt, error);
            if (retry == null) break;
            attempt = retry.StartsNewRun ? 0 : attempt + 1;
            now += retry.Delay.TotalMilliseconds;
        }
        return times;
    }

    private static int RequestsBy(List<double> times, double ms) => times.Count(time => time <= ms);

    [Theory]
    [InlineData("network")]
    [InlineData("timeout")]
    [InlineData("refresh")]
    public void RecoversATransientInitialFailureWithoutAFocusEvent(string kind)
    {
        var first = kind switch { "network" => Network, "timeout" => NoAnswer, _ => RefreshNoAnswer };
        var times = Schedule(n => n == 1 ? first : null, 3_100);
        Assert.Equal(2, RequestsBy(times, 3_100));
    }

    [Fact]
    public void BoundsImmediateRetriesThenPollsForRecovery()
    {
        var times = Schedule(n => n <= 3 ? Network : null, 63_100);
        Assert.Equal(3, RequestsBy(times, 3_100));
        Assert.Equal(4, RequestsBy(times, 63_100));
    }

    [Fact]
    public void StopsRequestingAManifestThatIsNoLongerAvailable()
    {
        var times = Schedule(_ => Problem(404), 120_000);
        Assert.Equal(1, RequestsBy(times, 120_000));
        Assert.Null(SeekPreviewRefreshPolicy.AfterFailure(0, Problem(403)));
    }

    [Fact]
    public void WaitsForRetryAfterBeforeRetryingARateLimitedManifest()
    {
        var times = Schedule(n => n == 1 ? Problem(429, 10) : null, 20_000);
        Assert.Equal(1, RequestsBy(times, 9_999));
        Assert.Equal(2, RequestsBy(times, 10_019));
    }

    [Fact]
    public void PollsARateLimitedManifestAfterBoundedRetriesAndHonorsALongRetryAfter()
    {
        var times = Schedule(n => n <= 3 ? Problem(429, 90) : null, 300_000);
        Assert.Equal(3, RequestsBy(times, 180_010));
        Assert.Equal(3, RequestsBy(times, 240_010));
        Assert.Equal(4, RequestsBy(times, 270_010));
    }

    [Fact]
    public void ServerErrorsAreTransientAndClientErrorsTerminal()
    {
        Assert.True(SeekPreviewRefreshPolicy.IsTransient(Problem(500)));
        Assert.True(SeekPreviewRefreshPolicy.IsTransient(Problem(503)));
        Assert.True(SeekPreviewRefreshPolicy.IsTransient(Problem(0)));
        Assert.False(SeekPreviewRefreshPolicy.IsTransient(Problem(400)));
        Assert.False(SeekPreviewRefreshPolicy.IsTransient(Problem(422)));
        Assert.False(SeekPreviewRefreshPolicy.IsTransient(new JsonException("bad body")));
    }

    [Fact]
    public void LoadsOnlyThePointedSheetAndTheNext()
    {
        Assert.Equal(new[] { 2, 3 }, SeekPreviewSheetCache.Wanted(2, 5));
        Assert.Equal(new[] { 4 }, SeekPreviewSheetCache.Wanted(4, 5));
        Assert.Empty(SeekPreviewSheetCache.Wanted(-1, 5));
        Assert.Empty(SeekPreviewSheetCache.Wanted(5, 5));
    }

    [Fact]
    public void CacheEvictsLeastRecentlyUsedWithinCountAndKeepsTheSheetsInView()
    {
        var cache = new SeekPreviewSheetCache(maxSheets: 3, maxBytes: long.MaxValue);
        Assert.Empty(cache.Add(0, 10, "s0", [0, 1]));
        Assert.Empty(cache.Add(1, 10, "s1", [0, 1]));
        Assert.Empty(cache.Add(5, 10, "s5", [5, 6]));
        Assert.True(cache.TryGet(0, out var path0) && path0 == "s0"); // 0 is now the most recent
        var evicted = cache.Add(6, 10, "s6", [5, 6]);
        Assert.Equal(new[] { (1, "s1") }, evicted);
        Assert.Equal(new[] { 6, 0, 5 }, cache.Sheets);
        Assert.Equal(3, cache.Count);
    }

    [Fact]
    public void CacheHonorsItsByteBudgetButNeverDropsTheSheetsInView()
    {
        var cache = new SeekPreviewSheetCache(maxSheets: 10, maxBytes: 25);
        cache.Add(0, 10, "s0", [0, 1]);
        cache.Add(1, 10, "s1", [0, 1]);
        // Over budget, but 0 and 1 are both in view: nothing else to drop.
        Assert.Empty(cache.Add(2, 10, "s2", [0, 1, 2]));
        Assert.Equal(30, cache.Bytes);
        Assert.Equal(new[] { (0, "s0"), (1, "s1") }, cache.Add(3, 10, "s3", [2, 3]).OrderBy(e => e.Sheet));
        Assert.Equal(new[] { "s3", "s2" }, cache.Clear());
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void ReplacingASheetReturnsItsOldFile()
    {
        var cache = new SeekPreviewSheetCache();
        cache.Add(0, 10, "old", [0]);
        Assert.Equal(new[] { (0, "old") }, cache.Add(0, 10, "new", [0]));
        Assert.True(cache.TryGet(0, out var path) && path == "new");
    }

    [Theory]
    [InlineData(300, 168, 256, 143)]
    [InlineData(320, 180, 256, 144)]
    [InlineData(160, 90, 160, 90)]
    [InlineData(256, 144, 256, 144)]
    public void DecodedTilesAreBoundedForTheTooltip(int width, int height, int decodedWidth, int decodedHeight)
        => Assert.Equal((decodedWidth, decodedHeight), SeekPreviewSheetCache.DecodedTileSize(width, height));

    [Fact]
    public void FileVersionReadsTrickplayAvailable()
    {
        var detail = JsonSerializer.Deserialize<WatchDetailResponse>("""{"versions":[{"file_id":42,"trickplay_available":true},{"file_id":43}]}""",
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!;
        Assert.True(detail.Versions[0].TrickplayAvailable);
        Assert.False(detail.Versions[1].TrickplayAvailable);
    }

    [Fact]
    public async Task ManifestIsReadForThePlayedFileWithTheInitiatingProfile()
    {
        var handler = new ManifestHandler();
        using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://silo.example"); client.SetProfile("profile-1");
        var context = client.CaptureContext();
        var manifest = await new PlaybackApi(client).GetWatchTrickplayAsync("episode 7", 42, context);
        Assert.Equal("/api/v2/watch/episode%207/trickplay", handler.Path);
        Assert.Equal("?file_id=42", handler.Query);
        Assert.Equal("profile-1", handler.Profile);
        Assert.Equal(250, manifest.ThumbnailCount);
        Assert.Equal(2, manifest.Sheets!.Count);
    }

    [Fact]
    public async Task ManifestReplyForAnotherProfileIsDropped()
    {
        var handler = new ManifestHandler();
        using var http = new HttpClient(handler);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://silo.example"); client.SetProfile("profile-1");
        var context = client.CaptureContext();
        handler.Answered = () => client.SetProfile("profile-2");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new PlaybackApi(client).GetWatchTrickplayAsync("movie", 42, context));
    }

    private sealed class ManifestHandler : HttpMessageHandler
    {
        public string? Path, Query, Profile;
        public Action? Answered;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Path = request.RequestUri!.AbsolutePath;
            Query = request.RequestUri.Query;
            Profile = request.Headers.TryGetValues("X-Profile-Id", out var values) ? values.Single() : null;
            Answered?.Invoke();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"file_id":"42","interval_ms":10000,"thumbnail_width":300,"thumbnail_height":168,"tile_columns":10,"tile_rows":10,
                     "thumbnail_count":250,"expires_at":"2026-11-23T13:00:00Z",
                     "sheets":[{"index":1,"url":"https://cdn.example/s1.jpg"},{"index":0,"url":"https://cdn.example/s0.jpg"}]}
                    """, Encoding.UTF8, "application/json"),
            });
        }
    }
}
