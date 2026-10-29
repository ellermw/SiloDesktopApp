using SiloPlayer.Core.Models.Updates;
using SiloPlayer.Core.Services.Updates;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SiloPlayer.Tests;

public sealed class IncrementalUpdateTests
{
    private static readonly string Hash = new('a', 64);
    internal static UpdateManifest ValidManifest() => new(1, "1.2.465.0", 32, Hash,
        [new("SiloPlayer.exe", 8, Hash, 0, 16, Hash), new("SiloPlayer.dll", 8, Hash, 16, 16, Hash)]);

    [Fact]
    public void AcceptsBoundedManifestAndIdenticalDeduplicatedObjects()
    {
        var manifest = ValidManifest();
        UpdateManifestValidation.Validate(manifest);
        UpdateManifestValidation.Validate(manifest with { Files = manifest.Files.Append(manifest.Files[0] with { Path = "copy.dll" }).ToArray() });
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("a/../b")]
    [InlineData("/absolute")]
    [InlineData("C:\\outside")]
    [InlineData("a\\b")]
    [InlineData("a:stream")]
    [InlineData("con.txt")]
    [InlineData("con .txt")]
    [InlineData("CONIN$")]
    [InlineData("CONOUT$")]
    [InlineData("a/NUL")]
    [InlineData("a.")]
    [InlineData("a ")]
    [InlineData("a//b")]
    [InlineData("a\u0001b")]
    public void RejectsUnsafePaths(string path)
    {
        var manifest = ValidManifest();
        Assert.Throws<InvalidDataException>(() => UpdateManifestValidation.Validate(manifest with
        { Files = manifest.Files.Append(manifest.Files[0] with { Path = path }).ToArray() }));
    }

    [Theory]
    [InlineData("state.json")]
    [InlineData("versions/v1/a")]
    [InlineData(".updates/a")]
    [InlineData("transactions/a")]
    [InlineData("installation.json")]
    public void BootstrapCannotOverwriteInstallationControlPaths(string path)
    {
        var manifest = ValidManifest();
        Assert.Throws<InvalidDataException>(() => UpdateManifestValidation.Validate(manifest with
        { Files = manifest.Files.Append(manifest.Files[0] with { Path = path, Area = "bootstrap" }).ToArray() }));
    }

    [Fact]
    public void RejectsIdentityBoundsCaseCollisionsAndOverlappingObjects()
    {
        var manifest = ValidManifest();
        foreach (var bad in new[] { manifest with { Schema = 2 }, manifest with { BootstrapProtocol = 2 },
            manifest with { BackwardCompatibleData = false }, manifest with { Version = "1.2.465" },
            manifest with { PackSize = long.MaxValue }, manifest with { PackSha256 = "bad" },
            manifest with { Files = manifest.Files.Append(manifest.Files[0] with { Path = "siloplayer.EXE" }).ToArray() },
            manifest with { Files = [manifest.Files[0], manifest.Files[1] with { Offset = 1 }] },
            manifest with { Files = [manifest.Files[0], manifest.Files[1] with { Size = long.MaxValue }] },
            manifest with { Files = [manifest.Files[0], manifest.Files[1] with { Area = "other" }] } })
            Assert.Throws<InvalidDataException>(() => UpdateManifestValidation.Validate(bad));
    }

