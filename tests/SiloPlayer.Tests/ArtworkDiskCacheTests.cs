using System.Diagnostics;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class ArtworkDiskCacheTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("silo-artwork-test-");
    private readonly TestClock _clock = new();
    private readonly HttpClient _http = new(new ArtworkHandler());

    private string DirectoryPath => _root.FullName;
    private string IndexPath => Path.Combine(DirectoryPath, ArtworkDiskCache.IndexName);

    [Fact]
    public async Task RecentlyRequestedArtworkSurvivesEqualWindowsAccessTimes()
    {
        using var cache = Open(90);
        var paths = new Dictionary<string, string>();
        foreach (var id in new[] { "one", "two", "three" })
        {
            paths.Add(await Download(cache, id), id);
            _clock.Advance(TimeSpan.FromMinutes(1));
        }

        var first = Directory.EnumerateFiles(DirectoryPath, "*.img").First();
        await cache.GetImageDiskPathAsync(paths[first], "poster", Url(paths[first]), _http);
        foreach (var path in paths.Keys)
            File.SetLastAccessTimeUtc(path, new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));

        var result = await cache.DiskCache.MaintainAsync();

        Assert.True(File.Exists(first), "The most recently requested artwork was evicted.");
        Assert.Equal(1, result.FilesRemoved);
        Assert.Equal(64L, result.BytesRemaining);
    }

    [Fact]
    public async Task TrimsToEightyFivePercentAndStops()
    {
        using var cache = Open(640);
        var files = Enumerable.Range(1, 21).Select(key => Seed(key)).ToArray();
        var result = await cache.DiskCache.MaintainAsync();
        Assert.Equal(4, result.FilesRemoved);
        Assert.Equal(544, result.BytesRemaining);
        Assert.All(files[..4], path => Assert.False(File.Exists(path)));
        Assert.All(files[4..], path => Assert.True(File.Exists(path)));
        Assert.False((await cache.DiskCache.MaintainAsync()).Trimmed);
    }

    [Fact]
    public async Task DownloadCrossingTheCapSchedulesBackgroundTrim()
    {
        using var logs = new FixtureLog();
        var ordinary = Enumerable.Range(1, 3).Select(key => Seed(key)).ToArray();
        using var cache = new ImageService(DirectoryPath, _clock, automaticMaintenance: true, maxDiskCacheBytes: 100);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!File.Exists(IndexPath)) await Task.Delay(20, timeout.Token);

        var newPath = await Download(cache, "crosses-the-cap");
        while (!File.Exists(logs.Path) || new FileInfo(logs.Path).Length == 0) await Task.Delay(20, timeout.Token);

        Assert.False(File.Exists(ordinary[0]));
        Assert.False(File.Exists(ordinary[1]));
        Assert.True(File.Exists(newPath));
        var line = Assert.Single(File.ReadAllLines(logs.Path));
        Assert.Matches(@"artwork_trim \| ms=\d+ \| files_removed=2 \| bytes_removed=64 \| files_remaining=2 \| bytes_remaining=64 \| rescan=False$", line);
    }

    [Fact]
    public async Task ProtectedArtworkSurvivesAndGoesLastOnlyAboveCap()
    {
        using var cache = Open(110);
        var kept = new List<string>();
        foreach (var id in new[] { "continue", "next", "other-next" })
        {
            cache.ProtectProgressArtwork(id, "poster", Url(id) + "?signature=home");
            kept.Add(await Download(cache, id));
            _clock.Advance(TimeSpan.FromMinutes(1));
        }

        var ordinary = Seed(1, minutesAgo: 1);
        var result = await cache.DiskCache.MaintainAsync();
        Assert.Equal(96L, result.BytesRemaining); // Protected bytes may exceed the headroom target.
        Assert.False(File.Exists(ordinary));
        Assert.All(kept, path => Assert.True(File.Exists(path)));

        cache.ProtectProgressArtwork("fourth", "poster", Url("fourth"));
        var fourth = await Download(cache, "fourth");
        _clock.Advance(TimeSpan.FromMinutes(1));
        result = await cache.DiskCache.MaintainAsync();
        Assert.Equal(64L, result.BytesRemaining);
        Assert.False(File.Exists(kept[0]));
        Assert.True(File.Exists(kept[2]));
        Assert.True(File.Exists(fourth));
    }

    [Fact]
    public async Task ProtectionExpiresAfterThirtyDaysAndSurvivesRestartUntilThen()
    {
        string kept;
        using (var cache = Open(90))
        {
            kept = await Download(cache, "kept");
            cache.ProtectProgressArtwork("kept", "poster", Url("kept"));
            await cache.DiskCache.MaintainAsync();
        }

        _clock.Advance(TimeSpan.FromDays(29));
        using var restarted = Open(90);
        Seed(1);
        Seed(2);
        Assert.True((await restarted.DiskCache.MaintainAsync()).IndexLoaded);
        Assert.True(File.Exists(kept));
        _clock.Advance(TimeSpan.FromDays(2));
        Seed(3);
        Seed(4);
        await Eventually(restarted, state => state.Trimmed);
        Assert.False(File.Exists(kept));
    }

    [Fact]
    public async Task ExpiresUnusedArtworkEvenWithRoomAndProtection()
    {
        using var cache = Open();
        var expired = await Download(cache, "expired");
        var touched = await Download(cache, "touched");
        await cache.GetImageAsync("touched", "poster", Url("touched"), _http);
        _clock.Advance(TimeSpan.FromDays(59));
        await cache.GetImageAsync("touched", "poster", Url("touched"), _http);
        cache.ProtectProgressArtwork("expired", "poster", Url("expired"));
        await cache.DiskCache.MaintainAsync();
        Assert.True(File.Exists(expired));

        _clock.Advance(TimeSpan.FromDays(1));
        var result = await cache.DiskCache.MaintainAsync();
        Assert.False(result.Trimmed);
        Assert.Equal(1, result.FilesRemoved);
        Assert.False(File.Exists(expired));
        Assert.True(File.Exists(touched));
    }

    [Theory]
    [InlineData("intact", true)]
    [InlineData("missing", false)]
    [InlineData("damaged", false)]
    public async Task ReopensFromIndexOrRebuildsFromFileTimes(string state, bool indexLoaded)
    {
        string oldest;
        using (var cache = Open())
        {
            oldest = await Download(cache, "oldest-write");
            _clock.Advance(TimeSpan.FromMinutes(1));
            await Download(cache, "second-write");
            _clock.Advance(TimeSpan.FromMinutes(1));
            await cache.GetImageAsync("oldest-write", "poster", Url("oldest-write"), _http);
            File.SetLastWriteTimeUtc(oldest, _clock.GetUtcNow().AddMinutes(-10).UtcDateTime);
            await cache.GetImageAsync("oldest-write", "poster", Url("oldest-write"), _http);
            // The byte-memory hit cannot observe an external write stamp change.
            await cache.GetImageDiskPathAsync("oldest-write", "poster", Url("oldest-write"), _http);
            await cache.DiskCache.MaintainAsync();
        }

        // The legacy file times rank this file oldest, unlike the app's index.
        File.SetLastAccessTimeUtc(oldest, File.GetLastWriteTimeUtc(oldest));
        if (state == "missing") File.Delete(IndexPath);
        if (state == "damaged")
        {
            var bytes = File.ReadAllBytes(IndexPath);
            bytes[32] ^= 1;
            File.WriteAllBytes(IndexPath, bytes);
        }
        _clock.Advance(TimeSpan.FromMinutes(1));
        using var restarted = Open(55);
        var result = await restarted.DiskCache.MaintainAsync();
        Assert.Equal(indexLoaded, result.IndexLoaded);
        Assert.True(result.Rescanned);
        Assert.Equal(32L, result.BytesRemaining);
        Assert.Equal(indexLoaded, File.Exists(oldest));
    }

    [Fact]
    public async Task RebuildPreservesRecentlyAccessedLegacyArtwork()
    {
        using var cache = Open();
        var recent = Seed(1, minutesAgo: 70 * 24 * 60);
        var expired = Seed(2, minutesAgo: 70 * 24 * 60);
        File.SetLastAccessTimeUtc(recent, _clock.GetUtcNow().AddDays(-1).UtcDateTime);

        var result = await cache.DiskCache.MaintainAsync();

        Assert.False(result.IndexLoaded);
        Assert.Equal(1, result.FilesRemoved);
        Assert.True(File.Exists(recent));
        Assert.False(File.Exists(expired));
    }

    [Fact]
    public async Task ReconciliationReusesIndexValidationOnlyForUnchangedFiles()
    {
        var unchanged = Seed(1);
        var changed = Seed(2);
        using (var cache = Open())
            await cache.DiskCache.MaintainAsync();

        File.WriteAllText(changed, "<html>Not artwork</html>");
        // The unchanged file can be inventoried, but reopening it to sniff would fail.
        using var held = File.Open(unchanged, FileMode.Open, FileAccess.Read, FileShare.None);
        using var restarted = Open();
        var result = await restarted.DiskCache.MaintainAsync();

        Assert.True(result.IndexLoaded);
        Assert.True(result.Rescanned);
        Assert.True(restarted.DiskCache.TryUseFile(unchanged));
        Assert.False(File.Exists(changed));
    }

    [Fact]
    public async Task ReconcilesExternalDeletionAndAdditionWithoutRescanning()
    {
        using var cache = Open();
        var old = Seed(1);
        Assert.Equal(32L, (await cache.DiskCache.MaintainAsync()).BytesRemaining);
        File.Delete(old);
        var added = Seed(2, size: 48);

        var result = await Eventually(cache, state => state.BytesRemaining == 48 && state.FilesRemaining == 1);
        Assert.False(result.Rescanned);

        File.WriteAllBytes(added, new byte[80]);
        File.SetLastWriteTimeUtc(added, _clock.GetUtcNow().AddDays(-1).UtcDateTime);
        result = await Eventually(cache, state => state.BytesRemaining == 80 && state.FilesRemaining == 1);
        Assert.False(result.Rescanned);
    }

    [Fact]
    public async Task InUseAndRecentlyRequestedArtworkSkipsEviction()
    {
        using var cache = Open(110);
        var inUse = Seed(1);
        await cache.DiskCache.MaintainAsync();
        using (cache.DiskCache.BeginUse(inUse))
        {
            var ordinaryFirst = await Download(cache, "ordinary-one");
            var ordinarySecond = await Download(cache, "ordinary-two");
            _clock.Advance(TimeSpan.FromMinutes(1));
            var recent = await Download(cache, "recent");
            var result = await cache.DiskCache.MaintainAsync();
            Assert.Equal(64L, result.BytesRemaining);
            Assert.True(File.Exists(inUse));
            Assert.True(File.Exists(recent));
            Assert.False(File.Exists(ordinaryFirst));
            Assert.False(File.Exists(ordinarySecond));
        }
    }

    [Fact]
    public async Task RemovesStraysAndDoesNotFollowDirectoryJunction()
    {
        using var cache = Open();
        var ordinary = Seed(1);
        var zero = Seed(2, size: 0);
        var document = Seed(3);
        File.WriteAllText(document, "<html>Cache poison</html>");
        var partial = Path.Combine(DirectoryPath, "0123456789ABCDEF.img.crashed.tmp");
        var unrelated = Path.Combine(DirectoryPath, "poster.img");
        File.WriteAllText(partial, "partial");
        File.WriteAllText(unrelated, "unknown format");
        var outside = Directory.CreateTempSubdirectory("silo-artwork-outside-");
        var junction = Path.Combine(DirectoryPath, "junction");
        try
        {
            var sentinel = Path.Combine(outside.FullName, "stray.tmp");
            File.WriteAllText(sentinel, "outside");
            await CreateJunction(junction, outside.FullName);

            using (cache.DiskCache.BeginUse(partial))
            {
                await cache.DiskCache.MaintainAsync();
                Assert.True(File.Exists(partial));
            }
            await Eventually(cache, _ => !File.Exists(partial));
            Assert.True(File.Exists(ordinary));
            Assert.False(File.Exists(zero));
            Assert.False(File.Exists(document));
            Assert.False(File.Exists(unrelated));
            Assert.False(Directory.Exists(junction));
            Assert.Equal("outside", File.ReadAllText(sentinel));
        }
        finally
        {
            if (Directory.Exists(junction)) Directory.Delete(junction);
            outside.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartupVerificationFailureUsesMemoryAndRetriesWithoutRestart(bool retryFromMaintenance)
    {
        const string id = "unverified-cache";
        var configuredDirectory = Path.Combine(DirectoryPath, "ImageCache");
        var target = Directory.CreateDirectory(Path.Combine(DirectoryPath, "moved-cache")).FullName;
        File.WriteAllText(configuredDirectory, "blocks cache creation");
        try
        {
            using var cache = new ImageService(configuredDirectory, _clock, automaticMaintenance: false);
            Assert.Null(await cache.GetImageDiskPathAsync(id, "poster", Url(id), _http));
            var bytes = await cache.GetImageAsync(id, "poster", Url(id), _http);
            Assert.Equal(new byte[32], bytes);
            Assert.Same(bytes, await cache.GetImageAsync(id, "poster", Url(id), _http));
            Assert.False(cache.IsKnownUnavailable(id, "poster", Url(id)));
            Assert.Equal("blocks cache creation", File.ReadAllText(configuredDirectory));

            File.Delete(configuredDirectory);
            await CreateJunction(configuredDirectory, target);
            Assert.False((await cache.DiskCache.MaintainAsync()).Rescanned); // Still in backoff.
            _clock.Advance(TimeSpan.FromMinutes(1));
            if (retryFromMaintenance) Assert.True((await cache.DiskCache.MaintainAsync()).Rescanned);

            var path = await Download(cache, id);
            var privateDirectory = cache.DiskCache.DirectoryPath;
            Assert.Equal(target, Path.GetDirectoryName(privateDirectory));
            Assert.Equal(privateDirectory, Path.GetDirectoryName(path));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
            Assert.Empty(Directory.EnumerateFiles(target));
        }
        finally
        {
            if (Directory.Exists(configuredDirectory)) Directory.Delete(configuredDirectory);
        }
    }

    [Fact]
    public async Task LinkedCacheRootServesArtworkAndRunsMaintenance()
    {
        var target = Directory.CreateDirectory(Path.Combine(DirectoryPath, "moved-cache")).FullName;
        var junction = Path.Combine(DirectoryPath, "ImageCache");
        try
        {
            await CreateJunction(junction, target);
            using var cache = new ImageService(junction, _clock, automaticMaintenance: false, maxDiskCacheBytes: 70);
            var privateDirectory = cache.DiskCache.DirectoryPath;
            var stray = Path.Combine(privateDirectory, $"0123456789ABCDEF.img.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(stray, "abandoned");
            var oldest = await Download(cache, "oldest");
            _clock.Advance(TimeSpan.FromMinutes(1));
            var newer = await Download(cache, "newer");
            _clock.Advance(TimeSpan.FromMinutes(1));
            var newest = await Download(cache, "newest");
            Assert.Equal(privateDirectory, Path.GetDirectoryName(newest));
            Assert.True(File.Exists(Path.Combine(target, Path.GetFileName(privateDirectory), Path.GetFileName(newest))));

            var result = await cache.DiskCache.MaintainAsync();

            Assert.True(result.Rescanned);
            Assert.True(result.Trimmed);
            Assert.Equal(32L, result.BytesRemaining);
            Assert.False(File.Exists(oldest));
            Assert.False(File.Exists(newer));
            Assert.False(File.Exists(stray));
            Assert.True(File.Exists(Path.Combine(privateDirectory, ArtworkDiskCache.IndexName)));
            Assert.True(cache.DiskCache.TryUseFile(newest));

            cache.Dispose();
            _clock.Advance(TimeSpan.FromDays(61));
            using var reopened = new ImageService(junction, _clock, automaticMaintenance: false, maxDiskCacheBytes: 70);
            result = await reopened.DiskCache.MaintainAsync();
            Assert.True(result.IndexLoaded);
            Assert.False(result.Trimmed);
            Assert.False(File.Exists(newest));
        }
        finally
        {
            if (Directory.Exists(junction)) Directory.Delete(junction);
        }
    }

    [Theory]
    [InlineData(false, 128, false)]
    [InlineData(false, 40, true)]
    [InlineData(true, 128, false)]
    [InlineData(true, 40, true)]
    public async Task FirstPassOnSharedLinkedTargetPreservesUnrelatedFiles(bool linkParent, int cap, bool trims)
    {
        var shared = Directory.CreateDirectory(Path.Combine(DirectoryPath, "shared")).FullName;
        var target = linkParent ? Directory.CreateDirectory(Path.Combine(shared, "ImageCache")).FullName : shared;
        var junction = Path.Combine(DirectoryPath, linkParent ? "SiloPlayer" : "ImageCache");
        var configuredDirectory = linkParent ? Path.Combine(junction, "ImageCache") : junction;
        var expiredForeignImage = Seed(1, minutesAgo: 70 * 24 * 60, directory: target);
        var emptyForeignImage = Seed(2, size: 0, directory: target);
        var notes = Path.Combine(target, "notes.txt");
        var unrelatedTemp = Path.Combine(target, "0123456789ABCDEF.img.crashed.tmp");
        var indexTemp = Path.Combine(target, $"{ArtworkDiskCache.IndexName}.{Guid.NewGuid():N}.tmp");
        var foreignIndex = Path.Combine(target, ArtworkDiskCache.IndexName);
        File.WriteAllText(notes, "not cache data");
        File.WriteAllText(unrelatedTemp, "not an app-generated temporary file");
        File.WriteAllText(indexTemp, "foreign temporary file");
        File.WriteAllText(foreignIndex, "foreign index");
        try
        {
            await CreateJunction(junction, shared);
            using var cache = new ImageService(configuredDirectory, _clock, automaticMaintenance: false, maxDiskCacheBytes: cap);
            var oldest = await Download(cache, "oldest");
            _clock.Advance(TimeSpan.FromMinutes(1));
            var newer = await Download(cache, "newer");

            var result = await cache.DiskCache.MaintainAsync();

            Assert.True(result.Rescanned);
            Assert.Equal(trims, result.Trimmed);
            Assert.Equal(trims ? 32L : 64L, result.BytesRemaining);
            Assert.Equal(!trims, File.Exists(oldest));
            Assert.True(File.Exists(newer));
            Assert.Equal(32L, new FileInfo(expiredForeignImage).Length);
            Assert.Equal(0L, new FileInfo(emptyForeignImage).Length);
            Assert.Equal("not cache data", File.ReadAllText(notes));
            Assert.Equal("not an app-generated temporary file", File.ReadAllText(unrelatedTemp));
            Assert.Equal("foreign temporary file", File.ReadAllText(indexTemp));
            Assert.Equal("foreign index", File.ReadAllText(foreignIndex));
        }
        finally
        {
            if (Directory.Exists(junction)) Directory.Delete(junction);
        }
    }

    [Theory]
    [InlineData("populated")]
    [InlineData("empty")]
    [InlineData("forged")]
    public async Task RelocatedCacheDoesNotAdoptAnUnclaimedSubdirectory(string state)
    {
        var target = Directory.CreateDirectory(Path.Combine(DirectoryPath, "shared")).FullName;
        var unclaimed = Directory.CreateDirectory(Path.Combine(target, "SiloPlayer.ArtworkCache")).FullName;
        var sentinel = Path.Combine(unclaimed, "notes.txt");
        var marker = Path.Combine(unclaimed, ".silo-artwork-cache");
        var index = Path.Combine(unclaimed, ArtworkDiskCache.IndexName);
        if (state != "empty") File.WriteAllText(sentinel, "not cache data");
        if (state == "forged")
        {
            File.WriteAllBytes(marker, []);
            File.WriteAllText(index, "foreign index");
        }
        var junction = Path.Combine(DirectoryPath, "ImageCache");
        try
        {
            await CreateJunction(junction, target);
            using var cache = new ImageService(junction, _clock, automaticMaintenance: false);
            if (state == "empty")
            {
                Assert.Empty(Directory.EnumerateFileSystemEntries(unclaimed));
                File.WriteAllText(sentinel, "not cache data");
            }

            Assert.NotEqual(unclaimed, cache.DiskCache.DirectoryPath);
            Assert.Equal(cache.DiskCache.DirectoryPath, Path.GetDirectoryName(await Download(cache, "new")));
            Assert.True((await cache.DiskCache.MaintainAsync()).Rescanned);
            Assert.Equal("not cache data", File.ReadAllText(sentinel));
            if (state == "forged")
            {
                Assert.Equal(0L, new FileInfo(marker).Length);
                Assert.Equal("foreign index", File.ReadAllText(index));
            }
            else Assert.Equal(sentinel, Assert.Single(Directory.EnumerateFileSystemEntries(unclaimed)));
        }
        finally
        {
            if (Directory.Exists(junction)) Directory.Delete(junction);
        }
    }

    [Fact]
    public async Task RelocatedCacheRejectsReplacementAtTheRecordedPath()
    {
        var target = Directory.CreateDirectory(Path.Combine(DirectoryPath, "shared")).FullName;
        var junction = Path.Combine(DirectoryPath, "ImageCache");
        try
        {
            await CreateJunction(junction, target);
            using var original = new ImageService(junction, _clock, automaticMaintenance: false);
            var claimed = original.DiskCache.DirectoryPath;
            Directory.Move(claimed, claimed + "-original");
            Directory.CreateDirectory(claimed);
            var sentinel = Path.Combine(claimed, "notes.txt");
            File.WriteAllText(sentinel, "not cache data");

            Assert.False((await original.DiskCache.MaintainAsync()).Rescanned);
            Assert.Null(await original.GetImageDiskPathAsync("new", "poster", Url("new"), _http));
            using var reopened = new ImageService(junction, _clock, automaticMaintenance: false);
            Assert.NotEqual(claimed, reopened.DiskCache.DirectoryPath);
            Assert.True((await reopened.DiskCache.MaintainAsync()).Rescanned);
            Assert.Equal("not cache data", File.ReadAllText(sentinel));
        }
        finally
        {
            if (Directory.Exists(junction)) Directory.Delete(junction);
        }
    }

    [Fact]
    public void VerifiedDirectoryPinsItsAncestorsUntilMutationEnds()
    {
        var ancestor = Path.Combine(DirectoryPath, "ancestor");
        var cache = Directory.CreateDirectory(Path.Combine(ancestor, "cache")).FullName;
        var moved = Path.Combine(DirectoryPath, "moved");
        using (var directory = WindowsCacheDirectory.Open(cache))
        {
            var error = Record.Exception(() => Directory.Move(ancestor, moved));
            Assert.True(error is IOException or UnauthorizedAccessException);
            Assert.True(Directory.Exists(cache));
            Assert.False(Directory.Exists(moved));
        }
        Directory.Move(ancestor, moved);
        Assert.True(Directory.Exists(Path.Combine(moved, "cache")));
    }

    [Fact]
    public async Task DisposeCancelsAnOverCapTrim()
    {
        using var cache = Open(64);
        for (var i = 1; i <= 3000; i++) Seed(i);
        using var watcher = new FileSystemWatcher(DirectoryPath, "*.img") { NotifyFilter = NotifyFilters.FileName };
        var deleting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.Deleted += (_, _) => deleting.TrySetResult();
        watcher.EnableRaisingEvents = true;

        var trim = cache.DiskCache.MaintainAsync();
        await deleting.Task.WaitAsync(TimeSpan.FromSeconds(20));
        cache.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await trim);
        Assert.True(Directory.EnumerateFiles(DirectoryPath, "*.img").Take(2).Count() > 1);
    }

    private async Task<ArtworkDiskCache.MaintenanceResult> Eventually(ImageService cache, Func<ArtworkDiskCache.MaintenanceResult, bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var result = await cache.DiskCache.MaintainAsync();
            if (predicate(result)) return result;
            await Task.Delay(20, timeout.Token);
        }
    }

    private static async Task CreateJunction(string junction, string target)
    {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/d /c mklink /j \"{junction}\" \"{target}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        });
        await process!.WaitForExitAsync();
        Assert.Equal(0, process.ExitCode);
    }

    private ImageService Open(long cap = 1024)
        => new(DirectoryPath, _clock, automaticMaintenance: false, maxDiskCacheBytes: cap);

    private async Task<string> Download(ImageService cache, string id)
        => Assert.IsType<string>(await cache.GetImageDiskPathAsync(id, "poster", Url(id), _http));

    private string Seed(int key, int size = 32, int minutesAgo = 5, string? directory = null)
    {
        var path = Path.Combine(directory ?? DirectoryPath, $"{key:X16}.img");
        File.WriteAllBytes(path, new byte[size]);
        var timestamp = _clock.GetUtcNow().AddMinutes(-minutesAgo).AddSeconds(key % 60).UtcDateTime;
        File.SetLastWriteTimeUtc(path, timestamp);
        File.SetLastAccessTimeUtc(path, timestamp);
        return path;
    }

    private static string Url(string id) => $"https://fixture.invalid/{id}.png";

    public void Dispose()
    {
        _http.Dispose();
        _root.Delete(recursive: true);
    }

    private sealed class TestClock : TimeProvider
    {
        private long _now = DateTimeOffset.UtcNow.UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _now), TimeSpan.Zero);
        internal void Advance(TimeSpan time) => Interlocked.Add(ref _now, time.Ticks);
    }

    private sealed class FixtureLog : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("silo-artwork-log-");
        private readonly IDisposable _scope;
        internal FixtureLog() => _scope = LocalLog.BeginOperation("artwork-test", _directory.FullName);
        internal string Path => System.IO.Path.Combine(_directory.FullName, "library_performance.txt");
        public void Dispose()
        {
            _scope.Dispose();
            _directory.Delete(recursive: true);
        }
    }

    private sealed class ArtworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                { Content = new ByteArrayContent(new byte[32]) });
    }
}
