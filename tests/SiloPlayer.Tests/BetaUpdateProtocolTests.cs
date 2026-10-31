using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SiloPlayer.Core.Models.Updates;
using SiloPlayer.Core.Services.Updates;

namespace SiloPlayer.Tests;

public sealed class BetaUpdateProtocolTests : IDisposable
{
    private readonly string sandbox = Path.Combine(Path.GetTempPath(), "silo-beta-protocol-" + Guid.NewGuid().ToString("N"));
    private string Root => Path.Combine(sandbox, "installation");

    [Theory]
    [InlineData("1.2.518-beta.12", "1.2.517.12")]
    [InlineData("1.2.518", "1.2.518.0")]
    public async Task BuilderEmitsTheCanonicalReleaseAndMappedNumericIdentity(string release, string numeric)
    {
        var client = Path.Combine(sandbox, "payload"); Directory.CreateDirectory(client);
        await File.WriteAllTextAsync(Path.Combine(client, "SiloPlayer.exe"), "synthetic payload");
        await File.WriteAllTextAsync(Path.Combine(client, "SiloPlayer.dll"), "synthetic assembly");
        var output = Path.Combine(sandbox, "assets");
        var manifest = await UpdatePackBuilder.BuildAsync(client, null, output, ReleaseVersion.Parse(release));
        Assert.Equal(release, manifest.ReleaseVersion); Assert.Equal(numeric, manifest.Version);
        var reread = JsonSerializer.Deserialize<UpdateManifest>(await File.ReadAllBytesAsync(Path.Combine(output, UpdateManifestValidation.ManifestName)))!;
        UpdateManifestValidation.Validate(reread);
        Assert.Equal(release, reread.ReleaseVersion); Assert.Equal(numeric, reread.Version);
    }

    [Theory]
    [InlineData("1.2.518-beta.2")]
    [InlineData("1.2.518")]
    [InlineData("v1.2.518-beta.12")]
    [InlineData("1.2.518-beta.12+repacked")]
    public void ManifestSemanticIdentityCannotDisagreeWithItsNumericVersion(string semantic)
    {
        var manifest = IncrementalUpdateTests.ValidManifest() with { Version = "1.2.517.12" };
        var json = JsonSerializer.SerializeToNode(manifest)!; json["ReleaseVersion"] = semantic;
        var altered = json.Deserialize<UpdateManifest>()!;
        Assert.Throws<InvalidDataException>(() => UpdateManifestValidation.Validate(altered));
    }

    [Theory]
    [InlineData("1.2.518-beta.2", "1.2.518-beta.12", "1.2.517.12")]
    [InlineData("1.2.517", "1.2.518-beta.12", "1.2.517.12")]
    [InlineData("1.2.518-beta.12", "1.2.518", "1.2.518.0")]
    public async Task DirectSkippedBetaAndStableTransitionsReuseVerifiedFilesAndRetainRecovery(string installed, string target, string expectedNumeric)
    {
        var source = await ArrangeAsync(installed, target);
        using var wire = new Assets(target, source.Assets); using var http = new HttpClient(wire);
        var service = await CreateAsync(http, installed, target, source.Client);
        await service.CheckAsync(); await service.DownloadAsync();
        Assert.True(service.State.Error is null, service.State.Error);
        var package = Assert.IsType<PreparedUpdatePackage>(service.State.PreparedPackage);
        Assert.Equal(expectedNumeric, package.Manifest.Version);
        Assert.Equal(target, JsonSerializer.SerializeToNode(package.Manifest)!["ReleaseVersion"]!.GetValue<string>());
        Assert.Single(wire.Ranges); Assert.Equal(0, wire.FullInstallerRequests);
        Assert.Equal("new assembly", await File.ReadAllTextAsync(Path.Combine(package.Directory, "client", "SiloPlayer.dll")));
        Assert.True(await service.ReverifyReadyAsync());
        var preferences = File.ReadAllBytes(Path.Combine(sandbox, "synthetic-user-settings.json"));
        var bootstrap = File.ReadAllBytes(Path.Combine(Root, "SiloPlayer.Launcher.dll"));
        var staged = SideBySideInstallation.Stage(Root, package, _ => { }, default);
        Assert.Equal(source.Numeric, SideBySideInstallation.ReadState(Root)!.ActiveVersion);
        SideBySideInstallation.Activate(staged);
        Assert.Equal(expectedNumeric, SideBySideInstallation.ReadState(Root)!.ActiveVersion);
        SideBySideInstallation.Rollback(Root, staged.TransactionId);
        Assert.Equal(source.Numeric, SideBySideInstallation.ReadState(Root)!.ActiveVersion);
        Assert.Equal(preferences, File.ReadAllBytes(Path.Combine(sandbox, "synthetic-user-settings.json")));
        Assert.Equal(bootstrap, File.ReadAllBytes(Path.Combine(Root, "SiloPlayer.Launcher.dll")));
        Assert.True(File.Exists(Path.Combine(source.Client, "SiloPlayer.dll")));
        Assert.Equal(new[] { source.Numeric, expectedNumeric }.Order(), Directory.GetDirectories(Path.Combine(Root, "versions")).Select(Path.GetFileName).Order());
    }