    [Fact]
    public async Task BuilderIsReproducibleAndDeduplicatesExactObjects()
    {
        using var tree = new Tree();
        Directory.CreateDirectory(tree.Path("source"));
        File.WriteAllText(tree.Path("source/SiloPlayer.exe"), "identical");
        File.WriteAllText(tree.Path("source/SiloPlayer.dll"), "identical");
        var first = await UpdatePackBuilder.BuildAsync(tree.Path("source"), null, tree.Path("one"), new Version(1, 2, 465, 0));
        var second = await UpdatePackBuilder.BuildAsync(tree.Path("source"), null, tree.Path("two"), new Version(1, 2, 465, 0));
        Assert.Equal(first.Files[0].Offset, first.Files[1].Offset);
        Assert.Equal(first.Files[0].PackedSize, first.PackSize);
        Assert.Equal(File.ReadAllBytes(tree.Path("one/" + UpdateManifestValidation.ManifestName)), File.ReadAllBytes(tree.Path("two/" + UpdateManifestValidation.ManifestName)));
        Assert.Equal(File.ReadAllBytes(tree.Path("one/" + UpdateManifestValidation.PackName)), File.ReadAllBytes(tree.Path("two/" + UpdateManifestValidation.PackName)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SkippedVersionsCopyVerifiedLocalFilesAndFetchOnlyChangedObjects(bool corruptSameSizeLocal)
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(fixture.Tree.Path("installed"));
        File.WriteAllBytes(fixture.Tree.Path("installed/SiloPlayer.exe"), corruptSameSizeLocal ? [0, 0, 0, 0] : fixture.Raw[0]);
        using var http = new HttpClient(fixture);
        var client = new IncrementalUpdateClient(http, fixture.Tree.Path("cache"), fixture.Tree.Path("installed"));
        var result = await client.PrepareAsync(fixture.Release, _ => { }, default);
        Assert.True(await client.VerifyPreparedAsync(result, default));
        Assert.Equal(corruptSameSizeLocal ? 2 : 1, fixture.Ranges.Count);
        if (!corruptSameSizeLocal) Assert.Equal(fixture.Manifest.Files[1].Offset, fixture.Ranges[0].From);
        File.WriteAllBytes(Path.Combine(result.Directory, "client", "SiloPlayer.dll"), [1, 2]);
        Assert.False(await client.VerifyPreparedAsync(result, default));
    }

    [Theory]
    [InlineData("wrong-range")]
    [InlineData("wrong-total")]
    [InlineData("wrong-length")]
    [InlineData("wrong-packed-hash")]
    [InlineData("wrong-raw-hash")]
    [InlineData("oversized-raw")]
    [InlineData("encoded")]
    [InlineData("trailing-packed")]
    public async Task RejectsBadRangesHashesAndDecompression(string fault)
    {
        using var fixture = new Fixture(fault); using var http = new HttpClient(fixture);
        var client = new IncrementalUpdateClient(http, fixture.Tree.Path("cache"), fixture.Tree.Path("absent"));
        await Assert.ThrowsAsync<InvalidDataException>(() => client.PrepareAsync(fixture.Release, _ => { }, default));
        Assert.False(Directory.Exists(fixture.Tree.Path("cache/ready")));
    }

    [Fact]
    public async Task IgnoredRangesDownloadAndVerifyTheCompletePack()
    {
        using var fixture = new Fixture("no-range"); using var http = new HttpClient(fixture);
        var client = new IncrementalUpdateClient(http, fixture.Tree.Path("cache"), fixture.Tree.Path("absent"));
        var result = await client.PrepareAsync(fixture.Release, _ => { }, default);
        Assert.True(await client.VerifyPreparedAsync(result, default));
        Assert.Equal(1, fixture.FullRequests);
        Assert.Equal(fixture.Pack.Length, fixture.FullBytes);
    }

    [Fact]
    public async Task CancellationResumesAnExactObjectAndRejectsTamperedCachedObjects()
    {
        using var fixture = new Fixture(); using var http = new HttpClient(fixture);
        using var stop = new CancellationTokenSource();
        var client = new IncrementalUpdateClient(http, fixture.Tree.Path("cache"), fixture.Tree.Path("absent"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.PrepareAsync(fixture.Release, p =>
        { if (p.Stage == "Preparing files" && p.CompletedFiles == 1) stop.Cancel(); }, stop.Token));
        fixture.Ranges.Clear();
        var result = await client.PrepareAsync(fixture.Release, _ => { }, default);
        Assert.Single(fixture.Ranges);
        Assert.True(await client.VerifyPreparedAsync(result, default));
        var objects = Directory.GetFiles(fixture.Tree.Path("cache"), "*.object", SearchOption.AllDirectories);
        Assert.NotEmpty(objects);
        foreach (var path in objects) File.WriteAllBytes(path, [1]);
        File.Delete(Path.Combine(result.Directory, "client", "SiloPlayer.exe"));
        fixture.Ranges.Clear();
        await client.PrepareAsync(fixture.Release, _ => { }, default);
        Assert.Single(fixture.Ranges);
    }

    [Fact]
    public async Task InterruptedObjectResumesOnlyTheRemainingCompressedBytes()
    {
        using var fixture = new Fixture("large"); using var http = new HttpClient(fixture);
        using var stop = new CancellationTokenSource();
        var client = new IncrementalUpdateClient(http, fixture.Tree.Path("cache"), fixture.Tree.Path("absent"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.PrepareAsync(fixture.Release, p =>
        { if (p.Stage == "Downloading changed files" && p.CompletedBytes >= 65536) stop.Cancel(); }, stop.Token));
        fixture.Ranges.Clear();
        var prepared = await client.PrepareAsync(fixture.Release, _ => { }, default);
        Assert.True(await client.VerifyPreparedAsync(prepared, default));
        Assert.Equal(65536, fixture.Ranges[0].From);
        Assert.Equal(fixture.Manifest.Files[0].PackedSize - 1, fixture.Ranges[0].To);
    }

    [Fact]
    public async Task PreparationProgressUsesChangedCompressedBytesAndRejectsAnOversizedCachedManifest()
    {
        using var fixture = new Fixture(); using var http = new HttpClient(fixture);
        Directory.CreateDirectory(fixture.Tree.Path("installed"));
        File.WriteAllBytes(fixture.Tree.Path("installed/SiloPlayer.exe"), fixture.Raw[0]);
        var client = new IncrementalUpdateClient(http, fixture.Tree.Path("cache"), fixture.Tree.Path("installed"));
        var progress = new List<IncrementalUpdateProgress>();
        var result = await client.PrepareAsync(fixture.Release, progress.Add, default);
        var download = Assert.Single(progress.Where(p => p.Stage == "Downloading changed files"));
        Assert.Equal(fixture.Manifest.Files[1].PackedSize, download.TotalBytes);
        Assert.Equal(download.TotalBytes, download.CompletedBytes);
        using (var file = File.OpenWrite(result.ManifestPath)) file.SetLength(UpdateManifestValidation.MaximumManifestBytes + 1);
        Assert.False(await client.VerifyPreparedAsync(result, default));
    }

    [Fact]
    public async Task AuthorityFetchesFixedTagMetadataAndManifestIndependently()
    {
        using var fixture = new Fixture(); using var http = new HttpClient(fixture);
        var result = await new IncrementalReleaseAuthority(http).FetchAsync("v1.2.465", fixture.Tree.Path("authority"), default);
        Assert.Equal("v1.2.465", result.Release.Tag);
        Assert.Equal(fixture.Release.Incremental!.ManifestUrl, result.Release.Incremental!.ManifestUrl);
        Assert.Equal(fixture.Release.Incremental.ManifestSha256, result.Release.Incremental.ManifestSha256, ignoreCase: true);
        Assert.Equal(fixture.Release.Incremental.PackSha256, result.Release.Incremental.PackSha256, ignoreCase: true);
        Assert.Equal(fixture.Manifest.Version, result.Manifest.Version);
        Assert.Equal(fixture.Release.Incremental!.ManifestSha256, Digest(File.ReadAllBytes(result.ManifestPath)));
        Assert.Equal(1, fixture.AuthorityRequests);
        Assert.Empty(fixture.Ranges);
    }

    [Theory]
    [InlineData("authority-wrong-tag")]
    [InlineData("authority-duplicate")]
    [InlineData("authority-no-digest")]
    public async Task AuthorityRejectsUnboundOrMalformedIncrementalMetadata(string fault)
    {
        using var fixture = new Fixture(fault); using var http = new HttpClient(fixture);
        await Assert.ThrowsAsync<InvalidDataException>(() => new IncrementalReleaseAuthority(http).FetchAsync("v1.2.465", fixture.Tree.Path("authority"), default));
        Assert.False(Directory.Exists(fixture.Tree.Path("authority")));
    }

    [Theory]
    [InlineData("authority-duplicate")]
    [InlineData("authority-no-digest")]
    public async Task InvalidOptionalAssetsPreserveLegacyInstallerDiscovery(string fault)
    {
        using var fixture = new Fixture(fault); using var http = new HttpClient(fixture);
        var service = new AppUpdateService(http, fixture.Tree.Path("cache"), new Version(1, 2, 464));
        await service.CheckAsync();
        Assert.NotNull(service.State.Release);
        Assert.Null(service.State.Release.Incremental);
        Assert.Equal(fixture.Release.InstallerUrl, service.State.Release.InstallerUrl);
        Assert.Null(service.State.Error);
    }

    [Theory]
    [InlineData(403)]
    [InlineData(429)]
    public async Task AuthorityPropagatesGitHubRateLimitWithoutDownloadingFiles(int status)
    {
        using var fixture = new Fixture("rate-limit-" + status); using var http = new HttpClient(fixture);
        var error = await Assert.ThrowsAnyAsync<HttpRequestException>(() => new IncrementalReleaseAuthority(http).FetchAsync("v1.2.465", fixture.Tree.Path("authority"), default));
        Assert.Equal((HttpStatusCode)status, error.StatusCode);
        Assert.Empty(fixture.Ranges);
    }

    internal sealed class Tree : IDisposable
    {
        private readonly string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "silo-incremental-tests-" + Guid.NewGuid().ToString("N"));
        public string Path(string path) => System.IO.Path.Combine(root, path.Replace('/', System.IO.Path.DirectorySeparatorChar));
        public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class Fixture : HttpMessageHandler
    {
        internal Tree Tree { get; } = new();
        internal byte[][] Raw { get; } = [Encoding.UTF8.GetBytes("old!"), Encoding.UTF8.GetBytes("new dll payload")];
        internal byte[] Pack;
        internal UpdateManifest Manifest;
        internal AppRelease Release;
        internal List<(long From, long To)> Ranges { get; } = [];
        internal int FullRequests;
        internal long FullBytes;
        internal int AuthorityRequests;
        private readonly string? fault;
        private readonly byte[] manifestBytes;
        internal Fixture(string? fault = null)
        {
            this.fault = fault;
            if (fault == "large") { Raw[0] = new byte[200000]; new Random(87).NextBytes(Raw[0]); }
            using var pack = new MemoryStream(); var files = new List<UpdateFileEntry>();
            for (var i = 0; i < Raw.Length; i++)
            {
                using var compressed = new MemoryStream();
                using (var brotli = new BrotliStream(compressed, CompressionLevel.SmallestSize, true)) brotli.Write(Raw[i]);
                if (fault == "trailing-packed") compressed.WriteByte(17);
                var bytes = compressed.ToArray();
                files.Add(new(i == 0 ? "SiloPlayer.exe" : "SiloPlayer.dll", fault == "oversized-raw" ? 1 : Raw[i].Length,
                    fault == "wrong-raw-hash" ? Hash : Digest(Raw[i]), pack.Length, bytes.Length, fault == "wrong-packed-hash" ? Hash : Digest(bytes)));
                pack.Write(bytes);
            }
            Pack = pack.ToArray(); Manifest = new(1, "1.2.465.0", Pack.Length, Digest(Pack), files);
            manifestBytes = JsonSerializer.SerializeToUtf8Bytes(Manifest);
            var prefix = "https://github.com/ellermw/SiloDesktopApp/releases/download/v1.2.465/";
            Release = new(new Version(1, 2, 465, 0), "v1.2.465", "", DateTimeOffset.UtcNow, new(prefix + "SiloInstaller-Windows-x64.exe"), Hash, 99)
            { Incremental = new(new(prefix + UpdateManifestValidation.ManifestName), Digest(manifestBytes), manifestBytes.Length,
                new(prefix + UpdateManifestValidation.PackName), Digest(Pack), Pack.Length) };
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Null(request.Headers.Authorization);
            if (request.RequestUri!.Host == "api.github.com")
            {
                Assert.Contains(request.RequestUri.AbsolutePath, new[] { "/repos/ellermw/SiloDesktopApp/releases/tags/v1.2.465", "/repos/ellermw/SiloDesktopApp/releases/latest" });
                AuthorityRequests++;
                if (fault?.StartsWith("rate-limit-") == true)
                {
                    var limited = new HttpResponseMessage((HttpStatusCode)int.Parse(fault[11..]));
                    limited.Headers.Add("X-RateLimit-Remaining", "0"); return Task.FromResult(limited);
                }
                object Asset(string name, Uri uri, string sha, long size) => new
                { name, state = "uploaded", size, digest = "sha256:" + sha.ToLowerInvariant(), browser_download_url = uri.AbsoluteUri };
                var assets = new List<object>
                {
                    Asset("SiloInstaller-Windows-x64.exe", Release.InstallerUrl, Release.Sha256, Release.Size),
                    Asset(UpdateManifestValidation.ManifestName, Release.Incremental!.ManifestUrl,
                        fault == "authority-no-digest" ? "bad" : Release.Incremental.ManifestSha256, Release.Incremental.ManifestSize),
                    Asset(UpdateManifestValidation.PackName, Release.Incremental.PackUrl, Release.Incremental.PackSha256, Release.Incremental.PackSize)
                };
                if (fault == "authority-duplicate") assets.Add(assets[1]);
                var metadata = JsonSerializer.SerializeToUtf8Bytes(new { draft = false, prerelease = false,
                    tag_name = fault == "authority-wrong-tag" ? "v1.2.466" : Release.Tag, assets,
                    published_at = "2026-10-29T12:00:00Z", body = "Synthetic release" });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(metadata) });
            }
            if (request.RequestUri!.AbsolutePath.EndsWith(UpdateManifestValidation.ManifestName))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(manifestBytes) });
            var range = request.Headers.Range?.Ranges.SingleOrDefault();
            var response = new HttpResponseMessage(range is null || fault == "no-range" ? HttpStatusCode.OK : HttpStatusCode.PartialContent);
            if (range is null) { FullRequests++; FullBytes += Pack.Length; }
            if (range is not null) Ranges.Add((range.From!.Value, range.To!.Value));
            var data = range is null || fault == "no-range" ? Pack : Pack[(int)range.From!.Value..((int)range.To!.Value + 1)];
            response.Content = new ByteArrayContent(data);
            if (range is not null && fault != "no-range") response.Content.Headers.ContentRange = new ContentRangeHeaderValue(
                range.From!.Value + (fault == "wrong-range" ? 1 : 0), range.To!.Value + (fault == "wrong-range" ? 1 : 0), Pack.Length + (fault == "wrong-total" ? 1 : 0));
            if (fault == "wrong-length") response.Content.Headers.ContentLength = data.Length + 1;
            if (fault == "encoded") response.Content.Headers.ContentEncoding.Add("gzip");
            return Task.FromResult(response);
        }
        protected override void Dispose(bool disposing) { Tree.Dispose(); base.Dispose(disposing); }
    }
    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