    [Fact]
    public async Task PrivilegedAuthorityIndependentlyRechecksThePublishedBetaTagAndManifest()
    {
        var source = await ArrangeAsync("1.2.518-beta.2", "1.2.518-beta.12");
        using var wire = new Assets("1.2.518-beta.12", source.Assets); using var http = new HttpClient(wire);
        var authority = await new IncrementalReleaseAuthority(http).FetchAsync("v1.2.518-beta.12", Path.Combine(sandbox, "authority"), default);
        Assert.Equal("1.2.518-beta.12", authority.Release.DisplayVersion);
        Assert.Equal("1.2.517.12", authority.Manifest.Version);
        Assert.Contains(wire.Metadata, url => url.EndsWith("/tags/v1.2.518-beta.12"));
    }

    [Fact]
    public async Task StagingItselfRejectsAnUnapprovedMajorEvenIfDiscoveryIsBypassed()
    {
        var source = await ArrangeAsync("1.2.517", "2.0.1");
        using var wire = new Assets("2.0.1", source.Assets); using var http = new HttpClient(wire);
        var release = wire.Release("2.0.1");
        var metadata = release.ToJsonString();
        // The unprivileged package preparer verifies bytes; privileged Stage owns the major guard.
        var prepared = await new IncrementalUpdateClient(http, Path.Combine(sandbox, "cache"), source.Client).PrepareAsync(
            new AppRelease(new Version(2, 0, 1, 0), "v2.0.1", "", DateTimeOffset.UtcNow,
                new Uri("https://github.com/ellermw/SiloDesktopApp/releases/download/v2.0.1/SiloInstaller-Windows-x64.exe"), Assets.FullHash, Assets.FullPackage.Length)
            { SemanticVersion = ReleaseVersion.Parse("2.0.1"), Incremental = wire.Incremental("2.0.1") }, _ => { }, default);
        Assert.Throws<InvalidOperationException>(() => SideBySideInstallation.Stage(Root, prepared, _ => { }, default));
        Assert.Equal("1.2.517.0", SideBySideInstallation.ReadState(Root)!.ActiveVersion);
    }

    [Theory]
    [InlineData("missing-identity")]
    [InlineData("wrong-identity")]
    [InlineData("tampered-hash")]
    [InlineData("incomplete-pair")]
    public async Task ManipulatedIncrementalAuthorityNeverSilentlyBecomesFullInstallerSuccess(string bad)
    {
        var source = await ArrangeAsync("1.2.518-beta.2", "1.2.518-beta.12");
        var path = Path.Combine(source.Assets, UpdateManifestValidation.ManifestName);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        if (bad == "missing-identity") json.AsObject().Remove("ReleaseVersion");
        if (bad == "wrong-identity") json["ReleaseVersion"] = "1.2.518-beta.2";
        await File.WriteAllTextAsync(path, json.ToJsonString());
        using var wire = new Assets("1.2.518-beta.12", source.Assets) { BadManifestHash = bad == "tampered-hash", IncompletePair = bad == "incomplete-pair" };
        using var http = new HttpClient(wire); var service = await CreateAsync(http, "1.2.518-beta.2", "1.2.518-beta.12", source.Client);
        await service.CheckAsync(); await service.DownloadAsync();
        Assert.NotNull(service.State.Error); Assert.Null(service.State.PreparedPackage); Assert.Null(service.State.ReadyInstaller);
        Assert.Equal(0, wire.FullInstallerRequests); Assert.False(service.ShouldInstallOnExit);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VerifiedFullFallbackIsPreparedButNeverAutomaticallyInstallsAVersionedMigration(bool unsupportedProtocol)
    {
        var source = await ArrangeAsync("1.2.518-beta.2", "1.2.518-beta.12");
        if (unsupportedProtocol)
        {
            var path = Path.Combine(source.Assets, UpdateManifestValidation.ManifestName);
            var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!; json["BootstrapProtocol"] = 2;
            await File.WriteAllTextAsync(path, json.ToJsonString());
        }
        using var wire = new Assets("1.2.518-beta.12", unsupportedProtocol ? source.Assets : null);
        using var http = new HttpClient(wire); var service = await CreateAsync(http, "1.2.518-beta.2", "1.2.518-beta.12", source.Client);
        service.SetMode(AppUpdateMode.Automatic); await service.CheckAsync(); await service.DownloadAsync();
        Assert.True(service.State.Error is null, service.State.Error);
        var installer = Assert.IsType<string>(service.State.ReadyInstaller);
        Assert.Equal(Assets.FullPackage, await File.ReadAllBytesAsync(installer)); Assert.True(await service.ReverifyReadyAsync());
        Assert.Null(service.State.PreparedPackage); Assert.Equal(1, wire.FullInstallerRequests);
        Assert.False(service.ShouldInstallOnExit); // A full migration requires an explicit manual choice.
    }

    [Fact]
    public async Task InterruptedSkippedBetaPreparationCanRetryWithoutIntermediateVersionsOrLostPreferences()
    {
        var source = await ArrangeAsync("1.2.518-beta.2", "1.2.518-beta.12");
        using var wire = new Assets("1.2.518-beta.12", source.Assets) { TruncateNextRange = true };
        using var http = new HttpClient(wire); var service = await CreateAsync(http, "1.2.518-beta.2", "1.2.518-beta.12", source.Client);
        await service.CheckAsync(); await service.DownloadAsync(); Assert.NotNull(service.State.Error); Assert.Null(service.State.PreparedPackage);
        Assert.Equal("1.2.517.2", SideBySideInstallation.ReadState(Root)!.ActiveVersion);
        await service.DownloadAsync(); Assert.Null(service.State.Error); Assert.NotNull(service.State.PreparedPackage); Assert.True(await service.ReverifyReadyAsync());
        Assert.Equal(2, wire.Ranges.Count); Assert.Equal(0, wire.FullInstallerRequests);
        Assert.Single(Directory.GetDirectories(Path.Combine(Root, "versions")));
        Assert.Equal("synthetic profile=viewer; subtitle=Spanish; volume=42", await File.ReadAllTextAsync(Path.Combine(sandbox, "synthetic-user-settings.json")));
    }

    private async Task<AppUpdateService> CreateAsync(HttpClient http, string installed, string target, string client)
    {
        var semantic = ReleaseVersion.Parse(installed);
        var service = new AppUpdateService(http, Path.Combine(sandbox, "cache"), semantic.GetWindowsVersion())
        { InstalledReleaseVersion = semantic, PreferIncrementalUpdates = true, InstalledPayloadDirectory = client };
        if (target.Contains('-')) await service.SetChannelAsync(AppUpdateChannel.Beta);
        return service;
    }

    private async Task<(string Client, string Assets, string Numeric)> ArrangeAsync(string installed, string target)
    {
        var numeric = ReleaseVersion.Parse(installed).GetWindowsVersion();
        var client = Path.Combine(Root, "versions", numeric.ToString(4)); Directory.CreateDirectory(client);
        await File.WriteAllTextAsync(Path.Combine(client, "SiloPlayer.exe"), "synthetic unchanged executable");
        await File.WriteAllTextAsync(Path.Combine(client, "SiloPlayer.dll"), "old assembly");
        await File.WriteAllTextAsync(Path.Combine(client, "data.bin"), "unchanged common payload");
        await File.WriteAllTextAsync(Path.Combine(Root, "SiloPlayer.Launcher.dll"), "immutable synthetic Protocol1 bootstrap");
        await File.WriteAllTextAsync(Path.Combine(sandbox, "synthetic-user-settings.json"), "synthetic profile=viewer; subtitle=Spanish; volume=42");
        var baseline = await UpdatePackBuilder.BuildAsync(client, null, Path.Combine(sandbox, "baseline-pack"), numeric);
        SideBySideInstallation.Initialize(Root, baseline);
        var fresh = Path.Combine(sandbox, "new-client"); Directory.CreateDirectory(fresh);
        foreach (var file in Directory.EnumerateFiles(client).Where(f => !Path.GetFileName(f).StartsWith('.'))) File.Copy(file, Path.Combine(fresh, Path.GetFileName(file)));
        await File.WriteAllTextAsync(Path.Combine(fresh, "SiloPlayer.dll"), "new assembly");
        var assets = Path.Combine(sandbox, "target-assets");
        await UpdatePackBuilder.BuildAsync(fresh, null, assets, ReleaseVersion.Parse(target));
        return (client, assets, numeric.ToString(4));
    }

    private sealed class Assets(string target, string? directory) : HttpMessageHandler
    {
        internal static readonly byte[] FullPackage = Encoding.UTF8.GetBytes("synthetic full package");
        internal static string FullHash => Convert.ToHexString(SHA256.HashData(FullPackage));
        public bool BadManifestHash, IncompletePair, TruncateNextRange;
        public int FullInstallerRequests;
        public List<(long From, long To)> Ranges = [];
        public List<string> Metadata = [];
        public IncrementalReleaseAssets Incremental(string version)
        {
            var manifest = File.ReadAllBytes(Path.Combine(directory!, UpdateManifestValidation.ManifestName));
            var pack = File.ReadAllBytes(Path.Combine(directory!, UpdateManifestValidation.PackName));
            var prefix = $"https://github.com/ellermw/SiloDesktopApp/releases/download/v{version}/";
            return new(new Uri(prefix + UpdateManifestValidation.ManifestName), Convert.ToHexString(SHA256.HashData(manifest)), manifest.Length,
                new Uri(prefix + UpdateManifestValidation.PackName), Convert.ToHexString(SHA256.HashData(pack)), pack.Length);
        }
        public JsonObject Release(string version)
        {
            var release = UpdateChannelTests.Release(version);
            var full = release["assets"]![0]!; full["size"] = FullPackage.Length; full["digest"] = "sha256:" + FullHash;
            if (version == target && directory is not null)
                foreach (var name in new[] { UpdateManifestValidation.ManifestName, UpdateManifestValidation.PackName })
                {
                    if (IncompletePair && name == UpdateManifestValidation.PackName) continue;
                    var bytes = File.ReadAllBytes(Path.Combine(directory, name));
                    release["assets"]!.AsArray().Add(new JsonObject { ["name"] = name, ["state"] = "uploaded", ["size"] = bytes.Length,
                        ["digest"] = "sha256:" + (BadManifestHash && name == UpdateManifestValidation.ManifestName ? new string('a', 64) : Convert.ToHexString(SHA256.HashData(bytes))),
                        ["browser_download_url"] = $"https://github.com/ellermw/SiloDesktopApp/releases/download/v{version}/{name}" });
                }
            return release;
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Null(request.Headers.Authorization); var uri = request.RequestUri!;
            if (uri.Host == "api.github.com")
            {
                Metadata.Add(uri.AbsoluteUri);
                var json = uri.AbsolutePath.EndsWith("/latest") ? Release(target.Contains('-') ? "1.2.517" : target).ToJsonString()
                    : uri.AbsolutePath.Contains("/tags/") ? Release(target).ToJsonString() : JsonSerializer.Serialize(new[] { Release(target) });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
            }
            var name = Path.GetFileName(uri.AbsolutePath);
            if (name == "SiloInstaller-Windows-x64.exe") { FullInstallerRequests++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(FullPackage) }); }
            var bytes = File.ReadAllBytes(Path.Combine(directory!, name));
            if (request.Headers.Range is { } range)
            {
                var item = range.Ranges.Single(); var from = item.From!.Value; var to = item.To!.Value; Ranges.Add((from, to));
                var content = bytes[(int)from..((int)to + 1)];
                if (TruncateNextRange) { TruncateNextRange = false; content = content[..(content.Length / 2)]; }
                var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(content) };
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, bytes.Length); return Task.FromResult(response);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
    }
    public void Dispose() { if (Directory.Exists(sandbox)) Directory.Delete(sandbox, true); }
}
